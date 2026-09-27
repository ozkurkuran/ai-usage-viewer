param([string]$Dotnet='dotnet')
$ErrorActionPreference='Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:MSBUILDDISABLENODEREUSE='1'
& $Dotnet test (Join-Path $PSScriptRoot '..\AiUsageViewer.slnx') -c Release --disable-build-servers -m:1 -p:UseSharedCompilation=false --logger 'console;verbosity=normal'
if($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
