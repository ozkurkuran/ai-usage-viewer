param([string]$Dotnet='dotnet',[string]$Output='',[string]$Version='',[switch]$SkipRestore)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactRoot=Join-Path $repo 'artifacts'
if(-not $Output) { $Output=Join-Path $artifactRoot 'windows-widgets\publish' }
$Output=[IO.Path]::GetFullPath($Output)
if(-not $Output.StartsWith($artifactRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Output must be under artifacts.' }
if(-not $Version) { $Version=([xml](Get-Content -LiteralPath (Join-Path $repo 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version }
if($Version -notmatch '^\d+\.\d+\.\d+(-[a-zA-Z0-9.]+)?$') { throw 'Invalid version.' }
if(Test-Path -LiteralPath $Output) {
    $resolved=(Resolve-Path -LiteralPath $Output).Path
    if(-not $resolved.StartsWith($artifactRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Unexpected output path.' }
    if((Get-Item -LiteralPath $Output).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Output cannot be a reparse point.' }
    Remove-Item -LiteralPath $Output -Recurse -Force
}
$publishArgs=@('publish',(Join-Path $repo 'src\AiUsageViewer.Widgets\AiUsageViewer.Widgets.csproj'),'-c','Release','-r','win-x64','--self-contained','true','--disable-build-servers','-m:1','-p:UseSharedCompilation=false',"-p:Version=$Version",'-p:DebugType=None','-p:DebugSymbols=false','-o',$Output)
if($SkipRestore) { $publishArgs+='--no-restore' }
& $Dotnet @publishArgs
if($LASTEXITCODE -ne 0) { throw 'Widget provider publish failed.' }
foreach($required in @('AIUsageViewer.Widgets.exe','coreclr.dll','Microsoft.Windows.Widgets.dll')) {
    if(-not(Test-Path -LiteralPath (Join-Path $Output $required))) { throw "Widget dependency missing: $required" }
}
$packageCache=if($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
$license=Join-Path $packageCache 'microsoft.windowsappsdk.widgets\2.0.5\license.txt'
Copy-Item -LiteralPath $license -Destination (Join-Path $Output 'WindowsAppSDK-LICENSE.txt')
