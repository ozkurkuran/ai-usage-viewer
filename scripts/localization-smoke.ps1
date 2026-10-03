param(
    [Parameter(Mandatory)][string]$Executable,
    [string]$Output='artifacts/localization-smoke',
    [string[]]$Languages=@('en','tr','es','de','fr','pt','pt-BR','ru','nl','cs','it','pl')
)
$ErrorActionPreference='Stop'
$executablePath=(Resolve-Path -LiteralPath $Executable).Path
$outputPath=[IO.Path]::GetFullPath($Output)
$reports=@()
foreach($language in $Languages) {
    if($language -notin @('en','tr','es','de','fr','pt','pt-BR','ru','nl','cs','it','pl')) { throw "Unsupported language: $language" }
    $capture=Join-Path $outputPath $language
    $data=Join-Path $capture ('data-'+[Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $capture -Force | Out-Null
    $arguments='--demo --language '+$language+' --smoke-suite --screenshot "'+$capture+'" --data-dir "'+$data+'"'
    $process=Start-Process -FilePath $executablePath -ArgumentList $arguments -WindowStyle Hidden -PassThru
    $deadline=[DateTime]::UtcNow.AddMinutes(3)
    while(-not $process.WaitForExit(1000)) {
        if([DateTime]::UtcNow -gt $deadline) { throw "Smoke process timed out (PID $($process.Id)); inspect before stopping it." }
    }
    if($process.ExitCode -ne 0) {
        $errorFile=Join-Path $data 'startup-error.txt'
        if(Test-Path -LiteralPath $errorFile) { Get-Content -LiteralPath $errorFile }
        throw "Language $language exited $($process.ExitCode)."
    }
    $report=Get-Content -LiteralPath (Join-Path $capture 'smoke-report.json') -Raw | ConvertFrom-Json
    if(-not $report.passed) { throw "Smoke failed for $language" }
    $reports+=[pscustomobject]@{language=$language;passed=$report.passed;checks=$report.checks.Count}
    Write-Output "Language $language passed."
}
$reports | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputPath 'languages.json') -Encoding utf8
