<#
.SYNOPSIS
    Tests Assert-ReleaseCode.ps1 on changes to a copy of the real library, in a throw-away git repository.

.DESCRIPTION
    The repository starts as the release: the library project, Directory.Build.props, global.json and the package README exactly as they
    are in this checkout. Every case changes the copy, commits it and asks the script whether that commit still builds the release's
    library. A change that only describes the package or the version must be accepted. Any change that can alter what the library does
    must be refused, however it is written: next to a comment, on one line, in a condition, in a target, in another build file. Fails when
    any answer is wrong.
#>
[CmdletBinding()]
param(
    # The script to test, and a wildcard on the names of the cases to run: both are for trying the tests themselves on a weakened copy.
    [string] $Guard = (Join-Path $PSScriptRoot 'Assert-ReleaseCode.ps1'),
    [string] $Filter = '*'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$guard = (Resolve-Path $Guard).Path
$work = Join-Path ([System.IO.Path]::GetTempPath()) ('rp-release-code-' + [guid]::NewGuid().ToString('N'))
$utf8 = [System.Text.UTF8Encoding]::new($false)

$project = 'src/ResonantPulse.Core/ResonantPulse.Core.csproj'
$props = 'Directory.Build.props'
$readmeItem = '<None Include="..\..\packaging\nuget\README.md" Pack="true" PackagePath="\" Visible="false" />'
$overflow = '<CheckForOverflowUnderflow>true</CheckForOverflowUnderflow>'

function Invoke-WorkGit([string[]] $Arguments) {
    $output = & git -C $work -c user.name=test -c user.email=test@example.invalid -c commit.gpgsign=false -c core.autocrlf=false @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed: $output" }
}

function Read-Text([string] $Path) { [System.IO.File]::ReadAllText((Join-Path $work $Path)) }

function Write-Text([string] $Path, [string] $Text) {
    $full = Join-Path $work $Path
    [void] [System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($full))
    [System.IO.File]::WriteAllText($full, $Text, $utf8)
}

function Edit-Text([string] $Path, [string] $Old, [string] $New) {
    $text = Read-Text $Path
    if (-not $text.Contains($Old)) { throw "The test expects '$Old' in $Path and it is not there: update the test." }
    Write-Text $Path $text.Replace($Old, $New)
}

function Edit-Pattern([string] $Path, [string] $Pattern, [string] $New) {
    $text = Read-Text $Path
    $changed = [regex]::Replace($text, $Pattern, $New)
    if ($changed -ceq $text) { throw "The test expects a match of '$Pattern' in $Path and finds none: update the test." }
    Write-Text $Path $changed
}

function Add-Text([string] $Path, [string] $Text) { Write-Text $Path ((Read-Text $Path) + $Text) }

function Remove-Work([string] $Path) { Remove-Item -LiteralPath (Join-Path $work $Path) -Force }

function Move-Work([string] $From, [string] $To) { Move-Item -LiteralPath (Join-Path $work $From) -Destination (Join-Path $work $To) }

# Each case: what it does to the copy, and the answer that is right. 'same' is for what only describes the package or the version.
$cases = @(
    @{ Name = 'nothing changed (an empty commit)'; Expect = 'same'; Change = { } }
    @{ Name = 'description, tags and copyright changed'; Expect = 'same'; Change = {
            Edit-Pattern $project '<Description>[^<]*</Description>' '<Description>Another text.</Description>'
            Edit-Pattern $project '<PackageTags>[^<]*</PackageTags>' '<PackageTags>one;two</PackageTags>'
            Edit-Pattern $project '<Copyright>[^<]*</Copyright>' '<Copyright>Someone else</Copyright>'
        } }
    @{ Name = 'package id, project URL and repository URL changed'; Expect = 'same'; Change = {
            Edit-Pattern $project '<PackageId>[^<]*</PackageId>' '<PackageId>Other.Id</PackageId>'
            Edit-Pattern $project '<PackageProjectUrl>[^<]*</PackageProjectUrl>' '<PackageProjectUrl>https://example.invalid/</PackageProjectUrl>'
            Edit-Pattern $project '<RepositoryUrl>[^<]*</RepositoryUrl>' '<RepositoryUrl>https://example.invalid/repo</RepositoryUrl>'
        } }
    @{ Name = 'package metadata moved into a property group of its own'; Expect = 'same'; Change = {
            Edit-Text $project '<PackageId>ResonantPulse.Core</PackageId>' ''
            Edit-Text $project '</Project>' '<PropertyGroup><PackageId>Other</PackageId></PropertyGroup></Project>'
        } }
    @{ Name = 'the packed README taken out of the project'; Expect = 'same'; Change = { Edit-Text $project $readmeItem '' } }
    @{ Name = 'the packed README taken out of the project together with its comment and item group'; Expect = 'same'; Change = {
            Edit-Pattern $project '(?s)\s*<ItemGroup>.*?</ItemGroup>' ''
        } }
    @{ Name = 'the packed README on one line, attributes in another order'; Expect = 'same'; Change = {
            Edit-Text $project $readmeItem '<None Visible="false" PackagePath="\" Pack="true" Include="..\..\packaging\nuget\README.md"></None>'
        } }
    @{ Name = 'comments, layout and line breaks changed'; Expect = 'same'; Change = {
            Edit-Text $project '<Nullable>enable</Nullable>' '<Nullable>enable</Nullable> <!-- a comment -->'
            Edit-Text $project '<LangVersion>14</LangVersion>' "<LangVersion>`n      14  </LangVersion>"
            Edit-Text $project '<PropertyGroup>' "<!-- a note -->`n  <PropertyGroup>"
        } }
    @{ Name = 'the version raised'; Expect = 'same'; Change = { Edit-Text $props '<Version>1.0.0</Version>' '<Version>2.3.4</Version>' } }
    @{ Name = 'authors and product changed'; Expect = 'same'; Change = {
            Edit-Text $props '<Authors>Tino Donderwinkel</Authors>' '<Authors>Someone</Authors>'
            Edit-Text $props '<Product>Resonant Pulse</Product>' '<Product>Other</Product>'
        } }
    @{ Name = 'the integration README and the lock file changed'; Expect = 'same'; Change = {
            Add-Text 'src/ResonantPulse.Core/README.md' "`nMore text.`n"
            Add-Text 'src/ResonantPulse.Core/packages.lock.json' "`n"
        } }

    @{ Name = 'overflow checking added'; Expect = 'different'; Change = {
            Edit-Text $project '<Nullable>enable</Nullable>' "<Nullable>enable</Nullable>`n    $overflow"
        } }
    @{ Name = 'overflow checking on a line of its own, followed by a comment'; Expect = 'different'; Change = {
            Edit-Text $project '<Nullable>enable</Nullable>' "<Nullable>enable</Nullable>`n    $overflow <!-- checked -->"
        } }
    @{ Name = 'overflow checking next to another element and a comment'; Expect = 'different'; Change = {
            Edit-Text $project '<Nullable>enable</Nullable>' "<Nullable>enable</Nullable>$overflow <!-- checked -->"
        } }
    @{ Name = 'a file removed from the build, in an item group on a line of its own'; Expect = 'different'; Change = {
            Edit-Text $project '</Project>' "<ItemGroup><Compile Remove=`"TimingCsvExporter.cs`" /></ItemGroup>`n</Project>"
        } }
    @{ Name = 'a file removed from the build, in an item group on one line'; Expect = 'different'; Change = {
            Edit-Text $project '</Project>' '<ItemGroup><Compile Remove="TimingCsvExporter.cs" /></ItemGroup></Project>'
        } }
    @{ Name = 'overflow checking in a conditional property group'; Expect = 'different'; Change = {
            Edit-Text $project '</Project>' "<PropertyGroup Condition=`"'`$(Configuration)' == 'Release'`">$overflow</PropertyGroup></Project>"
        } }
    @{ Name = 'overflow checking set by a target'; Expect = 'different'; Change = {
            Edit-Text $project '</Project>' "<Target Name=`"T`" BeforeTargets=`"CoreCompile`"><PropertyGroup>$overflow</PropertyGroup></Target></Project>"
        } }
    @{ Name = 'an item named like a package property'; Expect = 'different'; Change = {
            Edit-Text $project '</Project>' '<ItemGroup><Description Include="x" /></ItemGroup></Project>'
        } }
    @{ Name = 'another file packed'; Expect = 'different'; Change = {
            Edit-Text $project '</Project>' '<ItemGroup><None Include="LICENSE" Pack="true" PackagePath="\" /></ItemGroup></Project>'
        } }
    @{ Name = 'a property imported from another file'; Expect = 'different'; Change = {
            Edit-Text $project '</Project>' '<Import Project="extra.props" /></Project>'
        } }
    @{ Name = 'nullable annotations turned off'; Expect = 'different'; Change = { Edit-Text $project '<Nullable>enable</Nullable>' '<Nullable>disable</Nullable>' } }
    @{ Name = 'the language version changed'; Expect = 'different'; Change = { Edit-Text $project '<LangVersion>14</LangVersion>' '<LangVersion>13</LangVersion>' } }
    @{ Name = 'the target framework changed'; Expect = 'different'; Change = { Edit-Text $project '<TargetFramework>net10.0</TargetFramework>' '<TargetFramework>net9.0</TargetFramework>' } }
    @{ Name = 'warnings suppressed differently'; Expect = 'different'; Change = { Edit-Text $project '1591</NoWarn>' '1591;8618</NoWarn>' } }
    @{ Name = 'a compilation symbol defined'; Expect = 'different'; Change = {
            Edit-Text $project '<Nullable>enable</Nullable>' '<Nullable>enable</Nullable><DefineConstants>$(DefineConstants);EXTRA</DefineConstants>'
        } }
    @{ Name = 'a package referenced'; Expect = 'different'; Change = {
            Edit-Text $project '</Project>' '<ItemGroup><PackageReference Include="Some.Package" Version="1.0.0" /></ItemGroup></Project>'
        } }
    @{ Name = 'a property written in another case'; Expect = 'different'; Change = { Edit-Text $project '<Nullable>enable</Nullable>' '<nullable>enable</nullable>' } }
    @{ Name = 'the project file removed'; Expect = 'different'; Change = { Remove-Work $project } }
    @{ Name = 'a source file changed'; Expect = 'different'; Change = { Add-Text 'src/ResonantPulse.Core/Rational.cs' "`n// changed`n" } }
    @{ Name = 'a source file added'; Expect = 'different'; Change = { Write-Text 'src/ResonantPulse.Core/Added.cs' 'internal class Added { }' } }
    @{ Name = 'a source file removed'; Expect = 'different'; Change = { Remove-Work 'src/ResonantPulse.Core/TempoMap.cs' } }
    @{ Name = 'a source file renamed'; Expect = 'different'; Change = { Move-Work 'src/ResonantPulse.Core/TempoMap.cs' 'src/ResonantPulse.Core/TempoMap2.cs' } }
    @{ Name = 'a resource file added to the library folder'; Expect = 'different'; Change = { Write-Text 'src/ResonantPulse.Core/Strings.resx' '<root />' } }
    @{ Name = 'an editorconfig added to the library folder'; Expect = 'different'; Change = { Write-Text 'src/ResonantPulse.Core/.editorconfig' 'root = true' } }
    @{ Name = 'a build property changed in Directory.Build.props'; Expect = 'different'; Change = {
            Edit-Text $props '<Deterministic>true</Deterministic>' '<Deterministic>false</Deterministic>'
        } }
    @{ Name = 'the language version changed in Directory.Build.props'; Expect = 'different'; Change = {
            Edit-Text $props '<LangVersion>latest</LangVersion>' '<LangVersion>12</LangVersion>'
        } }
    @{ Name = 'overflow checking next to the version in Directory.Build.props'; Expect = 'different'; Change = {
            Edit-Text $props '<Version>1.0.0</Version>' "<Version>1.0.0</Version>$overflow <!-- x -->"
        } }
    @{ Name = 'Directory.Build.targets added at the root'; Expect = 'different'; Change = {
            Write-Text 'Directory.Build.targets' "<Project><PropertyGroup>$overflow</PropertyGroup></Project>"
        } }
    @{ Name = 'Directory.Build.targets added in src'; Expect = 'different'; Change = {
            Write-Text 'src/Directory.Build.targets' "<Project><PropertyGroup>$overflow</PropertyGroup></Project>"
        } }
    @{ Name = 'Directory.Build.props added in src'; Expect = 'different'; Change = {
            Write-Text 'src/Directory.Build.props' "<Project><PropertyGroup>$overflow</PropertyGroup></Project>"
        } }
    @{ Name = 'Directory.Packages.props added'; Expect = 'different'; Change = { Write-Text 'Directory.Packages.props' '<Project />' } }
    @{ Name = 'Directory.Build.rsp added'; Expect = 'different'; Change = { Write-Text 'Directory.Build.rsp' '-p:CheckForOverflowUnderflow=true' } }
    @{ Name = 'NuGet.config added'; Expect = 'different'; Change = { Write-Text 'NuGet.config' '<configuration />' } }
    @{ Name = 'the SDK version changed'; Expect = 'different'; Change = { Edit-Pattern 'global.json' '"version": "[^"]*"' '"version": "1.2.3"' } }
)

try {
    $cases = @($cases | Where-Object { $_.Name -like $Filter })
    if ($cases.Count -eq 0) { throw "No case matches '$Filter'." }
    [void] (New-Item -ItemType Directory -Path $work)
    Invoke-WorkGit @('init', '-q')
    $tracked = @(& git -C $root ls-files -- src/ResonantPulse.Core Directory.Build.props global.json packaging/nuget/README.md)
    if ($LASTEXITCODE -ne 0 -or $tracked.Count -eq 0) { throw 'Could not list the library files of this checkout.' }
    foreach ($file in $tracked) {
        Write-Text $file (([System.IO.File]::ReadAllText((Join-Path $root $file))) -replace "`r`n", "`n")
    }
    Invoke-WorkGit @('add', '-A')
    Invoke-WorkGit @('commit', '-q', '-m', 'release')
    $release = (& git -C $work rev-parse HEAD).Trim()

    $wrong = 0
    foreach ($case in $cases) {
        Invoke-WorkGit @('reset', '-q', '--hard', $release)
        Invoke-WorkGit @('clean', '-fdq')
        & $case.Change
        Invoke-WorkGit @('add', '-A')
        Invoke-WorkGit @('commit', '-q', '--allow-empty', '-m', $case.Name)

        $answer = 'same'
        $message = ''
        try { & $guard -Release $release -Candidate HEAD -Repository $work 6>$null }
        catch {
            if ($_.Exception.Message -notlike "The library or its build settings differ*") { throw }
            $answer = 'different'
            $message = $_.Exception.Message
        }

        $right = $answer -eq $case.Expect
        if (-not $right) { $wrong++ }
        '{0,-5} {1}: {2} (expected {3})' -f ($(if ($right) { 'ok' } else { 'WRONG' })), $case.Name, $answer, $case.Expect
        if (-not $right -and $message) { $message }
    }

    if ($wrong -gt 0) { throw "$wrong of $($cases.Count) cases got the wrong answer." }
    "All $($cases.Count) cases got the right answer."
}
finally {
    if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
}
