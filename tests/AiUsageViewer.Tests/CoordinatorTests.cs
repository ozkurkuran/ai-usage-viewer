using System.Collections.Concurrent;
using AiUsageViewer.Application;
using AiUsageViewer.Core;

namespace AiUsageViewer.Tests;

public class CoordinatorTests
{
    private static AccountProfile Account(string id="account")=>new() { Id=id,Provider=ProviderKind.Claude,Label=id };
    private static ProviderResult Ready(AccountProfile account,DateTimeOffset time)=>new(ConnectionState.Ready,
        new(account.Id,account.Provider,time,[new("window","window",42,null)],[]));

    [Fact] public async Task FailurePreservesSuccessfulObservationAndManualRefreshHonorsMinimumGap()
    {
        var clock=new TestClock();var account=Account();var calls=0;
        var provider=new FakeProvider((a,_)=>Task.FromResult(++calls==1?Ready(a,clock.GetUtcNow()):new ProviderResult(ConnectionState.Unavailable,MessageCode:"network_unavailable")));
        var coordinator=new QuotaCoordinator([provider],new MemoryStore(),clock);
        await coordinator.RefreshAsync([account]);var original=Assert.Single(coordinator.Statuses).LastGood;
        clock.Advance(TimeSpan.FromSeconds(20));await coordinator.RefreshAsync([account],manual:true);
        var failed=Assert.Single(coordinator.Statuses);
        Assert.Same(original,failed.LastGood);Assert.True(failed.IsStale(clock.GetUtcNow()));Assert.Equal(ConnectionState.Unavailable,failed.State);
        await coordinator.RefreshAsync([account],manual:true);Assert.Equal(2,calls);
    }
    [Fact] public async Task RetryAfterCannotBeBypassedByManualRefresh()
    {
        var clock=new TestClock();var calls=0;
        var provider=new FakeProvider((_,_)=> { calls++;return Task.FromResult(new ProviderResult(ConnectionState.RateLimited,RetryAfter:TimeSpan.FromHours(1))); });
        var coordinator=new QuotaCoordinator([provider],new MemoryStore(),clock);
        await coordinator.RefreshAsync([Account()]);clock.Advance(TimeSpan.FromMinutes(10));
        await coordinator.RefreshAsync([Account()],manual:true);Assert.Equal(1,calls);
        clock.Advance(TimeSpan.FromHours(1));await coordinator.RefreshAsync([Account()],manual:true);Assert.Equal(2,calls);
    }
    [Fact] public async Task DuplicateAccountsNeverOverlapAndGlobalConcurrencyIsBounded()
    {
        int active=0,maximum=0,calls=0;
        var provider=new FakeProvider(async(a,ct)=> {
            calls++;var current=Interlocked.Increment(ref active);maximum=Math.Max(maximum,current);
            await Task.Delay(30,ct);Interlocked.Decrement(ref active);return Ready(a,DateTimeOffset.UtcNow);
        });
        var coordinator=new QuotaCoordinator([provider],new MemoryStore());
        var accounts=Enumerable.Range(0,7).Select(i=>Account("a"+i)).ToList();accounts.Add(accounts[0]);
        await coordinator.RefreshAsync(accounts);
        Assert.Equal(7,calls);Assert.InRange(maximum,1,3);Assert.Equal(7,coordinator.Statuses.Count);
    }
    [Fact] public async Task CancellationDoesNotReplaceCachedSuccessWithAnError()
    {
        var clock=new TestClock();var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var calls=0;
        var provider=new FakeProvider(async(a,ct)=> {
            if(++calls==1) return Ready(a,clock.GetUtcNow());entered.SetResult();await Task.Delay(Timeout.Infinite,ct);return Ready(a,clock.GetUtcNow());
        });
        var coordinator=new QuotaCoordinator([provider],new MemoryStore(),clock);
        await coordinator.RefreshAsync([Account()]);var previous=Assert.Single(coordinator.Statuses);
        clock.Advance(TimeSpan.FromMinutes(6));using var cancellation=new CancellationTokenSource();
        var refresh=coordinator.RefreshAsync([Account()],ct:cancellation.Token);await entered.Task;cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>refresh);
        Assert.Equal(previous,Assert.Single(coordinator.Statuses));
    }
    private sealed class TestClock:TimeProvider
    {
        private DateTimeOffset now=DateTimeOffset.Parse("2026-09-27T12:00:00Z");
        public override DateTimeOffset GetUtcNow()=>now;
        public void Advance(TimeSpan amount)=>now+=amount;
    }
    private sealed class FakeProvider(Func<AccountProfile,CancellationToken,Task<ProviderResult>> fetch):IQuotaProvider
    {
        public ProviderKind Kind=>ProviderKind.Claude;
        public ProviderCapabilities Capabilities=>new(true,true,false,false,false);
        public Task<ProviderResult> FetchAsync(AccountProfile account,CancellationToken cancellationToken)=>fetch(account,cancellationToken);
    }
    private sealed class MemoryStore:IUsageStore
    {
        private readonly ConcurrentDictionary<string,AccountStatus> statuses=new();
        public Task SaveStatusAsync(AccountStatus status,CancellationToken cancellationToken=default) { statuses[status.Account.Id]=status;return Task.CompletedTask; }
        public Task<AccountStatus?> LoadStatusAsync(AccountProfile account,CancellationToken cancellationToken=default)=>Task.FromResult(statuses.GetValueOrDefault(account.Id));
        public Task<UsageSummary> SummarizeAsync(UsageFilter filter,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public Task<IReadOnlyList<UsageGroup>> GroupAsync(UsageFilter filter,string dimension,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public Task<IReadOnlyList<DailyUsage>> DailyAsync(UsageFilter filter,TimeZoneInfo zone,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
        public Task<IReadOnlyList<UsageEvent>> ReadEventsAsync(UsageFilter filter,CancellationToken cancellationToken=default)=>throw new NotSupportedException();
    }
}
