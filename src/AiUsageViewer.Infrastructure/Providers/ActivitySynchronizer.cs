using AiUsageViewer.Core;
using AiUsageViewer.Infrastructure.Storage;

namespace AiUsageViewer.Infrastructure.Providers;
public sealed class ActivitySynchronizer(IActivityProvider provider,ActivityStore store,TimeProvider? timeProvider=null)
{
    private readonly TimeProvider clock=timeProvider??TimeProvider.System;
    private readonly SemaphoreSlim gate=new(1,1);
    public async Task RefreshAsync(IEnumerable<AccountProfile> accounts,bool manual,CancellationToken ct)
    {
        if(!await gate.WaitAsync(0,ct)) return;
        try
        {
            foreach(var account in accounts.Where(a=>a.Enabled&&a.Provider==ProviderKind.OpenRouter))
            {
                var previous=await store.StatusAsync(account.Id,ct);var now=clock.GetUtcNow();
                var key=account.SecretReference??"";var changed=previous is not null&&previous.ConnectionKey!=key;
                if(changed) previous=null;
                if(previous?.NextAttempt>now&&(!manual||previous.State==ConnectionState.RateLimited||now-previous.LastAttempt<TimeSpan.FromSeconds(15))) continue;
                ActivityResult result;
                try { result=await provider.FetchActivityAsync(account,ct); }
                catch(Exception e) when(e is IOException or System.Security.Cryptography.CryptographicException or UnauthorizedAccessException)
                { result=new(ConnectionState.Unavailable,MessageCode:"local_data_unavailable"); }
                now=clock.GetUtcNow();var today=DateOnly.FromDateTime(now.UtcDateTime);
                var good=result.State==ConnectionState.Ready;
                var delay=result.RetryAfter??TimeSpan.FromMinutes(15);
                if(delay<TimeSpan.FromSeconds(15)) delay=TimeSpan.FromSeconds(15);
                if(result.State==ConnectionState.RateLimited&&delay<TimeSpan.FromMinutes(5)) delay=TimeSpan.FromMinutes(5);
                var status=new ActivityStatus(account.Id,key,result.State,now,now+delay,good?now:previous?.LastGood,
                    good?today.AddDays(-30):previous?.From,good?today.AddDays(-1):previous?.Through,result.MessageCode);
                await store.SaveAsync(status,good?result.Rows:null,changed,ct);
            }
        }
        finally { gate.Release(); }
    }
}
