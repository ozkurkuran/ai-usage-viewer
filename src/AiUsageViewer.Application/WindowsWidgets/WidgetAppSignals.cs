using System.Security.Cryptography;
using System.Text;
using System.Runtime.Versioning;

namespace AiUsageViewer.Application.WindowsWidgets;

[SupportedOSPlatform("windows")]
public static class WidgetAppSignals
{
    public static string Name(string directory, string action)
    {
        if (action is not ("open" or "refresh")) throw new ArgumentException("Unknown widget action.", nameof(action));
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Path.GetFullPath(directory).ToLowerInvariant())))[..24];
        return "Local\\AiUsageViewer.Widget." + hash + "." + action;
    }

    public static bool Send(string directory, string action)
    {
        if (action == "background") return true;
        try { using var signal = EventWaitHandle.OpenExisting(Name(directory, action)); return signal.Set(); }
        catch (WaitHandleCannotBeOpenedException) { return false; }
    }
}
