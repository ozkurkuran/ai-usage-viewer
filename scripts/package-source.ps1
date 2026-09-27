param([string]$Version='0.3.0-beta.2')
$ErrorActionPreference='Stop'
if($Version -notmatch '^\d+\.\d+\.\d+(-[a-zA-Z0-9.]+)?$') { throw 'Invalid version.' }
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$release=Join-Path $repo 'artifacts\release'
New-Item -ItemType Directory -Path $release -Force | Out-Null
# Includes the current working tree, so an unpublished checkout can be reviewed.
# Git exclusions keep local data/build products out; explicit boundaries fail closed.
$paths=@(& git -C $repo ls-files --cached --others --exclude-standard | Sort-Object -Unique)
if($LASTEXITCODE -ne 0 -or $paths.Count -eq 0) { throw 'Cannot enumerate source checkout.' }
foreach($relative in $paths) {
    if($relative -match '(^|/)(\.git|bin|obj|artifacts|local|TestResults)/|(^|/)settings\.json$|\.(db|db-wal|db-shm|secrets|jsonl)$') { throw 'Non-source file refused.' }
    $full=[IO.Path]::GetFullPath((Join-Path $repo $relative))
    if(-not $full.StartsWith($repo+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Source path escaped checkout.' }
    if((Get-Item -LiteralPath $full).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Source links are not packaged.' }
}
$archive=Join-Path $release "AIUsageViewer-$Version-source.zip"
$temporary=$archive+'.tmp'
$stream=[IO.File]::Open($temporary,[IO.FileMode]::Create,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
try {
    $zip=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create,$true)
    try { foreach($relative in $paths) { [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,(Join-Path $repo $relative),$relative,[IO.Compression.CompressionLevel]::Optimal) | Out-Null } }
    finally { $zip.Dispose() }
} finally { $stream.Dispose() }
Move-Item -LiteralPath $temporary -Destination $archive -Force
$checksums=Get-ChildItem -LiteralPath $release -File | Where-Object { $_.Name -like "AIUsageViewer-$Version-*" -and $_.Extension -in '.zip','.exe' } | Sort-Object Name | ForEach-Object { (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+$_.Name }
$checksums | Set-Content -LiteralPath (Join-Path $release 'SHA256SUMS.txt') -Encoding utf8
[pscustomobject]@{ Archive=$archive; SourceFiles=$paths.Count; Bytes=(Get-Item -LiteralPath $archive).Length }
