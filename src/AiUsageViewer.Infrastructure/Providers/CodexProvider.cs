using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using AiUsageViewer.Core;

namespace AiUsageViewer.Infrastructure.Providers;

public sealed class CodexProvider : IQuotaProvider
{
    public ProviderKind Kind=>ProviderKind.Codex;
    public ProviderCapabilities Capabilities=>new(true,true,false,false,false);

    public async Task<ProviderResult> FetchAsync(AccountProfile account,CancellationToken cancellationToken)
    {
        var executable=ResolveExecutable(account.Executable);
        if(executable is null) return new(ConnectionState.Unavailable,MessageCode:"codex_not_found");
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var start=new ProcessStartInfo(executable) { UseShellExecute=false,CreateNoWindow=true,
            RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true };
        start.ArgumentList.Add("app-server"); start.ArgumentList.Add("--listen"); start.ArgumentList.Add("stdio://");
        if(account.ProfileDirectory is { Length:>0 } profile) start.Environment["CODEX_HOME"]=profile;
        using var process=new Process { StartInfo=start };
        Task? drain=null;
        try
        {
            if(!process.Start()) return new(ConnectionState.Unavailable,MessageCode:"codex_start_failed");
            drain=DrainAsync(process.StandardError,timeout.Token);
            return await ExchangeAsync(process.StandardOutput,process.StandardInput,account,timeout.Token);
        }
        catch(OperationCanceledException) when(!cancellationToken.IsCancellationRequested)
        { return new(ConnectionState.Unavailable,MessageCode:"timeout"); }
        catch(Exception e) when(e is Win32Exception or IOException or InvalidOperationException or JsonException)
        { return new(ConnectionState.Unavailable,MessageCode:"codex_rpc_unavailable"); }
        finally
        {
            timeout.Cancel();
            try { if(!process.HasExited) process.Kill(entireProcessTree:true); } catch(InvalidOperationException) {} catch(Win32Exception) {}
            if(drain is not null) { try { await drain; } catch(OperationCanceledException) {} catch(IOException) {} }
        }
    }

    public static string? ResolveExecutable(string? configured)
    {
        if(!string.IsNullOrWhiteSpace(configured)) return File.Exists(configured)?configured:null;
        var candidates=(Environment.GetEnvironmentVariable("PATH")??"").Split(Path.PathSeparator)
            .Where(x=>!string.IsNullOrWhiteSpace(x)).Select(x=>Path.Combine(x.Trim('"'),"codex.exe"))
            .Append(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","OpenAI","Codex","bin","codex.exe"));
        return candidates.FirstOrDefault(File.Exists);
    }

    private static async Task DrainAsync(StreamReader reader,CancellationToken ct)
    {
        var buffer=new char[4096]; while(await reader.ReadAsync(buffer,ct)>0) {} // Never retain stderr or auth payloads.
    }

    public static async Task<ProviderResult> ExchangeAsync(TextReader output,TextWriter input,AccountProfile account,CancellationToken ct)
    {
        await SendAsync(new { id=1,method="initialize",@params=new { clientInfo=new { name="ai_usage_viewer",title="Ai UsageNest",version="0.3.0" } } });
        var initialize=await ReadAsync(1);
        if(initialize.Get("error").ValueKind==JsonValueKind.Object) return new(ConnectionState.Unavailable,MessageCode:"codex_initialize_failed");
        await SendAsync(new { method="initialized",@params=new {} });
        await SendAsync(new { id=2,method="account/read",@params=new { refreshToken=false } });
        var identity=await ReadAsync(2);
        var current=identity.Get("result").Get("account");
        if(current.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            return new(ConnectionState.SignInRequired,MessageCode:"codex_login_required");
        if(current.Get("type").Text()=="apiKey") return new(ConnectionState.Unsupported,MessageCode:"subscription_account_required");
        await SendAsync(new { id=3,method="account/rateLimits/read",@params=new {} });
        var response=await ReadAsync(3);
        if(response.Get("error").ValueKind==JsonValueKind.Object)
            return new(ConnectionState.Unavailable,MessageCode:"codex_quota_unavailable");
        return Parse(account,response.Get("result"),DateTimeOffset.UtcNow);

        async Task SendAsync(object request)
        {
            await input.WriteLineAsync(JsonSerializer.Serialize(request).AsMemory(),ct);
            await input.FlushAsync(ct);
        }
        async Task<JsonElement> ReadAsync(int id)
        {
            while(true)
            {
                var line=await output.ReadLineAsync(ct)??throw new IOException("RPC stream closed.");
                if(line.Length>4*1024*1024) throw new IOException("RPC response too large.");
                using var document=JsonDocument.Parse(line);
                if(document.RootElement.Get("id").Number()==id) return document.RootElement.Clone();
            }
        }
    }

    public static ProviderResult Parse(AccountProfile account,JsonElement result,DateTimeOffset now)
    {
        var windows=new List<QuotaWindow>();
        var byId=result.Get("rateLimitsByLimitId");
        var baseline=result.Get("rateLimits");
        var baselineId=baseline.Get("limitId").Text()??"codex";
        Add(baselineId,baseline);
        if(byId.ValueKind==JsonValueKind.Object)
            foreach(var group in byId.EnumerateObject()) Add(group.Name,group.Value);
        if(windows.Count==0) return new(ConnectionState.Unsupported,MessageCode:"quota_not_reported");
        return new(ConnectionState.Ready,new(account.Id,account.Provider,now,windows,[],baseline.Get("planType").Text()));

        void Add(string scope,JsonElement group)
        {
            if(group.ValueKind!=JsonValueKind.Object) return;
            foreach(var property in group.EnumerateObject())
            {
                var window=property.Value;
                if(window.ValueKind!=JsonValueKind.Object) continue;
                var used=window.Get("usedPercent").Number();
                var resets=window.Get("resetsAt").Time();
                if(used is null && resets is null) continue;
                var previous=windows.FirstOrDefault(w=>w.Id==scope+":"+property.Name);
                windows.RemoveAll(w=>w.Id==scope+":"+property.Name);
                var minutes=window.Get("windowDurationMins").Number();
                windows.Add(new(scope+":"+property.Name,property.Name,used is { } u?Math.Clamp(u,0,100):previous?.UsedPercent,resets??previous?.ResetsAt,scope,
                    WindowMinutes:minutes is >= 0 and <= long.MaxValue?(long)minutes.Value:previous?.WindowMinutes));
            }
        }
    }
}
