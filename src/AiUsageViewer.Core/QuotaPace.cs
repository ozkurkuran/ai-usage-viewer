namespace AiUsageViewer.Core;

public enum PaceState { Unknown, Under, On, Ahead }
public enum QuotaSeverity { Normal, Ahead, Critical }

// How far a quota window has progressed in time versus how much of it is used.
public sealed record QuotaReading(double Used, double? Elapsed, PaceState Pace, QuotaSeverity Severity,
    DateTimeOffset? RunsOutAt, TimeSpan? WindowLength, DateTimeOffset? ResetsAt);

public static class QuotaPace
{
    // Fallback window lengths when a provider payload does not report one.
    public static readonly TimeSpan SessionWindow = TimeSpan.FromHours(5);
    public static readonly TimeSpan WeeklyWindow = TimeSpan.FromDays(7);
    // Normal quota refresh cadence (QuotaCoordinator); data older than twice this is stale.
    public static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);
    public const double PaceMargin = 10;
    public const double CriticalPercent = 90;
    public const double ForecastMinimumElapsed = 10;

    public static TimeSpan? WindowLength(QuotaWindow window) =>
        window.WindowMinutes is > 0 and var minutes ? TimeSpan.FromMinutes(minutes) : window.Label switch
        {
            "five_hour" or "session" => SessionWindow,
            "seven_day" or "weekly" => WeeklyWindow,
            "daily" => TimeSpan.FromDays(1),
            _ => null
        };

    public static double? ElapsedPercent(DateTimeOffset? resetsAt, TimeSpan? length, DateTimeOffset now)
    {
        if (resetsAt is not { } reset || length is not { } window || window <= TimeSpan.Zero) return null;
        var elapsed = 1 - (reset - now) / window;
        return Math.Clamp(elapsed * 100, 0, 100);
    }

    public static PaceState Pace(double used, double? elapsed) => elapsed switch
    {
        null => PaceState.Unknown,
        var e when used > e + PaceMargin => PaceState.Ahead,
        var e when used < e - PaceMargin => PaceState.Under,
        _ => PaceState.On
    };

    public static QuotaSeverity Severity(double used, double? elapsed) =>
        used >= CriticalPercent ? QuotaSeverity.Critical :
        elapsed is { } e && used > e + PaceMargin ? QuotaSeverity.Ahead : QuotaSeverity.Normal;

    // At the current average rate, when the window is used up; null unless that happens before the reset.
    public static DateTimeOffset? RunsOutAt(double used, double? elapsed, DateTimeOffset? resetsAt, TimeSpan? length, DateTimeOffset now)
    {
        if (elapsed is not { } e || e < ForecastMinimumElapsed || used <= e + PaceMargin || used >= 100 ||
            resetsAt is not { } reset || length is not { } window) return null;
        var elapsedHours = window.TotalHours * e / 100;
        if (elapsedHours <= 0) return null;
        var hoursToFull = (100 - used) / (used / elapsedHours);
        var at = now.AddHours(hoursToFull);
        return at < reset ? RoundToHalfHour(at) : null;
    }

    public static DateTimeOffset RoundToHalfHour(DateTimeOffset value)
    {
        var step = TimeSpan.FromMinutes(30).Ticks;
        return new DateTimeOffset((value.Ticks + step / 2) / step * step, value.Offset);
    }

    public static QuotaReading? Read(QuotaWindow window, DateTimeOffset now)
    {
        if (window.UsedPercent is not { } usedPercent) return null;
        var used = (double)usedPercent;
        var length = WindowLength(window);
        var elapsed = ElapsedPercent(window.ResetsAt, length, now);
        return new(used, elapsed, Pace(used, elapsed), Severity(used, elapsed),
            RunsOutAt(used, elapsed, window.ResetsAt, length, now), length, window.ResetsAt);
    }

    // "Show quota as: Left" inverts both the fill and the pace marker.
    public static double DisplayFill(double used, bool remaining) => remaining ? 100 - used : used;
    public static double? DisplayMarker(double? elapsed, bool remaining) => elapsed is { } e ? remaining ? 100 - e : e : null;

    // Stale: the last refresh failed, or the last good data is older than twice the refresh interval.
    public static bool IsStale(AccountStatus status, DateTimeOffset now) => status.LastGood is { } good &&
        (status.State is not (ConnectionState.Ready or ConnectionState.Partial) || now - good.ObservedAt > 2 * RefreshInterval);
}
