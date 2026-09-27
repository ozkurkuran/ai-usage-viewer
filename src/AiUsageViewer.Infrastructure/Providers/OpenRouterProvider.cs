using System.Text.Json;
using AiUsageViewer.Core;

namespace AiUsageViewer.Infrastructure.Providers;

public sealed class OpenRouterProvider(ProviderHttp http,ISecretStore secrets) : IQuotaProvider
{
    public ProviderKind Kind=>ProviderKind.OpenRouter;
    public ProviderCapabilities Capabilities=>new(false,true,true,true,true);
    public async Task<ProviderResult> FetchAsync(AccountProfile account,CancellationToken cancellationToken)
    {
        var token=account.SecretReference is { } reference?secrets.Read(reference):null;
        if(string.IsNullOrWhiteSpace(token)) return new(ConnectionState.SignInRequired,MessageCode:"api_key_required");
        var results=await Task.WhenAll(http.GetAsync("https://openrouter.ai/api/v1/key",token,cancellationToken),
            http.GetAsync("https://openrouter.ai/api/v1/credits",token,cancellationToken));
        if(!results.Any(x=>x.Success)) return results.FirstOrDefault(x=>x.Status==429)?.Failure ?? results[0].Failure;
        var now=DateTimeOffset.UtcNow;
        var snapshot=Parse(account,results[0].Success?results[0].Data:default,results[1].Success?results[1].Data:default,now);
        bool[] valid=[results[0].Success&&(snapshot.Windows.Count>0||snapshot.Money.Any(m=>m.Scope=="key")),
            results[1].Success&&snapshot.Money.Any(m=>m.Id=="balance")];
        var sources=results.Select((r,i)=>new SourceStatus(i==0?"key":"credits",valid[i]?ConnectionState.Ready:r.Success?ConnectionState.Unavailable:r.Failure.State,
            valid[i]?now:null,valid[i]?null:r.Success?"invalid_response":r.Failure.MessageCode)).ToList();
        snapshot=snapshot with { Sources=sources };
        var rateLimited=results.FirstOrDefault(r=>r.Status==429);
        return new(rateLimited is not null?ConnectionState.RateLimited:valid.All(v=>v)?ConnectionState.Ready:valid.Any(v=>v)?ConnectionState.Partial:ConnectionState.Unavailable,snapshot,
            valid.All(v=>v)?null:results[1].Status==403?"credits_permission_required":"partial_response",rateLimited?.RetryAfter);
    }

    public static ProviderSnapshot Parse(AccountProfile account,JsonElement keyResponse,JsonElement creditsResponse,DateTimeOffset now)
    {
        var key=keyResponse.Get("data"); var credits=creditsResponse.Get("data");
        var windows=new List<QuotaWindow>(); var money=new List<MoneyMetric>();
        var limit=key.Get("limit").Number(); var remaining=key.Get("limit_remaining").Number();
        var reset=key.Get("limit_reset").Text();
        var period=reset switch { "daily"=>"usage_daily","weekly"=>"usage_weekly","monthly"=>"usage_monthly",null or ""=>"usage",_=>null };
        decimal? used=remaining is { } r && limit is { } l?Math.Max(0,l-r):period is not null?key.Get(period).Number():null;
        if(remaining is null && used is not null && key.Get("include_byok_in_limit").True())
        {
            var byok=period is not null?key.Get("byok_"+period).Number():null;
            used=byok is not null?used+byok:null; // Cannot infer a combined limit with an absent BYOK bucket.
        }
        if(limit is >= 0)
            windows.Add(new("key_limit",reset??"lifetime",limit>0 && used is { } u?Math.Clamp(u/limit.Value*100,0,100):null,
                null,"key",used,limit,remaining??(used is { } value?Math.Max(0,limit.Value-value):null),"USD"));
        foreach(var field in new[]{"usage_daily","usage_weekly","usage_monthly","usage","byok_usage"})
            if(key.Get(field).Number() is { } spend) money.Add(new(field,field,spend,Scope:"key"));
        if(credits.Get("total_credits").Number() is { } purchased && credits.Get("total_usage").Number() is { } spent)
            money.Add(new("balance","balance",purchased-spent));
        return new(account.Id,account.Provider,now,windows,money,
            key.Get("is_management_key").True()?"management":key.Get("is_free_tier").True()?"free":"pay_as_you_go");
    }
}
