using System.Text.Json;
using AiUsageViewer.Application;

namespace AiUsageViewer.Infrastructure.Storage;
public sealed class NotificationStore(string directory)
{
    private string FilePath=>Path.Combine(directory,"notifications.json");
    public NotificationEvaluator Load()
    {
        try { return new(File.Exists(FilePath)?JsonSerializer.Deserialize<Dictionary<string,AlertAccount>>(File.ReadAllText(FilePath)):null); }
        catch(Exception e) when(e is IOException or JsonException or UnauthorizedAccessException) { return new(); }
    }
    public void Save(NotificationEvaluator evaluator)
    {
        Directory.CreateDirectory(directory);var temporary=FilePath+".tmp";
        File.WriteAllText(temporary,JsonSerializer.Serialize(evaluator.State));File.Move(temporary,FilePath,true);
    }
}
