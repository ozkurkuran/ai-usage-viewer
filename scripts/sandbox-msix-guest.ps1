param([string]$Version='0.3.0-beta.3',[ValidateSet('Setup','Verify','Check')][string]$Phase='Setup')
# Runs only inside disposable Windows Sandbox: trusts a throwaway test certificate,
# installs the MSIX, checks activation/containerized data/StartupTask, then uninstalls.
if($env:USERNAME -ne 'WDAGUtilityAccount') { throw 'This script runs only in disposable Windows Sandbox.' }
$ErrorActionPreference='Stop'
if($Version -notmatch '^\d+\.\d+\.\d+(-[a-zA-Z0-9.]+)?$') { throw 'Invalid version.' }
$output='C:\ViewerOutput'
$stagePath=Join-Path $output 'msix-stage.json'
$resultPath=Join-Path $output 'msix-result.json'
$dashboardTitle='Ai UsageNest'
Add-Type @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
public static class ViewerLogonIdentity {
    [DllImport("advapi32.dll", SetLastError=true)]
    private static extern bool GetTokenInformation(IntPtr token, int informationClass, byte[] data, int length, out int required);
    public static string Current() {
        byte[] data=new byte[128]; int required;
        using(var identity=WindowsIdentity.GetCurrent()) {
            if(!GetTokenInformation(identity.Token,10,data,data.Length,out required)) throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
        }
        return BitConverter.ToUInt64(data,8).ToString("X16");
    }
}
[ComImport, Guid("2e941141-7f97-4756-ba1d-9decde894a3d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
public interface IViewerActivationManager {
    void ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string appUserModelId, [MarshalAs(UnmanagedType.LPWStr)] string arguments, int options, out uint processId);
}
[ComImport, Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
public class ViewerActivationManager {}
public static class ViewerActivation {
    // Same activation path as the Start menu; arguments keep the package identity.
    public static uint Launch(string appUserModelId, string arguments) {
        uint processId;
        ((IViewerActivationManager)new ViewerActivationManager()).ActivateApplication(appUserModelId,arguments,0,out processId);
        return processId;
    }
}
public static class ViewerWindows {
    private delegate bool EnumProc(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumProc callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int length);
    public static string[] VisibleTitles(uint processId) {
        var titles=new List<string>();
        EnumWindows(delegate(IntPtr window, IntPtr parameter) {
            uint owner; GetWindowThreadProcessId(window,out owner);
            if(owner==processId && IsWindowVisible(window)) { var text=new StringBuilder(256); GetWindowText(window,text,text.Capacity); titles.Add(text.ToString()); }
            return true;
        }, IntPtr.Zero);
        return titles.ToArray();
    }
}
'@
function Wait-For([scriptblock]$Condition,[int]$Seconds,[string]$Failure) {
    $deadline=[DateTime]::UtcNow.AddSeconds($Seconds)
    while(-not (& $Condition)) { if([DateTime]::UtcNow -gt $deadline) { throw $Failure };Start-Sleep -Milliseconds 500 }
}
function Get-ViewerPackage { Get-AppxPackage | Where-Object { $_.InstallLocation -and (Test-Path -LiteralPath (Join-Path $_.InstallLocation 'AIUsageViewer.exe')) } | Select-Object -First 1 }
function Write-Failure([string]$Message,$Checks) {
    @{passed=$false;version=$Version;phase=$Phase;checkedAt=[DateTime]::UtcNow.ToString('o');checks=@($Checks);error=$Message} | ConvertTo-Json | Set-Content -LiteralPath $resultPath -Encoding UTF8
}

$logonId=[ViewerLogonIdentity]::Current()
if($Phase -eq 'Verify') {
    # Do not block Windows while it processes its startup entries.
    Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File','C:\ViewerScripts\sandbox-msix-guest.ps1','-Phase','Check','-Version',$Version) -WindowStyle Hidden | Out-Null
    return
}
if($Phase -eq 'Setup') {
    if(Test-Path -LiteralPath $stagePath) { return }
    Start-Transcript -LiteralPath (Join-Path $output 'guest-log.txt') -Force | Out-Null
    $checks=@()
    try {
        $result=[ordered]@{ osBuild=(Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion').CurrentBuild;dotnetOnPath=[bool](Get-Command dotnet -ErrorAction SilentlyContinue) }
        $package=Get-ChildItem -LiteralPath 'C:\ViewerPackages' -Filter "AIUsageViewer-$Version-win-x64*-test-signed.msix" | Select-Object -First 1
        if(-not $package) { throw 'Test-signed MSIX not found.' }
        # Trust applies only inside this disposable guest.
        Import-Certificate -FilePath 'C:\ViewerPackages\AIUsageViewer-test-signing.cer' -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
        Add-AppxPackage -Path $package.FullName
        $installed=Get-ViewerPackage
        if(-not $installed) { throw 'Installed package not found.' }
        $checks+='test-signed MSIX installed per user'
        $aumid=$installed.PackageFamilyName+'!AIUsageViewer'
        $containerData=Join-Path $env:LOCALAPPDATA ('Packages\'+$installed.PackageFamilyName+'\LocalCache\Local\AiUsageViewer')

        $first=[ViewerActivation]::Launch($aumid,'')
        Wait-For { [ViewerWindows]::VisibleTitles($first) -contains $dashboardTitle } 120 'Start menu activation did not show the dashboard.'
        if(-not (Get-Process -Id $first).Path.StartsWith($installed.InstallLocation,[StringComparison]::OrdinalIgnoreCase)) { throw 'App did not run from the package.' }
        $checks+='Start menu activation shows the dashboard from the package'
        Wait-For { Test-Path -LiteralPath (Join-Path $containerData 'usage.db') } 60 'Packaged app data was not initialized.'
        if(Test-Path -LiteralPath (Join-Path $env:LOCALAPPDATA 'AiUsageViewer')) { throw 'App data was written outside the package container.' }
        $checks+='app data initialized in the package container'
        Wait-For { Test-Path -LiteralPath (Join-Path $containerData 'windows-widget.json') } 60 'Windows widget snapshot was not published.'
        $snapshot=Get-Content -LiteralPath (Join-Path $containerData 'windows-widget.json') -Raw | ConvertFrom-Json
        if($snapshot.Version -ne 1 -or -not $snapshot.PublishedAt) { throw 'Invalid Windows widget snapshot.' }
        $checks+='Windows widget display snapshot published in the package container'
        Stop-Process -Id $first -Force;Wait-For { -not (Get-Process -Id $first -ErrorAction SilentlyContinue) } 30 'App did not stop.'

        $validation=[ViewerActivation]::Launch($aumid,'--validate-package')
        Wait-For { -not (Get-Process -Id $validation -ErrorAction SilentlyContinue) } 120 'Package validation did not finish.'
        $report=Get-Content -LiteralPath (Join-Path $containerData 'package-validation.json') -Raw | ConvertFrom-Json
        Copy-Item -LiteralPath (Join-Path $containerData 'package-validation.json') -Destination $output
        Get-ChildItem -LiteralPath $containerData -Filter 'widget-*-probe.json' | Copy-Item -Destination $output
        if(-not $report.packaged -or $report.familyName -ne $installed.PackageFamilyName -or -not $report.startupEnabled) { throw 'StartupTask was not enabled.' }
        $checks+='StartupTask enabled through the app'
        if(-not $report.widgetRuntime -or -not $report.widgetProvider) { throw 'Windows widget runtime or packaged COM activation failed.' }
        $checks+='self-contained widget runtime and packaged IWidgetProvider COM activation'
        Get-Process 'AIUsageViewer.Widgets' -ErrorAction SilentlyContinue | Stop-Process -Force

        $once='HKCU:\Software\Microsoft\Windows\CurrentVersion\RunOnce'
        New-Item -Path $once -Force | Out-Null
        Set-ItemProperty -LiteralPath $once -Name 'AiUsageViewerMsixValidation' -Value ('powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\ViewerScripts\sandbox-msix-guest.ps1 -Phase Verify -Version '+$Version)
        $result.stage='ready-for-logoff';$result.version=$Version;$result.logonId=$logonId;$result.checks=$checks
        $result.packageFullName=$installed.PackageFullName;$result.packageFamilyName=$installed.PackageFamilyName
        $result.identityName=$installed.Name;$result.packageVersion=$installed.Version.ToString();$result.installLocation=$installed.InstallLocation
        $result.preparedAt=[DateTime]::UtcNow.ToString('o')
        $result | ConvertTo-Json | Set-Content -LiteralPath $stagePath -Encoding UTF8
        Stop-Transcript | Out-Null
        return
    } catch {
        Write-Failure $_.Exception.Message $checks
        Stop-Transcript | Out-Null
        Stop-Computer -Force
        return
    }
}

$stage=Get-Content -LiteralPath $stagePath -Raw | ConvertFrom-Json
$checks=@($stage.checks)
try {
    if(-not $stage.logonId -or $logonId -eq $stage.logonId) { throw 'No new Windows logon identity was observed.' }
    $app=$null
    Wait-For { $script:app=Get-Process AIUsageViewer -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($stage.installLocation,[StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1;[bool]$script:app } 180 'StartupTask did not launch the app after sign-in.'
    Start-Sleep -Seconds 5
    if($app.HasExited) { throw 'Started app exited.' }
    if([ViewerWindows]::VisibleTitles([uint32]$app.Id) -contains $dashboardTitle) { throw 'StartupTask launch opened the dashboard.' }
    $checks+='StartupTask launched the app in the background after a new Windows logon'
    Stop-Process -Id $app.Id -Force;Wait-For { -not (Get-Process -Id $app.Id -ErrorAction SilentlyContinue) } 30 'App did not stop.'
    Remove-AppxPackage -Package $stage.packageFullName
    if(Get-AppxPackage -Name $stage.identityName) { throw 'Package remained after uninstall.' }
    $checks+='uninstall removed the package'
    $containerRoot=Join-Path $env:LOCALAPPDATA ('Packages\'+$stage.packageFamilyName)
    [ordered]@{passed=$true;version=$Version;packageVersion=$stage.packageVersion;osBuild=$stage.osBuild;dotnetOnPath=$stage.dotnetOnPath;
        newWindowsLogonVerified=$true;checks=$checks;packageDataRemovedOnUninstall=-not(Test-Path -LiteralPath $containerRoot);
        startedAt=$stage.preparedAt;checkedAt=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath $resultPath -Encoding UTF8
} catch { Write-Failure $_.Exception.Message $checks }
Stop-Computer -Force
