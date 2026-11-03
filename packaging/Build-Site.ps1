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

foreach ($page in $pages) {
    $relative = Get-Relative $page.FullName
    $text = [System.IO.File]::ReadAllText($page.FullName)
    if ($text -match '\{\{|<!--/?store:') { Fail "${relative}: a placeholder or a Store marker is left in the page." }
    if ($text -match '(?i)\b(TODO|FIXME|lorem ipsum|XXX)\b') { Fail "${relative}: unfinished text (TODO, FIXME, lorem ipsum or XXX)." }
    if ($text -notmatch '<html lang="[a-z]{2}') { Fail "${relative}: the html element has no lang." }
    if ($text -notmatch '(?s)<title>[^<]+</title>') { Fail "${relative}: no title." }
    if ($text -notmatch '<meta name="description" content="[^"]{20,}"') { Fail "${relative}: no description." }
    foreach ($image in [regex]::Matches($text, '<img\b[^>]*>')) {
        if ($image.Value -notmatch '\salt="') { Fail "${relative}: a picture has no alt text: $($image.Value.Substring(0, [Math]::Min(80, $image.Value.Length)))" }
    }
    $ids = @([regex]::Matches($text, '\sid="([^"]+)"') | ForEach-Object { $_.Groups[1].Value })
    foreach ($reference in [regex]::Matches($text, '\b(?<attribute>href|src|srcset|content)="(?<value>[^"]*)"')) {
        $attribute = $reference.Groups['attribute'].Value
        $value = $reference.Groups['value'].Value
        if ($attribute -eq 'content' -and -not $value.StartsWith($PublicBase)) { continue }
        if ($value -match '^(mailto:|tel:|data:)') { continue }
        if ($value.StartsWith('http://')) { Fail "${relative}: $attribute='$value' is not https."; continue }
        if ($value.StartsWith('https://') -and -not $value.StartsWith($PublicBase)) { continue }
        if ($value.StartsWith($PublicBase)) { $value = $value.Substring($PublicBase.Length) }
        elseif ($value.StartsWith($BasePath)) { $value = $value.Substring($BasePath.Length) }
        elseif ($value.StartsWith('/')) { Fail "${relative}: $attribute='$value' is an absolute path outside $BasePath."; continue }
        $fragment = $null
        if ($value.Contains('#')) { $fragment = $value.Substring($value.IndexOf('#') + 1); $value = $value.Substring(0, $value.IndexOf('#')) }
        if ($value.Contains('?')) { $value = $value.Substring(0, $value.IndexOf('?')) }
        if ($value -eq '' -and $fragment) {
            if ($ids -notcontains $fragment) { Fail "${relative}: the link #$fragment has no target." }
            continue
        }
        $base = if ($reference.Groups['value'].Value.StartsWith('/') -or $reference.Groups['value'].Value.StartsWith($PublicBase)) { $outputFull } else { $page.DirectoryName }
        $target = if ($value -eq '' -or $value -eq './') { Join-Path $base 'index.html' } else { Join-Path $base $value }
        # Only what is in the uploaded site exists on the web: a link that climbs out of it can find a file of the repository, and break after deployment.
        $target = [System.IO.Path]::GetFullPath($target)
        if ($target -ne $outputFull -and -not $target.StartsWith($outputFull + [System.IO.Path]::DirectorySeparatorChar, $pathComparison)) {
            Fail "${relative}: $attribute='$($reference.Groups['value'].Value)' points outside the site."; continue
        }
        if (Test-Path -LiteralPath $target -PathType Container) { $target = Join-Path $target 'index.html' }
        if (-not (Test-Path -LiteralPath $target -PathType Leaf)) { Fail "${relative}: $attribute='$($reference.Groups['value'].Value)' points at nothing."; continue }
        if ($fragment -and $target.EndsWith('.html')) {
            $targetIds = @([regex]::Matches([System.IO.File]::ReadAllText($target), '\sid="([^"]+)"') | ForEach-Object { $_.Groups[1].Value })
            if ($targetIds -notcontains $fragment) { Fail "${relative}: $attribute='$($reference.Groups['value'].Value)' has no #$fragment in its target." }
        }
    }
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
