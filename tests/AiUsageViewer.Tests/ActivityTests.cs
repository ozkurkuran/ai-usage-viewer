using System.Net;
using System.Text.Json;
using AiUsageViewer.Core;
using AiUsageViewer.Infrastructure.Providers;
using AiUsageViewer.Infrastructure.Storage;
using Microsoft.Data.Sqlite;

namespace AiUsageViewer.Tests;

public sealed class ActivityTests : IDisposable
{
    private readonly string directory=Path.Combine(Path.GetTempPath(),"AiUsageViewer-tests",Guid.NewGuid().ToString("N"));
    private static readonly DateOnly Today=new(2026,9,27);
    private static AccountProfile Account(string id="a",string key="key")=>new() { Id=id,Provider=ProviderKind.OpenRouter,SecretReference=key };
    public ActivityTests()=>Directory.CreateDirectory(directory);
    public void Dispose() { SqliteConnection.ClearAllPools();Directory.Delete(directory,true); }
    private async Task<ActivityStore> Store()
    {
        var store=new ActivityStore(Path.Combine(directory,"usage.db"));await store.InitializeAsync();return store;
    }
    private static string JsonRow(string day="2026-09-26",string usage="0.015")=>$$"""
        {"date":"{{day}}","model":"openai/gpt-4.1","endpoint_id":"endpoint","prompt_tokens":50,
        "completion_tokens":125,"reasoning_tokens":25,"requests":5,"usage":{{usage}},"byok_usage_inference":0.012}
        """;
    private static ActivityResult Parse(params string[] rows)=>OpenRouterActivity.Parse("a",JsonDocument.Parse("{\"data\":["+string.Join(',',rows)+"]}").RootElement,Today);
    [Fact] public void CompletedUtcDaysAreIsolatedFromCurrentAndExpiredDays()
    {
        var result=Parse(JsonRow(),JsonRow("2026-09-27"),JsonRow("2026-08-27"));
        var row=Assert.Single(result.Rows!);Assert.Equal(Today.AddDays(-1),row.Day);
        Assert.Equal(50,row.Input);Assert.Equal(125,row.Output);Assert.Equal(25,row.Reasoning);
        Assert.Equal(.015m,row.Spend);Assert.Equal(.012m,row.ByokSpend);
    }
    [Theory][InlineData("null")][InlineData("-1")]
    public void InvalidSpendCannotOverwriteCompleteHistory(string usage)
    { Assert.Equal(ConnectionState.Unavailable,Parse(JsonRow(usage:usage)).State); }
    [Fact] public void MissingCountsAreUnknownNotZero()
    { Assert.Equal(ConnectionState.Unavailable,Parse(JsonRow().Replace("\"requests\":5,","")).State); }
    [Fact] public async Task ManagementPermissionFailureIsSeparateFromQuotaConnection()
    {
        using var http=new ProviderHttp(new Forbidden());
        var result=await new OpenRouterActivity(http,new Secrets()).FetchActivityAsync(Account(),default);
        Assert.Equal(ConnectionState.Unsupported,result.State);Assert.Equal("history_management_key_required",result.MessageCode);Assert.Null(result.Rows);
    }
    [Fact] public async Task RefreshReplacesDaysRetainsOlderHistoryAndSeparatesAccounts()
    {
        var store=await Store();var clock=new Clock();var now=clock.GetUtcNow();
        var old=new ActivityRow("a",Today.AddDays(-40),"old","e",1,2,0,1,1,0);
        var status=new ActivityStatus("a","key",ConnectionState.Ready,now,now,now,Today.AddDays(-50),Today.AddDays(-1),null);
        await store.SaveAsync(status,[old],false);
        var provider=new Fake((a,_)=>Task.FromResult(new ActivityResult(ConnectionState.Ready,
            [new(a.Id,Today.AddDays(-1),"current","e",10,20,5,1,2,1)])));
        var sync=new ActivitySynchronizer(provider,store,clock);
        await sync.RefreshAsync([Account(),Account("b")],true,default);
        clock.Advance(TimeSpan.FromMinutes(16));await sync.RefreshAsync([Account()],true,default);
        Assert.Equal(2,(await store.ReadAsync("a")).Count);Assert.Single(await store.ReadAsync("b"));
        Assert.Equal(3,(await store.ReadAsync("a")).Sum(r=>r.Spend));
        provider.Fetch=(_,_)=>Task.FromResult(new ActivityResult(ConnectionState.Unavailable));
        clock.Advance(TimeSpan.FromMinutes(16));await sync.RefreshAsync([Account()],true,default);
        Assert.Equal(2,(await store.ReadAsync("a")).Count);Assert.NotNull((await store.StatusAsync("a"))!.LastGood);
        await sync.RefreshAsync([Account(key:"replacement")],true,default);
        Assert.Empty(await store.ReadAsync("a"));Assert.Null((await store.StatusAsync("a"))!.LastGood);
        Assert.Single(await store.ReadAsync("b"));
    }
    [Fact] public async Task ActivityRespectsRetryAfterAndCancellationKeepsHistory()
    {
        var store=await Store();var clock=new Clock();int calls=0;
        var provider=new Fake((_,_)=> { calls++;return Task.FromResult(new ActivityResult(ConnectionState.RateLimited,RetryAfter:TimeSpan.FromHours(1))); });
        var sync=new ActivitySynchronizer(provider,store,clock);
        await sync.RefreshAsync([Account()],true,default);clock.Advance(TimeSpan.FromMinutes(20));
        await sync.RefreshAsync([Account()],true,default);Assert.Equal(1,calls);
        var before=await store.StatusAsync("a");clock.Advance(TimeSpan.FromHours(1));
        using var cancellation=new CancellationTokenSource();
        provider.Fetch=(_,ct)=> { cancellation.Cancel();return Task.FromCanceled<ActivityResult>(ct); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>sync.RefreshAsync([Account()],true,cancellation.Token));
        Assert.Equal(before,await store.StatusAsync("a"));
    }
    [Fact] public async Task QuotaCacheDoesNotSurviveConnectionChanges()
    {
        var db=new UsageDatabase(Path.Combine(directory,"usage.db"));await db.InitializeAsync();var account=Account();
        var snapshot=new ProviderSnapshot(account.Id,account.Provider,DateTimeOffset.UtcNow,[],[new("balance","balance",5)]);
        await db.SaveStatusAsync(new(account,snapshot,null,null,ConnectionState.Ready,null));
        Assert.NotNull(await db.LoadStatusAsync(account with { Label="Renamed" }));
        Assert.Null(await db.LoadStatusAsync(account with { SecretReference="new-key" }));
        Assert.Null(await db.LoadStatusAsync(account with { ProfileDirectory="another" }));
        Assert.Null(await db.LoadStatusAsync(account with { Executable="another.exe" }));
    }
    private sealed class Clock:TimeProvider
    { private DateTimeOffset now=new(2026,9,27,12,0,0,TimeSpan.Zero);public override DateTimeOffset GetUtcNow()=>now;public void Advance(TimeSpan amount)=>now+=amount; }
    private sealed class Fake(Func<AccountProfile,CancellationToken,Task<ActivityResult>> fetch):IActivityProvider
    { public Func<AccountProfile,CancellationToken,Task<ActivityResult>> Fetch { get;set; }=fetch;public Task<ActivityResult> FetchActivityAsync(AccountProfile account,CancellationToken ct)=>Fetch(account,ct); }
    private sealed class Forbidden:HttpMessageHandler
    { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)=>Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)); }
    private sealed class Secrets:ISecretStore
    { public string? Read(string reference)=>"synthetic";public void Write(string reference,string value){}public void Delete(string reference){} }
}
