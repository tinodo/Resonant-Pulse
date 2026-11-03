[CmdletBinding()]
param(
    [string]$Output,
    # The site sources. Only a test of this script uses another folder.
    [string]$Source,
    # 'true' once the Microsoft Store listing is live (the repository variable STORE_FIRST_PUBLICATION_COMPLETED); anything else shows "coming soon".
    [string]$StorePublished = 'false',
    # The Store product id (the repository variable STORE_PRODUCT_ID). Needed, and checked, only when the listing is live.
    [string]$StoreProductId = '',
    [string]$BasePath = '/Resonant-Pulse/',
    [string]$PublicBase = 'https://tinodo.github.io/Resonant-Pulse/'
)

# Assembles the website from site/ and the real screenshots of the application, flips the Microsoft Store block to what is true, and checks the
# result before anything can deploy it: every link and picture resolves, every page has what a page needs, the privacy page and
# docs/PRIVACY.md state the same effective date, and nothing unfinished is left in. Output: artifacts/site.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $Output) { $Output = Join-Path $root 'artifacts/site' }
if (-not $Source) { $Source = Join-Path $root 'site' }
$source = $Source
$screenshots = Join-Path $root 'packaging/store-assets/screenshots/en-US'
$privacyMarkdown = Join-Path $root 'docs/PRIVACY.md'
$screenshotNames = '01-play.png', '02-studio.png', '03-train.png', '04-stage.png', '05-library.png'
$maxBytes = 8MB

$published = switch ($StorePublished.Trim().ToLowerInvariant()) {
    'true' { $true }
    { $_ -in '', 'false' } { $false }
    default { throw "STORE_FIRST_PUBLICATION_COMPLETED must be true or false, not '$StorePublished'." }
}
if ($published -and $StoreProductId -notmatch '^[0-9A-Za-z]{12}$') {
    throw "The Store listing is marked as published but the product id '$StoreProductId' is not a 12 character Store id."
}

if (Test-Path -LiteralPath $Output) { Remove-Item -LiteralPath $Output -Recurse -Force }
New-Item -ItemType Directory -Path $Output -Force | Out-Null
Copy-Item -Path (Join-Path $source '*') -Destination $Output -Recurse
$screenshotOutput = Join-Path $Output 'screenshots'
New-Item -ItemType Directory -Path $screenshotOutput -Force | Out-Null
foreach ($name in $screenshotNames) {
    $file = Join-Path $screenshots $name
    if (-not (Test-Path -LiteralPath $file)) { throw "The screenshot $file is missing." }
    Copy-Item -LiteralPath $file -Destination $screenshotOutput
}

$failures = [System.Collections.Generic.List[string]]::new()
function Fail([string]$message) { $failures.Add($message) }

function Get-PngSize([string]$path) {
    $bytes = [System.IO.File]::ReadAllBytes($path)
    $signature = [byte[]](0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)
    for ($i = 0; $i -lt 8; $i++) { if ($bytes.Length -lt 24 -or $bytes[$i] -ne $signature[$i]) { return $null } }
    $width = ([int]$bytes[16] -shl 24) + ([int]$bytes[17] -shl 16) + ([int]$bytes[18] -shl 8) + [int]$bytes[19]
    $height = ([int]$bytes[20] -shl 24) + ([int]$bytes[21] -shl 16) + ([int]$bytes[22] -shl 8) + [int]$bytes[23]
    return @($width, $height)
}

$encoding = [System.Text.UTF8Encoding]::new($false)
$pages = @(Get-ChildItem -LiteralPath $Output -Filter '*.html' -File -Recurse)

# The Microsoft Store block: the badge and the link when the listing is live, "coming soon" until then.
$keep = if ($published) { 'published' } else { 'pending' }
$drop = if ($published) { 'pending' } else { 'published' }
foreach ($page in $pages) {
    $text = [System.IO.File]::ReadAllText($page.FullName)
    $text = [regex]::Replace($text, "(?s)[ \t]*<!--store:$drop-->.*?<!--/store:$drop-->[ \t]*\r?\n?", '')
    $text = [regex]::Replace($text, "(?s)[ \t]*<!--/?store:$keep-->[ \t]*\r?\n?", '')
    if ($published) { $text = $text.Replace('{{STORE_PRODUCT_ID}}', $StoreProductId.ToLowerInvariant()) }
    [System.IO.File]::WriteAllText($page.FullName, $text, $encoding)
}
if (-not $published) {
    foreach ($badge in 'microsoft-store-badge-for-dark-bg.svg', 'microsoft-store-badge-for-light-bg.svg') {
        Remove-Item -LiteralPath (Join-Path $Output "assets/$badge") -Force -ErrorAction SilentlyContinue
    }
}

$allFiles = @(Get-ChildItem -LiteralPath $Output -File -Recurse)
$outputFull = (Resolve-Path -LiteralPath $Output).Path.TrimEnd('\', '/')
$pathComparison = if ([System.IO.Path]::DirectorySeparatorChar -eq '\') { [System.StringComparison]::OrdinalIgnoreCase } else { [System.StringComparison]::Ordinal }
function Get-Relative([string]$path) { [System.IO.Path]::GetRelativePath($Output, $path).Replace('\', '/') }

# One tag, as a browser finds it: a ">" inside a quoted value does not end it (<a title="1 > 0" href="x"> is one tag).
$tagBody = '(?:[^>"'']|"[^"]*"|''[^'']*'')*'
$tagPattern = "<[a-zA-Z]$tagBody>"
# One attribute of a tag: its name, and what follows the name up to the end of its value.
$attributePattern = '\s(?<name>[^\s"''<>/=]+)(?<rest>\s*=\s*(?:"[^"]*"|''[^'']*''|[^\s"''=<>`]+))?'
# The attributes that the checks read. They read them written name="value" and in no other way, so any other way is refused.
$readAttributes = @('href', 'src', 'srcset', 'imagesrcset', 'poster', 'data', 'action', 'formaction', 'background', 'content', 'http-equiv', 'alt', 'lang', 'id', 'rel', 'property', 'name', 'style')

# The tags of a page with their attributes, read one by one, as a browser does: the name in lower case, the text of the attribute, and its
# value when it is written name="value" (WellFormed), the only form that the checks read. A comment is recognised where a browser does, outside
# a tag (<!-- inside a quoted value is text), and a commented-out tag is not one. The text of a style, script, textarea or title element is not
# markup: it is Content of its tag (a style sheet is read from there), and tags that it seems to hold are not tags. Left is what stands in the
# tag besides its name and its attributes, which should be nothing: an attribute that follows a closing quote without a space
# (<a class="x"href="y">) is read by a browser and not by the checks.
function Get-Tags([string]$Text) {
    foreach ($match in [regex]::Matches($Text, "(?is)<!--.*?-->|(?<open><(?<raw>style|script|textarea|title)(?=[\s/>])$tagBody>)(?<content>.*?)</\k<raw>\s*>|(?<open>$tagPattern)")) {
        if (-not $match.Groups['open'].Success) { continue }
        $open = $match.Groups['open'].Value
        $body = $open -replace '^<[a-zA-Z][^\s/>]*', ''
        $attributes = @(foreach ($attribute in [regex]::Matches($body, $attributePattern)) {
            $quoted = [regex]::Match($attribute.Groups['rest'].Value, '^="([^"]*)"$')
            [pscustomobject] @{
                Name = $attribute.Groups['name'].Value.ToLowerInvariant()
                Text = $attribute.Value.Trim()
                WellFormed = $quoted.Success
                Value = if ($quoted.Success) { $quoted.Groups[1].Value } else { '' }
            }
        })
        [pscustomobject] @{
            Text = $open
            Name = [regex]::Match($open, '^<([a-zA-Z][^\s/>]*)').Groups[1].Value.ToLowerInvariant()
            Attributes = $attributes
            Left = ([regex]::Replace($body, $attributePattern, '') -replace '\s*/?>$', '').Trim()
            Content = $match.Groups['content'].Value
        }
    }
}

# The ids of a page: the id attributes of its tags, in the form that the checks read.
function Get-PageIds([string]$Text) {
    @(Get-Tags $Text | ForEach-Object { $_.Attributes } | Where-Object { $_.Name -eq 'id' -and $_.WellFormed } | ForEach-Object { $_.Value })
}

# Checks one reference ($Raw, as written in $Where: a page, a style sheet, the sitemap) and says what is wrong with it, if anything. It must
# resolve to a file inside the uploaded site, because only that exists on the web: a link that climbs out of it can find a file of the
# repository and break after deployment. $FromDirectory is where a relative reference starts; $PageIds are the ids of the page, for a
# reference to an anchor of the same page.
function Test-Reference([string]$Where, [string]$Label, [string]$Raw, [string]$FromDirectory, [string[]]$PageIds) {
    $value = $Raw.Trim()
    if ($value -match '^(mailto:|tel:|data:)') { return }
    if ($value.StartsWith('http://')) { Fail "${Where}: $Label='$Raw' is not https."; return }
    if ($value.StartsWith('https://') -and -not $value.StartsWith($PublicBase)) { return }
    $fromRoot = $value.StartsWith('/') -or $value.StartsWith($PublicBase)
    if ($value.StartsWith($PublicBase)) { $value = $value.Substring($PublicBase.Length) }
    elseif ($value.StartsWith($BasePath)) { $value = $value.Substring($BasePath.Length) }
    elseif ($value.StartsWith('/')) { Fail "${Where}: $Label='$Raw' is an absolute path outside $BasePath."; return }
    $fragment = $null
    if ($value.Contains('#')) { $fragment = $value.Substring($value.IndexOf('#') + 1); $value = $value.Substring(0, $value.IndexOf('#')) }
    if ($value.Contains('?')) { $value = $value.Substring(0, $value.IndexOf('?')) }
    if ($value -eq '' -and $fragment -and -not $fromRoot) {
        # (#top, in any letter case, is the top of the page whether or not an id says so.)
        if ($PageIds -cnotcontains $fragment -and $fragment -ne 'top') { Fail "${Where}: the link #$fragment has no target." }
        return
    }
    $value = [System.Uri]::UnescapeDataString($value)
    $base = if ($fromRoot) { $outputFull } else { $FromDirectory }
    $target = if ($value -eq '' -or $value -eq './') { Join-Path $base 'index.html' } else { Join-Path $base $value }
    $target = [System.IO.Path]::GetFullPath($target)
    if ($target -ne $outputFull -and -not $target.StartsWith($outputFull + [System.IO.Path]::DirectorySeparatorChar, $pathComparison)) {
        Fail "${Where}: $Label='$Raw' points outside the site."
        return
    }
    if (Test-Path -LiteralPath $target -PathType Container) { $target = Join-Path $target 'index.html' }
    if (-not (Test-Path -LiteralPath $target -PathType Leaf)) { Fail "${Where}: $Label='$Raw' points at nothing."; return }
    if ($fragment -and $target.EndsWith('.html')) {
        $targetIds = @(Get-PageIds ([System.IO.File]::ReadAllText($target)))
        if ($targetIds -cnotcontains $fragment -and $fragment -ne 'top') { Fail "${Where}: $Label='$Raw' has no #$fragment in its target." }
    }
}

# The addresses that a search engine or a social card takes from a page must be those of this site, not of some other one.
function Test-SharedAddress([string]$Where, $Tag) {
    $values = @{}
    # (Two attributes of one name are refused where the tags are read: a browser keeps the first and the checks read both.)
    foreach ($attribute in $Tag.Attributes) { if ($attribute.WellFormed) { $values[$attribute.Name] = $attribute.Value } }
    # The rel attribute is a list of words: <link rel="alternate canonical"> is a canonical address too.
    $canonical = @($values['rel'] -split '\s+') -contains 'canonical'
    if (-not $canonical -and $values['property'] -notin 'og:url', 'og:image' -and $values['name'] -ne 'twitter:image') { return }
    $address = if ($values.ContainsKey('href')) { $values['href'] } elseif ($values.ContainsKey('content')) { $values['content'] } else { $null }
    if ($null -eq $address -or -not $address.StartsWith($PublicBase)) { Fail "${Where}: $($Tag.Text) does not point at $PublicBase." }
}

# A string of style text that starts at $Index (a quote): its value, with $Index moved past the closing quote. A backslash escapes the next
# character, and a string that is not closed ends at the end of its line, as in a browser.
function Read-CssString([string]$Css, [ref]$Index) {
    $quote = $Css[$Index.Value]
    $Index.Value++
    $value = [System.Text.StringBuilder]::new()
    while ($Index.Value -lt $Css.Length) {
        $d = $Css[$Index.Value]
        if ($d -eq $quote) { $Index.Value++; break }
        if ($d -eq "`n") { break }
        if ($d -eq '\' -and $Index.Value + 1 -lt $Css.Length) {
            if ($Css[$Index.Value + 1] -ne "`n") { [void]$value.Append($Css[$Index.Value + 1]) }
            $Index.Value += 2
        }
        else { [void]$value.Append($d); $Index.Value++ }
    }
    $value.ToString()
}

# Every url() and @import in style text, found the way a browser reads the text. A comment holds none, and neither does a string ("/*" and
# '@import "x"' are text); an unquoted url() runs to its closing parenthesis, whatever it holds (url(https://example.com/*) has no comment
# in it). The keywords are not case sensitive, and a line ends with CR, LF, CRLF or FF, as in the specification.
function Get-CssReferences([string]$Css) {
    $Css = [regex]::Replace($Css, "`r`n?|`f", "`n")
    $found = [System.Collections.Generic.List[object]]::new()
    $n = $Css.Length
    $i = 0
    while ($i -lt $n) {
        $c = $Css[$i]
        if ($c -eq '/' -and $i + 1 -lt $n -and $Css[$i + 1] -eq '*') {
            $end = $Css.IndexOf('*/', $i + 2, [System.StringComparison]::Ordinal)
            $i = if ($end -lt 0) { $n } else { $end + 2 }
        }
        elseif ($c -eq '"' -or $c -eq "'") { [void](Read-CssString $Css ([ref]$i)) }
        elseif ($c -eq '@' -and [string]::Compare($Css, $i, '@import', 0, 7, [System.StringComparison]::OrdinalIgnoreCase) -eq 0) {
            $i += 7
            # White space and comments may stand between the keyword and its address.
            while ($i -lt $n) {
                if ([char]::IsWhiteSpace($Css[$i])) { $i++ }
                elseif ($Css[$i] -eq '/' -and $i + 1 -lt $n -and $Css[$i + 1] -eq '*') {
                    $end = $Css.IndexOf('*/', $i + 2, [System.StringComparison]::Ordinal)
                    $i = if ($end -lt 0) { $n } else { $end + 2 }
                }
                else { break }
            }
            # (@import url(x) is found as the url() that it holds.)
            if ($i -lt $n -and ($Css[$i] -eq '"' -or $Css[$i] -eq "'")) {
                $start = $i
                $value = Read-CssString $Css ([ref]$i)
                # An escape in an address is a character that this check does not decode (\fa is U+00FA, not "fa"): refused.
                if ($Css.Substring($start, $i - $start).Contains('\')) { $found.Add([pscustomobject] @{ Label = 'unreadable'; Value = 'an escape in an address' }) }
                else { $found.Add([pscustomobject] @{ Label = '@import'; Value = $value }) }
            }
        }
        elseif (($c -eq 'u' -or $c -eq 'U') -and [string]::Compare($Css, $i, 'url(', 0, 4, [System.StringComparison]::OrdinalIgnoreCase) -eq 0 -and ($i -eq 0 -or $Css[$i - 1] -notmatch '[\w-]')) {
            $i += 4
            while ($i -lt $n -and [char]::IsWhiteSpace($Css[$i])) { $i++ }
            $start = $i
            if ($i -lt $n -and ($Css[$i] -eq '"' -or $Css[$i] -eq "'")) { $value = Read-CssString $Css ([ref]$i) }
            else {
                # An unquoted address runs to the closing parenthesis, whatever it holds.
                while ($i -lt $n -and $Css[$i] -ne ')') { $i++ }
                $value = $Css.Substring($start, $i - $start)
            }
            if ($Css.Substring($start, $i - $start).Contains('\')) { $found.Add([pscustomobject] @{ Label = 'unreadable'; Value = 'an escape in an address' }) }
            else { $found.Add([pscustomobject] @{ Label = 'url()'; Value = $value }) }
            while ($i -lt $n -and $Css[$i] -ne ')') { $i++ }
            if ($i -lt $n) { $i++ }
        }
        # What this scanner does not read is refused, not passed over: an escape outside a string can spell any keyword (u\72l), and the
        # strings of image-set() and src() are addresses.
        elseif ($c -eq '\') { $found.Add([pscustomobject] @{ Label = 'unreadable'; Value = 'an escape (a backslash outside a string)' }); $i += 2 }
        elseif (($c -eq 'i' -or $c -eq 'I') -and [string]::Compare($Css, $i, 'image-set(', 0, 10, [System.StringComparison]::OrdinalIgnoreCase) -eq 0) {
            $found.Add([pscustomobject] @{ Label = 'unreadable'; Value = 'image-set()' })
            $i += 10
        }
        elseif (($c -eq 's' -or $c -eq 'S') -and [string]::Compare($Css, $i, 'src(', 0, 4, [System.StringComparison]::OrdinalIgnoreCase) -eq 0 -and ($i -eq 0 -or $Css[$i - 1] -notmatch '[\w-]')) {
            $found.Add([pscustomobject] @{ Label = 'unreadable'; Value = 'src()' })
            $i += 4
        }
        else { $i++ }
    }
    $found.ToArray()
}

# url(...) and @import in style text: a style sheet, a style block or a style attribute. Text on a page that is not style (a code sample that
# shows a url()) is not a reference.
function Test-StyleUrls([string]$Where, [string]$Css, [string]$FromDirectory, [string[]]$PageIds) {
    foreach ($reference in @(Get-CssReferences $Css)) {
        if ($reference.Label -eq 'unreadable') { Fail "${Where}: style with $($reference.Value), which this check does not read."; continue }
        $value = $reference.Value.Trim()
        if ($reference.Label -eq 'url()' -and ($value -eq '' -or $value.StartsWith('#'))) { continue }
        Test-Reference $Where $reference.Label $value $FromDirectory $PageIds
    }
}

foreach ($page in $pages) {
    $relative = Get-Relative $page.FullName
    $text = [System.IO.File]::ReadAllText($page.FullName)
    if ($text -match '\{\{|<!--/?store:') { Fail "${relative}: a placeholder or a Store marker is left in the page." }
    if ($text -match '(?i)\b(TODO|FIXME|lorem ipsum|XXX)\b') { Fail "${relative}: unfinished text (TODO, FIXME, lorem ipsum or XXX)." }
    if ($text -notmatch '<html lang="[a-z]{2}') { Fail "${relative}: the html element has no lang." }
    if ($text -notmatch '(?s)<title>[^<]+</title>') { Fail "${relative}: no title." }
    if ($text -notmatch '<meta name="description" content="[^"]{20,}"') { Fail "${relative}: no description." }
    $tags = @(Get-Tags $text)
    $ids = @($tags | ForEach-Object { $_.Attributes } | Where-Object { $_.Name -eq 'id' -and $_.WellFormed } | ForEach-Object { $_.Value })
    foreach ($tag in $tags) {
        # A browser keeps the first of two attributes of one name, and the checks below read them all: refused.
        foreach ($group in @($tag.Attributes | Group-Object -Property Name)) {
            if ($group.Count -gt 1 -and $readAttributes -contains ($group.Name -replace '^.*:', '')) {
                Fail "${relative}: $($tag.Text) has the attribute '$($group.Name)' $($group.Count) times, which a browser reads once and the checks do not."
            }
        }
        foreach ($attribute in $tag.Attributes) {
            # A namespace prefix does not make another attribute: xlink:href is an href.
            $name = $attribute.Name -replace '^.*:', ''
            # An attribute in single quotes, in none, or with spaces around the equals sign would be passed over without a word.
            if (-not $attribute.WellFormed) {
                if ($readAttributes -contains $name) {
                    Fail "${relative}: $($tag.Text) has the attribute '$($attribute.Text)', which is not written name=""value"" (no single quotes, no spaces around the equals sign), and the checks read that form only."
                }
                continue
            }
            $value = $attribute.Value
            if ($name -in 'href', 'src', 'poster', 'data', 'action', 'formaction', 'background') {
                Test-Reference $relative $attribute.Name $value $page.DirectoryName $ids
            }
            elseif ($name -in 'srcset', 'imagesrcset') {
                foreach ($candidate in @($value -split ',' | ForEach-Object { ($_.Trim() -split '\s+')[0] } | Where-Object { $_ })) {
                    Test-Reference $relative $attribute.Name $candidate $page.DirectoryName $ids
                }
            }
            elseif ($name -eq 'content') {
                if ($value.StartsWith($PublicBase)) { Test-Reference $relative 'content' $value $page.DirectoryName $ids }
                # <meta http-equiv="refresh" content="0; url=page.html">: after the delay, the rest is a link (url= is optional), to the end of the
                # text (a semicolon is part of it) or to the closing quote.
                elseif ($tag.Name -eq 'meta' -and @($tag.Attributes | Where-Object { $_.Name -eq 'http-equiv' -and $_.Value -eq 'refresh' }).Count -gt 0) {
                    $refresh = [regex]::Match($value, '(?i)^\s*[\d.]+\s*[;,]?\s*(?:url\s*=\s*)?(?:"(?<address>[^"]*)"|''(?<address>[^'']*)''|(?<address>.*))$')
                    if ($refresh.Success -and $refresh.Groups['address'].Value.Trim()) { Test-Reference $relative 'refresh' $refresh.Groups['address'].Value $page.DirectoryName $ids }
                }
            }
            # A style attribute is an attribute value, with its character references; a style block (below) is text that a browser does not decode.
            elseif ($name -eq 'style') { Test-StyleUrls $relative ([System.Net.WebUtility]::HtmlDecode($value)) $page.DirectoryName $ids }
        }
        if ($tag.Left) { Fail "${relative}: $($tag.Text) has '$($tag.Left)' between its attributes, which the checks do not read." }
        if ($tag.Name -eq 'img' -and -not ($tag.Attributes | Where-Object { $_.Name -eq 'alt' -and $_.WellFormed })) {
            Fail "${relative}: a picture has no alt text: $($tag.Text.Substring(0, [Math]::Min(80, $tag.Text.Length)))"
        }
        if ($tag.Name -in 'link', 'meta') { Test-SharedAddress $relative $tag }
        # A style block is text that a browser does not decode.
        if ($tag.Name -eq 'style') { Test-StyleUrls $relative $tag.Content $page.DirectoryName $ids }
    }
}

foreach ($style in @(Get-ChildItem -LiteralPath $Output -Filter '*.css' -File -Recurse)) {
    Test-StyleUrls (Get-Relative $style.FullName) ([System.IO.File]::ReadAllText($style.FullName)) $style.DirectoryName @()
}

# What the sitemap and robots.txt announce must exist on this site.
$announced = @()
$sitemap = Join-Path $Output 'sitemap.xml'
if (Test-Path -LiteralPath $sitemap) {
    $announced += @([regex]::Matches([System.IO.File]::ReadAllText($sitemap), '<loc>\s*(?<value>[^<]*?)\s*</loc>') | ForEach-Object { , @('sitemap.xml', $_.Groups['value'].Value) })
}
$robots = Join-Path $Output 'robots.txt'
if (Test-Path -LiteralPath $robots) {
    $announced += @([System.IO.File]::ReadAllLines($robots) | Where-Object { $_ -match '^\s*Sitemap:\s*\S+' } | ForEach-Object { , @('robots.txt', ($_ -replace '^\s*Sitemap:\s*', '').Trim()) })
}
foreach ($item in $announced) {
    if (-not $item[1].StartsWith($PublicBase)) { Fail "$($item[0]): '$($item[1])' is not under $PublicBase." }
    else { Test-Reference $item[0] 'address' $item[1] $outputFull @() }
}

foreach ($name in $screenshotNames) {
    $size = Get-PngSize (Join-Path $screenshotOutput $name)
    if (-not $size -or $size[0] -ne 1920 -or $size[1] -ne 1080) { Fail "screenshots/${name} is not a 1920x1080 PNG." }
}
$ogSize = Get-PngSize (Join-Path $Output 'assets/og-image.png')
if (-not $ogSize -or $ogSize[0] -ne 1200 -or $ogSize[1] -ne 630) { Fail 'assets/og-image.png is not a 1200x630 PNG.' }

# The privacy policy has one wording. docs/PRIVACY.md and the page say the same effective date and, up to the last section (which says where
# the policy is published, differently in each), the same words: formatting and links are ignored, a changed or missing word is not.
$dateExpression = 'Effective date:\s*(\d{1,2} [A-Za-z]+ \d{4})'
$markdownText = [System.IO.File]::ReadAllText($privacyMarkdown)
$pageText = [System.IO.File]::ReadAllText((Join-Path $Output 'privacy.html'))
$markdownDate = [regex]::Match($markdownText, $dateExpression)
$pageDate = [regex]::Match($pageText, $dateExpression)
if (-not $markdownDate.Success) { Fail 'docs/PRIVACY.md has no "Effective date: D Month YYYY".' }
if (-not $pageDate.Success) { Fail 'privacy.html has no "Effective date: D Month YYYY".' }
if ($markdownDate.Success -and $pageDate.Success -and $markdownDate.Groups[1].Value -ne $pageDate.Groups[1].Value) {
    Fail "The privacy policy is dated $($pageDate.Groups[1].Value) on the website and $($markdownDate.Groups[1].Value) in docs/PRIVACY.md."
}
function Get-Words([string]$text) { @([regex]::Matches($text.ToLowerInvariant(), '[\p{L}\p{N}]+') | ForEach-Object { $_.Value }) }
$markdownBody = [regex]::Match($markdownText, '(?s)^\s*#[^\r\n]*\r?\n(.*?)\r?\n## Changes and contact')
$pageBody = [regex]::Match($pageText, '(?s)<h1>[^<]*</h1>(.*?)<h2>Changes and contact</h2>')
if (-not $markdownBody.Success) { Fail 'docs/PRIVACY.md has no title followed by sections that end with "## Changes and contact".' }
if (-not $pageBody.Success) { Fail 'privacy.html has no h1 followed by sections that end with <h2>Changes and contact</h2>.' }
if ($markdownBody.Success -and $pageBody.Success) {
    $markdownWords = Get-Words ([regex]::Replace($markdownBody.Groups[1].Value, '\[([^\]]*)\]\([^)]*\)', '$1'))
    $pageWords = Get-Words ([System.Net.WebUtility]::HtmlDecode([regex]::Replace($pageBody.Groups[1].Value, '<[^>]+>', ' ')))
    $common = [Math]::Min($markdownWords.Count, $pageWords.Count)
    $at = -1
    for ($i = 0; $i -lt $common; $i++) { if ($markdownWords[$i] -ne $pageWords[$i]) { $at = $i; break } }
    if ($at -lt 0 -and $markdownWords.Count -ne $pageWords.Count) { $at = $common }
    if ($at -ge 0) {
        $from = [Math]::Max(0, $at - 4)
        $markdownContext = ($markdownWords[$from..([Math]::Min($markdownWords.Count - 1, $at + 4))]) -join ' '
        $pageContext = ($pageWords[$from..([Math]::Min($pageWords.Count - 1, $at + 4))]) -join ' '
        Fail "The privacy policy differs between docs/PRIVACY.md and privacy.html at word $at; docs/PRIVACY.md has '$markdownContext', privacy.html has '$pageContext'."
    }
}

$total = ($allFiles | Measure-Object Length -Sum).Sum
if ($total -gt $maxBytes) { Fail "The site is $total bytes, more than the $maxBytes byte budget." }

Write-Output "Website: $Output ($($allFiles.Count) files, $([Math]::Round($total / 1KB)) KB); Microsoft Store block: $keep"
foreach ($file in $allFiles | Sort-Object FullName) { Write-Output ('  {0,9:N0}  {1}' -f $file.Length, (Get-Relative $file.FullName)) }
if ($failures.Count -gt 0) {
    $failures | ForEach-Object { Write-Output "FAILED: $_" }
    throw "The website has $($failures.Count) problem(s)."
}
