using System.Globalization;
using System.Text.Json;

namespace AiUsageViewer.Core;

public sealed record AppLanguage(string Code, string NativeName, string CultureName);

public static class AppLanguages
{
    public const string System = "auto";
    // Capture Windows' display language before the application changes its culture.
    private static readonly CultureInfo WindowsCulture = CultureInfo.CurrentUICulture;
    public static IReadOnlyList<AppLanguage> Supported { get; } = Array.AsReadOnly(new AppLanguage[] {
        new("en", "English", "en-US"), new("tr", "Türkçe", "tr-TR"),
        new("es", "Español", "es-ES"), new("de", "Deutsch", "de-DE"),
        new("fr", "Français", "fr-FR"), new("pt", "Português (Portugal)", "pt-PT"),
        new("pt-BR", "Português (Brasil)", "pt-BR"), new("ru", "Русский", "ru-RU"),
        new("nl", "Nederlands", "nl-NL"), new("cs", "Čeština", "cs-CZ"),
        new("it", "Italiano", "it-IT"), new("pl", "Polski", "pl-PL")
    });

    public static bool IsSupported(string? language) =>
        Supported.Any(x => string.Equals(x.Code, language, StringComparison.OrdinalIgnoreCase));

    public static string Resolve(string? language, CultureInfo? systemCulture = null)
    {
        if (string.IsNullOrWhiteSpace(language) || language.Equals(System, StringComparison.OrdinalIgnoreCase))
            language = (systemCulture ?? WindowsCulture).Name;
        var tag = language.Trim().Replace('_', '-');
        var exact = Supported.FirstOrDefault(x => x.Code.Equals(tag, StringComparison.OrdinalIgnoreCase));
        if (exact is not null) return exact.Code;
        var neutral = tag.Split('-')[0];
        return Supported.FirstOrDefault(x => x.Code.Equals(neutral, StringComparison.OrdinalIgnoreCase))?.Code ?? "en";
    }

    public static string NormalizeSetting(string? language) =>
        string.IsNullOrWhiteSpace(language) || language.Equals(System, StringComparison.OrdinalIgnoreCase)
            ? System : Resolve(language);

    public static CultureInfo CultureFor(string language)
    {
        var resolved = Resolve(language);
        if (language == System && Resolve(WindowsCulture.Name) == resolved && IsSupported(WindowsCulture.TwoLetterISOLanguageName))
            return WindowsCulture;
        return CultureInfo.GetCultureInfo(Supported.Single(x => x.Code == resolved).CultureName);
    }

    public static void ApplyCulture(string language)
    {
        var culture = CultureFor(language);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        CultureInfo.DefaultThreadCurrentCulture = culture;
        CultureInfo.DefaultThreadCurrentUICulture = culture;
    }
}

public sealed class UiText(string language)
{
    public string Language { get; } = AppLanguages.Resolve(language);
    private static readonly IReadOnlyDictionary<string, Dictionary<string, string>> Catalogs =
        AppLanguages.Supported.ToDictionary(x => x.Code, x => Load(x.Code));
    public static IReadOnlyDictionary<string, string> Catalog(string language) => Catalogs[AppLanguages.Resolve(language)];
    public string this[string key] => Catalogs[Language].TryGetValue(key, out var value) ? value
        : Catalogs["en"].GetValueOrDefault(key, key);
    private static Dictionary<string, string> Load(string language)
    {
        using var stream = typeof(UiText).Assembly.GetManifestResourceStream($"AiUsageViewer.Core.Locales.{language}.json")
            ?? throw new InvalidDataException($"Missing language catalog: {language}");
        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream)
            ?? throw new InvalidDataException($"Invalid language catalog: {language}");
    }
}
