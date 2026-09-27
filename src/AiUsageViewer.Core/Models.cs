namespace AiUsageViewer.Core;

public enum ProviderKind { Claude, Codex, OpenRouter }
public enum ConnectionState { Ready, Partial, SignInRequired, RateLimited, Unavailable, Unsupported }
public enum IdentityQuality { Verified, Fallback, UncertainFork }

// All buckets are disjoint; reasoning is a subset of Output and never added to Total.
public sealed record TokenUsage(long Input = 0, long Output = 0, long CacheRead = 0,
    long CacheWrite5m = 0, long CacheWrite1h = 0, long Reasoning = 0)
{
    public long Total => checked(Input + Output + CacheRead + CacheWrite5m + CacheWrite1h);
    public static TokenUsage operator +(TokenUsage a, TokenUsage b) => new(
        checked(a.Input + b.Input), checked(a.Output + b.Output), checked(a.CacheRead + b.CacheRead),
        checked(a.CacheWrite5m + b.CacheWrite5m), checked(a.CacheWrite1h + b.CacheWrite1h),
        checked(a.Reasoning + b.Reasoning));
    public TokenUsage Difference(TokenUsage previous) => new(
        Math.Max(0, Input - previous.Input), Math.Max(0, Output - previous.Output),
        Math.Max(0, CacheRead - previous.CacheRead), Math.Max(0, CacheWrite5m - previous.CacheWrite5m),
        Math.Max(0, CacheWrite1h - previous.CacheWrite1h), Math.Max(0, Reasoning - previous.Reasoning));
}

public sealed record UsageEvent(string Id, ProviderKind Provider, string SessionId, string Project,
    string Model, DateTimeOffset Timestamp, TokenUsage Tokens, IdentityQuality Identity = IdentityQuality.Verified,
    string? AccountId = null);

public sealed record AccountProfile
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public ProviderKind Provider { get; init; }
    public string Label { get; init; } = "";
    public string? ProfileDirectory { get; init; }
    public string? Executable { get; init; }
    public string? SecretReference { get; init; }
    public bool Enabled { get; init; } = true;
    public override string ToString()=>Label;
    public bool SameConnection(AccountProfile other)=>Id==other.Id&&Provider==other.Provider&&
        string.Equals(ProfileDirectory,other.ProfileDirectory,StringComparison.OrdinalIgnoreCase)&&
        string.Equals(Executable,other.Executable,StringComparison.OrdinalIgnoreCase)&&SecretReference==other.SecretReference;
}

public sealed record SourceLocation(string Id, ProviderKind Provider, string Directory, bool Enabled = true, bool Optional = false);
public sealed record ProviderCapabilities(bool LocalTokens, bool Quotas, bool Balance, bool ReportedSpend, bool History);

public sealed record QuotaWindow(string Id, string Label, decimal? UsedPercent, DateTimeOffset? ResetsAt,
    string Scope = "account", decimal? Used = null, decimal? Limit = null, decimal? Remaining = null,
    string Unit = "percent", long? WindowMinutes = null)
{
    public decimal? RemainingPercent => UsedPercent is { } used ? Math.Clamp(100 - used, 0, 100) : null;
    public string Band => UsedPercent switch { >= 90 => "critical", >= 75 => "high", >= 50 => "medium", null => "unknown", _ => "low" };
}

public sealed record MoneyMetric(string Id, string Label, decimal Value, string Currency = "USD", string Scope = "account");
public sealed record SourceStatus(string Id,ConnectionState State,DateTimeOffset? LastSuccess,string? MessageCode=null);
public sealed record ProviderSnapshot(string AccountId, ProviderKind Provider, DateTimeOffset ObservedAt,
    IReadOnlyList<QuotaWindow> Windows, IReadOnlyList<MoneyMetric> Money, string? Plan = null,IReadOnlyList<SourceStatus>? Sources=null)
{
    public ProviderSnapshot RetainUnavailableSources(ProviderSnapshot? previous)
    {
        if(Provider!=ProviderKind.OpenRouter||Sources is null||previous is null||AccountId!=previous.AccountId) return this;
        var windows=Windows.ToList();var money=Money.ToList();var sources=new List<SourceStatus>();
        foreach(var source in Sources)
        {
            if(source.State==ConnectionState.Ready) { sources.Add(source);continue; }
            var oldSource=previous.Sources?.FirstOrDefault(s=>s.Id==source.Id);
            var scope=source.Id=="key"?"key":"account";
            sources.Add(source with { LastSuccess=oldSource?.LastSuccess });
            if(oldSource?.LastSuccess is null) continue;
            if(source.Id=="key") windows.AddRange(previous.Windows.Where(w=>!windows.Any(n=>n.Id==w.Id)));
            money.AddRange(previous.Money.Where(m=>m.Scope==scope&&!money.Any(n=>n.Id==m.Id)));
        }
        return this with { Windows=windows,Money=money,Sources=sources };
    }
}
public sealed record ProviderResult(ConnectionState State, ProviderSnapshot? Snapshot = null,
    string? MessageCode = null, TimeSpan? RetryAfter = null);
public sealed record AccountStatus(AccountProfile Account, ProviderSnapshot? LastGood,
    DateTimeOffset? LastAttempt, DateTimeOffset? NextAttempt, ConnectionState State, string? MessageCode)
{
    public bool IsStale(DateTimeOffset now) => LastGood is not null &&
        (State is not ConnectionState.Ready || now - LastGood.ObservedAt > TimeSpan.FromMinutes(15));
}

public sealed record UsageFilter(DateTimeOffset? From = null, DateTimeOffset? Until = null,
    ProviderKind? Provider = null, string? Model = null, string? Project = null, string? SessionId = null);
public sealed record UsageGroup(string Key, TokenUsage Tokens, long Requests);
public sealed record DailyUsage(DateOnly Day, TokenUsage Tokens, long Requests);
public sealed record UsageSummary(TokenUsage Tokens, long Requests, long UncertainRequests,
    DateTimeOffset? FirstSeen, DateTimeOffset? LastSeen);

public sealed record ModelPrice(string Model, DateTimeOffset EffectiveFrom, decimal InputPerMillion,
    decimal OutputPerMillion, decimal? CacheReadPerMillion, decimal? CacheWrite5mPerMillion,
    decimal? CacheWrite1hPerMillion, string Source, string Currency = "USD", string Basis = "standard_short_context")
{
    public override string ToString()=>Model;
    public bool IsValid=>!string.IsNullOrWhiteSpace(Model)&&InputPerMillion>=0&&OutputPerMillion>=0&&
        (CacheReadPerMillion is null or >=0)&&(CacheWrite5mPerMillion is null or >=0)&&(CacheWrite1hPerMillion is null or >=0)&&
        Currency is { Length:3 }&&Currency.All(c=>c is >= 'A' and <= 'Z');
    public decimal? Estimate(TokenUsage tokens)
    {
        if(!IsValid || tokens.CacheRead>0&&CacheReadPerMillion is null || tokens.CacheWrite5m>0&&CacheWrite5mPerMillion is null ||
            tokens.CacheWrite1h>0&&CacheWrite1hPerMillion is null) return null;
        return (tokens.Input*InputPerMillion+tokens.Output*OutputPerMillion+tokens.CacheRead*(CacheReadPerMillion??0)+
            tokens.CacheWrite5m*(CacheWrite5mPerMillion??0)+tokens.CacheWrite1h*(CacheWrite1hPerMillion??0))/1_000_000m;
    }
}

public static class UsageDates
{
    public static DateTimeOffset StartOfDay(DateOnly date, TimeZoneInfo zone)
    {
        var time = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        // Some zones advance their clocks at midnight.
        while (zone.IsInvalidTime(time)) time = time.AddMinutes(1);
        var offset = zone.IsAmbiguousTime(time) ? zone.GetAmbiguousTimeOffsets(time).Max() : zone.GetUtcOffset(time);
        return new DateTimeOffset(time, offset).ToUniversalTime();
    }
}
