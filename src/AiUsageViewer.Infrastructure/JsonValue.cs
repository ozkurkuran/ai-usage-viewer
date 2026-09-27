using System.Globalization;
using System.Text.Json;

namespace AiUsageViewer.Infrastructure;

internal static class JsonValue
{
    public static JsonElement Get(this JsonElement value, string name) =>
        value.ValueKind == JsonValueKind.Object && value.TryGetProperty(name, out var child) ? child : default;
    public static string? Text(this JsonElement value) => value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    public static decimal? Number(this JsonElement value) => value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number)
        ? number : value.ValueKind == JsonValueKind.String && decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out number) ? number : null;
    public static long Count(this JsonElement value) => value.Number() is >= 0 and <= 1_000_000_000_000m ? (long)value.Number()!.Value : 0;
    public static bool True(this JsonElement value) => value.ValueKind == JsonValueKind.True;
    public static DateTimeOffset? Time(this JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.String && DateTimeOffset.TryParse(value.GetString(), CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out var time)) return time.ToUniversalTime();
        if (value.Number() is { } unix && unix is >= -62135596800 and <= 253402300799)
            return DateTimeOffset.FromUnixTimeSeconds((long)unix);
        return null;
    }
}
