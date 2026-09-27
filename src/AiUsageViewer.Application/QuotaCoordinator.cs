using System.Collections.Concurrent;
using AiUsageViewer.Core;

namespace AiUsageViewer.Application;

public sealed class QuotaCoordinator(IEnumerable<IQuotaProvider> providers,IUsageStore store,TimeProvider? clock=null)
{
    private readonly Dictionary<ProviderKind,IQuotaProvider> providers=providers.ToDictionary(x=>x.Kind);
    private readonly TimeProvider clock=clock??TimeProvider.System;
    private readonly ConcurrentDictionary<string,AccountStatus> statuses=new();
    private readonly ConcurrentDictionary<string,SemaphoreSlim> accountLocks=new();
    private readonly ConcurrentDictionary<string,int> failures=new();
    private readonly SemaphoreSlim concurrency=new(3,3);
    public event Action<AccountStatus>? Changed;
    public IReadOnlyList<AccountStatus> Statuses=>statuses.Values.ToList();

    public async Task LoadAsync(IEnumerable<AccountProfile> accounts,CancellationToken ct=default)
    {
        var active=accounts.ToList();
        foreach(var id in statuses.Keys.Where(id=>!active.Any(a=>a.Id==id))) statuses.TryRemove(id,out _);
        foreach(var account in active)
        {
            var saved=await store.LoadStatusAsync(account,ct);
            if(saved is not null&&saved.Account.SameConnection(account)) statuses[account.Id]=saved with { Account=account };
            else { statuses.TryRemove(account.Id,out _);failures.TryRemove(account.Id,out _); }
        }
    }

    public Task RefreshAsync(IEnumerable<AccountProfile> accounts,bool manual=false,CancellationToken ct=default)
        =>Task.WhenAll(accounts.Where(a=>a.Enabled).Select(a=>RefreshAccountAsync(a,manual,ct)));

    private async Task RefreshAccountAsync(AccountProfile account,bool manual,CancellationToken ct)
    {
        var gate=accountLocks.GetOrAdd(account.Id,_=>new(1,1));
        if(!await gate.WaitAsync(0,ct)) return;
        try
        {
            var now=clock.GetUtcNow();
            statuses.TryGetValue(account.Id,out var previous);
            if(previous is not null&&!previous.Account.SameConnection(account)) { previous=null;failures.TryRemove(account.Id,out _); }
            if(previous?.NextAttempt>now && (!manual || previous.State==ConnectionState.RateLimited || now-previous.LastAttempt<TimeSpan.FromSeconds(15))) return;
            await concurrency.WaitAsync(ct);
            ProviderResult result;
            try
            {
                using var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct);
                deadline.CancelAfter(TimeSpan.FromSeconds(40));
                try
                {
                    result=providers.TryGetValue(account.Provider,out var provider)
                        ?await provider.FetchAsync(account,deadline.Token).WaitAsync(deadline.Token)
                        :new(ConnectionState.Unsupported,MessageCode:"provider_unsupported");
                }
                catch(OperationCanceledException) when(!ct.IsCancellationRequested) { result=new(ConnectionState.Unavailable,MessageCode:"timeout"); }
                catch(Exception e) when(e is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
                { result=new(ConnectionState.Unavailable,MessageCode:"local_data_unavailable"); }
            }
            finally { concurrency.Release(); }
            now=clock.GetUtcNow();
            var attemptFailures=result.State is ConnectionState.Ready or ConnectionState.Partial?0:failures.AddOrUpdate(account.Id,1,(_,n)=>Math.Min(n+1,8));
            if(attemptFailures==0) failures[account.Id]=0;
            var delay=result.RetryAfter??(attemptFailures==0?TimeSpan.FromMinutes(5):TimeSpan.FromSeconds(Math.Min(1800,30*Math.Pow(2,attemptFailures-1))));
            if(delay<TimeSpan.FromSeconds(15)) delay=TimeSpan.FromSeconds(15);
            if(result.State==ConnectionState.RateLimited && delay<TimeSpan.FromMinutes(5)) delay=TimeSpan.FromMinutes(5);
            var status=new AccountStatus(account,result.Snapshot?.RetainUnavailableSources(previous?.LastGood)??previous?.LastGood,now,now+delay,result.State,result.MessageCode);
            statuses[account.Id]=status;
            await store.SaveStatusAsync(status,ct);
            Changed?.Invoke(status);
        }
        finally { gate.Release(); }
    }
}
