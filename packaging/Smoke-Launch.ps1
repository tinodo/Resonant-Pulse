[CmdletBinding()]
param([Parameter(Mandatory)][string]$Payload,[string]$Screenshot = 'artifacts\startup.png')
$ErrorActionPreference = 'Stop'
$process = Start-Process -FilePath (Join-Path ([IO.Path]::GetFullPath($Payload)) 'ResonantPulse.exe') -PassThru
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(30)
    do {
        Start-Sleep -Milliseconds 500
        $process.Refresh()
        if ($process.HasExited) { throw "App exited during startup with code $($process.ExitCode)." }
    } while ($process.MainWindowHandle -eq [IntPtr]::Zero -and [DateTime]::UtcNow -lt $deadline)
    if ($process.MainWindowHandle -eq [IntPtr]::Zero) { throw 'No native window was created within 30 seconds.' }
    Start-Sleep -Seconds 3
    $process.Refresh()
    if ($process.HasExited -or -not $process.Responding) { throw 'Application crashed or stopped responding after activation.' }
    Add-Type -AssemblyName System.Drawing
    Add-Type -AssemblyName System.Windows.Forms
    $bounds = [Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bitmap = [Drawing.Bitmap]::new($bounds.Width,$bounds.Height)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($bounds.Location,[Drawing.Point]::Empty,$bounds.Size)
        $bitmap.Save([IO.Path]::GetFullPath($Screenshot),[Drawing.Imaging.ImageFormat]::Png)
    } finally { $graphics.Dispose(); $bitmap.Dispose() }
    Write-Output "Native window activated and remained responsive. Title: $($process.MainWindowTitle)"
    Write-Output 'This startup check does not establish audio timing, interaction or accessibility acceptance.'
} finally {
    $process.Refresh()
    if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
    $process.Dispose()
}
