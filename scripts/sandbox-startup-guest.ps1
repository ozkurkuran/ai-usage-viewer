param([string]$Version='0.3.0-beta.2',[ValidateSet('Setup','Verify','Check')][string]$Phase='Setup')
if($env:USERNAME -ne 'WDAGUtilityAccount') { throw 'This script runs only in disposable Windows Sandbox.' }
$ErrorActionPreference='Stop'
if($Version -notmatch '^\d+\.\d+\.\d+(-[a-zA-Z0-9.]+)?$') { throw 'Invalid version.' }
$output='C:\ViewerOutput'
$stagePath=Join-Path $output 'startup-stage.json'
$resultPath=Join-Path $output 'startup-result.json'
$portable=Join-Path $env:USERPROFILE 'Startup Viewer Portable'
$exe=Join-Path $portable 'AIUsageViewer.exe'
$data=Join-Path $env:LOCALAPPDATA 'AI Usage Viewer Startup Check'
try {
    Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Security.Principal;
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
'@
    $logonId=[ViewerLogonIdentity]::Current()
    if($Phase -eq 'Verify') {
        # Do not block Windows while it processes its startup entries.
        Start-Process -FilePath (Join-Path $PSHOME 'powershell.exe') -ArgumentList @('-NoProfile','-ExecutionPolicy','Bypass','-File','C:\ViewerScripts\sandbox-startup-guest.ps1','-Phase','Check','-Version',$Version) -WindowStyle Hidden | Out-Null
        return
    }
    if($Phase -eq 'Setup') {
        if(Test-Path -LiteralPath $stagePath) { return }
        Add-Type -AssemblyName System.IO.Compression.FileSystem
        [IO.Compression.ZipFile]::ExtractToDirectory("C:\ViewerPackages\AIUsageViewer-$Version-win-x64-portable.zip",$portable)
        New-Item -ItemType Directory -Path $data -Force | Out-Null
        @{Version=1;Language='en';StartWithWindows=$true;ShowWidgetOnLaunch=$false;NotificationsEnabled=$false;Accounts=@();Sources=@()} |
            ConvertTo-Json | Set-Content -LiteralPath (Join-Path $data 'settings.json') -Encoding UTF8
        $runKey='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
        New-Item -Path $runKey -Force | Out-Null
        # Exact command shape produced by WindowsStartup.BuildCommand, including spaces.
        Set-ItemProperty -LiteralPath $runKey -Name 'AiUsageViewer' -Value ('"'+$exe+'" --background --data-dir "'+$data+'"')
        $once='HKCU:\Software\Microsoft\Windows\CurrentVersion\RunOnce'
        New-Item -Path $once -Force | Out-Null
        Set-ItemProperty -LiteralPath $once -Name 'AiUsageViewerValidation' -Value ('powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\ViewerScripts\sandbox-startup-guest.ps1 -Phase Verify -Version '+$Version)
        if(Get-Process AIUsageViewer -ErrorAction SilentlyContinue) { throw 'App unexpectedly running before sign-in test.' }
        @{stage='ready-for-logoff';version=$Version;logonId=$logonId;preparedAt=[DateTime]::UtcNow.ToString('o')} |
            ConvertTo-Json | Set-Content -LiteralPath $stagePath -Encoding UTF8
        return
    }
    $stage=Get-Content -LiteralPath $stagePath -Raw | ConvertFrom-Json
    if(-not $stage.logonId -or $logonId -eq $stage.logonId) { throw 'No new Windows logon identity was observed.' }
    $deadline=[DateTime]::UtcNow.AddMinutes(2)
    $app=$null
    do {
        $app=Get-Process AIUsageViewer -ErrorAction SilentlyContinue | Where-Object Path -eq $exe | Select-Object -First 1
        if(-not $app) { Start-Sleep -Seconds 1 }
    } while(-not $app -and [DateTime]::UtcNow -lt $deadline)
    if(-not $app) { throw 'Windows Run entry did not launch the app after sign-in.' }
    Start-Sleep -Seconds 5
    $app.Refresh()
    if($app.HasExited -or -not(Test-Path -LiteralPath (Join-Path $data 'usage.db'))) { throw 'Started app did not initialize.' }
    if($app.MainWindowHandle -ne [IntPtr]::Zero) { throw 'Background startup unexpectedly showed a main window.' }
    @{passed=$true;version=$Version;checkedAt=[DateTime]::UtcNow.ToString('o');newWindowsLogonVerified=$true;
        launchSource='HKCU Run';pathsWithSpaces=$true;dataInitialized=$true;visibleMainWindow=$false} |
        ConvertTo-Json | Set-Content -LiteralPath $resultPath -Encoding UTF8
} catch {
    @{passed=$false;version=$Version;checkedAt=[DateTime]::UtcNow.ToString('o');error=$_.Exception.Message} |
        ConvertTo-Json | Set-Content -LiteralPath $resultPath -Encoding UTF8
}
if($Phase -eq 'Check') { Stop-Computer -Force }
