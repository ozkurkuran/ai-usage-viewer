using System.Text.Json;

namespace AiUsageViewer.Application.WindowsWidgets;

// Display-only projection. Never serialize AccountProfile, provider payloads or credentials.
public sealed record WidgetAccount(string Name, string Status, string[] Metrics);
public sealed record WidgetSnapshot(int Version, string Language, DateOnly Day,
    DateTimeOffset DataUpdatedAt, DateTimeOffset PublishedAt, string Total, string? Cost,
    WidgetAccount[] Accounts, bool Demo = false)
{
    public const int CurrentVersion = 1;
}

public static class WidgetSnapshotFile
{
    public const string FileName = "windows-widget.json";
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AiUsageViewer");

    public static void Write(string directory, WidgetSnapshot snapshot)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, FileName);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(snapshot));
            File.Move(temporary, path, true);
        }
        finally { File.Delete(temporary); }
    }

    public static WidgetSnapshot? Read(string directory)
    {
        try
        {
            using var stream = new FileStream(Path.Combine(directory, FileName), FileMode.Open,
                FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (stream.Length > 256 * 1024) return null;
            var snapshot = JsonSerializer.Deserialize<WidgetSnapshot>(stream);
            return snapshot is { Version: WidgetSnapshot.CurrentVersion, Accounts: not null, Total: not null }
                && snapshot.Accounts.All(a => a is { Name: not null, Status: not null, Metrics: not null }
                    && a.Metrics.All(m => m is not null)) ? snapshot : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }
}
