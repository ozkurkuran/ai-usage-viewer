param([string]$Dotnet='dotnet',[string]$Iscc='',[string]$Version='0.3.0-beta.2',[switch]$SkipRestore)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactRoot=Join-Path $repo 'artifacts'
$publish=Join-Path $artifactRoot 'publish\win-x64'
$release=Join-Path $artifactRoot 'release'
$project=Join-Path $repo 'src\AiUsageViewer.App\AiUsageViewer.App.csproj'
if($Version -notmatch '^\d+\.\d+\.\d+(-[a-zA-Z0-9.]+)?$') { throw 'Invalid version.' }
# Refuse to package stale files. Deletion is limited to the resolved build output.
if(Test-Path -LiteralPath $publish) {
    $resolved=(Resolve-Path -LiteralPath $publish).Path
    if(-not $resolved.StartsWith($artifactRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected output path.' }
    if((Get-Item -LiteralPath $publish).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Build output cannot be a reparse point.' }
    Remove-Item -LiteralPath $publish -Recurse -Force
}
New-Item -ItemType Directory -Path $publish,$release -Force | Out-Null
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE='true'
$argsForPublish=@('publish',$project,'-c','Release','-r','win-x64','--self-contained','true','--disable-build-servers','-m:1','-p:UseSharedCompilation=false',"-p:Version=$Version",'-p:DebugType=None','-p:DebugSymbols=false','-o',$publish)
if($SkipRestore) { $argsForPublish+='--no-restore' }
& $Dotnet @argsForPublish
if($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
Copy-Item -LiteralPath (Join-Path $repo 'LICENSE'),(Join-Path $repo 'README.md'),(Join-Path $repo 'THIRD_PARTY_NOTICES.md') -Destination $publish
Copy-Item -LiteralPath (Join-Path $repo 'docs\licenses') -Destination (Join-Path $publish 'licenses') -Recurse
Copy-Item -LiteralPath (Join-Path $repo 'docs') -Destination (Join-Path $publish 'docs') -Recurse
$forbidden=Get-ChildItem -LiteralPath $publish -Recurse -File | Where-Object { $_.Extension -in '.db','.secrets','.jsonl' -or $_.Name -eq 'settings.json' }
if($forbidden) { throw 'Private data unexpectedly present in publish output.' }
if(-not(Test-Path -LiteralPath (Join-Path $publish 'coreclr.dll'))) { throw 'Self-contained runtime is missing.' }
$manifest=Get-ChildItem -LiteralPath $publish -File -Recurse | Sort-Object FullName | ForEach-Object {
    [pscustomobject]@{ path=[IO.Path]::GetRelativePath($publish,$_.FullName).Replace('\','/');sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() }
}
$manifest | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $publish 'files.sha256.json') -Encoding utf8
$zip=Join-Path $release "AIUsageViewer-$Version-win-x64-portable.zip"
Compress-Archive -Path (Join-Path $publish '*') -DestinationPath $zip -Force
if($Iscc) {
    & $Iscc '/Qp' "/DAppVersion=$Version" "/DPublishDir=$publish" "/DReleaseDir=$release" (Join-Path $repo 'installer\AIUsageViewer.iss')
    if($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
}
$checksums=Get-ChildItem -LiteralPath $release -File | Where-Object { $_.Name -like "AIUsageViewer-$Version-*" -and $_.Extension -in '.zip','.exe' } | Sort-Object Name | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+$_.Name }
$checksums | Set-Content -LiteralPath (Join-Path $release 'SHA256SUMS.txt') -Encoding utf8
Get-ChildItem -LiteralPath $release -File | Select-Object Name,Length
