using System.Globalization;
using System.Text.Json;
using AiUsageViewer.Core;

namespace AiUsageViewer.Infrastructure.Providers;
public sealed class OpenRouterActivity(ProviderHttp http,ISecretStore secrets) : IActivityProvider
{
    public async Task<ActivityResult> FetchActivityAsync(AccountProfile account,CancellationToken ct)
    {
        var token=account.SecretReference is { } reference?secrets.Read(reference):null;
        if(string.IsNullOrWhiteSpace(token)) return new(ConnectionState.SignInRequired,MessageCode:"api_key_required");
        var result=await http.GetAsync("https://openrouter.ai/api/v1/activity",token,ct);
        if(!result.Success) return new(result.Status==403?ConnectionState.Unsupported:result.Failure.State,
            MessageCode:result.Status==403?"history_management_key_required":result.Failure.MessageCode,RetryAfter:result.RetryAfter);
        return Parse(account.Id,result.Data,DateOnly.FromDateTime(DateTime.UtcNow));
    }
    public static ActivityResult Parse(string account,JsonElement root,DateOnly today)
    {
        var data=root.Get("data");if(data.ValueKind!=JsonValueKind.Array) return new(ConnectionState.Unavailable,MessageCode:"invalid_response");
        var rows=new List<ActivityRow>();var invalid=false;
        foreach(var item in data.EnumerateArray())
        {
            if(!DateOnly.TryParseExact(item.Get("date").Text(),"yyyy-MM-dd",CultureInfo.InvariantCulture,DateTimeStyles.None,out var day)||
                item.Get("model").Text() is not { Length:>0 } model||item.Get("endpoint_id").Text() is not { Length:>0 } endpoint||
                item.Get("usage").Number() is not >=0||!ValidCount(item.Get("prompt_tokens"))||!ValidCount(item.Get("completion_tokens"))||
                !ValidCount(item.Get("requests"))||!ValidCount(item.Get("reasoning_tokens"))||item.Get("byok_usage_inference").Number() is not >=0)
            { invalid=true;continue; }
            if(day>=today||day<today.AddDays(-30)) continue; // This surface is explicitly completed UTC days.
            var output=item.Get("completion_tokens").Count();
            rows.Add(new(account,day,model,endpoint,item.Get("prompt_tokens").Count(),output,
                Math.Min(output,item.Get("reasoning_tokens").Count()),item.Get("requests").Count(),item.Get("usage").Number()!.Value,
                Math.Max(0,item.Get("byok_usage_inference").Number()??0)));
        }
        // An invalid page cannot replace a previously complete daily snapshot.
        return invalid?new(ConnectionState.Unavailable,MessageCode:"invalid_activity_rows"):new(ConnectionState.Ready,rows);
    }
    private static bool ValidCount(JsonElement value)=>value.Number() is { } n&&n>=0&&n<=long.MaxValue&&decimal.Truncate(n)==n;
}
