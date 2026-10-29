[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$Payload,
    [string]$Screenshot = 'artifacts\startup.png',
    [int]$ExistingProcessId,
    [string]$PackageFamilyName
)
$ErrorActionPreference = 'Stop'
# This script starts the payload against the machine's real data folder (after clearing it, so the verification is a first use),
# copies the screen and force-stops the process, so it is restricted to disposable GitHub-hosted runners (as Smoke-Package.ps1
# is). On a developer machine use Verify-Local.ps1: isolated data, a private desktop and a refusal of builds that ignore
# RESONANT_PULSE_HOME.
if ($env:GITHUB_ACTIONS -ne 'true' -or $env:RUNNER_ENVIRONMENT -ne 'github-hosted') {
    throw 'Smoke-Launch.ps1 is restricted to disposable GitHub-hosted runners; use packaging\Verify-Local.ps1 on a developer machine.'
}
$artifactDirectory = [IO.Path]::GetDirectoryName([IO.Path]::GetFullPath($Screenshot))
$executable = Join-Path ([IO.Path]::GetFullPath($Payload)) 'ResonantPulse.exe'
if ($ExistingProcessId) {
    $process = [Diagnostics.Process]::GetProcessById($ExistingProcessId)
    if (-not [string]::Equals($process.MainModule.FileName,$executable,[StringComparison]::OrdinalIgnoreCase)) {
        $process.Dispose()
        throw 'The supplied process does not belong to the expected application payload.'
    }
} else {
    # The verification is a first use: it starts from nothing a run before this one left in the machine's data folder (the
    # checks add lanes to the project, and the project is restored the next time the application starts). The runner is
    # disposable, and the check above is why this is safe to do.
    $dataFolder = Join-Path $env:LOCALAPPDATA 'ResonantPulse'
    if (Test-Path -LiteralPath $dataFolder) { Remove-Item -LiteralPath $dataFolder -Recurse -Force }
    # Earlier builds reopened the page that was open when they were last closed, so someone who last left Settings found the
    # app opening there. The saved page is ignored now: it is saved here as Settings and the verifier requires Play.
    $null = New-Item -ItemType Directory -Force -Path $dataFolder
    [IO.File]::WriteAllText((Join-Path $dataFolder 'preferences.json'), '{"LastView":"Settings"}', [Text.UTF8Encoding]::new($false))
    $process = Start-Process -FilePath $executable -PassThru -RedirectStandardOutput (Join-Path $artifactDirectory 'application-stdout.log') -RedirectStandardError (Join-Path $artifactDirectory 'application-stderr.log')
}
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
    Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class NativeCaptureBounds
{
    [StructLayout(LayoutKind.Sequential)]
    public struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr window, uint attribute, out Rect bounds, uint size);
}
'@
    $frame = [NativeCaptureBounds+Rect]::new()
    $result = [NativeCaptureBounds]::DwmGetWindowAttribute($process.MainWindowHandle,9,[ref]$frame,16)
    if ($result -ne 0) { throw "Could not read native window bounds: HRESULT $result." }
    $bounds = [Drawing.Rectangle]::FromLTRB($frame.Left,$frame.Top,$frame.Right,$frame.Bottom)
    $screen = [Windows.Forms.Screen]::FromHandle($process.MainWindowHandle).Bounds
    if ($bounds.Width -le 0 -or $bounds.Height -le 0 -or -not $screen.Contains($bounds)) {
        throw 'The native window is empty or extends beyond its display; required controls may be off-screen.'
    }
    $bitmap = [Drawing.Bitmap]::new($bounds.Width,$bounds.Height)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $graphics.CopyFromScreen($bounds.Location,[Drawing.Point]::Empty,$bounds.Size)
        $bitmap.Save([IO.Path]::GetFullPath($Screenshot),[Drawing.Imaging.ImageFormat]::Png)
    } finally { $graphics.Dispose(); $bitmap.Dispose() }
    & "$env:SystemRoot\System32\WindowsPowerShell\v1.0\powershell.exe" -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'Verify-FirstUse.ps1') -AppProcessId $process.Id -ReportPath (Join-Path $artifactDirectory 'first-use.json')
    if ($LASTEXITCODE -ne 0) { throw 'Native first-use interaction verification failed.' }
    Write-Output "Native window activated and remained responsive. Title: $($process.MainWindowTitle)"
    Write-Output 'First-use visibility and basic playback are checked when an output exists; acoustic timing and complete interaction/accessibility acceptance are not established.'
} finally {
    try {
        $logs = @(Join-Path $env:LOCALAPPDATA 'ResonantPulse\Logs\app.log')
        if ($PackageFamilyName) {
            $logs += Join-Path $env:LOCALAPPDATA "Packages\$PackageFamilyName\LocalCache\Local\ResonantPulse\Logs\app.log"
        }
        foreach ($log in $logs) {
            if (Test-Path -LiteralPath $log) {
                $name = if ($log -eq $logs[0]) { 'native-app.log' } else { 'packaged-app.log' }
                Copy-Item -LiteralPath $log -Destination (Join-Path $artifactDirectory $name)
            }
        }
        & wevtutil.exe qe Application '/q:*[System[(EventID=1000 or EventID=1026) and TimeCreated[timediff(@SystemTime) <= 120000]]]' /f:text /c:6 /rd:true | Set-Content (Join-Path $artifactDirectory 'native-events.log')
        if ($LASTEXITCODE -ne 0) { Write-Warning 'Windows application-event diagnostics could not be collected.' }
    } finally {
        $process.Refresh()
        if (-not $process.HasExited) { Stop-Process -Id $process.Id -Force }
        $process.Dispose()
    }
}
