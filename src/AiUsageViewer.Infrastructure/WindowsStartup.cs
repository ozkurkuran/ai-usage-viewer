using Microsoft.Win32;

namespace AiUsageViewer.Infrastructure;
public static class WindowsStartup
{
    private const string Key=@"Software\Microsoft\Windows\CurrentVersion\Run";
    public static string BuildCommand(string executable,string dataDirectory)
    {
        if(!Path.IsPathFullyQualified(executable)||!Path.IsPathFullyQualified(dataDirectory)||
            executable.Contains('"')||dataDirectory.Contains('"')||!executable.EndsWith(".exe",StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Absolute application and data paths are required.");
        return "\""+executable+"\" --background --data-dir \""+Path.TrimEndingDirectorySeparator(dataDirectory)+"\"";
    }
    public static void SetEnabled(bool enabled,string executable,string dataDirectory)
    {
        using var key=Registry.CurrentUser.CreateSubKey(Key,true);
        if(enabled) key.SetValue("AiUsageViewer",BuildCommand(executable,dataDirectory),RegistryValueKind.String);
        else key.DeleteValue("AiUsageViewer",false);
    }
}
