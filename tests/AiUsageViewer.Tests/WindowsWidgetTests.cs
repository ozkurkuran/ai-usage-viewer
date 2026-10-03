using System.Text.Json;
using AiUsageViewer.Application.WindowsWidgets;

namespace AiUsageViewer.Tests;

public sealed class WindowsWidgetTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 3, 12, 0, 0, TimeSpan.Zero);
    private static WidgetSnapshot Snapshot => new(1, "en", new(2026, 10, 3), Now, Now, "123,456", "≈ 0.80 USD",
        [new("Claude", "Ready", ["Session: 38% used", "Weekly: 54% used"])]);

    [Fact]
    public void YesterdayNeverAppearsAsTodaysTotal()
    {
        var card = WidgetCard.Render(Snapshot with { Day = new(2026, 10, 2) }, BoardWidgetSize.Medium, Now, TimeZoneInfo.Utc);
        Assert.DoesNotContain("123,456", card);
        Assert.Contains("Cached data", card);
        Assert.DoesNotContain("0.80 USD", card);
    }

    [Fact]
    public void MidnightUsesLocalDayRatherThanUtcDay()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("test+3", TimeSpan.FromHours(3), "test+3", "test+3");
        var now = new DateTimeOffset(2026, 10, 2, 22, 0, 0, TimeSpan.Zero);
        Assert.Contains("123,456", WidgetCard.Render(Snapshot with { PublishedAt = now, DataUpdatedAt = now }, BoardWidgetSize.Small, now, zone));
    }

    [Fact]
    public void FreshHeartbeatCannotHideFailedCollection()
    {
        Assert.Contains("Cached data", WidgetCard.Render(Snapshot with { DataUpdatedAt = Now.AddMinutes(-10) }, BoardWidgetSize.Medium, Now, TimeZoneInfo.Utc));
    }

    [Fact]
    public void FutureHeartbeatIsNotFresh()
    {
        Assert.Contains("Cached data", WidgetCard.Render(Snapshot with { PublishedAt = Now.AddHours(1) }, BoardWidgetSize.Medium, Now, TimeZoneInfo.Utc));
    }

    [Theory]
    [InlineData(BoardWidgetSize.Small, 1)]
    [InlineData(BoardWidgetSize.Medium, 2)]
    [InlineData(BoardWidgetSize.Large, 2)]
    public void CardsHaveValidJsonAndWorkingActions(BoardWidgetSize size, int count)
    {
        using var card = JsonDocument.Parse(WidgetCard.Render(Snapshot, size, Now, TimeZoneInfo.Utc));
        Assert.Equal("AdaptiveCard", card.RootElement.GetProperty("type").GetString());
        var actions = card.RootElement.GetProperty("actions");
        Assert.Equal(count, actions.GetArrayLength());
        Assert.Equal("open", actions[0].GetProperty("verb").GetString());
    }

    [Fact]
    public void AccountLabelsCannotInjectCardObjects()
    {
        var label = "\"}],\"actions\":[{\"type\":\"Action.OpenUrl\"}";
        var card = WidgetCard.Render(Snapshot with { Accounts = [new(label, "Ready", [])] }, BoardWidgetSize.Large, Now, TimeZoneInfo.Utc);
        using var document = JsonDocument.Parse(card);
        Assert.Equal(2, document.RootElement.GetProperty("actions").GetArrayLength());
    }

    [Fact]
    public void FirstRunUsesSetupPromptAndTurkishIsLocalized()
    {
        using var empty = JsonDocument.Parse(WidgetCard.Render(null, BoardWidgetSize.Medium, Now));
        Assert.Equal("open", empty.RootElement.GetProperty("actions")[0].GetProperty("verb").GetString());
        Assert.DoesNotContain("123,456", empty.RootElement.ToString());
        using var document = JsonDocument.Parse(WidgetCard.Render(Snapshot with { Language = "tr" }, BoardWidgetSize.Medium, Now, TimeZoneInfo.Utc));
        Assert.Equal("Uygulamayı aç", document.RootElement.GetProperty("actions")[0].GetProperty("title").GetString());
    }

    [Fact]
    public void SnapshotFileRejectsMalformedOrUnsupportedData()
    {
        var directory = Path.Combine(Path.GetTempPath(), "UsageNest-widget-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.Null(WidgetSnapshotFile.Read(directory));
            WidgetSnapshotFile.Write(directory, Snapshot);
            Assert.Equal(Snapshot.Total, WidgetSnapshotFile.Read(directory)!.Total);
            File.WriteAllText(Path.Combine(directory, WidgetSnapshotFile.FileName), "{broken");
            Assert.Null(WidgetSnapshotFile.Read(directory));
            WidgetSnapshotFile.Write(directory, Snapshot with { Version = 99 });
            Assert.Null(WidgetSnapshotFile.Read(directory));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public void ActionsAreScopedToTheSameDataDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "UsageNest-widget-" + Guid.NewGuid().ToString("N"));
        using var signal = new EventWaitHandle(false, EventResetMode.AutoReset, WidgetAppSignals.Name(directory, "open"));
        Assert.True(WidgetAppSignals.Send(directory.ToUpperInvariant(), "open"));
        Assert.True(signal.WaitOne(0));
        Assert.False(WidgetAppSignals.Send(directory + "-other", "open"));
    }
}
