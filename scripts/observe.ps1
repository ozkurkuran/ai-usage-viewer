param([Parameter(Mandatory)][string]$Executable,[Parameter(Mandatory)][string]$DataDirectory,[ValidateRange(60,43200)][int]$Seconds=1800)
$ErrorActionPreference='Stop'
$exe=(Resolve-Path -LiteralPath $Executable).Path
$data=[IO.Path]::GetFullPath($DataDirectory)
New-Item -ItemType Directory -Path $data -Force | Out-Null
$arguments='--background --observe-seconds '+$Seconds+' --data-dir "'+$data+'"'
$started=[DateTime]::UtcNow
$process=Start-Process -FilePath $exe -ArgumentList $arguments -WindowStyle Hidden -PassThru
[pscustomobject]@{ProcessId=$process.Id;StartedAt=$started.ToString('o');DataDirectory=$data;Seconds=$Seconds} | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $data 'observation-process.json')
$deadline=$started.AddSeconds($Seconds+600)
while(-not $process.WaitForExit(1000)) {
    if([DateTime]::UtcNow -gt $deadline) { throw "Observation timeout; inspect the existing process $($process.Id) before taking action." }
}
if($process.ExitCode -ne 0) { throw "Observation exited $($process.ExitCode)." }
$report=Get-Content -LiteralPath (Join-Path $data 'runtime-observation.json') -Raw | ConvertFrom-Json
if(-not $report.complete -or [DateTimeOffset]$report.startedAt -lt $started) { throw 'Missing or stale observation report.' }
$first=$report.samples[0];$last=$report.samples[-1]
[pscustomobject]@{ElapsedSeconds=$report.elapsedSeconds;Samples=$report.samples.Count;CompletedScans=$last.completedScans-$first.completedScans;
    ChangedFiles=$last.changedFiles-$first.changedFiles;CpuSeconds=$last.cpuSeconds-$first.cpuSeconds;
    PrivateBytesStart=$first.privateBytes;PrivateBytesEnd=$last.privateBytes;HandlesStart=$first.handles;HandlesEnd=$last.handles;
    MaximumDispatcherDelaySeconds=$report.maximumDispatcherDelaySeconds;ProviderStates=$report.providerStates} | ConvertTo-Json -Depth 5
