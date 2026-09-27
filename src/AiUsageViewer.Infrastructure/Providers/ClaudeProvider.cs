using System.Text.Json;
using AiUsageViewer.Core;

namespace AiUsageViewer.Infrastructure.Providers;

public sealed class ClaudeProvider(ProviderHttp http) : IQuotaProvider
{
    public ProviderKind Kind=>ProviderKind.Claude;
    public ProviderCapabilities Capabilities=>new(true,true,false,false,false);
    public async Task<ProviderResult> FetchAsync(AccountProfile account,CancellationToken cancellationToken)
    {
        var path=Path.Combine(account.ProfileDirectory??"", ".credentials.json");
        if(!File.Exists(path)) return new(ConnectionState.SignInRequired,MessageCode:"claude_login_required");
        string? token;
        try
        {
            if(new FileInfo(path).Length>1024*1024) return new(ConnectionState.Unavailable,MessageCode:"invalid_credentials_file");
            using var credentials=JsonDocument.Parse(await File.ReadAllTextAsync(path,cancellationToken));
            token=credentials.RootElement.Get("claudeAiOauth").Get("accessToken").Text();
        }
        catch(Exception e) when(e is IOException or UnauthorizedAccessException or JsonException)
        { return new(ConnectionState.SignInRequired,MessageCode:"claude_credentials_unavailable"); }
        if(string.IsNullOrWhiteSpace(token)) return new(ConnectionState.SignInRequired,MessageCode:"claude_login_required");
        var response=await http.GetAsync("https://api.anthropic.com/api/oauth/usage",token,cancellationToken,
            new Dictionary<string,string> { ["anthropic-beta"]="oauth-2025-04-20" });
        return response.Success?Parse(account,response.Data,DateTimeOffset.UtcNow):response.Failure;
    }

    public static ProviderResult Parse(AccountProfile account,JsonElement root,DateTimeOffset now)
    {
        if(root.ValueKind!=JsonValueKind.Object) return new(ConnectionState.Unavailable,MessageCode:"invalid_response");
        var windows=new List<QuotaWindow>();
        // The structured list is the display contract used by current Claude Code.
        // Prefer it to compatibility fields, which also include internal/promotional buckets.
        var limits=root.Get("limits");
        if(limits.ValueKind==JsonValueKind.Array)
            foreach(var item in limits.EnumerateArray())
            {
                var kind=item.Get("kind").Text();
                if(string.IsNullOrWhiteSpace(kind)) continue;
                var group=item.Get("group").Text();
                var scope=item.Get("scope");var model=scope.Get("model");
                var modelId=model.Get("id").Text();var modelName=model.Get("display_name").Text();var surface=scope.Get("surface").Text();
                var scopeLabel=string.Join(" · ",new[]{modelName??modelId,surface}.Where(s=>!string.IsNullOrWhiteSpace(s)));
                var id=scopeLabel.Length==0&&kind=="session"?"five_hour":scopeLabel.Length==0&&kind=="weekly_all"?"seven_day":
                    "limit:"+Uri.EscapeDataString(kind)+":"+Uri.EscapeDataString(modelId??modelName??"")+":"+Uri.EscapeDataString(surface??"");
                var label=group=="session"||kind=="session"?"session":group=="weekly"||kind.StartsWith("weekly",StringComparison.Ordinal)?"weekly":kind;
                var percent=item.Get("percent").Number();
                var window=new QuotaWindow(id,label,percent is { } value?Math.Clamp(value,0,100):null,item.Get("resets_at").Time(),
                    scopeLabel.Length==0?"account":scopeLabel,WindowMinutes:label=="weekly"?10080:null);
                // Stable scope identity prevents identical entries from becoming duplicate cards.
                windows.RemoveAll(existing=>existing.Id==id);windows.Add(window);
            }
        if(windows.Count>0) return new(ConnectionState.Ready,new(account.Id,account.Provider,now,windows,[]));
        foreach(var property in root.EnumerateObject())
        {
            var usage=property.Value.Get("utilization").Number();
            if(property.Value.ValueKind!=JsonValueKind.Object || property.Name=="extra_usage") continue;
            if(usage is not null || property.Value.Get("resets_at").Time() is not null)
                windows.Add(new(property.Name,property.Name,usage is { } u?Math.Clamp(u,0,100):null,
                    property.Value.Get("resets_at").Time(),property.Name.StartsWith("seven_day_")?property.Name:"account"));
        }
        if(windows.Count==0) return new(ConnectionState.Unsupported,MessageCode:"quota_not_reported");
        return new(ConnectionState.Ready,new(account.Id,account.Provider,now,windows,[]));
    }
}
