[CmdletBinding()]
param(
    # SemVer without the leading v: 1.0.0, or 1.1.0-preview.1 for a pre-release. Release 1.0.0 is package 1.0.0.
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z][0-9A-Za-z.-]*)?$')]
    [string]$Version = '0.1.0-dev',
    [switch]$NoRestore,
    [switch]$SkipConsumer
)

# Packs the engine (src/ResonantPulse.Core) as the NuGet package ResonantPulse.Core and checks the result before anything can publish it:
# the package holds exactly the expected files, its nuspec says what GitHub Packages and users rely on (id, version, license, readme,
# repository and the commit it was built from), it has no dependencies, and a stand-alone consumer project restores it from a local
# folder (never from a registry), builds against it and exercises it. Output: artifacts/nuget/ResonantPulse.Core.<version>.nupkg.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$output = Join-Path $root 'artifacts/nuget'
$project = Join-Path $root 'src/ResonantPulse.Core/ResonantPulse.Core.csproj'
$packageId = 'ResonantPulse.Core'
$repositoryUrl = 'https://github.com/tinodo/Resonant-Pulse'
$projectUrl = 'https://tinodo.github.io/Resonant-Pulse/'

$numeric = [version](($Version -split '-')[0])
if ($numeric.Major -gt 65535 -or $numeric.Minor -gt 65535 -or $numeric.Build -gt 65535) {
    throw 'The version components must be between 0 and 65535: they become the assembly version.'
}

if (Test-Path -LiteralPath $output) { Remove-Item -LiteralPath $output -Recurse -Force }
New-Item -ItemType Directory -Path $output -Force | Out-Null

$packArguments = @('pack', $project, '--configuration', 'Release', '--output', $output, "-p:Version=$Version")
if ($env:GITHUB_ACTIONS -eq 'true') { $packArguments += '-p:ContinuousIntegrationBuild=true' }
if ($NoRestore) { $packArguments += '--no-restore' }
& dotnet @packArguments
if ($LASTEXITCODE -ne 0) { throw "Pack failed with exit code $LASTEXITCODE." }

$files = @(Get-ChildItem -LiteralPath $output -File)
$expectedName = "$packageId.$Version.nupkg"
if ($files.Count -ne 1 -or $files[0].Name -ne $expectedName) {
    throw "Expected only $expectedName in $output but found: $(($files | ForEach-Object Name) -join ', ')."
}
$package = $files[0]

$head = $null
if (Get-Command git -ErrorAction SilentlyContinue) {
    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { $head = (& git -C $root rev-parse HEAD 2>$null) } finally { $ErrorActionPreference = $previousPreference }
    if ($LASTEXITCODE -ne 0) { $head = $null }
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$expectedEntries = @("$packageId.nuspec", 'README.md', 'lib/net10.0/ResonantPulse.Core.dll', 'lib/net10.0/ResonantPulse.Core.xml')
$structuralEntries = '^(\[Content_Types\]\.xml|_rels/\.rels|package/services/metadata/core-properties/[^/]+\.psmdcp)$'
$entries = @()
$nuspecText = $null
$assemblyPath = [System.IO.Path]::GetTempFileName()
$archive = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)
try {
    $entries = @($archive.Entries | ForEach-Object { $_.FullName })
    $nuspecEntry = $archive.GetEntry("$packageId.nuspec")
    $assemblyEntry = $archive.GetEntry('lib/net10.0/ResonantPulse.Core.dll')
    if ($nuspecEntry) {
        $reader = New-Object System.IO.StreamReader($nuspecEntry.Open())
        try { $nuspecText = $reader.ReadToEnd() } finally { $reader.Dispose() }
    }
    if ($assemblyEntry) {
        [System.IO.Compression.ZipFileExtensions]::ExtractToFile($assemblyEntry, $assemblyPath, $true)
        $assemblyHash = (Get-FileHash -LiteralPath $assemblyPath -Algorithm SHA256).Hash
    }
    $assemblyVersion = if ($assemblyEntry) { [System.Reflection.AssemblyName]::GetAssemblyName($assemblyPath).Version } else { $null }
}
finally {
    $archive.Dispose()
    Remove-Item -LiteralPath $assemblyPath -Force -ErrorAction SilentlyContinue
}

$missing = @($expectedEntries | Where-Object { $entries -notcontains $_ })
if ($missing.Count -gt 0) { throw "The package is missing: $($missing -join ', ')." }
$unexpected = @($entries | Where-Object { $expectedEntries -notcontains $_ -and $_ -notmatch $structuralEntries })
if ($unexpected.Count -gt 0) { throw "The package has unexpected files: $($unexpected -join ', ')." }

[xml]$nuspec = $nuspecText
$metadata = $nuspec.package.metadata
if ($metadata.id -ne $packageId -or $metadata.version -ne $Version) {
    throw "The package is $($metadata.id) $($metadata.version), expected $packageId $Version."
}
if ($metadata.license.type -ne 'expression' -or $metadata.license.InnerText -ne 'MIT') {
    throw 'The package must declare the MIT license expression.'
}
if ($metadata.readme -ne 'README.md') { throw 'The package must name README.md as its readme.' }
if ($metadata.projectUrl -ne $projectUrl) { throw "The package project URL must be $projectUrl." }
if ($metadata.repository.type -ne 'git' -or $metadata.repository.url -ne $repositoryUrl) {
    throw "The package repository must be git $repositoryUrl; GitHub Packages links the package to the repository from it."
}
if ($head -and $metadata.repository.commit -ne $head) {
    throw "The package commit is '$($metadata.repository.commit)', expected $head."
}
if (-not $metadata.authors -or -not $metadata.description -or $metadata.tags -notmatch 'metronome') {
    throw 'The package must have authors, a description and tags.'
}
$groups = @($metadata.dependencies.group)
if ($groups.Count -ne 1 -or $groups[0].targetFramework -ne 'net10.0') {
    throw 'The package must have exactly one dependency group, for net10.0.'
}
if (@($groups[0].dependency).Count -gt 0 -and $null -ne $groups[0].dependency) {
    throw "The package must have no dependencies but depends on: $(@($groups[0].dependency | ForEach-Object { $_.id }) -join ', ')."
}
$expectedAssemblyVersion = [version]::new($numeric.Major, $numeric.Minor, $numeric.Build, 0)
if ($assemblyVersion -ne $expectedAssemblyVersion) {
    throw "ResonantPulse.Core.dll has assembly version $assemblyVersion, expected $expectedAssemblyVersion."
}

Write-Output "NuGet package: $($package.FullName) ($($package.Length) bytes)"
Write-Output "$($metadata.id) $($metadata.version), assembly $assemblyVersion, repository $($metadata.repository.url) at $($metadata.repository.commit)"

if (-not $SkipConsumer) {
    # A user of the package sees only the package: a private folder as the only source and as the package cache, so that nothing comes
    # from a registry or from an earlier build of the same version.
    $work = Join-Path ([System.IO.Path]::GetTempPath()) ('rp-consumer-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $work | Out-Null
    $savedEnvironment = @{}
    foreach ($name in 'MSBUILDDISABLENODEREUSE', 'DOTNET_CLI_USE_MSBUILD_SERVER', 'DOTNET_NOLOGO', 'DOTNET_CLI_TELEMETRY_OPTOUT') {
        $savedEnvironment[$name] = [Environment]::GetEnvironmentVariable($name)
    }
    try {
        Copy-Item -Path (Join-Path $root 'packaging/nuget/Consumer/*') -Destination $work -Recurse
        $feed = [System.Security.SecurityElement]::Escape($output)
        $cache = [System.Security.SecurityElement]::Escape((Join-Path $work 'packages'))
        $config = @"
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <config><add key="globalPackagesFolder" value="$cache" /></config>
  <packageSources><clear /><add key="local" value="$feed" /></packageSources>
</configuration>
"@
        Set-Content -LiteralPath (Join-Path $work 'nuget.config') -Value $config -Encoding utf8
        $env:MSBUILDDISABLENODEREUSE = '1'
        $env:DOTNET_CLI_USE_MSBUILD_SERVER = '0'
        $env:DOTNET_NOLOGO = '1'
        $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
        Push-Location $work
        try {
            & dotnet run --configuration Release "-p:ResonantPulseCoreVersion=$Version" -- $Version $assemblyHash
            if ($LASTEXITCODE -ne 0) { throw "The consumer check failed with exit code $LASTEXITCODE." }
        }
        finally { Pop-Location }
    }
    finally {
        foreach ($name in $savedEnvironment.Keys) { [Environment]::SetEnvironmentVariable($name, $savedEnvironment[$name]) }
        Remove-Item -LiteralPath $work -Recurse -Force -ErrorAction SilentlyContinue
    }
}
