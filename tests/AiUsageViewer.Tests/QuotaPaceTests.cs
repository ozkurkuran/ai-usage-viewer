using System.Globalization;
using AiUsageViewer.Core;

namespace AiUsageViewer.Tests;

public sealed class QuotaPaceTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 17, 27, 0, TimeSpan.FromHours(3));

    [Fact]
    public void ElapsedFollowsTimeUntilResetAndWindowLength()
    {
        // 1 h 59 m left of 5 h ≈ 60 %; 2 d 23 h left of a week ≈ 58 %.
        Assert.Equal(60.3, QuotaPace.ElapsedPercent(Now.AddHours(1).AddMinutes(59), QuotaPace.SessionWindow, Now)!.Value, 1);
        Assert.Equal(57.7, QuotaPace.ElapsedPercent(Now.AddDays(2).AddHours(23), QuotaPace.WeeklyWindow, Now)!.Value, 1);
        Assert.Null(QuotaPace.ElapsedPercent(null, QuotaPace.WeeklyWindow, Now));
        Assert.Equal(0, QuotaPace.ElapsedPercent(Now.AddDays(9), QuotaPace.WeeklyWindow, Now));
    }

    [Fact]
    public void WindowLengthPrefersProviderPayload()
    {
        Assert.Equal(TimeSpan.FromMinutes(300), QuotaPace.WindowLength(new("s", "five_hour", 10, Now, WindowMinutes: 300)));
        Assert.Equal(TimeSpan.FromMinutes(120), QuotaPace.WindowLength(new("s", "five_hour", 10, Now, WindowMinutes: 120)));
        Assert.Equal(QuotaPace.SessionWindow, QuotaPace.WindowLength(new("s", "five_hour", 10, Now)));
        Assert.Equal(QuotaPace.WeeklyWindow, QuotaPace.WindowLength(new("w", "seven_day", 10, Now)));
        Assert.Null(QuotaPace.WindowLength(new("x", "custom", 10, Now)));
    }

    [Theory]
    [InlineData(38, 60, QuotaSeverity.Normal, PaceState.Under)]
    [InlineData(54, 58, QuotaSeverity.Normal, PaceState.On)]
    [InlineData(67, 45, QuotaSeverity.Ahead, PaceState.Ahead)]
    [InlineData(55, 45, QuotaSeverity.Normal, PaceState.On)]
    [InlineData(92, 95, QuotaSeverity.Critical, PaceState.On)]
    [InlineData(90, 20, QuotaSeverity.Critical, PaceState.Ahead)]
    public void RowStatusUsesPaceMarginAndCriticalThreshold(double used, double elapsed, QuotaSeverity severity, PaceState pace)
    {
        Assert.Equal(severity, QuotaPace.Severity(used, elapsed));
        Assert.Equal(pace, QuotaPace.Pace(used, elapsed));
    }

    [Fact]
    public void ForecastMatchesMockupAndRoundsToHalfHour()
    {
        // ChatGPT Plus weekly: 67 % used, resets Wed 14:27 (3 d 21 h left) → runs out ~Mon 06:30.
        var reset = Now.AddDays(3).AddHours(21);
        var elapsed = QuotaPace.ElapsedPercent(reset, QuotaPace.WeeklyWindow, Now);
        var runsOut = QuotaPace.RunsOutAt(67, elapsed, reset, QuotaPace.WeeklyWindow, Now);
        Assert.Equal(new DateTimeOffset(2026, 10, 5, 6, 30, 0, Now.Offset), runsOut);
    }

    [Fact]
    public void ForecastCutOffs()
    {
        var reset = Now.AddDays(3);
        Assert.Null(QuotaPace.RunsOutAt(30, 50, reset, QuotaPace.WeeklyWindow, Now));   // not ahead of pace
        Assert.Null(QuotaPace.RunsOutAt(30, 8, reset, QuotaPace.WeeklyWindow, Now));    // window just started
        Assert.Null(QuotaPace.RunsOutAt(100, 50, reset, QuotaPace.WeeklyWindow, Now));  // already used up
        Assert.Null(QuotaPace.RunsOutAt(62, 50, Now.AddMinutes(30), QuotaPace.WeeklyWindow, Now)); // reset comes first
        Assert.NotNull(QuotaPace.RunsOutAt(30, 12, Now.AddDays(6), QuotaPace.WeeklyWindow, Now));
    }

    [Fact]
    public void RemainingModeInvertsFillAndMarker()
    {
        Assert.Equal(79, QuotaPace.DisplayFill(21, true));
        Assert.Equal(21, QuotaPace.DisplayFill(21, false));
        Assert.Equal(40, QuotaPace.DisplayMarker(60, true));
        Assert.Equal(60, QuotaPace.DisplayMarker(60, false));
        Assert.Null(QuotaPace.DisplayMarker(null, true));
    }

    [Fact]
    public void StaleWhenRefreshFailedOrDataOlderThanTwoIntervals()
    {
        var account = new AccountProfile { Label = "Claude Pro" };
        AccountStatus Status(ConnectionState state, TimeSpan age) => new(account,
            new(account.Id, ProviderKind.Claude, Now - age, [], []), Now, null, state, null);
        Assert.False(QuotaPace.IsStale(Status(ConnectionState.Ready, TimeSpan.FromMinutes(9)), Now));
        Assert.True(QuotaPace.IsStale(Status(ConnectionState.Ready, TimeSpan.FromMinutes(11)), Now));
        Assert.True(QuotaPace.IsStale(Status(ConnectionState.Unavailable, TimeSpan.FromMinutes(1)), Now));
        Assert.False(QuotaPace.IsStale(new(account, null, Now, null, ConnectionState.Unavailable, null), Now));
    }

    [Fact]
    public void FormatsPercentDurationsThousandsAndCostPerCulture()
    {
        var en = UiFormat.For("en");
        var tr = UiFormat.For("tr");
        Assert.Equal("38% used", en.PercentUsed(38));
        Assert.Equal("62% left", en.PercentLeft(38));
        Assert.Equal("%38 kullanıldı", tr.PercentUsed(38));
        Assert.Equal("%62 kaldı", tr.PercentLeft(38));
        Assert.Equal("1h 59m", en.Duration(new TimeSpan(1, 59, 0)));
        Assert.Equal("2d 23h", en.Duration(new TimeSpan(2, 23, 5, 0)));
        Assert.Equal("1 sa 59 dk", tr.Duration(new TimeSpan(1, 59, 0)));
        Assert.Equal("2 g 23 sa", tr.Duration(new TimeSpan(2, 23, 5, 0)));
        Assert.Equal("115,068", en.Number(115068L));
        Assert.Equal("115.068", tr.Number(115068L));
        Assert.Equal("≈ $0.31", en.Estimate(0.31m));
        Assert.Equal("≈ $0,31", tr.Estimate(0.31m));
        Assert.Equal("< $0.01", en.Estimate(0.004m));
        Assert.Equal("5k", en.Axis(5000));
        Assert.Equal("5 B", tr.Axis(5000));
        Assert.Equal("Sal", tr.Day(new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero)));
        Assert.Equal("Tue", en.Day(new DateTimeOffset(2026, 10, 6, 12, 0, 0, TimeSpan.Zero)));
    }

    [Fact]
    public void NiceTicksNeverUseTheDataMaximum()
    {
        Assert.Equal(new double[] { 0, 5000, 10000, 15000, 20000, 25000 }, UiFormat.NiceTicks(21300));
        Assert.Equal(new double[] { 0, 0.25, 0.5, 0.75, 1 }, UiFormat.NiceTicks(0.75));
        Assert.Equal(new double[] { 0, 50000, 100000, 150000, 200000 }, UiFormat.NiceTicks(115068));
        Assert.Equal(new double[] { 0, 1 }, UiFormat.NiceTicks(0));
    }
}
