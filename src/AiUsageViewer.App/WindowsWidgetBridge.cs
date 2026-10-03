using AiUsageViewer.Application.WindowsWidgets;
using System.Windows.Threading;

namespace AiUsageViewer.App;

internal sealed class WindowsWidgetBridge : IDisposable
{
    private readonly List<EventWaitHandle> events = [];
    private readonly List<RegisteredWaitHandle> waits = [];

    public WindowsWidgetBridge(string directory, Dispatcher dispatcher, Action open, Func<Task> refresh)
    {
        foreach (var action in new[] { "open", "refresh" })
        {
            var signal = new EventWaitHandle(false, EventResetMode.AutoReset, WidgetAppSignals.Name(directory, action));
            events.Add(signal);
            waits.Add(ThreadPool.RegisterWaitForSingleObject(signal, (_, _) => {
                if (!dispatcher.HasShutdownStarted)
                    dispatcher.BeginInvoke(async () => {
                        if (action == "open") open();
                        else try { await refresh(); } catch (OperationCanceledException) { }
                    });
            }, null, Timeout.Infinite, false));
        }
    }

    public void Dispose()
    {
        foreach (var wait in waits) wait.Unregister(null);
        foreach (var signal in events) signal.Dispose();
    }
}
