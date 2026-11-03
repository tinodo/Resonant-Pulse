<#
.SYNOPSIS
    Tests Build-Site.ps1: the real site is accepted in both Store states, and each kind of defect that the script exists to catch is refused.

.DESCRIPTION
    Every case copies site/, changes the copy, runs Build-Site.ps1 on it in a new PowerShell and looks at the answer. A site that is
    right must pass (also when its links are written in the several valid ways, and when the privacy page differs from docs/PRIVACY.md
    in formatting only). A site with a link or a picture that points at nothing or outside the site, a page without what a page needs,
    unfinished text, a privacy policy that says something else, or too many bytes must fail, with the reason. Fails when any answer is
    wrong.
#>
[CmdletBinding()]
param(
    # The script to test, and a wildcard on the names of the cases to run: both are for trying the tests themselves on a weakened copy
    # (which must sit in this folder, as the script finds the repository from its own place).
    [string] $Tested = (Join-Path $PSScriptRoot 'Build-Site.ps1'),
    [string] $Filter = '*'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$buildScript = (Resolve-Path $Tested).Path
$pwsh = (Get-Process -Id $PID).Path
$work = Join-Path ([System.IO.Path]::GetTempPath()) ('rp-site-tests-' + [guid]::NewGuid().ToString('N'))
$source = Join-Path $work 'source'
$output = Join-Path $work 'output'
$utf8 = [System.Text.UTF8Encoding]::new($false)

function Edit-Site([string] $Page, [string] $Old, [string] $New) {
    $path = Join-Path $source $Page
    $text = [System.IO.File]::ReadAllText($path)
    if (-not $text.Contains($Old)) { throw "The test expects '$Old' in $Page and it is not there: update the test." }
    [System.IO.File]::WriteAllText($path, $text.Replace($Old, $New), $utf8)
}

function Edit-SitePattern([string] $Page, [string] $Pattern, [string] $New) {
    $path = Join-Path $source $Page
    $text = [System.IO.File]::ReadAllText($path)
    $changed = [regex]::Replace($text, $Pattern, $New)
    if ($changed -ceq $text) { throw "The test expects a match of '$Pattern' in $Page and finds none: update the test." }
    [System.IO.File]::WriteAllText($path, $changed, $utf8)
}

# Adds a paragraph to the end of the home page.
function Add-ToHome([string] $Html) { Edit-Site 'index.html' '</body>' "$Html`n</body>" }

# 'accepted', or a pattern that the reason for the refusal must match.
$cases = @(
    @{ Name = 'the site as it is, the Store listing pending'; Expect = 'accepted'; Change = { } }
    @{ Name = 'the site as it is, the Store listing published'; Expect = 'accepted'; Arguments = @{ StorePublished = 'true'; StoreProductId = '9NRFH2C8X2QL' }; Change = { } }
    @{ Name = 'links to the root of the site, with the base path, and with the public address'; Expect = 'accepted'; Change = {
            Add-ToHome '<p><a href=".">a</a> <a href="./">b</a> <a href="/Resonant-Pulse/">c</a> <a href="https://tinodo.github.io/Resonant-Pulse/privacy.html">d</a> <a href="privacy.html?x=1">e</a></p>'
        } }
    @{ Name = 'a formatting-only change of the privacy page'; Expect = 'accepted'; Change = {
            Edit-Site 'privacy.html' '<p>Resonant Pulse works on your own computer.' '<p><strong>Resonant Pulse</strong> works on your own computer.'
        } }

    @{ Name = 'a link to a page that does not exist'; Expect = "points at nothing"; Change = { Add-ToHome '<p><a href="nowhere.html">x</a></p>' } }
    @{ Name = 'a link to an anchor that is not on the page'; Expect = 'has no target'; Change = { Add-ToHome '<p><a href="#nothing-here">x</a></p>' } }
    @{ Name = 'a link to an anchor that is not on the other page'; Expect = 'has no #nothing-here in its target'; Change = { Add-ToHome '<p><a href="privacy.html#nothing-here">x</a></p>' } }
    @{ Name = 'a picture that does not exist'; Expect = 'points at nothing'; Change = { Add-ToHome '<p><img src="screenshots/09-nothing.png" alt="x"></p>' } }
    @{ Name = 'a picture without alt text'; Expect = 'has no alt text'; Change = { Add-ToHome '<p><img src="screenshots/01-play.png"></p>' } }
    @{ Name = 'a link that climbs out of the site to a file that exists outside it'; Expect = 'points outside the site'; Change = {
            [System.IO.File]::WriteAllText((Join-Path $work 'outside.html'), '<html></html>', $utf8)
            Add-ToHome '<p><a href="../outside.html">x</a></p>'
        } }
    @{ Name = 'a link to a path outside the base path'; Expect = 'absolute path outside'; Change = { Add-ToHome '<p><a href="/elsewhere/x.html">x</a></p>' } }
    @{ Name = 'a link that is not https'; Expect = 'is not https'; Change = { Add-ToHome '<p><a href="http://example.com/">x</a></p>' } }
    @{ Name = 'a page without a title'; Expect = 'no title'; Change = { Edit-SitePattern 'index.html' '(?s)<title>.*?</title>' '' } }
    @{ Name = 'a page without a description'; Expect = 'no description'; Change = { Edit-SitePattern 'index.html' '<meta name="description" content="[^"]*">' '' } }
    @{ Name = 'a page without a language'; Expect = 'has no lang'; Change = { Edit-Site 'index.html' '<html lang="en">' '<html>' } }
    @{ Name = 'unfinished text'; Expect = 'unfinished text'; Change = { Add-ToHome '<p>TODO write this</p>' } }
    @{ Name = 'a placeholder left in'; Expect = 'placeholder'; Change = { Add-ToHome '<p>{{SOMETHING}}</p>' } }
    @{ Name = 'a preview picture of the wrong size'; Expect = 'og-image.png is not a 1200x630 PNG'; Change = {
            $png = [byte[]](0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0x0D, 0x49, 0x48, 0x44, 0x52, 0, 0, 0, 0x0A, 0, 0, 0, 0x0A)
            [System.IO.File]::WriteAllBytes((Join-Path $source 'assets/og-image.png'), $png)
        } }
    @{ Name = 'a privacy policy with another effective date'; Expect = 'The privacy policy is dated'; Change = {
            Edit-SitePattern 'privacy.html' 'Effective date: \d{1,2} [A-Za-z]+ \d{4}' 'Effective date: 1 January 2020'
        } }
    @{ Name = 'a privacy policy with a changed word'; Expect = 'differs between docs/PRIVACY.md and privacy.html'; Change = {
            Edit-Site 'privacy.html' 'shows no advertising' 'shows advertising'
        } }
    @{ Name = 'a site over the size budget'; Expect = 'more than the \d+ byte budget'; Change = {
            [System.IO.File]::WriteAllBytes((Join-Path $source 'assets/big.bin'), [byte[]]::new(9MB))
        } }
    @{ Name = 'a published Store listing without a product id'; Expect = 'product id'; Arguments = @{ StorePublished = 'true' }; Change = { } }
    @{ Name = 'a Store variable that is neither true nor false'; Expect = 'must be true or false'; Arguments = @{ StorePublished = 'yes' }; Change = { } }
)

try {
    $cases = @($cases | Where-Object { $_.Name -like $Filter })
    if ($cases.Count -eq 0) { throw "No case matches '$Filter'." }

    $wrong = 0
    foreach ($case in $cases) {
        foreach ($folder in $source, $output) { if (Test-Path -LiteralPath $folder) { Remove-Item -LiteralPath $folder -Recurse -Force } }
        New-Item -ItemType Directory -Path $work -Force | Out-Null
        Copy-Item -LiteralPath (Join-Path $root 'site') -Destination $source -Recurse
        & $case.Change

        $arguments = @('-NoProfile', '-File', $buildScript, '-Source', $source, '-Output', $output)
        if ($case.ContainsKey('Arguments')) { foreach ($key in $case.Arguments.Keys) { $arguments += "-$key"; $arguments += [string] $case.Arguments[$key] } }
        $text = (& $pwsh @arguments 2>&1 | ForEach-Object { $_.ToString() }) -join "`n"
        $exitCode = $LASTEXITCODE

        $right = if ($case.Expect -eq 'accepted') { $exitCode -eq 0 } else { $exitCode -ne 0 -and $text -match $case.Expect }
        if (-not $right) { $wrong++ }
        $answer = if ($exitCode -eq 0) { 'accepted' } else { 'refused' }
        '{0,-5} {1}: {2} (expected {3})' -f ($(if ($right) { 'ok' } else { 'WRONG' })), $case.Name, $answer, $(if ($case.Expect -eq 'accepted') { 'accepted' } else { "refused, '$($case.Expect)'" })
        if (-not $right) { $text }
    }

    if ($wrong -gt 0) { throw "$wrong of $($cases.Count) cases got the wrong answer." }
    "All $($cases.Count) cases got the right answer."
    # The last child process may have been a build that was refused on purpose (exit code 1). A caller that goes by $LASTEXITCODE, as the
    # pwsh shell of GitHub Actions does, must not take that for a failure of the tests.
    $global:LASTEXITCODE = 0
}
finally {
    if (Test-Path -LiteralPath $work) { Remove-Item -LiteralPath $work -Recurse -Force }
}
