using System.Net;
using System.Text.Json;
using AiUsageViewer.Core;
using AiUsageViewer.Infrastructure.Providers;

namespace AiUsageViewer.Tests;

public class ProviderTests
{
    private static AccountProfile Account(ProviderKind provider)=>new() { Id="test",Provider=provider,SecretReference="test-key" };
    private static JsonElement Json(string data)=>JsonDocument.Parse(data).RootElement.Clone();

    [Fact] public void ClaudeSupportsDynamicWindowsAndDoesNotInventMissingPercentages()
    {
        var result=ClaudeProvider.Parse(Account(ProviderKind.Claude),Json("""
            {"five_hour":{"utilization":38,"resets_at":"2026-09-27T11:00:00Z"},
             "seven_day":{"utilization":null,"resets_at":"2026-09-30T11:00:00Z"},
             "seven_day_sonnet":{"utilization":14},"extra_usage":{"is_enabled":false}}
            """),DateTimeOffset.UtcNow);
        Assert.Equal(ConnectionState.Ready,result.State);
        Assert.Equal(3,result.Snapshot!.Windows.Count);
        Assert.Null(result.Snapshot.Windows[1].UsedPercent);
        Assert.Equal(62,result.Snapshot.Windows[0].RemainingPercent);
    }
    [Fact] public void EmptyQuotaPayloadIsUnsupportedRatherThanUnused()=>Assert.Equal(ConnectionState.Unsupported,
        ClaudeProvider.Parse(Account(ProviderKind.Claude),Json("{}"),DateTimeOffset.UtcNow).State);

    [Fact] public void ClaudeStructuredLimitsMatchOfficialScopesWithoutCompatibilityDuplicates()
    {
        var result=ClaudeProvider.Parse(Account(ProviderKind.Claude),Json("""
            {"five_hour":{"utilization":8},"seven_day":{"utilization":10},"internal_promo":{"utilization":0},
             "limits":[{"kind":"session","group":"session","percent":31,"resets_at":"2026-09-28T00:30:00+03:00"},
             {"kind":"weekly_all","group":"weekly","percent":44,"is_active":false},
             {"kind":"weekly_scoped","group":"weekly","percent":9,"scope":{"model":{"id":null,"display_name":"Example model"},"surface":null}}]}
            """),DateTimeOffset.UtcNow);
        Assert.Equal(3,result.Snapshot!.Windows.Count);
        Assert.Equal("five_hour",result.Snapshot.Windows[0].Id);Assert.Equal(31,result.Snapshot.Windows[0].UsedPercent);
        Assert.Equal(DateTimeOffset.Parse("2026-09-27T21:30:00Z"),result.Snapshot.Windows[0].ResetsAt);
        Assert.Equal(44,result.Snapshot.Windows[1].UsedPercent); // inactive means not currently binding, not absent
        Assert.Equal("Example model",result.Snapshot.Windows[2].Scope);Assert.Equal(10080,result.Snapshot.Windows[2].WindowMinutes);
    }
    [Fact] public void ClaudeStructuredLimitsKeepUnknownPercentAndDistinctScopes()
    {
        var result=ClaudeProvider.Parse(Account(ProviderKind.Claude),Json("""
            {"limits":[null,{},
             {"kind":"weekly_scoped","group":"weekly","percent":null,"scope":{"model":{"id":"m1","display_name":"Shared name"},"surface":"api"}},
             {"kind":"weekly_scoped","group":"weekly","percent":12,"scope":{"model":{"id":"m1","display_name":"Shared name"},"surface":"chat"}},
             {"kind":"weekly_scoped","group":"weekly","percent":13,"scope":{"model":{"id":"m1","display_name":"Shared name"},"surface":"chat"}}]}
            """),DateTimeOffset.UtcNow);
        Assert.Equal(2,result.Snapshot!.Windows.Count);Assert.Null(result.Snapshot.Windows[0].UsedPercent);
        Assert.Equal(13,result.Snapshot.Windows[1].UsedPercent);Assert.NotEqual(result.Snapshot.Windows[0].Id,result.Snapshot.Windows[1].Id);
    }
    [Theory][InlineData("[]")][InlineData("[{},null]")][InlineData("null")]
    public void ClaudeFallsBackWhenStructuredLimitsAreNotUsable(string limits)
    {
        var result=ClaudeProvider.Parse(Account(ProviderKind.Claude),Json("{\"five_hour\":{\"utilization\":17},\"limits\":"+limits+"}"),DateTimeOffset.UtcNow);
        Assert.Equal(17,Assert.Single(result.Snapshot!.Windows).UsedPercent);
    }

    [Fact] public void CodexReadsPrimarySecondaryAndAdditionalModelWindows()
    {
        var result=CodexProvider.Parse(Account(ProviderKind.Codex),Json("""
            {"rateLimits":{"limitId":"codex","planType":"plus","primary":{"usedPercent":21,"windowDurationMins":300,"resetsAt":1790528400}},
            "rateLimitsByLimitId":{"codex":{"primary":{"usedPercent":21}},"spark":{"secondary":{"usedPercent":67,"windowDurationMins":10080}}}}
            """),DateTimeOffset.UtcNow);
        Assert.Equal(2,result.Snapshot!.Windows.Count);
        Assert.Equal(300,result.Snapshot.Windows[0].WindowMinutes);
        Assert.Equal("spark",result.Snapshot.Windows[1].Scope);
    }
    [Fact] public async Task CodexRpcOnlyInitializesAndReadsAccountAndLimits()
    {
        using var input=new StringWriter();
        using var output=new StringReader("""
            {"id":1,"result":{}}
            {"method":"irrelevant_notification","params":{}}
            {"id":2,"result":{"account":{"type":"chatgpt","email":"unused@example.test"}}}
            {"id":3,"result":{"rateLimits":{"primary":{"usedPercent":42}}}}
            """);
        var result=await CodexProvider.ExchangeAsync(output,input,Account(ProviderKind.Codex),default);
        Assert.Equal(ConnectionState.Ready,result.State);
        var methods=input.ToString().Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(line=>Json(line).GetProperty("method").GetString()).ToList();
        Assert.Equal(["initialize","initialized","account/read","account/rateLimits/read"],methods);
        Assert.DoesNotContain("unused@example.test",JsonSerializer.Serialize(result));
    }
    [Fact] public void OpenRouterMonthlyLimitUsesRemainingInsteadOfLifetimeSpend()
    {
        var snapshot=OpenRouterProvider.Parse(Account(ProviderKind.OpenRouter),Json("""
            {"data":{"usage":250,"usage_monthly":20,"limit":100,"limit_remaining":80,"limit_reset":"monthly"}}
            """),default,DateTimeOffset.UtcNow);
        var window=Assert.Single(snapshot.Windows);
        Assert.Equal(20,window.UsedPercent); Assert.Equal(80,window.RemainingPercent); Assert.Equal(20,window.Used);
        Assert.Equal(250,snapshot.Money.Single(x=>x.Id=="usage").Value);
    }
    [Fact] public void OpenRouterByokIsOnlyIncludedWhenRequiredByLimit()
    {
        var snapshot=OpenRouterProvider.Parse(Account(ProviderKind.OpenRouter),Json("""
            {"data":{"usage":250,"usage_monthly":20,"byok_usage_monthly":5,"include_byok_in_limit":true,"limit":100,"limit_reset":"monthly"}}
            """),default,DateTimeOffset.UtcNow);
        Assert.Equal(25,Assert.Single(snapshot.Windows).UsedPercent);
    }
    [Fact] public async Task StandardOpenRouterKeyKeepsUsefulDataWhenCreditsAreForbidden()
    {
        using var http=new ProviderHttp(new FakeHandler(request=>request.RequestUri!.AbsolutePath.EndsWith("/key")
            ?new(HttpStatusCode.OK) { Content=new StringContent("{\"data\":{\"usage_daily\":2.5}}") }
            :new(HttpStatusCode.Forbidden)));
        var result=await new OpenRouterProvider(http,new MemorySecrets()).FetchAsync(Account(ProviderKind.OpenRouter),default);
        Assert.Equal(ConnectionState.Partial,result.State);
        Assert.Equal(2.5m,Assert.Single(result.Snapshot!.Money).Value);
        Assert.Equal("credits_permission_required",result.MessageCode);
    }
    [Theory]
    [InlineData(401,ConnectionState.SignInRequired)]
    [InlineData(403,ConnectionState.SignInRequired)]
    [InlineData(429,ConnectionState.RateLimited)]
    [InlineData(500,ConnectionState.Unavailable)]
    public async Task HttpErrorsAreClassifiedWithoutLeakingResponse(int status,ConnectionState expected)
    {
        using var http=new ProviderHttp(new FakeHandler(_=>new((HttpStatusCode)status) { Content=new StringContent("secret response") }));
        var result=await http.GetAsync("https://example.test/","test-secret",default);
        Assert.Equal(expected,result.Failure.State);
        Assert.Equal(JsonValueKind.Undefined,result.Data.ValueKind);
        Assert.DoesNotContain("secret",JsonSerializer.Serialize(result.Failure));
    }
    [Fact] public async Task InvalidJsonIsReported()
    {
        using var http=new ProviderHttp(new FakeHandler(_=>new(HttpStatusCode.OK) { Content=new StringContent("not json") }));
        Assert.Equal("invalid_response",(await http.GetAsync("https://example.test/","test",default)).Error);
    }
    [Fact] public void PartialOpenRouterRefreshKeepsFailedSourcesWithTheirOriginalAge()
    {
        var account=Account(ProviderKind.OpenRouter);var old=DateTimeOffset.UtcNow.AddHours(-1);var now=old.AddHours(1);
        var previous=new ProviderSnapshot(account.Id,account.Provider,old,[new("key_limit","monthly",20,null)],
            [new("usage_daily","usage_daily",2,Scope:"key"),new("balance","balance",10)],Sources:[new("key",ConnectionState.Ready,old),new("credits",ConnectionState.Ready,old)]);
        var fresh=new ProviderSnapshot(account.Id,account.Provider,now,[new("key_limit","monthly",25,null)],
            [new("usage_daily","usage_daily",3,Scope:"key")],Sources:[new("key",ConnectionState.Ready,now),new("credits",ConnectionState.SignInRequired,null)]);
        var retained=fresh.RetainUnavailableSources(previous);
        Assert.Equal(25,Assert.Single(retained.Windows).UsedPercent);Assert.Equal(3,retained.Money.Single(m=>m.Scope=="key").Value);
        Assert.Equal(10,retained.Money.Single(m=>m.Id=="balance").Value);Assert.Equal(old,retained.Sources!.Single(s=>s.Id=="credits").LastSuccess);
        Assert.Equal(now,retained.Sources!.Single(s=>s.Id=="key").LastSuccess);
        Assert.Single((fresh with { AccountId="another" }).RetainUnavailableSources(previous).Money);
    }
    [Fact] public async Task OpenRouterPartial429KeepsValidKeyAndHonorsRetryAfter()
    {
        using var http=new ProviderHttp(new FakeHandler(request=> {
            if(request.RequestUri!.AbsolutePath.EndsWith("/key")) return new(HttpStatusCode.OK) { Content=new StringContent("{\"data\":{\"usage_daily\":2.5}}") };
            var response=new HttpResponseMessage(HttpStatusCode.TooManyRequests);response.Headers.RetryAfter=new(TimeSpan.FromHours(1));return response;
        }));
        var result=await new OpenRouterProvider(http,new MemorySecrets()).FetchAsync(Account(ProviderKind.OpenRouter),default);
        Assert.Equal(ConnectionState.RateLimited,result.State);Assert.Equal(TimeSpan.FromHours(1),result.RetryAfter);
        Assert.Equal(2.5m,Assert.Single(result.Snapshot!.Money).Value);
        Assert.Equal(ConnectionState.Ready,result.Snapshot.Sources!.Single(s=>s.Id=="key").State);
    }
    [Fact] public async Task EmptyOpenRouterDataIsUnknownNotAReadyZeroBalance()
    {
        using var http=new ProviderHttp(new FakeHandler(_=>new(HttpStatusCode.OK) { Content=new StringContent("{\"data\":{}}") }));
        var result=await new OpenRouterProvider(http,new MemorySecrets()).FetchAsync(Account(ProviderKind.OpenRouter),default);
        Assert.Equal(ConnectionState.Unavailable,result.State);Assert.Empty(result.Snapshot!.Money);
    }
    [Theory][InlineData("key\r\nInjected: value")][InlineData("bad key")]
    public async Task InvalidCredentialsDoNotReachTheNetworkOrCrashTheRefresh(string secret)
    {
        int calls=0;using var http=new ProviderHttp(new FakeHandler(_=> { calls++;return new(HttpStatusCode.OK); }));
        var result=await http.GetAsync("https://example.test/",secret,default);
        Assert.Equal(ConnectionState.SignInRequired,result.Failure.State);Assert.Equal(0,calls);
        Assert.DoesNotContain(secret,JsonSerializer.Serialize(result.Failure));
    }
    private sealed class FakeHandler(Func<HttpRequestMessage,HttpResponseMessage> respond):HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken cancellationToken)=>Task.FromResult(respond(request));
    }
    private sealed class MemorySecrets:ISecretStore
    {
        public string? Read(string reference)=>"synthetic-test-key";
        public void Write(string reference,string value){}
        public void Delete(string reference){}
    }
}
