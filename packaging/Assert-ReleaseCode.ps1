<#
.SYNOPSIS
    Checks that a commit builds the library of a release, so that the package of that release can be made from it.

.DESCRIPTION
    package.yml publishes the package of a version that already has a GitHub release, and for a release that was published before the
    packaging existed (1.0.0) that is a later commit than the tagged one. That is safe only if the later commit builds the same library.
    The two commits are compared on everything that decides what the library compiles to:

      * every file under src/ResonantPulse.Core, by content (its README and the lock file of a project without dependencies excepted);
      * the project file, and the Directory.Build.props, Directory.Build.targets and Directory.Packages.props files that MSBuild reads from
        the project's folder up to the root, as parsed XML. Comments and layout do not count, and neither do the properties that only
        describe the package or the version (description, tags, URLs, copyright, license, authors, version) or the README that is packed
        into the package. Everything else counts, however it is written: a property, an item, a condition, an attribute, a text;
      * the other build files (Directory.Build.rsp, MSBuild.rsp, NuGet.config, global.json) and the analyzer configuration (.editorconfig,
        .globalconfig, which can configure source generators), by content.

    The check reads only what is listed, so a project that depends on anything else is refused, whatever the other commit holds, until
    the check is taught to read it:

      * an Import, ImportGroup, Sdk, UsingTask or Target element, or a ProjectReference: another file, a task, or code that this check
        does not follow or evaluate;
      * a path outside the library's folder (with .., rooted, wherever it stands, or made from the project's or the solution's directory),
        a condition that looks at the file system (Exists, HasTrailingSlash), a property function, $([...]), which can read any file, or an
        escaped character (%2E), which can hide any of these;
      * a read of anything that the pipeline does not fix. Only $(Version) and $(Configuration), which the package is made with (-p:Version
        and --configuration Release: a global property wins over every file, so both commits read the same value), and $(NoWarn) inside
        the NoWarn property itself (<NoWarn>$(NoWarn);1591</NoWarn>, which adds to the default and hands it on to nothing else) may be
        read. A property that the files define themselves may not, however it is defined: its value can come from the environment
        (<P>$(P)</P> reads the environment's P), from itself, from a cycle, or write a path (..) one piece at a time. Nor may an
        environment variable, the operating system, a reserved property, an item list or item metadata; $(Name.Method()), %(Metadata)
        and @(Items) are refused whatever their names;
      * a package property that the rest of the project reads ($(Product), ...), because that property is left out of the comparison;
      * a package reference (the library has no package dependencies, and a package that restore resolves is code that is not among the
        compared files), and a name or a wildcard that could take in a file that this check leaves out of the comparison (README.md, the
        lock file, the project file, all in the library's own folder), which would then be a build input that nobody compares;
      * a CDATA section, which MSBuild reads in a way that this check does not follow, and a symbolic link or a submodule among the
        library's files, which are compared by their entries and not by what they point at. (Text that a comment splits is read joined,
        as MSBuild joins it: $<!---->(OS) is read as $(OS).)
      * TreatAsLocalProperty, which lets a project override the properties given on the command line: the package is made with
        -p:Version, so the version can be left out of the comparison and read freely only while nothing can override it.

    When the candidate is the release's own commit (release.yml makes the package at the commit it has just released) nothing is compared:
    the library is the release's.

    The package itself is made by the candidate's own packaging script (Build-NuGetPackage.ps1), which gives the version and the
    configuration and nothing else. It is part of the reviewed pipeline and is not compared.

    Throws, naming what differs or what is not read, when the library of the candidate cannot be shown to be the release's.

.PARAMETER Release
    The commit of the release (the tag's commit).

.PARAMETER Candidate
    The commit that the package would be built from.

.PARAMETER Repository
    The repository to read from. The current folder by default.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $Release,

    [string] $Candidate = 'HEAD',

    [string] $Repository = (Get-Location).Path
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$Repository = (Resolve-Path -LiteralPath $Repository).Path

$libraryFolder = 'src/ResonantPulse.Core'
$projectFile = "$libraryFolder/ResonantPulse.Core.csproj"
$xmlBuildFiles = @('Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props')
$otherBuildFiles = @('Directory.Build.rsp', 'MSBuild.rsp', 'NuGet.config', 'nuget.config', 'NuGet.Config', 'global.json', '.editorconfig', '.globalconfig')

# What only describes the package or the version, and so cannot change what the library does.
$permittedProperties = @(
    'IsPackable', 'PackageId', 'Description', 'PackageTags', 'PackageReadmeFile', 'PackageProjectUrl', 'PackageLicenseExpression',
    'Copyright', 'RepositoryUrl', 'RepositoryType', 'PublishRepositoryUrl', 'Authors', 'Company', 'Product', 'Version')
$permittedItems = @('../../packaging/nuget/README.md')

# What the check cannot follow or evaluate (see the description). The version is read harmlessly: the package is made with -p:Version, which
# wins over every file, on both sides, unless the project treats the version as local (TreatAsLocalProperty), which is refused too.
# The library has no package dependencies, and a package that restore resolves (a floating version, the lock file that this check leaves out)
# is code that is not among the compared files.
$unreadElements = @('Import', 'ImportGroup', 'Sdk', 'UsingTask', 'Target', 'ProjectReference', 'PackageReference', 'GlobalPackageReference', 'PackageDownload')
# The files that the comparison of the library's folder leaves out (they differ between a release and a later commit), and what could name them.
$leftOutOrWildcard = '(?i)README|packages\.lock\.json|\.csproj|[*?]'
$outsideReference = '(^|[^\w.])\.\.([^\w.]|$)' +                      # a path that climbs out of a folder
    '|(^|[\s''"(;=,])[\\/][^\s''")]' +                                # a rooted path, at the start of a value or inside one (a condition)
    '|(^|\W)[A-Za-z]:[\\/]' +                                         # a path with a drive letter
    '|\b(Exists|HasTrailingSlash)\s*\(' +                             # a condition that looks at the file system
    '|\$\((MSBuild(This|Project|Startup)\w*|Solution(Dir|Path))\)' +  # a path made from the project's or the solution's place
    '|\$\(\['                                                         # a property function, which can read anything
$packageProperty = '[$%@]\((' + (($permittedProperties | Where-Object { $_ -ne 'Version' }) -join '|') + ')\b'
# An escaped character (%2E, %24) can write any of the characters above without them showing.
$escapedCharacter = '%[0-9A-Fa-f]{2}'
# What a value or a condition reads. Only a plain $(Name) is understood. Anything else that starts an expression, $(Name.Method()), %(Metadata),
# @(Items), is refused.
$plainRead = '\$\(([A-Za-z_][\w\-]*)\)'
$expression = '[$%@]\('
# The properties that the pipeline fixes for both commits (-p:Version, --configuration Release): no file can change them (TreatAsLocalProperty,
# which could, is refused), so a read of one gives the same value on both sides and can be neither made from the environment nor a path.
$pipelineGiven = @('Version', 'Configuration')
$unreadReferences = [System.Collections.Generic.List[string]]::new()

# git's output as one string, as it is: no lines, no console encoding, so that names and files are read exactly.
function Invoke-Git([string[]] $Arguments) {
    $start = [System.Diagnostics.ProcessStartInfo]::new('git')
    foreach ($argument in @('-C', $Repository, '-c', 'core.quotepath=false') + $Arguments) { $start.ArgumentList.Add($argument) }
    $start.UseShellExecute = $false
    $start.RedirectStandardOutput = $true
    $start.StandardOutputEncoding = [System.Text.UTF8Encoding]::new($false)
    $process = [System.Diagnostics.Process]::Start($start)
    $output = $process.StandardOutput.ReadToEnd()
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "git $($Arguments -join ' ') failed with exit code $($process.ExitCode)." }
    $output
}

# Text on one line, escaped, so that two different texts never print the same.
function ConvertTo-Literal([string] $Text) {
    '"' + $Text.Replace('\', '\\').Replace('"', '\"').Replace("`r", '\r').Replace("`n", '\n').Replace("`t", '\t') + '"'
}

function Find-UnreadReferences($Root, [string] $Name) {
    foreach ($element in @($Root.SelectNodes('descendant-or-self::*'))) {
        if ($element.LocalName -in $unreadElements) { "$Name has a <$($element.LocalName)> element, which this check does not follow or evaluate" }
        foreach ($attribute in @($element.Attributes)) {
            if ($attribute.LocalName -eq 'TreatAsLocalProperty') {
                "$Name has TreatAsLocalProperty=""$($attribute.Value)"" on <$($element.LocalName)>, which lets the project override the properties given on the command line"
            }
        }
        # MSBuild joins the pieces of the text of an element (a comment may stand between them: $<!---->(OS) is $(OS)), so they are read joined, and
        # it reads a CDATA section in a way that this check does not follow.
        if (@($element.ChildNodes | Where-Object { $_.NodeType -eq 'CDATA' }).Count -gt 0) {
            "$Name has a CDATA section in <$($element.LocalName)>, which MSBuild reads in a way that this check does not follow"
        }
        $content = -join @($element.ChildNodes | Where-Object { $_.NodeType -in 'Text', 'CDATA', 'SignificantWhitespace', 'Whitespace' } | ForEach-Object { $_.Value })
        $values = @($element.Attributes | ForEach-Object { [pscustomobject] @{ Text = $_.Value; IsContent = $false } }) +
            @([pscustomobject] @{ Text = $content; IsContent = $true })
        foreach ($item in $values) {
            $value = $item.Text
            if ($value -match $outsideReference) {
                "$Name has '$($value.Trim())' in <$($element.LocalName)>, which refers outside the library's folder or runs code"
            }
            if ($value -match $leftOutOrWildcard) {
                "$Name has '$($value.Trim())' in <$($element.LocalName)>, which names a file that this check leaves out of the comparison (a README, a lock file) or could match one (a wildcard)"
            }
            if ($value -match $packageProperty) {
                "$Name reads the package property $($Matches[1]) in <$($element.LocalName)>, which this check leaves out of the comparison"
            }
            if ($value -match $escapedCharacter) {
                "$Name has '$($value.Trim())' in <$($element.LocalName)>, an escaped character, which can hide what this check looks for"
            }
            foreach ($read in [regex]::Matches($value, $plainRead)) {
                $readName = $read.Groups[1].Value
                # <NoWarn>$(NoWarn);1591</NoWarn> adds to the default value of the property and hands it on to nothing else.
                $accumulates = $item.IsContent -and $element.LocalName -eq 'NoWarn' -and $readName -eq 'NoWarn' -and $element.ParentNode.LocalName -eq 'PropertyGroup'
                if ($pipelineGiven -notcontains $readName -and -not $accumulates) {
                    "$Name reads $($read.Value) in <$($element.LocalName)>, which can come from the environment, from a definition of the project or from the operating system, or be a piece of a path, and this check does not follow that"
                }
            }
            if ([regex]::Replace($value, $plainRead, '') -match $expression) {
                "$Name has '$($value.Trim())' in <$($element.LocalName)>, an expression, item list or item metadata (not a plain `$(Name)), which this check does not evaluate"
            }
        }
    }
}

function Write-CanonicalElement($Element, [int] $Depth, $Lines) {
    $pad = '  ' * $Depth
    $attributes = @($Element.Attributes | Sort-Object Name | ForEach-Object { '{0}={1}' -f $_.Name, (ConvertTo-Literal $_.Value) })
    $Lines.Add($pad + '<' + ((@($Element.Name) + $attributes) -join ' ') + '>')
    foreach ($child in $Element.ChildNodes) {
        if ($child.NodeType -eq 'Element') {
            Write-CanonicalElement $child ($Depth + 1) $Lines
        }
        elseif ($child.NodeType -in 'Text', 'CDATA', 'SignificantWhitespace') {
            $text = $child.Value.Trim()
            if ($text) { $Lines.Add($pad + '  #text ' + (ConvertTo-Literal $text)) }
        }
    }
    $Lines.Add($pad + '</' + $Element.Name + '>')
}

# The project as MSBuild reads it, written one way: no comments, no layout, attributes in order, an element on its own lines; and without the
# package metadata that may differ.
function ConvertTo-CanonicalProject([string] $Xml, [bool] $DropPackageMetadata, [string] $Name) {
    $document = [System.Xml.XmlDocument]::new()
    $document.XmlResolver = $null
    try { $document.LoadXml($Xml.TrimStart([char] 0xFEFF)) }
    catch { throw "$Name is not valid XML: $($_.Exception.Message)" }

    foreach ($comment in @($document.SelectNodes('//comment()'))) { [void] $comment.ParentNode.RemoveChild($comment) }
    # The text on both sides of a comment is one text, as MSBuild reads it (FOO<!-- c -->BAR is FOOBAR, and FOO<!-- c --> BAR is FOO BAR).
    $document.Normalize()
    $root = $document.DocumentElement
    if ($DropPackageMetadata) {
        foreach ($group in @($root.ChildNodes | Where-Object { $_.NodeType -eq 'Element' })) {
            foreach ($child in @($group.ChildNodes | Where-Object { $_.NodeType -eq 'Element' })) {
                $drop = switch ($group.LocalName) {
                    'PropertyGroup' { $permittedProperties -contains $child.LocalName }
                    'ItemGroup' { $child.LocalName -eq 'None' -and $permittedItems -contains ($child.GetAttribute('Include') -replace '\\', '/') }
                    default { $false }
                }
                if ($drop) { [void] $group.RemoveChild($child) }
            }
        }
    }
    foreach ($group in @($root.ChildNodes | Where-Object { $_.NodeType -eq 'Element' -and $_.LocalName -in 'PropertyGroup', 'ItemGroup' })) {
        if (-not $group.HasChildNodes) { [void] $root.RemoveChild($group) }
    }
    foreach ($reference in @(Find-UnreadReferences $root $Name)) { $script:unreadReferences.Add($reference) }

    $lines = [System.Collections.Generic.List[string]]::new()
    Write-CanonicalElement $root 0 $lines
    $lines -join "`n"
}

function Get-Inputs([string] $Revision) {
    $paths = foreach ($directory in $libraryFolder, 'src', '') {
        foreach ($name in $xmlBuildFiles + $otherBuildFiles) { if ($directory) { "$directory/$name" } else { $name } }
    }
    # Names are compared exactly: two files whose names differ only in case are two files.
    $entries = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::Ordinal)
    $listing = Invoke-Git (@('ls-tree', '-r', '-z', $Revision, '--', $libraryFolder) + $paths)
    foreach ($entry in $listing -split "`0") {
        if (-not $entry) { continue }
        if ($entry -cnotmatch '(?s)^(\d+) \w+ ([0-9a-f]+)\t(.+)$') { throw "Unexpected entry from git ls-tree: $entry" }
        $mode, $blob, $entryPath = $Matches[1], $Matches[2], $Matches[3]
        $entries[$entryPath] = $blob
        # A link or a submodule is compared by what its entry says, not by what it points at, and what it points at is built.
        if ($mode -in '120000', '160000') {
            $script:unreadReferences.Add("$entryPath at $Revision is a symbolic link or a submodule, which this check compares by its entry and not by what it points at")
        }
    }

    $library = foreach ($path in $entries.Keys) {
        # Only these three, in the library's own folder, are left out: the project file is compared as XML, and the other two differ between a
        # release and a later commit. Anywhere else a file of that name is a library file like any other.
        if ($path.StartsWith("$libraryFolder/", [System.StringComparison]::Ordinal) -and $path -cne $projectFile -and
            $path -cne "$libraryFolder/README.md" -and $path -cne "$libraryFolder/packages.lock.json") {
            "$($entries[$path]) $(ConvertTo-Literal $path)"
        }
    }
    $inputs = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::Ordinal)
    $inputs['library files'] = @($library) -join "`n"

    $xml = [System.Collections.Generic.Dictionary[string, bool]]::new([System.StringComparer]::Ordinal)
    $xml[$projectFile] = $true
    foreach ($directory in $libraryFolder, 'src', '') {
        foreach ($name in $xmlBuildFiles) {
            $path = if ($directory) { "$directory/$name" } else { $name }
            $xml[$path] = $name -ne 'Directory.Packages.props'
        }
        foreach ($name in $otherBuildFiles) {
            $path = if ($directory) { "$directory/$name" } else { $name }
            $inputs[$path] = if ($entries.ContainsKey($path)) { "content $($entries[$path])" } else { '(missing)' }
        }
    }
    foreach ($path in $xml.Keys) {
        $inputs[$path] = if ($entries.ContainsKey($path)) {
            ConvertTo-CanonicalProject (Invoke-Git @('cat-file', 'blob', $entries[$path])) $xml[$path] "$path at $Revision"
        }
        else { '(missing)' }
    }
    , $inputs
}

# The package of a release that is made at the release's own commit (release.yml does) needs no comparison.
$releaseCommit = (Invoke-Git @('rev-parse', '--verify', "$Release^{commit}")).Trim()
$candidateCommit = (Invoke-Git @('rev-parse', '--verify', "$Candidate^{commit}")).Trim()
if ($releaseCommit -eq $candidateCommit) {
    Write-Host "The candidate $Candidate is the release's own commit, ${releaseCommit}: the library is the release's."
    return
}

$before = Get-Inputs $Release
$after = Get-Inputs $Candidate

$report = [System.Collections.Generic.List[string]]::new()
foreach ($name in $before.Keys) {
    if ($before[$name] -cne $after[$name]) {
        $report.Add("  $name")
        $differences = Compare-Object -ReferenceObject ($before[$name] -split "`n") -DifferenceObject ($after[$name] -split "`n") -CaseSensitive |
            Select-Object -First 12
        foreach ($difference in $differences) {
            $side = if ($difference.SideIndicator -eq '<=') { 'release:  ' } else { 'candidate:' }
            $report.Add("    $side $($difference.InputObject)")
        }
    }
}
foreach ($reference in $unreadReferences | Sort-Object -Unique) { $report.Add("  not compared: $reference") }
if ($report.Count -gt 0) {
    throw "The library cannot be shown to be the release's ($Release and $Candidate):`n$($report -join "`n")"
}
Write-Host "The library and its build settings at $Candidate are those of the release at $Release."
