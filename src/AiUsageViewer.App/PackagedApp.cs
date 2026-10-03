using System.Runtime.InteropServices;
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;

namespace AiUsageViewer.App;

// MSIX (Microsoft Store) integration. Writes to HKCU\...\Run are virtualized inside a
// package, so packaged builds use the manifest's StartupTask instead.
internal static class PackagedApp
{
    public const string StartupTaskId="AiUsageViewerStartup";
    private const int AppModelErrorNoPackage=15700;
    public static bool IsPackaged { get; }=DetectPackage();
    public static string? FamilyName=>IsPackaged?Package.Current.Id.FamilyName:null;

    private static bool DetectPackage()
    {
        var length=0;
        return GetCurrentPackageFullName(ref length,null)!=AppModelErrorNoPackage;
    }

    // A StartupTask cannot pass --background; the activation kind identifies it instead.
    public static bool LaunchedByStartupTask()
    {
        if(!IsPackaged) return false;
        try { return AppInstance.GetActivatedEventArgs()?.Kind==ActivationKind.StartupTask; }
        catch(Exception ex) when(ex is COMException or InvalidOperationException) { return false; }
    }

    public static async Task<bool> IsStartupEnabledAsync()
    {
        var task=await StartupTask.GetAsync(StartupTaskId);
        return task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
    }

    // Returns the resulting state. Windows keeps a user's or policy's decision, so enabling
    // can legitimately return false; the caller then explains where to change it.
    public static async Task<bool> SetStartupEnabledAsync(bool enabled)
    {
        var task=await StartupTask.GetAsync(StartupTaskId);
        if(enabled&&task.State==StartupTaskState.Disabled) await task.RequestEnableAsync();
        else if(!enabled&&task.State==StartupTaskState.Enabled) task.Disable();
        return await IsStartupEnabledAsync();
    }

    [DllImport("kernel32.dll",EntryPoint="GetCurrentPackageFullName",ExactSpelling=true,CharSet=CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength,char[]? packageFullName);
}
