using AiUsageViewer.Core;

namespace AiUsageViewer.App;

public sealed class Localization(string language)
{
    public const string AppName="Ai UsageNest";
    public const string PrivacyUrl="https://github.com/ozkurkuran/ai-usage-viewer/blob/main/PRIVACY.md";
    private readonly UiText text=new(language);
    public string Language=>text.Language;
    public string this[string key]=>text[key];
}
