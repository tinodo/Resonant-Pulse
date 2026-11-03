$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

# The pictures of the website (site/assets) are made from the Store artwork (Generate-StoreArt.ps1), so that the icon, the preview image and
# the Store listing show the same mark. Run Generate-StoreArt.ps1 first when the mark changes, then this script, and commit the results.
$root = Split-Path $PSScriptRoot -Parent
$storeAssets = Join-Path $PSScriptRoot 'store-assets'
$outputDirectory = Join-Path $root 'site/assets'
[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null

function Save-Scaled([string]$source, [string]$name, [int]$width, [int]$height) {
    $image = [System.Drawing.Image]::FromFile((Join-Path $storeAssets $source))
    $bitmap = [System.Drawing.Bitmap]::new($width, $height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
        $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
        # The source is scaled to fill the target and centred, so that what does not fit is cut from the edges.
        $scale = [Math]::Max($width / $image.Width, $height / $image.Height)
        $drawWidth = $image.Width * $scale
        $drawHeight = $image.Height * $scale
        $graphics.DrawImage($image, [single](($width - $drawWidth) / 2), [single](($height - $drawHeight) / 2), [single]$drawWidth, [single]$drawHeight)
        $bitmap.Save((Join-Path $outputDirectory $name), [System.Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $graphics.Dispose(); $bitmap.Dispose(); $image.Dispose() }
}

Save-Scaled 'StoreIcon-300x300.png' 'icon.png' 128 128
Save-Scaled 'StoreIcon-300x300.png' 'favicon-32.png' 32 32
Save-Scaled 'StoreIcon-300x300.png' 'apple-touch-icon.png' 180 180
Save-Scaled 'Hero-1920x1080.png' 'og-image.png' 1200 630

Write-Output "Generated website pictures in $outputDirectory"
