using AiUsageViewer.Core;

namespace AiUsageViewer.Application;

public enum UsageAlertKind { Usage, Reset, LowBalance, Pace }
public sealed record UsageAlert(UsageAlertKind Kind,string AccountLabel,string Metric,decimal? Value=null,string? Currency=null,DateTimeOffset? ResetsAt=null,DateTimeOffset? RunsOutAt=null);
// PaceNotified is a later addition; older saved state reads it as false.
public sealed record AlertWindow(decimal? Used,DateTimeOffset? ResetsAt,decimal? NotifiedThreshold,bool PaceNotified=false);
public sealed record AlertAccount(AccountProfile Account,DateTimeOffset ObservedAt,
    Dictionary<string,AlertWindow> Windows,Dictionary<string,decimal> Balances);

// Persist observations, not guessed reset timers. Only new successful snapshots can raise alerts.
public sealed class NotificationEvaluator
{
    public Dictionary<string,AlertAccount> State { get; }
    public NotificationEvaluator(Dictionary<string,AlertAccount>? state=null)=>State=state??[];
    public IReadOnlyList<UsageAlert> Observe(AccountStatus status,AppSettings settings)
    {
        var alerts=new List<UsageAlert>();
        if(status.State is not (ConnectionState.Ready or ConnectionState.Partial)||status.LastGood is not { } snapshot) return alerts;
        State.TryGetValue(status.Account.Id,out var previous);
        if(previous is not null&&!previous.Account.SameConnection(status.Account)) previous=null;
        if(previous?.ObservedAt>=snapshot.ObservedAt) return alerts;
        var windows=previous is null?new Dictionary<string,AlertWindow>():new(previous.Windows);
        var balances=previous is null?new Dictionary<string,decimal>():new(previous.Balances);
        foreach(var window in snapshot.Windows)
        {
            if(snapshot.Provider==ProviderKind.OpenRouter&&snapshot.Sources?.Any(s=>s.Id=="key"&&s.State!=ConnectionState.Ready)==true) continue;
            var old=previous?.Windows.GetValueOrDefault(window.Id);
            var newPeriod=old?.ResetsAt is { } reset&&reset<=snapshot.ObservedAt&&window.ResetsAt>reset;
            var notified=newPeriod||old?.Used<settings.NotifyUsagePercent?null:old?.NotifiedThreshold;
            if(settings.NotificationsEnabled&&window.UsedPercent is { } used&&used>=settings.NotifyUsagePercent&&notified!=settings.NotifyUsagePercent)
            {
                alerts.Add(new(UsageAlertKind.Usage,status.Account.Label,window.Label,used,ResetsAt:window.ResetsAt));notified=settings.NotifyUsagePercent;
            }
            if(settings.NotificationsEnabled&&settings.NotifyResets&&old?.ResetsAt is { } oldReset&&oldReset<=snapshot.ObservedAt&&
                window.ResetsAt>oldReset&&window.UsedPercent<old.Used)
                alerts.Add(new(UsageAlertKind.Reset,status.Account.Label,window.Label,window.UsedPercent));
            // Pace: once per quota window, when the current rate would use it up before the reset.
            // It is an early warning, so it stays quiet once the usage threshold alert applies.
            var paceNotified=!newPeriod&&old?.PaceNotified==true;
            if(settings.NotificationsEnabled&&settings.NotifyPace&&!paceNotified&&window.UsedPercent<settings.NotifyUsagePercent&&
                QuotaPace.Read(window,snapshot.ObservedAt) is { RunsOutAt: { } runsOut } reading)
            {
                alerts.Add(new(UsageAlertKind.Pace,status.Account.Label,window.Label,(decimal)reading.Used,ResetsAt:window.ResetsAt,RunsOutAt:runsOut));paceNotified=true;
            }
            windows[window.Id]=new(window.UsedPercent,window.ResetsAt,notified,paceNotified);
        }
        foreach(var balance in snapshot.Money.Where(m=>m.Id=="balance"&&m.Currency=="USD"))
        {
            if(snapshot.Sources?.Any(s=>s.Id=="credits"&&s.State!=ConnectionState.Ready)==true) continue;
            var key=balance.Id+":"+balance.Currency;
            if(settings.NotificationsEnabled&&balance.Value<=settings.NotifyLowBalance&&
                (previous is null||!previous.Balances.TryGetValue(key,out var old)||old>settings.NotifyLowBalance))
                alerts.Add(new(UsageAlertKind.LowBalance,status.Account.Label,balance.Label,balance.Value,balance.Currency));
            balances[key]=balance.Value;
        }
        State[status.Account.Id]=new(status.Account,snapshot.ObservedAt,windows,balances);
        return alerts;
    }
}
