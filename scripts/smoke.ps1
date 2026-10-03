param([Parameter(Mandatory)][string]$Executable,[string]$Output='artifacts/smoke',[switch]$Suite,[string]$DataDirectory='',[string]$Language='',[switch]$Views)
$ErrorActionPreference='Stop'
$executablePath=(Resolve-Path -LiteralPath $Executable).Path
$outputPath=[IO.Path]::GetFullPath($Output)
$dataPath=if($DataDirectory) { [IO.Path]::GetFullPath($DataDirectory) } else { Join-Path $outputPath ('data-'+[Guid]::NewGuid().ToString('N')) }
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
$arguments='--demo --screenshot "'+$outputPath+'" --data-dir "'+$dataPath+'"'
if($Suite) { $arguments+=' --smoke-suite' }
if($Language) { $arguments+=' --language '+$Language }
# Every view in dark and light at 100/150/200 % (needs -Suite).
if($Views) { $arguments+=' --views' }
$process=Start-Process -FilePath $executablePath -ArgumentList $arguments -WindowStyle Hidden -PassThru
$deadline=[DateTime]::UtcNow.AddMinutes($(if($Views) { 10 } else { 3 }))
while(-not $process.WaitForExit(1000)) {
    if([DateTime]::UtcNow -gt $deadline) { throw "Smoke process timed out (PID $($process.Id)); inspect before stopping it." }
}
if($process.ExitCode -ne 0) {
    $errorFile=Join-Path $dataPath 'startup-error.txt'
    if(Test-Path -LiteralPath $errorFile) { Get-Content -LiteralPath $errorFile }
    throw "Smoke process exited $($process.ExitCode)."
}
foreach($name in 'dashboard.png','widget.png','tray.png') {
    if(-not(Test-Path -LiteralPath (Join-Path $outputPath $name))) { throw "Missing screenshot: $name" }
}
if($Suite) {
    $report=Get-Content -LiteralPath (Join-Path $outputPath 'smoke-report.json') -Raw | ConvertFrom-Json
    if(-not $report.passed) { throw 'Smoke suite failed.' }
    $report | ConvertTo-Json -Depth 5
}
'Smoke passed.'
