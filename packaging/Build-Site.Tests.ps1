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

# Adds a paragraph to the end of the privacy page.
function Add-ToPrivacy([string] $Html) { Edit-Site 'privacy.html' '</body>' "$Html`n</body>" }

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
    @{ Name = 'links from another page to anchors of the home page, written with the base path and with the public address'; Expect = 'accepted'; Change = {
            Add-ToPrivacy '<p><a href="/Resonant-Pulse/#download">a</a> <a href="https://tinodo.github.io/Resonant-Pulse/#features">b</a> <a href="./#download">c</a></p>'
        } }
    @{ Name = 'a link from another page to an anchor that the home page does not have'; Expect = 'has no #nothing-here in its target'; Change = {
            Add-ToPrivacy '<p><a href="/Resonant-Pulse/#nothing-here">x</a></p>'
        } }
    @{ Name = 'a code sample on a page that shows a url() to nothing'; Expect = 'accepted'; Change = {
            Add-ToHome '<p><code>background: url(nothing.png)</code></p>'
        } }
    @{ Name = 'a comment in a style sheet that mentions a url() to nothing'; Expect = 'accepted'; Change = {
            Edit-Site 'assets/site.css' '.wrap {' '/* was: url(old.png) */ .wrap {'
        } }
    @{ Name = 'a style attribute with a url() to a file that is not there'; Expect = 'points at nothing'; Change = {
            Add-ToHome '<p style="background: url(missing.png)">x</p>'
        } }
    @{ Name = 'a style block with a url() to a file that is not there'; Expect = 'points at nothing'; Change = {
            Add-ToHome '<style>.y { background: url(missing.png); }</style>'
        } }
    @{ Name = 'a style sheet that imports a style sheet that is not there'; Expect = 'points at nothing'; Change = {
            Edit-Site 'assets/site.css' '.wrap {' '@import "missing.css"; .wrap {'
        } }
    @{ Name = 'a link in single quotes'; Expect = 'not written name='; Change = { Add-ToHome "<p><a href='nowhere.html'>x</a></p>" } }
    @{ Name = 'a link without quotes'; Expect = 'not written name='; Change = { Add-ToHome '<p><a href=nowhere.html>x</a></p>' } }
    @{ Name = 'a link with spaces around the equals sign'; Expect = 'not written name='; Change = { Add-ToHome '<p><a href = "nowhere.html">x</a></p>' } }
    @{ Name = 'a style attribute in single quotes'; Expect = 'not written name='; Change = { Add-ToHome "<p style='background: url(missing.png)'>x</p>" } }
    @{ Name = 'a style attribute with spaces around the equals sign'; Expect = 'not written name='; Change = {
            Add-ToHome '<p style = "background: url(missing.png)">x</p>'
        } }
    @{ Name = 'comment delimiters inside strings of a style sheet, then a url() to nothing'; Expect = 'points at nothing'; Change = {
            Edit-Site 'assets/site.css' '.wrap {' '.a::before { content: "/*"; } .b { background: url(missing.png); } .c::after { content: "*/"; } .wrap {'
        } }
    @{ Name = 'an apostrophe in a comment of a style sheet, then a url() to nothing'; Expect = 'points at nothing'; Change = {
            Edit-Site 'assets/site.css' '.wrap {' "/* it's a note */ .b { background: url(missing.png); } .wrap {"
        } }
    @{ Name = 'a comment in a style sheet that holds a string and a url() to nothing'; Expect = 'accepted'; Change = {
            Edit-Site 'assets/site.css' '.wrap {' '/* was: "x" and url(old.png) */ .wrap {'
        } }
    @{ Name = 'an unquoted url() that holds a comment delimiter, then a url() to nothing'; Expect = 'points at nothing'; Change = {
            Edit-Site 'assets/site.css' '.wrap {' '.a { background: url(https://example.com/images/*); } .b { background: url(missing.png); } .wrap {'
        } }
    @{ Name = 'a style attribute that hides a comment delimiter in character references, then a url() to nothing'; Expect = 'points at nothing'; Change = {
            Add-ToHome '<p style="--label: &quot;/*&quot;; background: url(missing.png)">x</p>'
        } }
    @{ Name = 'an @import in capitals to a style sheet that is not there'; Expect = 'points at nothing'; Change = {
            Edit-Site 'assets/site.css' '.wrap {' '@IMPORT "missing.css"; .wrap {'
        } }
    @{ Name = 'text in a string of a style sheet that looks like an @import'; Expect = 'accepted'; Change = {
            Edit-Site 'assets/site.css' '.wrap {' ".example::before { content: '@import ""missing.css""'; } .wrap {"
        } }
    @{ Name = 'a url() in capitals to a file that is not there'; Expect = 'points at nothing'; Change = {
            Edit-Site 'assets/site.css' '.wrap {' '.x { background: URL(missing.png); } .wrap {'
        } }
    @{ Name = 'a string that is not closed ends at its line, then a url() to nothing'; Expect = 'points at nothing'; Change = {
            Edit-Site 'assets/site.css' '.wrap {' ".a { content: `"oops`n} .b { background: url(missing.png); } .wrap {"
        } }
    @{ Name = 'a string that is not closed ends at a carriage return, then a url() to nothing'; Expect = 'points at nothing'; Change = {
            Edit-Site 'assets/site.css' '.wrap {' ".a { content: `"oops`r} .b { background: url(missing.png); } .wrap {"
        } }
    @{ Name = 'a line continuation inside a string of a style sheet, then a url() to nothing'; Expect = 'points at nothing'; Change = {
            Edit-Site 'assets/site.css' '.wrap {' ".a { content: `"one\`r`ntwo`"; } .b { background: url(missing.png); } .wrap {"
        } }
    @{ Name = 'a ">" inside a quoted value, then a link in single quotes'; Expect = 'not written name='; Change = {
            Add-ToHome '<p><a title="1 > 0" href=''nowhere.html''>x</a></p>'
        } }
    @{ Name = 'text inside a quoted value that looks like an attribute with spaces around the equals sign'; Expect = 'accepted'; Change = {
            Add-ToHome '<p><a title="the id = 3" href="privacy.html">x</a></p>'
        } }
    @{ Name = 'a link with its attribute name in capitals, to nothing'; Expect = 'points at nothing'; Change = { Add-ToHome '<p><a HREF="nowhere.html">x</a></p>' } }
    @{ Name = 'an @import with a comment before its address'; Expect = 'points at nothing'; Change = {
            Edit-Site 'assets/site.css' '.wrap {' '@import /**/ "missing.css"; .wrap {'
        } }
    @{ Name = 'an image-set() with an address in a string'; Expect = 'does not read'; Change = {
            Edit-Site 'assets/site.css' '.wrap {' '.x { background: image-set("missing.png" 1x); } .wrap {'
        } }
    @{ Name = 'a prefixed image-set()'; Expect = 'does not read'; Change = {
            Edit-Site 'assets/site.css' '.wrap {' '.x { background: -webkit-image-set("missing.png" 1x); } .wrap {'
        } }
    @{ Name = 'a src() with an address in a string'; Expect = 'does not read'; Change = {
            Edit-Site 'assets/site.css' '.wrap {' '.x { background: src("missing.png"); } .wrap {'
        } }
    @{ Name = 'an escaped keyword'; Expect = 'does not read'; Change = {
            Edit-Site 'assets/site.css' '.wrap {' '.x { background: u\72l(missing.png); } .wrap {'
        } }
    @{ Name = 'an escape inside a string of a style sheet'; Expect = 'accepted'; Change = {
            Edit-Site 'assets/site.css' '.wrap {' '.a::before { content: "\2192"; } .wrap {'
        } }
    @{ Name = 'attributes with no space between them'; Expect = 'between its attributes'; Change = {
            Add-ToHome '<div class="a"style="background: url(missing.png)">x</div>'
        } }
    @{ Name = 'a link to an anchor in another letter case'; Expect = 'has no target'; Change = { Add-ToHome '<p><a href="#Features">x</a></p>' } }
    @{ Name = 'a link from another page to an anchor in another letter case'; Expect = 'has no #Features in its target'; Change = {
            Add-ToPrivacy '<p><a href="/Resonant-Pulse/#Features">x</a></p>'
        } }
    @{ Name = 'a video poster that is not there'; Expect = 'points at nothing'; Change = { Add-ToHome '<video poster="screenshots/09-nothing.png"></video>' } }
    @{ Name = 'a preloaded picture set that is not there'; Expect = 'points at nothing'; Change = {
            Add-ToHome '<link rel="preload" as="image" imagesrcset="screenshots/09-nothing.png 1x">'
        } }
    @{ Name = 'an escape in a quoted address of a style sheet'; Expect = 'does not read'; Change = {
            Edit-Site 'assets/site.css' '.wrap {' '.x { background: url("assets/\favicon-32.png"); } .wrap {'
        } }
    @{ Name = 'an escape in a quoted @import address'; Expect = 'does not read'; Change = {
            Edit-Site 'assets/site.css' '.wrap {' '@import "assets/\favicon.css"; .wrap {'
        } }
    @{ Name = 'an escape in an unquoted address of a style sheet'; Expect = 'does not read'; Change = {
            Edit-Site 'assets/site.css' '.wrap {' '.x { background: url(assets/\favicon-32.png); } .wrap {'
        } }
    @{ Name = 'an attribute whose name ends with the name of one that is read'; Expect = 'accepted'; Change = {
            Add-ToHome '<button data-action="download">x</button>'
        } }
    @{ Name = 'a quoted value that holds text that looks like a reference'; Expect = 'accepted'; Change = {
            Add-ToHome "<div title='background=`"missing.png`"'>x</div>"
        } }
    @{ Name = 'a style block whose opening tag holds a ">" in a quoted value'; Expect = 'points at nothing'; Change = {
            Add-ToHome '<style title="1 > 0">.x { background: url(missing.png); }</style>'
        } }
    @{ Name = 'an id in the text of a quoted value is not an id'; Expect = 'has no target'; Change = {
            Add-ToHome "<p title='x id=`"fake`"'>y</p><p><a href=`"#fake`">z</a></p>"
        } }
    @{ Name = 'an id in the text of a quoted value of another page is not an id'; Expect = 'has no #fake in its target'; Change = {
            Add-ToPrivacy "<p title='x id=`"fake`"'>y</p>"
            Add-ToHome '<p><a href="privacy.html#fake">z</a></p>'
        } }
    @{ Name = 'a tag that is commented out'; Expect = 'accepted'; Change = {
            Add-ToHome "<!-- <a href='x'>old</a> <a href=`"nowhere.html`">older</a> -->"
        } }
    @{ Name = 'a refresh to a page that is not there'; Expect = 'points at nothing'; Change = {
            Add-ToHome '<meta http-equiv="refresh" content="0; url=missing.html">'
        } }
    @{ Name = 'a refresh to a page that is there'; Expect = 'accepted'; Change = {
            Add-ToHome '<meta http-equiv="refresh" content="0; url=privacy.html">'
        } }
    @{ Name = 'a refresh whose address holds a semicolon'; Expect = 'points at nothing'; Change = {
            Add-ToHome '<meta http-equiv="refresh" content="0; url=privacy.html;missing">'
        } }
    @{ Name = 'a refresh with a quoted address'; Expect = 'accepted'; Change = {
            Add-ToHome '<meta http-equiv="refresh" content="0; url=''privacy.html''">'
        } }
    @{ Name = 'a refresh without url= and with a comma'; Expect = 'points at nothing'; Change = {
            Add-ToHome '<meta http-equiv="refresh" content="0, missing.html">'
        } }
    @{ Name = 'a refresh of the same page'; Expect = 'accepted'; Change = { Add-ToHome '<meta http-equiv="refresh" content="30">' } }
    @{ Name = 'a quoted comment marker in an attribute, then a link to nothing'; Expect = 'points at nothing'; Change = {
            Add-ToHome '<a title="<!--" href="missing.html" data-end="-->">x</a>'
        } }
    @{ Name = 'a canonical address with its href twice'; Expect = 'times'; Change = {
            Add-ToHome '<link rel="canonical" href="https://example.com/" href="https://tinodo.github.io/Resonant-Pulse/">'
        } }
    @{ Name = 'a link with its rel twice'; Expect = 'times'; Change = {
            Add-ToHome '<link rel="stylesheet" rel="canonical" href="https://example.com/">'
        } }
    @{ Name = 'a preview address with its property twice'; Expect = 'times'; Change = {
            Add-ToHome '<meta property="og:title" property="og:image" content="https://example.com/x.png">'
        } }
    @{ Name = 'a canonical address among other words of rel'; Expect = 'does not point at'; Change = {
            Add-ToHome '<link rel="alternate canonical" href="https://example.com/">'
        } }
    @{ Name = 'a style block that is commented out'; Expect = 'accepted'; Change = {
            Add-ToHome '<!-- <style>.a { background: url(missing.png); }</style> -->'
        } }
    @{ Name = 'a comment inside a style block, then a url() to nothing'; Expect = 'points at nothing'; Change = {
            Add-ToHome '<style><!-- .a { background: url(missing.png); } --></style>'
        } }
    @{ Name = 'comment markers in the attributes of two tags, a style block between them'; Expect = 'points at nothing'; Change = {
            Add-ToHome '<p title="<!--"></p><style>.x { background: url(missing.png); }</style><p title="-->"></p>'
        } }
    @{ Name = 'tag-like text in a script'; Expect = 'accepted'; Change = {
            Add-ToHome '<script>var t = ''<a href="missing.html">'';</script>'
        } }
    @{ Name = 'tag-like text in a textarea and a title'; Expect = 'accepted'; Change = {
            Add-ToHome '<textarea><a href="missing.html"></textarea><svg><title>an <a href="missing.html"> in a title</title></svg>'
        } }
    @{ Name = 'a script that is not there'; Expect = 'points at nothing'; Change = { Add-ToHome '<script src="missing.js"></script>' } }
    @{ Name = 'a custom element whose name starts with script, then a script'; Expect = 'points at nothing'; Change = {
            Add-ToHome '<script-example><a href="missing.html">x</a></script-example><script>void 0;</script>'
        } }
    @{ Name = 'a link to the top of the page'; Expect = 'accepted'; Change = { Add-ToHome '<p><a href="#top">a</a> <a href="#TOP">b</a> <a href="privacy.html#top">c</a> <a href="#">d</a></p>' } }
    @{ Name = 'an SVG link to a file that is not there'; Expect = 'points at nothing'; Change = {
            Add-ToHome '<svg><use xlink:href="missing.svg#a"/></svg>'
        } }
    @{ Name = 'an SVG link in single quotes'; Expect = 'not written name='; Change = {
            Add-ToHome "<svg><use xlink:href='missing.svg#a'/></svg>"
        } }
    @{ Name = 'a preview address in single quotes'; Expect = 'not written name='; Change = {
            Add-ToHome "<meta property='og:image' content='https://example.com/x.png'>"
        } }
    @{ Name = 'a percent-encoded link to a file that exists'; Expect = 'accepted'; Change = { Add-ToHome '<p><a href="screenshots/01%2Dplay.png">x</a></p>' } }
    @{ Name = 'a picture with two sources, both there'; Expect = 'accepted'; Change = {
            Add-ToHome '<p><img src="screenshots/01-play.png" srcset="screenshots/01-play.png 1x, screenshots/02-studio.png 2x" alt="x"></p>'
        } }
    @{ Name = 'a picture with two sources, the second missing'; Expect = 'points at nothing'; Change = {
            Add-ToHome '<p><img src="screenshots/01-play.png" srcset="screenshots/01-play.png 1x, screenshots/09-nothing.png 2x" alt="x"></p>'
        } }
    @{ Name = 'a canonical address on another site'; Expect = 'does not point at'; Change = {
            Edit-Site 'index.html' '<link rel="canonical" href="https://tinodo.github.io/Resonant-Pulse/">' '<link rel="canonical" href="https://example.com/">'
        } }
    @{ Name = 'a preview picture address on another site'; Expect = 'does not point at'; Change = {
            Edit-Site 'index.html' 'content="https://tinodo.github.io/Resonant-Pulse/assets/og-image.png"' 'content="https://example.com/og-image.png"'
        } }
    @{ Name = 'a style sheet that refers to a file that is not there'; Expect = 'points at nothing'; Change = {
            Edit-Site 'assets/site.css' '.wrap {' '.x { background: url(missing.png); } .wrap {'
        } }
    @{ Name = 'a style sheet that refers to a file outside the site'; Expect = 'points outside the site'; Change = {
            Edit-Site 'assets/site.css' '.wrap {' '.x { background: url("../../README.md"); } .wrap {'
        } }
    @{ Name = 'a sitemap entry for a page that does not exist'; Expect = 'points at nothing'; Change = {
            Edit-Site 'sitemap.xml' '</urlset>' '<url><loc>https://tinodo.github.io/Resonant-Pulse/gone.html</loc></url></urlset>'
        } }
    @{ Name = 'a sitemap entry on another site'; Expect = 'is not under'; Change = {
            Edit-Site 'sitemap.xml' '</urlset>' '<url><loc>https://example.com/page.html</loc></url></urlset>'
        } }
    @{ Name = 'robots.txt that announces a sitemap on another site'; Expect = 'is not under'; Change = {
            Edit-Site 'robots.txt' 'Sitemap: https://tinodo.github.io/Resonant-Pulse/sitemap.xml' 'Sitemap: https://example.com/sitemap.xml'
        } }
    @{ Name = 'robots.txt that announces a sitemap that is not there'; Expect = 'points at nothing'; Change = {
            Edit-Site 'robots.txt' 'Sitemap: https://tinodo.github.io/Resonant-Pulse/sitemap.xml' 'Sitemap: https://tinodo.github.io/Resonant-Pulse/gone.xml'
        } }
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
