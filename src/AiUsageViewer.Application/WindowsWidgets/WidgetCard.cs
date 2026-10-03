using AiUsageViewer.Core;
using System.Text.Json;

namespace AiUsageViewer.Application.WindowsWidgets;

public enum BoardWidgetSize { Small, Medium, Large }

public static class WidgetCard
{
    public const string DefinitionId = "UsageNest.Overview";
    public const string ProviderClassId = "64CE39C7-6E5F-4A94-91FB-BEAC8A1DC514";

    public static string Render(WidgetSnapshot? snapshot, BoardWidgetSize size, DateTimeOffset now,
        TimeZoneInfo? zone = null)
    {
        zone ??= TimeZoneInfo.Local;
        var l = new UiText(snapshot?.Language ?? AppLanguages.System);
        var body = new List<object>();
        if (snapshot is null)
        {
            body.Add(Text(l["boardTitle"], "medium", true));
            body.Add(Text(l["boardSetup"]));
        }
        else
        {
            var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(now, zone).DateTime);
            var sameDay = snapshot.Day == today;
            var stale = now - snapshot.PublishedAt > TimeSpan.FromMinutes(2)
                || now - snapshot.DataUpdatedAt > TimeSpan.FromMinutes(2)
                || snapshot.PublishedAt > now.AddMinutes(1) || !sameDay;
            body.Add(Text(l["boardToday"] + (snapshot.Demo ? " · " + l["demo"] : ""), "small"));
            body.Add(Text(sameDay ? snapshot.Total : "—", "extraLarge", true, "none"));
            if (size != BoardWidgetSize.Small && sameDay && snapshot.Cost is { } cost)
                body.Add(Text(l["tokenCost"] + ": " + cost, "small"));
            if (stale)
                body.Add(Text(l["boardCached"], "small"));

            var limit = size switch { BoardWidgetSize.Small => 0, BoardWidgetSize.Medium => 2, _ => 3 };
            foreach (var account in snapshot.Accounts.Take(limit))
            {
                var metrics = account.Metrics.Take(size == BoardWidgetSize.Large ? 2 : 1);
                body.Add(new { type = "Container", spacing = "small", separator = true,
                    items = new object[] { Text(account.Name, "small", true, "none"),
                        Text(string.Join(" · ", metrics.DefaultIfEmpty(account.Status)), "small", spacing: "none"),
                        Text(account.Status, "small", spacing: "none") } });
            }
            if (size != BoardWidgetSize.Small && snapshot.Accounts.Length == 0)
                body.Add(Text(l["boardConnect"], "small"));
            if (snapshot.Accounts.Length > limit && size != BoardWidgetSize.Small)
                body.Add(Text(l["boardMore"], "small"));
        }
        var actions = new List<object> {
            new { type = "Action.Execute", title = l["openApp"], verb = "open" }
        };
        if (size != BoardWidgetSize.Small)
            actions.Add(new { type = "Action.Execute", title = l["refresh"], verb = "refresh" });
        return JsonSerializer.Serialize(new Dictionary<string, object> {
            ["$schema"] = "http://adaptivecards.io/schemas/adaptive-card.json",
            ["type"] = "AdaptiveCard", ["version"] = "1.5", ["body"] = body, ["actions"] = actions
        });
    }

    private static object Text(string value, string size = "small", bool bold = false, string spacing = "small")
        => new { type = "TextBlock", text = value.Length > 240 ? value[..237] + "…" : value,
            size, weight = bold ? "bolder" : "default", spacing, wrap = true, maxLines = 2 };
}
