<#
.SYNOPSIS
    Tests Assert-ReleaseCode.ps1 on changes to a copy of the real library, in a throw-away git repository.

.DESCRIPTION
    The repository starts as the release: the library project, Directory.Build.props, global.json and the package README exactly as they
    are in this checkout. Every case changes the copy, commits it and asks the script whether that commit still builds the release's
    library. A change that only describes the package or the version must be accepted. Any change that can alter what the library does
    must be refused, however it is written: next to a comment, on one line, in a condition, in a target, in another build file. A project
    that points at files the script does not read is refused even when the two commits are the same one. Fails when any answer is wrong.

    The cases find what they change by pattern, not by the value it has today, so that raising the version or retargeting the library does
    not break them.
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

$library = 'src/ResonantPulse.Core'
$project = "$library/ResonantPulse.Core.csproj"
$props = 'Directory.Build.props'
$overflow = '<CheckForOverflowUnderflow>true</CheckForOverflowUnderflow>'

# The properties that may differ: the contract of the check, written out again here so that dropping one of them from the check is noticed.
$permittedNames = @(
    'IsPackable', 'PackageId', 'Description', 'PackageTags', 'PackageReadmeFile', 'PackageProjectUrl', 'PackageLicenseExpression',
    'Copyright', 'RepositoryUrl', 'RepositoryType', 'PublishRepositoryUrl', 'Authors', 'Company', 'Product', 'Version')

function Invoke-WorkGit([string[]] $Arguments) {
    $output = & git -C $work -c user.name=test -c user.email=test@example.invalid -c commit.gpgsign=false -c core.autocrlf=false @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed: $output" }
}

function Save-Work([string] $Message, [bool] $IndexOnly) {
    if (-not $IndexOnly) { Invoke-WorkGit @('add', '-A') }
    Invoke-WorkGit @('commit', '-q', '--allow-empty', '-m', $Message)
}

function Reset-Work([string] $Commit) {
    Invoke-WorkGit @('reset', '-q', '--hard', $Commit)
    Invoke-WorkGit @('clean', '-fdq')
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

# Replaces the first match of a pattern with what the evaluator returns for it; fails when there is none or when nothing changes.
function Edit-Match([string] $Path, [string] $Pattern, [scriptblock] $Evaluator) {
    $text = Read-Text $Path
    $regex = [regex]::new($Pattern)
    $match = $regex.Match($text)
    if (-not $match.Success) { throw "The test expects a match of '$Pattern' in $Path and finds none: update the test." }
    $changed = $text.Substring(0, $match.Index) + (& $Evaluator $match) + $text.Substring($match.Index + $match.Length)
    if ($changed -ceq $text) { throw "The test changed nothing in $Path with '$Pattern': update the test." }
    Write-Text $Path $changed
}

function Edit-Pattern([string] $Path, [string] $Pattern, [string] $New) { Edit-Match $Path $Pattern { param($match) $New } }

# Adds text right after the first match of a pattern.
function Add-After([string] $Path, [string] $Pattern, [string] $Insert) { Edit-Match $Path $Pattern { param($match) $match.Value + $Insert } }

# Adds text at the end of a project file, inside it.
function Add-ToProject([string] $Path, [string] $Insert) { Edit-Text $Path '</Project>' "$Insert`n</Project>" }

function Add-Text([string] $Path, [string] $Text) { Write-Text $Path ((Read-Text $Path) + $Text) }

function Remove-Work([string] $Path) { Remove-Item -LiteralPath (Join-Path $work $Path) -Force }

function Move-Work([string] $From, [string] $To) { Move-Item -LiteralPath (Join-Path $work $From) -Destination (Join-Path $work $To) }

# A file that exists in the commit and nowhere else (no working file): a name that no file system takes, or two that differ in case only.
function Add-IndexEntry([string] $Path, [string] $Text) {
    $blob = ($Text | & git -C $work hash-object -w --stdin).Trim()
    Invoke-WorkGit @('-c', 'core.protectNTFS=false', 'update-index', '--add', '--cacheinfo', "100644,$blob,$Path")
}

# What 1.0.0 looked like before the packaging existed: no package metadata, and no packed README.
function Remove-PackageMetadata {
    foreach ($file in $project, $props) {
        foreach ($name in $permittedNames) {
            $text = Read-Text $file
            Write-Text $file ([regex]::Replace($text, "\s*<$name>[^<]*</$name>", ''))
        }
    }
    Write-Text $project ([regex]::Replace((Read-Text $project), '\s*<None [^>]*README\.md[^>]*/>', ''))
}

$cases = [System.Collections.Generic.List[hashtable]]::new()

# Each case: what it does to the copy (Change), and the answer that is right. 'same' is for what only describes the package or the version.
# Release changes the release's own commit first (the candidate is still the copy, changed); OnRelease makes the candidate start from that
# commit; Self asks about the changed commit against a child of it with the same content (another commit, so that it is read); Identical
# asks about the very same commit; IndexOnly changes the commit without working files.
function Add-Case([hashtable] $Case) { $cases.Add($Case) }

Add-Case @{ Name = 'nothing changed (an empty commit)'; Expect = 'same'; Change = { } }
Add-Case @{ Name = 'description, tags and copyright changed'; Expect = 'same'; Change = {
        Edit-Pattern $project '<Description>[^<]*</Description>' '<Description>Another text.</Description>'
        Edit-Pattern $project '<PackageTags>[^<]*</PackageTags>' '<PackageTags>one;two</PackageTags>'
        Edit-Pattern $project '<Copyright>[^<]*</Copyright>' '<Copyright>Someone else</Copyright>'
    } }
Add-Case @{ Name = 'package id, project URL and repository URL changed'; Expect = 'same'; Change = {
        Edit-Pattern $project '<PackageId>[^<]*</PackageId>' '<PackageId>Other.Id</PackageId>'
        Edit-Pattern $project '<PackageProjectUrl>[^<]*</PackageProjectUrl>' '<PackageProjectUrl>https://example.invalid/</PackageProjectUrl>'
        Edit-Pattern $project '<RepositoryUrl>[^<]*</RepositoryUrl>' '<RepositoryUrl>https://example.invalid/repo</RepositoryUrl>'
    } }
Add-Case @{ Name = 'package metadata moved into a property group of its own'; Expect = 'same'; Change = {
        Edit-Pattern $project '\s*<PackageId>[^<]*</PackageId>' ''
        Add-ToProject $project '<PropertyGroup><PackageId>Other</PackageId></PropertyGroup>'
    } }
Add-Case @{ Name = 'the packed README taken out of the project'; Expect = 'same'; Change = { Edit-Pattern $project '<None [^>]*README\.md[^>]*/>' '' } }
Add-Case @{ Name = 'the packed README taken out of the project together with its comment and item group'; Expect = 'same'; Change = {
        Edit-Pattern $project '(?s)\s*<ItemGroup>.*?</ItemGroup>' ''
    } }
Add-Case @{ Name = 'the packed README on one line, attributes in another order'; Expect = 'same'; Change = {
        Edit-Pattern $project '<None [^>]*README\.md[^>]*/>' '<None Visible="false" PackagePath="\" Pack="true" Include="..\..\packaging\nuget\README.md"></None>'
    } }
Add-Case @{ Name = 'comments, layout and line breaks changed'; Expect = 'same'; Change = {
        Add-After $project '<Nullable>[^<]*</Nullable>' ' <!-- a comment -->'
        Edit-Match $project '<LangVersion>([^<]*)</LangVersion>' { param($match) "<LangVersion>`n      $($match.Groups[1].Value)  </LangVersion>" }
        Edit-Text $project '<PropertyGroup>' "<!-- a note -->`n  <PropertyGroup>"
    } }
Add-Case @{ Name = 'the version raised'; Expect = 'same'; Change = { Edit-Pattern $props '<Version>[^<]*</Version>' '<Version>9.9.9-test</Version>' } }
Add-Case @{ Name = 'authors and product changed'; Expect = 'same'; Change = {
        Edit-Pattern $props '<Authors>[^<]*</Authors>' '<Authors>Someone</Authors>'
        Edit-Pattern $props '<Product>[^<]*</Product>' '<Product>Other</Product>'
    } }
Add-Case @{ Name = 'the integration README and the lock file changed'; Expect = 'same'; Change = {
        Add-Text "$library/README.md" "`nMore text.`n"
        if (Test-Path -LiteralPath (Join-Path $work "$library/packages.lock.json")) { Add-Text "$library/packages.lock.json" "`n" }
    } }
Add-Case @{ Name = 'the release has no package metadata and no packed README, as 1.0.0 had'; Expect = 'same'; Release = { Remove-PackageMetadata }; Change = { } }
foreach ($name in $permittedNames) {
    # Built from text, not as a closure, so that the cases run in this script's scope, where the helpers are.
    $releaseBlock = [scriptblock]::Create("Add-ToProject `$project '<PropertyGroup><$name>one</$name></PropertyGroup>'")
    $changeBlock = [scriptblock]::Create("Add-ToProject `$project '<PropertyGroup><$name>two</$name></PropertyGroup>'")
    Add-Case @{ Name = "the package property $name differs"; Expect = 'same'; Release = $releaseBlock; Change = $changeBlock }
}
Add-Case @{ Name = 'the version read by a condition (the package is made with -p:Version)'; Expect = 'same'; Self = $true; Change = {
        Add-ToProject $project "<PropertyGroup Condition=`"'`$(Version)' == '1.0.0'`"><DefineConstants>X</DefineConstants></PropertyGroup>"
    } }
Add-Case @{ Name = 'the project as it is, checked against itself'; Expect = 'same'; Self = $true; Change = { } }

Add-Case @{ Name = 'overflow checking added'; Expect = 'different'; Change = { Add-After $project '<Nullable>[^<]*</Nullable>' "`n    $overflow" } }
Add-Case @{ Name = 'overflow checking on a line of its own, followed by a comment'; Expect = 'different'; Change = {
        Add-After $project '<Nullable>[^<]*</Nullable>' "`n    $overflow <!-- checked -->"
    } }
Add-Case @{ Name = 'overflow checking next to another element and a comment'; Expect = 'different'; Change = {
        Add-After $project '<Nullable>[^<]*</Nullable>' "$overflow <!-- checked -->"
    } }
Add-Case @{ Name = 'a file removed from the build, in an item group on a line of its own'; Expect = 'different'; Change = {
        Add-ToProject $project '<ItemGroup><Compile Remove="TimingCsvExporter.cs" /></ItemGroup>'
    } }
Add-Case @{ Name = 'a file removed from the build, in an item group on one line'; Expect = 'different'; Change = {
        Edit-Text $project '</Project>' '<ItemGroup><Compile Remove="TimingCsvExporter.cs" /></ItemGroup></Project>'
    } }
Add-Case @{ Name = 'overflow checking in a conditional property group'; Expect = 'different'; Change = {
        Add-ToProject $project "<PropertyGroup Condition=`"'`$(Configuration)' == 'Release'`">$overflow</PropertyGroup>"
    } }
Add-Case @{ Name = 'an item named like a package property'; Expect = 'different'; Change = { Add-ToProject $project '<ItemGroup><Description Include="x" /></ItemGroup>' } }
Add-Case @{ Name = 'another file packed'; Expect = 'different'; Change = {
        Add-ToProject $project '<ItemGroup><None Include="LICENSE" Pack="true" PackagePath="\" /></ItemGroup>'
    } }
Add-Case @{ Name = 'nullable annotations changed'; Expect = 'different'; Change = { Edit-Pattern $project '<Nullable>[^<]*</Nullable>' '<Nullable>changed</Nullable>' } }
Add-Case @{ Name = 'the language version changed'; Expect = 'different'; Change = { Edit-Pattern $project '<LangVersion>[^<]*</LangVersion>' '<LangVersion>changed</LangVersion>' } }
Add-Case @{ Name = 'the target framework changed'; Expect = 'different'; Change = { Edit-Pattern $project '<TargetFramework>[^<]*</TargetFramework>' '<TargetFramework>changed</TargetFramework>' } }
Add-Case @{ Name = 'warnings suppressed differently'; Expect = 'different'; Change = { Edit-Text $project '</NoWarn>' ';8618</NoWarn>' } }
Add-Case @{ Name = 'a compilation symbol defined'; Expect = 'different'; Change = {
        Add-After $project '<Nullable>[^<]*</Nullable>' '<DefineConstants>$(DefineConstants);EXTRA</DefineConstants>'
    } }
Add-Case @{ Name = 'a package referenced'; Expect = 'different'; Change = {
        Add-ToProject $project '<ItemGroup><PackageReference Include="Some.Package" Version="1.0.0" /></ItemGroup>'
    } }
Add-Case @{ Name = 'a property written in another case'; Expect = 'different'; Change = {
        Edit-Match $project '<Nullable>([^<]*)</Nullable>' { param($match) "<nullable>$($match.Groups[1].Value)</nullable>" }
    } }
Add-Case @{ Name = 'the project file removed'; Expect = 'different'; Change = { Remove-Work $project } }
$sources = @(& git -C $root ls-files -- "$library/*.cs")
if ($sources.Count -lt 2) { throw 'The test needs at least two source files in the library.' }
Add-Case @{ Name = 'a source file changed'; Expect = 'different'; Change = { Add-Text $sources[0] "`n// changed`n" } }
Add-Case @{ Name = 'a source file added'; Expect = 'different'; Change = { Write-Text "$library/Added.cs" 'internal class Added { }' } }
Add-Case @{ Name = 'a source file removed'; Expect = 'different'; Change = { Remove-Work $sources[1] } }
Add-Case @{ Name = 'a source file renamed'; Expect = 'different'; Change = { Move-Work $sources[1] "$($sources[1]).renamed.cs" } }
Add-Case @{ Name = 'a resource file added to the library folder'; Expect = 'different'; Change = { Write-Text "$library/Strings.resx" '<root />' } }
Add-Case @{ Name = 'an editorconfig added to the library folder'; Expect = 'different'; Change = { Write-Text "$library/.editorconfig" 'root = true' } }
Add-Case @{ Name = 'an editorconfig added at the root'; Expect = 'different'; Change = { Write-Text '.editorconfig' "root = true`n[*.cs]`nbuild_property.X = 1" } }
Add-Case @{ Name = 'a globalconfig added in src'; Expect = 'different'; Change = { Write-Text 'src/.globalconfig' "is_global = true`nbuild_property.X = 1" } }
Add-Case @{ Name = 'two files whose names differ only in case, one of them changed'; Expect = 'different'; IndexOnly = $true; OnRelease = $true
    Release = { Add-IndexEntry "$library/Dup.cs" 'one'; Add-IndexEntry "$library/dup.cs" 'two' }
    Change = { Add-IndexEntry "$library/Dup.cs" 'changed' } }
Add-Case @{ Name = 'a file whose name has a double quote in it, added'; Expect = 'different'; IndexOnly = $true; Change = {
        Add-IndexEntry "$library/Q`"uote.cs" 'class Q { }'
    } }
Add-Case @{ Name = 'a build property changed in Directory.Build.props'; Expect = 'different'; Change = {
        Edit-Pattern $props '<Deterministic>[^<]*</Deterministic>' '<Deterministic>changed</Deterministic>'
    } }
Add-Case @{ Name = 'the language version changed in Directory.Build.props'; Expect = 'different'; Change = {
        Edit-Pattern $props '<LangVersion>[^<]*</LangVersion>' '<LangVersion>changed</LangVersion>'
    } }
Add-Case @{ Name = 'overflow checking next to the version in Directory.Build.props'; Expect = 'different'; Change = {
        Add-After $props '<Version>[^<]*</Version>' "$overflow <!-- x -->"
    } }
Add-Case @{ Name = 'Directory.Build.targets added at the root'; Expect = 'different'; Change = {
        Write-Text 'Directory.Build.targets' "<Project><PropertyGroup>$overflow</PropertyGroup></Project>"
    } }
Add-Case @{ Name = 'Directory.Build.targets added in src'; Expect = 'different'; Change = {
        Write-Text 'src/Directory.Build.targets' "<Project><PropertyGroup>$overflow</PropertyGroup></Project>"
    } }
Add-Case @{ Name = 'Directory.Build.props added in src'; Expect = 'different'; Change = {
        Write-Text 'src/Directory.Build.props' "<Project><PropertyGroup>$overflow</PropertyGroup></Project>"
    } }
Add-Case @{ Name = 'Directory.Packages.props added'; Expect = 'different'; Change = { Write-Text 'Directory.Packages.props' '<Project />' } }
Add-Case @{ Name = 'Directory.Build.rsp added'; Expect = 'different'; Change = { Write-Text 'Directory.Build.rsp' '-p:CheckForOverflowUnderflow=true' } }
Add-Case @{ Name = 'NuGet.config added'; Expect = 'different'; Change = { Write-Text 'NuGet.config' '<configuration />' } }
Add-Case @{ Name = 'the SDK version changed'; Expect = 'different'; Change = { Edit-Pattern 'global.json' '"version": "[^"]*"' '"version": "1.2.3"' } }
Add-Case @{ Name = 'a text that imitates the print of two texts'; Expect = 'different'
    Release = { Add-After $project '<Nullable>[^<]*</Nullable>' '<DefineConstants>A<![CDATA[B]]></DefineConstants>' }
    Change = { Add-After $project '<Nullable>[^<]*</Nullable>' "<DefineConstants>A`n      #text B</DefineConstants>" } }

# A project that points at files this check does not read, or at things it cannot evaluate, is refused even when both commits are the same one.
$unread = [ordered] @{
    'a project that imports a file' = { Add-ToProject $project '<Import Project="extra.props" />' }
    'a project with an Sdk element' = { Edit-Text $project '<PropertyGroup>' '<Sdk Name="Other.Sdk" Version="1.0.0" /><PropertyGroup>' }
    'a project with a target' = { Add-ToProject $project '<Target Name="T" BeforeTargets="CoreCompile"><Message Text="x" /></Target>' }
    'a project with a target that sets overflow checking' = {
        Add-ToProject $project "<Target Name=`"T`" BeforeTargets=`"CoreCompile`"><PropertyGroup>$overflow</PropertyGroup></Target>"
    }
    'a project with a task' = { Add-ToProject $project '<UsingTask TaskName="X" AssemblyFile="x.dll" />' }
    'a condition on the operating system' = {
        Add-ToProject $project "<PropertyGroup Condition=`"'`$(OS)' == 'Windows_NT'`"><DefineConstants>WIN</DefineConstants></PropertyGroup>"
    }
    'a condition on an environment variable' = {
        Add-ToProject $project "<PropertyGroup Condition=`"'`$(GITHUB_ACTIONS)' == 'true'`"><DefineConstants>CI</DefineConstants></PropertyGroup>"
    }
    'an include made from an environment variable' = { Add-ToProject $project "<ItemGroup><Compile Include=`"`$(HOME)/x.cs`" /></ItemGroup>" }
    'a value made from an environment variable' = { Add-After $project '<Nullable>[^<]*</Nullable>' "<KeyFile>`$(USERPROFILE)\keys\x.snk</KeyFile>" }
    'item metadata' = { Add-ToProject $project "<ItemGroup><Compile Update=`"x.cs`" Link=`"%(FullPath)`" /></ItemGroup>" }
    'an item list' = { Add-ToProject $project '<ItemGroup><EmbeddedResource Include="@(None)" /></ItemGroup>' }
    'an item list transformed with metadata' = { Add-ToProject $project "<PropertyGroup><Extra>@(None->'%(FullPath)')</Extra></PropertyGroup>" }
    'a Choose on the runtime' = {
        Add-ToProject $project "<Choose><When Condition=`"'`$(MSBuildRuntimeType)' == 'Core'`"><PropertyGroup><DefineConstants>CORE</DefineConstants></PropertyGroup></When></Choose>"
    }
    'an item definition on the operating system' = {
        Add-ToProject $project "<ItemDefinitionGroup Condition=`"'`$(OS)' == 'Unix'`"><Compile><Link>x</Link></Compile></ItemDefinitionGroup>"
    }
    'a method called on a reserved property' = { Add-After $project '<Nullable>[^<]*</Nullable>' "<Extra>`$(MSBuildProjectDirectory.Replace('a','b'))</Extra>" }
    'an escaped character' = { Add-ToProject $project '<ItemGroup><Compile Include="%2E%2E/Shared/Extra.cs" /></ItemGroup>' }
    'a default that the environment can override' = {
        Add-ToProject $project "<PropertyGroup><Flag Condition=`"'`$(Flag)' == ''`">x</Flag></PropertyGroup>"
    }
    'a method called on a property that the project defines' = {
        Add-ToProject $project "<PropertyGroup><ExternalPath>__/Shared/Other.cs</ExternalPath></PropertyGroup><ItemGroup><Compile Include=`"`$(ExternalPath.Replace('_','.'))`" /></ItemGroup>"
    }
    'an item list named like a property that may be read' = { Add-ToProject $project "<PropertyGroup><Extra>@(NoWarn)</Extra></PropertyGroup>" }
    'item metadata named like a property that may be read' = { Add-ToProject $project "<ItemGroup><Compile Update=`"x.cs`" Link=`"%(NoWarn)`" /></ItemGroup>" }
    'a property that is defined only inside a Choose, read outside it' = {
        Add-ToProject $project "<Choose><When Condition=`"'`$(Configuration)' == 'Debug'`"><PropertyGroup><Flag>x</Flag></PropertyGroup></When></Choose><PropertyGroup Condition=`"'`$(Flag)' == 'x'`"><DefineConstants>Y</DefineConstants></PropertyGroup>"
    }
    'a project with a project reference' = { Add-ToProject $project '<ItemGroup><ProjectReference Include="Other.csproj" /></ItemGroup>' }
    'a project that includes a file of another folder' = { Add-ToProject $project '<ItemGroup><Compile Include="..\Shared\Extra.cs" /></ItemGroup>' }
    'a property that points outside the library folder' = { Add-After $project '<Nullable>[^<]*</Nullable>' '<ApplicationIcon>../../assets/x.ico</ApplicationIcon>' }
    'a property made from the project directory' = { Add-After $project '<Nullable>[^<]*</Nullable>' '<KeyFile>$(MSBuildProjectDirectory)/x.snk</KeyFile>' }
    'a property with a rooted path' = { Add-After $project '<Nullable>[^<]*</Nullable>' '<KeyFile>C:\keys\x.snk</KeyFile>' }
    'a property function that reads a file' = { Add-After $project '<Nullable>[^<]*</Nullable>' "<Extra>`$([System.IO.File]::ReadAllText('x'))</Extra>" }
    'a condition on a file outside the library folder' = {
        Add-ToProject $project "<PropertyGroup Condition=`"Exists('..\x')`"><Nullable>enable</Nullable></PropertyGroup>"
    }
    'a condition on a file at a rooted path' = {
        Add-ToProject $project "<PropertyGroup Condition=`"Exists('/tmp/release-code-flag')`">$overflow</PropertyGroup>"
    }
    'a condition on a file at a path with a drive letter' = {
        Add-ToProject $project "<PropertyGroup Condition=`"Exists('C:\build\release-code-flag')`">$overflow</PropertyGroup>"
    }
    'a condition on a file named without a path' = {
        Add-ToProject $project "<PropertyGroup Condition=`"Exists('release-code-flag')`">$overflow</PropertyGroup>"
    }
    'a condition on a trailing slash' = {
        Add-ToProject $project "<PropertyGroup Condition=`"HasTrailingSlash('x')`">$overflow</PropertyGroup>"
    }
    'a rooted path inside a property value' = { Add-After $project '<Nullable>[^<]*</Nullable>' '<Extra>--flag=/etc/keys/x</Extra>' }
    'a path with a drive letter inside a property value' = { Add-After $project '<Nullable>[^<]*</Nullable>' '<Extra>--key=C:\keys\x</Extra>' }
    'a rooted path inside a condition that does not look at the file system' = {
        Add-ToProject $project "<PropertyGroup Condition=`"'`$(Flag)' == '/tmp/release-code-flag'`">$overflow</PropertyGroup>"
    }
    'a project that treats the version as local' = { Edit-Match $project '<Project\b' { param($match) '<Project TreatAsLocalProperty="Version"' } }
    'a property group that treats a property as local' = { Edit-Text $project '<PropertyGroup>' '<PropertyGroup TreatAsLocalProperty="Version">' }
    'logic that reads a package property' = {
        Add-ToProject $props "<PropertyGroup Condition=`"'`$(Product)' == 'Resonant Pulse'`"><DefineConstants>BRANDED</DefineConstants></PropertyGroup>"
    }
    'an item that reads a package property' = { Add-ToProject $project "<ItemGroup><Compile Include=`"`$(Description).cs`" /></ItemGroup>" }
    'Directory.Build.props that imports a file' = { Add-ToProject $props '<Import Project="common.props" />' }
}
foreach ($name in $unread.Keys) { Add-Case @{ Name = "$name, checked against itself"; Expect = 'different'; Self = $true; Change = $unread[$name] } }

# What a project may read, and the rule that the release's own commit is the release's, whatever it holds.
Add-Case @{ Name = 'a read of a property that the project defines, checked against itself'; Expect = 'same'; Self = $true; Change = {
        Add-ToProject $project "<PropertyGroup><Flag>x</Flag></PropertyGroup><PropertyGroup Condition=`"'`$(Flag)' == 'x'`"><DefineConstants>`$(DefineConstants);Y</DefineConstants></PropertyGroup>"
    } }
Add-Case @{ Name = 'a read of the configuration, checked against itself'; Expect = 'same'; Self = $true; Change = {
        Add-ToProject $project "<PropertyGroup Condition=`"'`$(Configuration)' == 'Release'`"><DefineConstants>`$(DefineConstants);Z</DefineConstants></PropertyGroup>"
    } }
Add-Case @{ Name = 'the release''s own commit, whatever the project holds'; Expect = 'same'; Identical = $true; Change = {
        Add-ToProject $project "<Import Project=`"extra.props`" /><PropertyGroup Condition=`"'`$(OS)' == 'Windows_NT'`">$overflow</PropertyGroup>"
    } }

try {
    $selected = @($cases | Where-Object { $_.Name -like $Filter })
    if ($selected.Count -eq 0) { throw "No case matches '$Filter'." }

    [void] (New-Item -ItemType Directory -Path $work)
    Invoke-WorkGit @('init', '-q')
    # Two files whose names differ only in case must stay two entries, whatever file system this runs on.
    Invoke-WorkGit @('config', 'core.ignorecase', 'false')
    $tracked = @(& git -C $root ls-files -- $library Directory.Build.props global.json packaging/nuget/README.md)
    if ($LASTEXITCODE -ne 0 -or $tracked.Count -eq 0) { throw 'Could not list the library files of this checkout.' }
    foreach ($file in $tracked) {
        Write-Text $file (([System.IO.File]::ReadAllText((Join-Path $root $file))) -replace "`r`n", "`n")
    }
    Save-Work 'base' $false
    $base = (& git -C $work rev-parse HEAD).Trim()

    $wrong = 0
    foreach ($case in $selected) {
        $indexOnly = $case.ContainsKey('IndexOnly')
        Reset-Work $base

        $release = $base
        if ($case.ContainsKey('Release')) {
            & $case.Release
            Save-Work "$($case.Name) (release)" $indexOnly
            $release = (& git -C $work rev-parse HEAD).Trim()
            if (-not $case.ContainsKey('OnRelease')) { Reset-Work $base }
        }
        & $case.Change
        Save-Work $case.Name $indexOnly
        if ($case.ContainsKey('Self')) {
            $release = (& git -C $work rev-parse HEAD).Trim()
            Invoke-WorkGit @('commit', '-q', '--allow-empty', '-m', "$($case.Name) (the same content again)")
        }
        if ($case.ContainsKey('Identical')) { $release = 'HEAD' }

        $answer = 'same'
        $message = ''
        try { & $guard -Release $release -Candidate HEAD -Repository $work 6>$null }
        catch {
            if ($_.Exception.Message -notlike "The library cannot be shown to be the release's*") { throw }
            $answer = 'different'
            $message = $_.Exception.Message
        }

        $right = $answer -eq $case.Expect
        if (-not $right) { $wrong++ }
        '{0,-5} {1}: {2} (expected {3})' -f ($(if ($right) { 'ok' } else { 'WRONG' })), $case.Name, $answer, $case.Expect
        if (-not $right -and $message) { $message }
    }

    if ($wrong -gt 0) { throw "$wrong of $($selected.Count) cases got the wrong answer." }
    "All $($selected.Count) cases got the right answer."
    # The last command may have failed on purpose; a caller that goes by $LASTEXITCODE, as the pwsh shell of GitHub Actions does, must not
    # take that for a failure of the tests.
    $global:LASTEXITCODE = 0
}
finally {
    if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
}
