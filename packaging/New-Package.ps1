[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Payload,
    [Parameter(Mandatory)][ValidateSet('x64','arm64')][string]$Architecture,
    [Parameter(Mandatory)][string]$Version,
    [Parameter(Mandatory)][string]$Output,
    [string]$IdentityName,
    [string]$Publisher,
    [string]$PublisherDisplayName,
    [switch]$Development
)
$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+\.\d+$') { throw 'MSIX version must contain four numeric components.' }
foreach ($component in $Version.Split('.')) {
    if ([int]$component -gt 65535) { throw 'An MSIX version component exceeds 65535.' }
}
if ($Development) {
    $IdentityName = 'ResonantPulse.Development'
    $Publisher = 'CN=ResonantPulse Development'
    $PublisherDisplayName = 'Resonant Pulse development'
} elseif ([string]::IsNullOrWhiteSpace($IdentityName) -or
          [string]::IsNullOrWhiteSpace($Publisher) -or
          [string]::IsNullOrWhiteSpace($PublisherDisplayName)) {
    throw 'Exact Partner Center package identity and publisher are required for Store packaging.'
}
& (Join-Path $PSScriptRoot 'Verify-Layout.ps1') -Payload $Payload
$outputPath = [IO.Path]::GetFullPath($Output)
$payloadPath = [IO.Path]::GetFullPath($Payload)
$null = New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($outputPath)) -Force
$assets = Join-Path $payloadPath 'Assets'
& (Join-Path $PSScriptRoot 'Generate-Assets.ps1') -OutputDirectory $assets
$manifest = [xml]@'
<?xml version="1.0" encoding="utf-8"?>
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
 xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
 xmlns:uap10="http://schemas.microsoft.com/appx/manifest/uap/windows10/10"
 xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
 IgnorableNamespaces="uap uap10 rescap">
 <Identity Name="UNSET" Publisher="UNSET" Version="0.0.0.0" ProcessorArchitecture="x64"/>
 <Properties><DisplayName>Resonant Pulse</DisplayName><PublisherDisplayName>UNSET</PublisherDisplayName><Logo>Assets\StoreLogo.png</Logo></Properties>
 <Dependencies><TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.26100.0" MaxVersionTested="10.0.26100.0"/></Dependencies>
 <Resources><Resource Language="en-US"/><Resource Language="nl-NL"/><Resource Language="de-DE"/><Resource Language="fr-FR"/><Resource Language="es-ES"/></Resources>
 <Applications>
  <Application Id="App" Executable="ResonantPulse.exe" uap10:RuntimeBehavior="packagedClassicApp" uap10:TrustLevel="mediumIL">
   <uap:VisualElements DisplayName="Resonant Pulse" Description="A local-first metronome and rhythm workstation." BackgroundColor="transparent" Square150x150Logo="Assets\Square150x150Logo.png" Square44x44Logo="Assets\Square44x44Logo.png">
    <uap:DefaultTile Wide310x150Logo="Assets\Wide310x150Logo.png"/>
   </uap:VisualElements>
  </Application>
 </Applications>
 <Capabilities><rescap:Capability Name="runFullTrust"/></Capabilities>
</Package>
'@
$manifest.Package.Identity.SetAttribute('Name',$IdentityName)
$manifest.Package.Identity.SetAttribute('Publisher',$Publisher)
$manifest.Package.Identity.SetAttribute('Version',$Version)
$manifest.Package.Identity.SetAttribute('ProcessorArchitecture',$Architecture)
$manifest.Package.Properties.PublisherDisplayName = $PublisherDisplayName
$manifest.Save((Join-Path $payloadPath 'AppxManifest.xml'))
$kitRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
$candidates = Get-ChildItem -LiteralPath $kitRoot -Directory | Where-Object { $_.Name -match '^\d+\.' } | Sort-Object { [version]$_.Name } -Descending
$makeAppx = $null
foreach ($directory in $candidates) {
    $candidate = Join-Path $directory.FullName 'x64\makeappx.exe'
    if (Test-Path -LiteralPath $candidate) { $makeAppx = $candidate; break }
}
if (-not $makeAppx) { throw 'Windows SDK MakeAppx is unavailable on this build runner.' }
& $makeAppx pack /d $payloadPath /p $outputPath /o
if ($LASTEXITCODE -ne 0) { throw "MakeAppx failed with exit code $LASTEXITCODE." }
Write-Output "Created unsigned MSIX: $outputPath. Unsigned is not sideload-installable or Store-certified."
