[CmdletBinding()]
param([string]$OutputDirectory = (Join-Path $PSScriptRoot '..\src\ResonantPulse.App\Assets'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$null = New-Item -ItemType Directory -Path $OutputDirectory -Force
$sizes = @{
    'Square44x44Logo.png' = @(44,44)
    'Square150x150Logo.png' = @(150,150)
    'Wide310x150Logo.png' = @(310,150)
    'StoreLogo.png' = @(50,50)
    'Icon256.png' = @(256,256)
}
foreach ($entry in $sizes.GetEnumerator()) {
    $width,$height = $entry.Value
    $bitmap = [Drawing.Bitmap]::new($width,$height)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.SmoothingMode = [Drawing.Drawing2D.SmoothingMode]::AntiAlias
        $graphics.Clear([Drawing.ColorTranslator]::FromHtml('#0C1116'))
        $side = [Math]::Min($width,$height)
        $cx = $width/2
        $cy = $height/2
        $r = $side*0.32
        $cyan = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#58E4F4'),[Math]::Max(2,$side*0.035))
        $magenta = [Drawing.Pen]::new([Drawing.ColorTranslator]::FromHtml('#EE83D3'),[Math]::Max(1,$side*0.022))
        $brush = [Drawing.SolidBrush]::new([Drawing.ColorTranslator]::FromHtml('#F3E687'))
        try {
            $graphics.DrawArc($cyan,[single]($cx-$r),[single]($cy-$r),[single]($r*2),[single]($r*2),[single](-85),[single](280))
            $graphics.DrawArc($magenta,[single]($cx-$r*.65),[single]($cy-$r*.65),[single]($r*1.3),[single]($r*1.3),[single](40),[single](245))
            $graphics.DrawLine($cyan,[single]$cx,[single]$cy,[single]($cx+$r*.65),[single]($cy-$r*.65))
            $dot = [single]($side*.09)
            $graphics.FillEllipse($brush,[single]($cx-$dot/2),[single]($cy-$r-$dot/2),$dot,$dot)
            $bitmap.Save((Join-Path $OutputDirectory $entry.Key),[Drawing.Imaging.ImageFormat]::Png)
        } finally { $cyan.Dispose(); $magenta.Dispose(); $brush.Dispose() }
    } finally { $graphics.Dispose(); $bitmap.Dispose() }
}
# ICO with an embedded PNG needs no unmanaged HICON or certificate installation.
$png = [IO.File]::ReadAllBytes((Join-Path $OutputDirectory 'Icon256.png'))
$stream = [IO.File]::Create((Join-Path $OutputDirectory 'App.ico'))
$writer = [IO.BinaryWriter]::new($stream)
try {
    $writer.Write([uint16]0); $writer.Write([uint16]1); $writer.Write([uint16]1)
    $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([uint16]1); $writer.Write([uint16]32)
    $writer.Write([uint32]$png.Length); $writer.Write([uint32]22); $writer.Write($png)
} finally { $writer.Dispose(); $stream.Dispose() }
