using System.Globalization;
using System.Text.Json;
using AiUsageViewer.Core;
using AiUsageViewer.Application.WindowsWidgets;
using AiUsageViewer.Infrastructure.Storage;

namespace AiUsageViewer.Tests;

public sealed class LocalizationTests
{
    [Theory]
    [InlineData("es-MX", "es")]
    [InlineData("es-AR", "es")]
    [InlineData("fr-CA", "fr")]
    [InlineData("en-CA", "en")]
    [InlineData("de-AT", "de")]
    [InlineData("nl-BE", "nl")]
    [InlineData("cs-CZ", "cs")]
    [InlineData("ru-RU", "ru")]
    [InlineData("pt-BR", "pt-BR")]
    [InlineData("pt-PT", "pt")]
    [InlineData("it-IT", "it")]
    [InlineData("pl-PL", "pl")]
    [InlineData("tr-TR", "tr")]
    [InlineData("ja-JP", "en")]
    public void SystemLanguageMatchesRegionalDisplayLanguages(string windowsLanguage, string expected)
    {
        Assert.Equal(expected, AppLanguages.Resolve(AppLanguages.System, CultureInfo.GetCultureInfo(windowsLanguage)));
    }

    [Theory]
    [InlineData("PT_br", "pt-BR")]
    [InlineData("FR-ca", "fr")]
    [InlineData("en", "en")]
    [InlineData("tr", "tr")]
    [InlineData("invalid", "en")]
    [InlineData(null, "auto")]
    [InlineData("AUTO", "auto")]
    public void SavedSettingsNormalizeWithoutLosingSupportedChoices(string? language, string expected)
    {
        Assert.Equal(expected, SettingsStore.Validate(new AppSettings { Language=language! }).Language);
    }

    [Fact]
    public void EveryLanguageHasAllUiAndBoardStrings()
    {
        var reference = UiText.Catalog("en");
        Assert.True(reference.Count >= 140);
        foreach (var language in AppLanguages.Supported)
        {
            var catalog = UiText.Catalog(language.Code);
            Assert.Equal(reference.Keys.Order(), catalog.Keys.Order());
            Assert.All(catalog.Values, value => Assert.False(string.IsNullOrWhiteSpace(value)));
            Assert.EndsWith(" ", catalog["startFailed"]);
            Assert.Equal(language.CultureName, AppLanguages.CultureFor(language.Code).Name);
        }
        Assert.NotEqual(UiText.Catalog("pt")["settings"], UiText.Catalog("pt-BR")["settings"]);
        Assert.Equal("Ayarlar", new UiText("tr")["settings"]);
        Assert.Equal("Settings", new UiText("unsupported")["settings"]);
        Assert.Equal("unknownKey", new UiText("de")["unknownKey"]);
    }

    [Fact]
    public void SettingsKeepSystemOrManualChoiceAcrossRestarts()
    {
        var directory = Path.Combine(Path.GetTempPath(), "AiUsageViewer-tests", Guid.NewGuid().ToString("N"));
        try
        {
            var store = new SettingsStore(directory);
            Assert.Equal(AppLanguages.System, store.Load().Language);
            foreach (var language in AppLanguages.Supported.Select(x => x.Code).Append(AppLanguages.System))
            {
                store.Save(new AppSettings { Language=language });
                Assert.Equal(language, store.Load().Language);
            }
            File.WriteAllText(Path.Combine(directory, "settings.json"), "{\"Version\":1}");
            Assert.Equal(AppLanguages.System, store.Load().Language);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void BoardUsesSameLanguageAsDesktopSnapshot()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var language in AppLanguages.Supported)
        {
            var snapshot = new WidgetSnapshot(WidgetSnapshot.CurrentVersion, language.Code,
                DateOnly.FromDateTime(now.UtcDateTime), now, now, "123", null, [], true);
            using var card = JsonDocument.Parse(WidgetCard.Render(snapshot, BoardWidgetSize.Medium, now, TimeZoneInfo.Utc));
            var text = new UiText(language.Code);
            Assert.Equal(text["boardToday"] + " · " + text["demo"], card.RootElement.GetProperty("body")[0].GetProperty("text").GetString());
            Assert.Equal(text["openApp"], card.RootElement.GetProperty("actions")[0].GetProperty("title").GetString());
            Assert.Equal(text["refresh"], card.RootElement.GetProperty("actions")[1].GetProperty("title").GetString());
        }
    }
}
