param([string]$Output='artifacts/sandbox',[string]$Version='0.3.0-beta.2',[ValidateSet('lifecycle','startup')][string]$Scenario='lifecycle')
$ErrorActionPreference='Stop'
if($Version -notmatch '^\d+\.\d+\.\d+(-[a-zA-Z0-9.]+)?$') { throw 'Invalid version.' }
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$outputPath=[IO.Path]::GetFullPath($Output)
foreach($marker in @('sandbox-id.txt','sandbox-result.json','startup-stage.json','startup-result.json')) {
    if(Test-Path -LiteralPath (Join-Path $outputPath $marker)) { throw 'Choose a fresh Sandbox output directory.' }
}
New-Item -ItemType Directory -Path $outputPath -Force | Out-Null
$escape=[System.Security.SecurityElement]
$packages=$escape::Escape((Join-Path $repo 'artifacts\release'))
$scripts=$escape::Escape($PSScriptRoot)
$results=$escape::Escape($outputPath)
$guestScript=if($Scenario -eq 'startup') { 'sandbox-startup-guest.ps1' } else { 'sandbox-guest.ps1' }
$config=@"
<Configuration>
  <Networking>Disable</Networking><ClipboardRedirection>Disable</ClipboardRedirection>
  <AudioInput>Disable</AudioInput><VideoInput>Disable</VideoInput><PrinterRedirection>Disable</PrinterRedirection>
  <vGPU>Disable</vGPU><MemoryInMB>4096</MemoryInMB>
  <MappedFolders>
    <MappedFolder><HostFolder>$packages</HostFolder><SandboxFolder>C:\ViewerPackages</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$scripts</HostFolder><SandboxFolder>C:\ViewerScripts</SandboxFolder><ReadOnly>true</ReadOnly></MappedFolder>
    <MappedFolder><HostFolder>$results</HostFolder><SandboxFolder>C:\ViewerOutput</SandboxFolder><ReadOnly>false</ReadOnly></MappedFolder>
  </MappedFolders>
  <LogonCommand><Command>powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\ViewerScripts\$guestScript -Version $Version</Command></LogonCommand>
</Configuration>
"@
$configuration=Join-Path $outputPath 'test.wsb'
Set-Content -LiteralPath $configuration -Value $config -Encoding UTF8
$cli=(Get-Command wsb.exe -ErrorAction Stop).Source
$id=[Guid]::NewGuid().ToString()
Set-Content -LiteralPath (Join-Path $outputPath 'sandbox-id.txt') -Value $id
& $cli start --id $id --config $config --raw
if($LASTEXITCODE -ne 0) { throw 'Sandbox creation failed.' }
$process=Start-Process -FilePath $cli -ArgumentList @('connect','--id',$id) -WindowStyle Hidden -PassThru
$resultName=if($Scenario -eq 'startup') { 'startup-result.json' } else { 'sandbox-result.json' }
[pscustomobject]@{ SandboxId=$id; ClientProcessId=$process.Id; Result=(Join-Path $outputPath $resultName) }
