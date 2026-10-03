using System.Diagnostics;
using System.Runtime.InteropServices;
using AiUsageViewer.Application.WindowsWidgets;
using Microsoft.Windows.Widgets;
using Microsoft.Windows.Widgets.Providers;

namespace AiUsageViewer.Widgets;

[Guid(WidgetCard.ProviderClassId)]
[WinRT.GeneratedWinRTExposedType]
public sealed partial class WidgetProvider : IWidgetProvider
{
    // Host callback objects expire when their callback returns; keep only value copies.
    private sealed record Entry(BoardWidgetSize Size, bool Active);
    private readonly Dictionary<string, Entry> widgets = [];
    private readonly object gate = new();
    private readonly Timer timer;
    internal static readonly ManualResetEvent Empty = new(false);

    public WidgetProvider()
    {
        try
        {
            foreach (var info in WidgetManager.GetDefault().GetWidgetInfos())
            {
                var context = info.WidgetContext;
                if (context.DefinitionId == WidgetCard.DefinitionId)
                    widgets[context.Id] = new(Size(context.Size), false);
            }
        }
        catch (Exception ex) when (ex is COMException or ArgumentException)
        { /* The Widgets host or package identity may not be available. Activate restores its context. */ }
        timer = new Timer(_ => RefreshActive(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15));
    }

    public void CreateWidget(WidgetContext widgetContext)
    {
        if (widgetContext.DefinitionId != WidgetCard.DefinitionId) return;
        lock (gate)
        {
            Empty.Reset();
            widgets[widgetContext.Id] = new(Size(widgetContext.Size), false);
            Update(widgetContext.Id);
        }
    }

    public void DeleteWidget(string widgetId, string customState)
    {
        lock (gate)
        {
            widgets.Remove(widgetId);
            if (widgets.Count == 0) { timer.Dispose(); Empty.Set(); }
        }
    }

    public void Activate(WidgetContext widgetContext)
    {
        if (widgetContext.DefinitionId != WidgetCard.DefinitionId) return;
        lock (gate)
        {
            widgets[widgetContext.Id] = new(Size(widgetContext.Size), true);
            Update(widgetContext.Id);
        }
        // Reuse the app's collector and quota backoff instead of running a second data engine.
        LaunchApp("background");
    }

    public void Deactivate(string widgetId)
    {
        lock (gate)
            if (widgets.TryGetValue(widgetId, out var entry)) widgets[widgetId] = entry with { Active = false };
    }

    public void OnWidgetContextChanged(WidgetContextChangedArgs contextChangedArgs)
    {
        var context = contextChangedArgs.WidgetContext;
        lock (gate)
        {
            if (!widgets.TryGetValue(context.Id, out var entry)) return;
            widgets[context.Id] = entry with { Size = Size(context.Size) };
            Update(context.Id);
        }
    }

    public void OnActionInvoked(WidgetActionInvokedArgs actionInvokedArgs)
    {
        var action = actionInvokedArgs.Verb;
        if (action is not ("open" or "refresh")) return;
        LaunchApp(action);
        lock (gate) Update(actionInvokedArgs.WidgetContext.Id);
    }

    private void RefreshActive()
    {
        lock (gate)
            foreach (var id in widgets.Where(w => w.Value.Active).Select(w => w.Key).ToArray()) Update(id);
    }

    private void Update(string id)
    {
        if (!widgets.TryGetValue(id, out var entry)) return;
        try
        {
            var snapshot = WidgetSnapshotFile.Read(WidgetSnapshotFile.DefaultDirectory);
            WidgetManager.GetDefault().UpdateWidget(new WidgetUpdateRequestOptions(id) {
                Template = WidgetCard.Render(snapshot, entry.Size, DateTimeOffset.UtcNow), Data = "{}", CustomState = ""
            });
        }
        catch (COMException) { /* Host can unpin or terminate between a callback and its update. */ }
    }

    private static BoardWidgetSize Size(WidgetSize size) => size switch {
        WidgetSize.Small => BoardWidgetSize.Small, WidgetSize.Large => BoardWidgetSize.Large, _ => BoardWidgetSize.Medium
    };

    private static void LaunchApp(string action)
    {
        try
        {
            var executable = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "AIUsageViewer.exe"));
            if (!File.Exists(executable)) return;
            var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add("--windows-widget-action"); start.ArgumentList.Add(action);
            Process.Start(start)?.Dispose();
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException) { }
    }
}
