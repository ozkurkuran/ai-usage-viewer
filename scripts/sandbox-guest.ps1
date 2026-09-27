param([string]$Version='0.3.0-beta.2')
# Run only as the logon command inside disposable Windows Sandbox.
if($env:USERNAME -ne 'WDAGUtilityAccount') { throw 'This script must run only inside Windows Sandbox.' }
$ErrorActionPreference='Stop'
if($Version -notmatch '^\d+\.\d+\.\d+(-[a-zA-Z0-9.]+)?$') { throw 'Invalid version.' }
$output='C:\ViewerOutput'
Start-Transcript -LiteralPath (Join-Path $output 'guest-log.txt') -Force | Out-Null
$result=[ordered]@{ passed=$false; version=$Version; startedAt=[DateTime]::UtcNow.ToString('o'); checks=@() }
function Run-Checked([string]$File,[string[]]$Arguments) {
    $p=Start-Process -FilePath $File -ArgumentList $Arguments -WindowStyle Hidden -PassThru
    $deadline=[DateTime]::UtcNow.AddMinutes(3)
    while(-not $p.WaitForExit(1000)) { if([DateTime]::UtcNow -gt $deadline) { throw 'Guest process timed out.' } }
    if($p.ExitCode -ne 0) { throw ('Guest process exit: '+$p.ExitCode) }
}
try {
    $result.osBuild=(Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion').CurrentBuild
    $result.dotnetOnPath=[bool](Get-Command dotnet -ErrorAction SilentlyContinue)
    $portable=Join-Path $env:USERPROFILE 'ViewerPortable'
    $archive=Get-Item -LiteralPath "C:\ViewerPackages\AIUsageViewer-$Version-win-x64-portable.zip"
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::ExtractToDirectory($archive.FullName,$portable)
    $manifest=Get-Content (Join-Path $portable 'files.sha256.json') -Raw | ConvertFrom-Json
    foreach($entry in $manifest) {
        if((Get-FileHash -LiteralPath (Join-Path $portable $entry.path) -Algorithm SHA256).Hash -ne $entry.sha256) { throw 'Package manifest mismatch.' }
    }
    $result.checks+='portable manifest verified'
    $data=Join-Path $env:LOCALAPPDATA 'AiUsageViewer.SandboxDemo'
    & C:\ViewerScripts\smoke.ps1 -Executable (Join-Path $portable 'AIUsageViewer.exe') -Output (Join-Path $output 'portable') -Suite -DataDirectory $data
    $first=Get-Content (Join-Path $output 'portable\smoke-report.json') -Raw | ConvertFrom-Json
    if(-not $first.runtimeDirectory.StartsWith($portable,[StringComparison]::OrdinalIgnoreCase)) { throw 'Application used a host runtime.' }
    $result.checks+='portable runs with its included runtime'
    $installer=Get-Item -LiteralPath "C:\ViewerPackages\AIUsageViewer-$Version-win-x64-Setup.exe"
    $installed=Join-Path $env:LOCALAPPDATA 'Programs\AI Usage Viewer'
    $installArguments=@('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART','/CURRENTUSER','/NOICONS',('/DIR="'+$installed+'"'))
    $previous='C:\ViewerPackages\AIUsageViewer-0.3.0-beta.1-win-x64-Setup.exe'
    if($Version -ne '0.3.0-beta.1' -and (Test-Path -LiteralPath $previous)) {
        Run-Checked $previous $installArguments
        & C:\ViewerScripts\smoke.ps1 -Executable (Join-Path $installed 'AIUsageViewer.exe') -Output (Join-Path $output 'previous-version') -Suite -DataDirectory $data
        $result.checks+='previous beta.1 installed with existing history before upgrade'
    }
    Run-Checked $installer.FullName $installArguments
    if((Get-Item -LiteralPath (Join-Path $installed 'AIUsageViewer.exe')).VersionInfo.ProductVersion -ne $Version) { throw 'Installed version mismatch.' }
    $result.checks+='per-user installer completed'
    & C:\ViewerScripts\smoke.ps1 -Executable (Join-Path $installed 'AIUsageViewer.exe') -Output (Join-Path $output 'installed') -Suite -DataDirectory $data
    $second=Get-Content (Join-Path $output 'installed\smoke-report.json') -Raw | ConvertFrom-Json
    if($first.storedRecords -ne $second.storedRecords -or $first.storedTokens -ne $second.storedTokens) { throw 'Usage changed after installation/restart.' }
    $result.checks+='existing history retained after installation and restart'
    Run-Checked $installer.FullName $installArguments
    $result.checks+='same-version reinstall completed'
    $runKey='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    New-Item -Path $runKey -Force | Out-Null
    Set-ItemProperty -LiteralPath $runKey -Name 'AiUsageViewer' -Value ('"'+(Join-Path $installed 'AIUsageViewer.exe')+'" --background')
    Run-Checked (Join-Path $installed 'unins000.exe') @('/VERYSILENT','/SUPPRESSMSGBOXES','/NORESTART')
    if(Test-Path -LiteralPath (Join-Path $installed 'AIUsageViewer.exe')) { throw 'Executable remained after uninstall.' }
    if((Get-ItemProperty -LiteralPath $runKey -ErrorAction SilentlyContinue).AiUsageViewer) { throw 'Startup entry remained after uninstall.' }
    if(-not(Test-Path -LiteralPath (Join-Path $data 'usage.db'))) { throw 'Uninstall removed user history.' }
    $result.checks+='uninstall removed app and its startup entry, preserving user data'
    $result.storedRecords=$first.storedRecords;$result.storedTokens=$first.storedTokens
    $result.passed=$true
} catch { $result.error=$_.Exception.Message }
finally {
    $result.finishedAt=[DateTime]::UtcNow.ToString('o')
    $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $output 'sandbox-result.json') -Encoding UTF8
    Stop-Transcript | Out-Null
}
# Shutdown only this disposable guest. The guard above rejects host execution.
Stop-Computer -Force
