param(
    [string]$Version='',
    [string]$PublishDir='',
    [string]$Output='',
    [string]$IdentityName='',
    [string]$Publisher='',
    [string]$PublisherDisplayName='',
    [string]$PackageVersion='',
    [string]$WindowsSdkBin='',
    [string]$Dotnet='dotnet',
    [string]$WidgetPublishDir='',
    [switch]$TestSign
)
# Builds an MSIX package from the self-contained publish output of package.ps1.
# The unsigned package is the Microsoft Store upload; Partner Center signs it.
# -TestSign also writes a package signed by a throwaway certificate for clean-machine tests.
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactRoot=Join-Path $repo 'artifacts'
if(-not $Version) { $Version=([xml](Get-Content -LiteralPath (Join-Path $repo 'Directory.Build.props') -Raw)).Project.PropertyGroup.Version }
if($Version -notmatch '^\d+\.\d+\.\d+(-[a-zA-Z0-9.]+)?$') { throw 'Invalid version.' }
if(-not $PublishDir) { $PublishDir=Join-Path $artifactRoot 'publish\win-x64' }
if(-not $Output) { $Output=Join-Path $artifactRoot 'msix' }
$PublishDir=[IO.Path]::GetFullPath($PublishDir);$Output=[IO.Path]::GetFullPath($Output)

# Store package versions are four numeric parts with a zero revision. Pre-releases are
# ordered before their final release: X.Y.Z-beta.N -> X.Y.(Z*100+N).0, X.Y.Z -> X.Y.(Z*100+99).0.
if(-not $PackageVersion) {
    if($Version -notmatch '^(\d+)\.(\d+)\.(\d+)(?:-beta\.(\d+))?$') { throw 'Only X.Y.Z and X.Y.Z-beta.N map automatically; pass -PackageVersion.' }
    $pre=if($Matches[4]) { [int]$Matches[4] } else { 99 }
    if($Matches[4] -and ($pre -lt 1 -or $pre -gt 98)) { throw 'Beta number must be 1-98.' }
    $PackageVersion='{0}.{1}.{2}.0' -f [int]$Matches[1],[int]$Matches[2],([int]$Matches[3]*100+$pre)
}
if($PackageVersion -notmatch '^\d{1,5}\.\d{1,5}\.\d{1,5}\.0$') { throw 'Package version must be A.B.C.0 for the Store.' }

$identityFile=Join-Path $repo 'packaging\msix\identity.json'
$identity=Get-Content -LiteralPath $identityFile -Raw | ConvertFrom-Json
if(-not $IdentityName) { $IdentityName=$identity.identityName }
if(-not $Publisher) { $Publisher=$identity.publisher }
if(-not $PublisherDisplayName) { $PublisherDisplayName=$identity.publisherDisplayName }
$displayName=if($identity.displayName) { $identity.displayName } else { 'Ai UsageNest' }
if($displayName.Length -gt 256 -or $displayName -match '[<>"&]') { throw 'Invalid display name.' }
$storeIdentity=[bool]($IdentityName -and $Publisher -and $PublisherDisplayName)
if(-not $storeIdentity) {
    if($IdentityName -or $Publisher -or $PublisherDisplayName) { throw 'Set all three identity values, or none for a local test build.' }
    $IdentityName='AIUsageViewer.LocalTest';$Publisher='CN=Ai UsageNest Local Test';$PublisherDisplayName='Mikrofab (local test)'
    Write-Warning 'packaging/msix/identity.json is empty: building a local test identity that Partner Center will reject.'
}
if($IdentityName -notmatch '^[A-Za-z0-9.-]{3,50}$') { throw 'Invalid package identity name.' }
try { $null=[Security.Cryptography.X509Certificates.X500DistinguishedName]::new($Publisher) } catch { throw 'Publisher must be a distinguished name such as CN=...' }

$exe=Join-Path $PublishDir 'AIUsageViewer.exe'
if(-not(Test-Path -LiteralPath $exe) -or -not(Test-Path -LiteralPath (Join-Path $PublishDir 'coreclr.dll'))) { throw 'Self-contained publish output not found; run package.ps1 first.' }
if(((Get-Item -LiteralPath $exe).VersionInfo.ProductVersion -split '\+')[0] -ne $Version) { throw 'Publish output version does not match; run package.ps1 for this version.' }
if(-not $WidgetPublishDir) {
    $WidgetPublishDir=Join-Path $artifactRoot 'windows-widgets\publish'
    & (Join-Path $PSScriptRoot 'package-widgets.ps1') -Dotnet $Dotnet -Version $Version -Output $WidgetPublishDir
}
$widgetExe=Join-Path $WidgetPublishDir 'AIUsageViewer.Widgets.exe'
if(-not(Test-Path -LiteralPath $widgetExe) -or ((Get-Item -LiteralPath $widgetExe).VersionInfo.ProductVersion -split '\+')[0] -ne $Version) { throw 'Widget provider version does not match.' }
foreach($required in @('coreclr.dll','Microsoft.Windows.Widgets.dll','WindowsAppSDK-LICENSE.txt')) {
    if(-not(Test-Path -LiteralPath (Join-Path $WidgetPublishDir $required))) { throw "Widget provider dependency missing: $required" }
}
if(-not(Test-Path -LiteralPath (Join-Path $repo 'packaging\msix\WidgetAssets\Overview.png'))) { throw 'Widget picker preview is missing.' }

if(-not $WindowsSdkBin) {
    $kits=Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $WindowsSdkBin=Get-ChildItem -LiteralPath $kits -Directory -ErrorAction SilentlyContinue | Where-Object Name -match '^10\.0\.\d+\.\d+$' |
        Sort-Object { [version]$_.Name } -Descending | ForEach-Object { Join-Path $_.FullName 'x64' } |
        Where-Object { Test-Path -LiteralPath (Join-Path $_ 'makeappx.exe') } | Select-Object -First 1
    if(-not $WindowsSdkBin) { throw 'Windows SDK makeappx.exe not found; pass -WindowsSdkBin.' }
}
$makeappx=Join-Path $WindowsSdkBin 'makeappx.exe';$makepri=Join-Path $WindowsSdkBin 'makepri.exe';$signtool=Join-Path $WindowsSdkBin 'signtool.exe'

# Rebuild the layout from scratch. Deletion is limited to the resolved output directory.
if(Test-Path -LiteralPath $Output) {
    $resolved=(Resolve-Path -LiteralPath $Output).Path
    if(-not $resolved.StartsWith($artifactRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)) { throw 'Output must be under artifacts.' }
    if((Get-Item -LiteralPath $Output).Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Output cannot be a reparse point.' }
    Remove-Item -LiteralPath $Output -Recurse -Force
}
$layout=Join-Path $Output 'layout';$pri=Join-Path $Output 'pri'
New-Item -ItemType Directory -Path $layout,(Join-Path $pri 'Assets') -Force | Out-Null
New-Item -ItemType Directory -Path (Join-Path $layout 'Assets') -Force | Out-Null
Copy-Item -Path (Join-Path $PublishDir '*') -Destination $layout -Recurse
Copy-Item -LiteralPath $WidgetPublishDir -Destination (Join-Path $layout 'Widgets') -Recurse
Copy-Item -LiteralPath (Join-Path $repo 'packaging\msix\WidgetAssets') -Destination (Join-Path $layout 'WidgetAssets') -Recurse
New-Item -ItemType Directory -Path (Join-Path $layout 'Public') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repo 'README.md'),(Join-Path $repo 'THIRD_PARTY_NOTICES.md'),(Join-Path $repo 'PRIVACY.md') -Destination $layout -Force
# Store packages ship license texts (licenses/) but not the internal project docs.
Remove-Item -LiteralPath (Join-Path $layout 'docs') -Recurse -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path $layout 'files.sha256.json') -ErrorAction SilentlyContinue
$forbidden=Get-ChildItem -LiteralPath $layout -Recurse -File | Where-Object { $_.Extension -in '.db','.secrets','.jsonl','.pfx' -or $_.Name -eq 'settings.json' }
if($forbidden) { throw 'Private data unexpectedly present in package layout.' }
Copy-Item -Path (Join-Path $repo 'packaging\msix\Assets\*') -Destination (Join-Path $layout 'Assets')
Copy-Item -Path (Join-Path $repo 'packaging\msix\Assets\*') -Destination (Join-Path $pri 'Assets')

$escape=[Security.SecurityElement]
$manifest=(Get-Content -LiteralPath (Join-Path $repo 'packaging\msix\AppxManifest.template.xml') -Raw).
    Replace('{{IdentityName}}',$escape::Escape($IdentityName)).Replace('{{Publisher}}',$escape::Escape($Publisher)).
    Replace('{{PublisherDisplayName}}',$escape::Escape($PublisherDisplayName)).Replace('{{PackageVersion}}',$PackageVersion).
    Replace('{{DisplayName}}',$escape::Escape($displayName))
if($manifest -match '\{\{') { throw 'Unreplaced manifest value.' }
$null=[xml]$manifest
[IO.File]::WriteAllText((Join-Path $layout 'AppxManifest.xml'),$manifest,[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $pri 'AppxManifest.xml'),$manifest,[Text.UTF8Encoding]::new($false))

# Index only the visual assets; indexing the runtime folder would add every DLL to resources.pri.
& $makepri createconfig /cf (Join-Path $pri 'priconfig.xml') /dq en-US /pv 10.0.0 /o | Out-Null
if($LASTEXITCODE -ne 0) { throw 'makepri createconfig failed.' }
& $makepri new /pr $pri /cf (Join-Path $pri 'priconfig.xml') /mn (Join-Path $pri 'AppxManifest.xml') /of (Join-Path $layout 'resources.pri') /o | Out-Null
if($LASTEXITCODE -ne 0) { throw 'makepri failed.' }

$suffix=if($storeIdentity) { '' } else { '-localtest' }
$package=Join-Path $Output "AIUsageViewer-$Version-win-x64$suffix.msix"
& $makeappx pack /d $layout /p $package /o | Out-Null
if($LASTEXITCODE -ne 0) { throw 'makeappx pack failed.' }
$outputs=@($package)

if($TestSign) {
    # Throwaway self-signed certificate created in memory; the private key never leaves this run.
    $rsa=[Security.Cryptography.RSA]::Create(3072)
    $request=[Security.Cryptography.X509Certificates.CertificateRequest]::new($Publisher,$rsa,[Security.Cryptography.HashAlgorithmName]::SHA256,[Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509BasicConstraintsExtension]::new($false,$false,0,$true))
    $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509KeyUsageExtension]::new([Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature,$true))
    $usages=[Security.Cryptography.OidCollection]::new();$null=$usages.Add([Security.Cryptography.Oid]::new('1.3.6.1.5.5.7.3.3'))
    $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension]::new($usages,$false))
    $request.CertificateExtensions.Add([Security.Cryptography.X509Certificates.X509SubjectKeyIdentifierExtension]::new($request.PublicKey,$false))
    $certificate=$request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddMinutes(-5),[DateTimeOffset]::UtcNow.AddDays(30))
    $password=[Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(24))
    $pfx=Join-Path $Output 'test-signing.pfx'
    $signed=Join-Path $Output "AIUsageViewer-$Version-win-x64$suffix-test-signed.msix"
    $cer=Join-Path $Output 'AIUsageViewer-test-signing.cer'
    try {
        [IO.File]::WriteAllBytes($pfx,$certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Pfx,$password))
        [IO.File]::WriteAllBytes($cer,$certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert))
        Copy-Item -LiteralPath $package -Destination $signed
        & $signtool sign /q /fd SHA256 /f $pfx /p $password $signed | Out-Null
        if($LASTEXITCODE -ne 0) { throw 'Test signing failed.' }
        $thumbprint=$certificate.Thumbprint
    } finally { Remove-Item -LiteralPath $pfx -Force -ErrorAction SilentlyContinue;$certificate.Dispose();$rsa.Dispose() }
    if((Get-AuthenticodeSignature -LiteralPath $signed).SignerCertificate.Thumbprint -ne $thumbprint) { throw 'Test signature not found.' }
    $outputs+=$signed,$cer
}
Remove-Item -LiteralPath $pri -Recurse -Force
$outputs | ForEach-Object { (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+[IO.Path]::GetFileName($_) } |
    Set-Content -LiteralPath (Join-Path $Output 'SHA256SUMS.txt') -Encoding utf8
[pscustomobject]@{ Version=$Version;PackageVersion=$PackageVersion;IdentityName=$IdentityName;Publisher=$Publisher;StoreIdentity=$storeIdentity;Files=($outputs | ForEach-Object { [IO.Path]::GetFileName($_) }) }
