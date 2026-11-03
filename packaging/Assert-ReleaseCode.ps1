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
        into the package. Everything else counts, however it is written: a property, an item, a condition, a target, an import;
      * the other build files (Directory.Build.rsp, MSBuild.rsp, NuGet.config, global.json), by content.

    The check reads only what is listed. A project or build file that imports another file or points outside the library's folder
    (an Import or Sdk element, a path with .., a rooted path, or one made from the project's or the solution's directory) depends on files
    that are not read, so it is refused whatever the other commit holds, until the check is taught to read them.

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
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)

$libraryFolder = 'src/ResonantPulse.Core'
$projectFile = "$libraryFolder/ResonantPulse.Core.csproj"
$xmlBuildFiles = @('Directory.Build.props', 'Directory.Build.targets', 'Directory.Packages.props')
$otherBuildFiles = @('Directory.Build.rsp', 'MSBuild.rsp', 'NuGet.config', 'nuget.config', 'NuGet.Config', 'global.json')

# What only describes the package or the version, and so cannot change what the library does.
$permittedProperties = @(
    'IsPackable', 'PackageId', 'Description', 'PackageTags', 'PackageReadmeFile', 'PackageProjectUrl', 'PackageLicenseExpression',
    'Copyright', 'RepositoryUrl', 'RepositoryType', 'PublishRepositoryUrl', 'Authors', 'Company', 'Product', 'Version')
$permittedItems = @('../../packaging/nuget/README.md')

# A project that imports another file, or points outside its own folder, depends on files that this check does not read. It is not compared
# further: it is refused, and the check must be taught to read what it points at before such a library can be published later.
$outsideReference = '(^|[^\w.])\.\.([^\w.]|$)|^\s*[\\/]|^\s*[A-Za-z]:[\\/]|\$\((MSBuild(This|Project|Startup)\w*|Solution(Dir|Path))\)'
$unreadReferences = [System.Collections.Generic.List[string]]::new()

function Invoke-Git([string[]] $Arguments) {
    $output = & git -C $Repository -c core.quotepath=false @Arguments
    if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed with exit code $LASTEXITCODE." }
    $output
}

function Find-UnreadReferences($Root, [string] $Name) {
    foreach ($element in @($Root.SelectNodes('descendant-or-self::*'))) {
        if ($element.LocalName -in 'Import', 'Sdk') { "$Name has an <$($element.LocalName)> element, and an imported file is not read" }
        foreach ($attribute in @($element.Attributes)) {
            if ($attribute.Value -match $outsideReference) {
                "$Name has <$($element.LocalName) $($attribute.LocalName)=""$($attribute.Value)""> which refers outside the library's folder"
            }
        }
        foreach ($node in @($element.ChildNodes | Where-Object { $_.NodeType -in 'Text', 'CDATA' })) {
            if ($node.Value -match $outsideReference) { "$Name has <$($element.LocalName)> with '$($node.Value.Trim())' which refers outside the library's folder" }
        }
    }
}

function Write-CanonicalElement($Element, [int] $Depth, $Lines) {
    $pad = '  ' * $Depth
    $attributes = @($Element.Attributes | Sort-Object Name | ForEach-Object { '{0}="{1}"' -f $_.Name, $_.Value })
    $Lines.Add($pad + '<' + ((@($Element.Name) + $attributes) -join ' ') + '>')
    foreach ($child in $Element.ChildNodes) {
        if ($child.NodeType -eq 'Element') {
            Write-CanonicalElement $child ($Depth + 1) $Lines
        }
        elseif ($child.NodeType -in 'Text', 'CDATA', 'SignificantWhitespace') {
            $text = $child.Value.Trim()
            if ($text) { $Lines.Add($pad + '  ' + $text) }
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
    $entries = [ordered] @{}
    $listing = @(Invoke-Git (@('ls-tree', '-r', $Revision, '--', $libraryFolder) + $paths)) | Where-Object { $_ }
    foreach ($line in $listing) {
        if ($line -notmatch '^\d+ \w+ ([0-9a-f]+)\t(.+)$') { throw "Unexpected line from git ls-tree: $line" }
        $entries[$Matches[2]] = $Matches[1]
    }

    $library = foreach ($path in $entries.Keys) {
        if ($path.StartsWith("$libraryFolder/") -and $path -notmatch '(/ResonantPulse\.Core\.csproj|/README\.md|/packages\.lock\.json)$') {
            "$($entries[$path]) $path"
        }
    }
    $inputs = [ordered] @{ 'library files' = (@($library) -join "`n") }

    $xml = @{ $projectFile = $true }
    foreach ($directory in $libraryFolder, 'src', '') {
        foreach ($name in $xmlBuildFiles) {
            $path = if ($directory) { "$directory/$name" } else { $name }
            $xml[$path] = $name -ne 'Directory.Packages.props'
        }
        foreach ($name in $otherBuildFiles) {
            $path = if ($directory) { "$directory/$name" } else { $name }
            $inputs[$path] = if ($entries.Contains($path)) { "content $($entries[$path])" } else { '(missing)' }
        }
    }
    foreach ($path in $xml.Keys) {
        $inputs[$path] = if ($entries.Contains($path)) {
            ConvertTo-CanonicalProject ((Invoke-Git @('cat-file', 'blob', $entries[$path])) -join "`n") $xml[$path] "$path at $Revision"
        }
        else { '(missing)' }
    }
    $inputs
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
