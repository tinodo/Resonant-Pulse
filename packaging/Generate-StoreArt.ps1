$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$outputDirectory = Join-Path $PSScriptRoot 'store-assets'
[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null

function New-Color([string]$hex) { [System.Drawing.ColorTranslator]::FromHtml($hex) }

# The Resonant Pulse mark is the application icon (Generate-Assets.ps1) drawn with the same proportions: a cyan ring that is open at the
# top left, a magenta arc inside it that is open at the right, a cyan hand and the yellow marker on the ring. It is defined in a 256-unit
# space (the icon's) and drawn centred on ($cx, $cy) at the given scale. The colours are the instrument's own (Themes/Resonant.xaml).
function Write-Mark($graphics, [single]$cx, [single]$cy, [single]$scale) {
    $cyan = [System.Drawing.Pen]::new((New-Color '#58E4F4'), 8.96)
    $magenta = [System.Drawing.Pen]::new((New-Color '#EE83D3'), 5.63)
    $yellow = [System.Drawing.SolidBrush]::new((New-Color '#F3E687'))
    $state = $graphics.Save()
    try {
        $cyan.StartCap = $cyan.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $magenta.StartCap = $magenta.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
        $graphics.TranslateTransform($cx, $cy)
        $graphics.ScaleTransform($scale, $scale)
        $graphics.TranslateTransform(-128, -124)
        $r = 81.92
        $inner = $r * 0.65
        $graphics.DrawArc($cyan, [single](128 - $r), [single](128 - $r), [single](2 * $r), [single](2 * $r), -85, 280)
        $graphics.DrawArc($magenta, [single](128 - $inner), [single](128 - $inner), [single](2 * $inner), [single](2 * $inner), 40, 245)
        $graphics.DrawLine($cyan, 128, 128, [single](128 + $inner), [single](128 - $inner))
        $graphics.FillEllipse($yellow, [single](128 - 11.52), [single](128 - $r - 11.52), 23.04, 23.04)
    }
    finally {
        $graphics.Restore($state)
        $yellow.Dispose(); $magenta.Dispose(); $cyan.Dispose()
    }
}

# A short run of clicks on a line, as the horizon under the dial draws them: lane clicks are magenta, the main click cyan and a strong
# click yellow and taller. It says "rhythm" without a word.
function Write-Horizon($graphics, [single]$x, [single]$y, [single]$width, [single]$unit) {
    $pattern = @(
        @(0.00, 'main', 1.0), @(0.10, 'lane', 0.8), @(0.20, 'lane', 0.8), @(0.30, 'main', 0.9), @(0.40, 'lane', 0.8),
        @(0.50, 'strong', 1.5), @(0.60, 'lane', 0.8), @(0.70, 'main', 0.9), @(0.80, 'lane', 0.8), @(0.90, 'lane', 0.8), @(1.00, 'main', 1.0))
    $line = [System.Drawing.Pen]::new((New-Color '#3A4A55'), [Math]::Max(2, $unit * 0.12))
    $graphics.DrawLine($line, $x, $y, $x + $width, $y)
    $line.Dispose()
    foreach ($click in $pattern) {
        $kind = $click[1]
        $color = switch ($kind) { 'lane' { '#EE83D3' } 'strong' { '#F3E687' } default { '#58E4F4' } }
        $barHeight = $unit * $click[2]
        $barWidth = $unit * 0.42
        $brush = [System.Drawing.SolidBrush]::new((New-Color $color))
        $left = $x + $width * $click[0] - $barWidth / 2
        $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
        $radius = $barWidth / 2
        $top = $y - $barHeight / 2
        $path.AddArc($left, $top, $barWidth, $barWidth, 180, 180)
        $path.AddArc($left, $top + $barHeight - $barWidth, $barWidth, $barWidth, 0, 180)
        $path.CloseFigure()
        $graphics.FillPath($brush, $path)
        $path.Dispose(); $brush.Dispose()
    }
}

function Write-Background($graphics, [int]$width, [int]$height) {
    $rectangle = [System.Drawing.Rectangle]::new(0, 0, $width, $height)
    $graphics.FillRectangle([System.Drawing.SolidBrush]::new((New-Color '#0C1116')), $rectangle)
    $path = [System.Drawing.Drawing2D.GraphicsPath]::new()
    $path.AddEllipse(-$width * 0.25, -$height * 0.25, $width * 1.5, $height * 1.5)
    $brush = [System.Drawing.Drawing2D.PathGradientBrush]::new($path)
    $brush.CenterColor = New-Color '#1A2833'
    $brush.SurroundColors = @((New-Color '#0C1116'))
    $graphics.FillRectangle($brush, $rectangle)
    $brush.Dispose(); $path.Dispose()
}

function Write-Text($graphics, [string]$text, [single]$x, [single]$y, [single]$size, [string]$colorHex, [string]$family, [bool]$center, [single]$width) {
    $font = [System.Drawing.Font]::new($family, $size, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
    $brush = [System.Drawing.SolidBrush]::new((New-Color $colorHex))
    $format = [System.Drawing.StringFormat]::new()
    if ($center) { $format.Alignment = [System.Drawing.StringAlignment]::Center }
    $layout = [System.Drawing.RectangleF]::new($x, $y, $width, $size * 2)
    $graphics.DrawString($text, $font, $brush, $layout, $format)
    $format.Dispose(); $brush.Dispose(); $font.Dispose()
}

function New-Art([string]$name, [int]$width, [int]$height, [scriptblock]$draw) {
    $bitmap = [System.Drawing.Bitmap]::new($width, $height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
        Write-Background $graphics $width $height
        & $draw $graphics
        $bitmap.Save((Join-Path $outputDirectory $name), [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $graphics.Dispose(); $bitmap.Dispose() }
}

$title = 'Segoe UI Semibold'
$body = 'Segoe UI'

New-Art 'StoreIcon-300x300.png' 300 300 { param($g) Write-Mark $g 150 150 1.2 }

New-Art 'Poster-720x1080.png' 720 1080 {
    param($g)
    Write-Mark $g 360 400 2.05
    Write-Text $g 'Resonant Pulse' 0 720 74 '#F0F5F3' $title $true 720
    Write-Text $g 'Metronome and rhythm workstation' 0 820 32 '#9AA6B1' $body $true 720
    Write-Horizon $g 150 940 420 26
}

New-Art 'BoxArt-1080x1080.png' 1080 1080 {
    param($g)
    Write-Mark $g 540 410 3.0
    Write-Text $g 'Resonant Pulse' 0 770 100 '#F0F5F3' $title $true 1080
    Write-Text $g 'Metronome and rhythm workstation' 0 895 42 '#9AA6B1' $body $true 1080
    Write-Horizon $g 270 1000 540 30
}

New-Art 'Hero-1920x1080.png' 1920 1080 {
    param($g)
    Write-Mark $g 520 540 3.7
    Write-Text $g 'Resonant Pulse' 1000 430 108 '#F0F5F3' $title $false 880
    Write-Text $g 'A metronome and rhythm workstation for Windows' 1004 575 36 '#9AA6B1' $body $false 900
    Write-Horizon $g 1020 720 700 38
}

Write-Output "Generated Store artwork in $outputDirectory"
