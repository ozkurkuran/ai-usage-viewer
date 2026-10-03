using System.Globalization;

namespace AiUsageViewer.Core;

// Culture-aware display formatting shared by the app, widget and notifications.
public sealed class UiFormat(UiText text, CultureInfo culture)
{
    public UiText Text { get; } = text;
    public CultureInfo Culture { get; } = culture;
    public static UiFormat For(string language) => new(new UiText(language), AppLanguages.CultureFor(language));

    public string Number(long value) => value.ToString("N0", Culture);
    public string Number(decimal value) => value.ToString("N0", Culture);
    private static string Whole(double value) => Math.Round(value, MidpointRounding.AwayFromZero).ToString("0", CultureInfo.InvariantCulture);
    private string Template(string key, params object[] values) => string.Format(Culture, Text[key], values);

    public string PercentUsed(double used) => Template("percentUsed", Whole(used));
    public string PercentLeft(double used) => Template("percentLeft", Whole(100 - used));
    public string Percent(double value) => Template("percentShort", Whole(value));
    public string QuotaValue(double used, bool remaining) => remaining ? PercentLeft(used) : PercentUsed(used);

    public string Duration(TimeSpan value)
    {
        if (value < TimeSpan.Zero) value = TimeSpan.Zero;
        return value.TotalDays >= 1 ? Template("durationDaysHours", (int)value.TotalDays, value.Hours) :
            value.TotalHours >= 1 ? Template("durationHoursMinutes", (int)value.TotalHours, value.Minutes) :
            Template("durationMinutes", Math.Max(0, (int)value.TotalMinutes));
    }

    // 19:26 today; "Tue 16:27" on another day.
    public string Time(DateTimeOffset value, DateTimeOffset now)
    {
        var local = value.ToLocalTime();
        return local.Date == now.ToLocalTime().Date ? local.ToString("HH:mm", Culture) : local.ToString("ddd HH:mm", Culture);
    }
    public string Day(DateTimeOffset value) => value.ToLocalTime().ToString("ddd", Culture);

    public string Money(decimal value, string currency = "USD", int decimals = 2) => currency == "USD"
        ? "$" + value.ToString("N" + decimals, Culture) : value.ToString("N" + decimals, Culture) + " " + currency;
    // API-equivalent estimate: "≈ $0.31"; below a cent "< $0.01".
    public string Estimate(decimal value, string currency = "USD") =>
        value > 0 && value < 0.01m ? "< " + Money(0.01m, currency) : "≈ " + Money(value, currency);

    // Chart axis labels: 0, 5k, 10k (EN) · 0, 5 B, 10 B (TR).
    public string Axis(double value)
    {
        if (Math.Abs(value) >= 1_000_000) return Trim(value / 1_000_000) + Text["millionShort"];
        if (Math.Abs(value) >= 1000) return Trim(value / 1000) + Text["thousandShort"];
        return Trim(value);
    }
    private string Trim(double value) => value.ToString("0.#", Culture);

    // "Nice" axis ticks: the top tick is the next 1, 2, 2.5, 5 or 10 × 10ⁿ above max, never max itself.
    public static IReadOnlyList<double> NiceTicks(double maximum)
    {
        if (maximum <= 0 || double.IsNaN(maximum)) return [0, 1];
        var magnitude = Math.Pow(10, Math.Floor(Math.Log10(maximum)));
        var fraction = maximum / magnitude;
        var nice = fraction <= 1 ? 1 : fraction <= 2 ? 2 : fraction <= 2.5 ? 2.5 : fraction <= 5 ? 5 : 10;
        var divisions = nice is 2.5 or 5 ? 5 : 4;
        var step = nice * magnitude / divisions;
        return Enumerable.Range(0, divisions + 1).Select(i => Math.Round(i * step, 10)).ToList();
    }
}
