[CmdletBinding()]
param(
    # The build to check. Build it first: dotnet build src\ResonantPulse.App\ResonantPulse.App.csproj -c Debug -r win-x64 -p:Platform=x64 -p:DistributionMode=Portable
    [string]$Exe = (Join-Path $PSScriptRoot '..\src\ResonantPulse.App\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\ResonantPulse.exe'),
    [string]$OutputPath = (Join-Path $PSScriptRoot '..\artifacts\verify-local'),
    # Run on the normal desktop instead of a private one (a window appears and takes the foreground).
    [switch]$Visible,
    # Also run the real audio scenarios (Start, live sound/tempo/meter edits, counted stops, pause/resume) on the Windows
    # default output, at an inaudible master gain (0.0001, about -80 dBFS) seeded into the scratch data folder.
    [switch]$SilentAudio,
    [switch]$InsidePrivateDesktop
)
# Owner-authorised local run of the native first-use verifier against your OWN build. Nothing audible is played (see
# -SilentAudio), no screen is copied, no keystroke is sent, and the test instance keeps its data in a scratch folder
# (RESONANT_PULSE_HOME), so the real projects and settings are untouched. By default everything happens on a private
# desktop, so the person at the machine sees nothing and cannot disturb the run.
$ErrorActionPreference = 'Stop'
$Exe = [IO.Path]::GetFullPath($Exe)
$OutputPath = [IO.Path]::GetFullPath($OutputPath)
if (-not (Test-Path -LiteralPath $Exe)) { throw "Build the app first; not found: $Exe" }
$null = New-Item -ItemType Directory -Force -Path $OutputPath
if (-not $Visible -and -not $InsidePrivateDesktop) {
    $log = Join-Path $OutputPath 'verify-local.log'
    $forward = @('-Exe', $Exe, '-OutputPath', $OutputPath, '-InsidePrivateDesktop')
    if ($SilentAudio) { $forward += '-SilentAudio' }
    & (Join-Path $PSScriptRoot 'Invoke-OnPrivateDesktop.ps1') -Script $PSCommandPath -LogPath $log -ArgumentList $forward
    $code = $LASTEXITCODE
    Get-Content -LiteralPath $log | Select-Object -Last 25
    exit $code
}
Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class LocalVerifyWindow
{
    public delegate bool EnumProc(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback, IntPtr parameter);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassNameW(IntPtr window, StringBuilder text, int capacity);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    // Process.MainWindowHandle can return an unrelated small window of the process (for example the input-method
    // indicator Windows adds on a desktop without a shell), so the application's window is found by its WinUI class.
    public static IntPtr FindMain(uint processId, string className)
    {
        IntPtr found = IntPtr.Zero;
        EnumWindows(delegate (IntPtr window, IntPtr parameter)
        {
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            if (owner != processId || !IsWindowVisible(window)) return true;
            StringBuilder text = new StringBuilder(256);
            GetClassNameW(window, text, text.Capacity);
            if (text.ToString() != className) return true;
            found = window;
            return false;
        }, IntPtr.Zero);
        return found;
    }
}
'@
$data = Join-Path $OutputPath 'data'
if (Test-Path -LiteralPath $data) { Remove-Item -LiteralPath $data -Recurse -Force }
$null = New-Item -ItemType Directory -Force -Path $data
# The verifier looks for English texts; the machine's display language must not change what it finds.
[IO.File]::WriteAllText((Join-Path $data 'preferences.json'), '{"Language":"en"}', [Text.UTF8Encoding]::new($false))
if ($SilentAudio) {
    # An inaudible master gain keeps the real audio engine running without a sound.
    [IO.File]::WriteAllText((Join-Path $data 'recovery.pulse'),
        '{"SchemaVersion":1,"Name":"Silent verification","Settings":{"MasterGain":0.0001},"Lanes":[],"Program":[],"Setlist":[],"Seed":1,"ProbabilityAlgorithm":"event-hash-v1"}',
        [Text.UTF8Encoding]::new($false))
}
$info = [Diagnostics.ProcessStartInfo]::new($Exe)
$info.WorkingDirectory = Split-Path -Parent $Exe
$info.UseShellExecute = $false
$info.EnvironmentVariables['RESONANT_PULSE_HOME'] = $data
$app = [Diagnostics.Process]::Start($info)
$exit = 1
try {
    $deadline = [DateTime]::UtcNow.AddSeconds(40)
    $mainWindow = [IntPtr]::Zero
    do {
        Start-Sleep -Milliseconds 400
        $mainWindow = [LocalVerifyWindow]::FindMain([uint32]$app.Id, 'WinUIDesktopWin32WindowClass')
    } while ($mainWindow -eq [IntPtr]::Zero -and [DateTime]::UtcNow -lt $deadline)
    if ($mainWindow -eq [IntPtr]::Zero) { throw 'The application window did not appear within 40 seconds.' }
    Start-Sleep -Seconds 3
    [void][LocalVerifyWindow]::SetWindowPos($mainWindow, [IntPtr](-1), 0, 0, 0, 0, 0x0043)
    [void][LocalVerifyWindow]::SetForegroundWindow($mainWindow)
    Start-Sleep -Milliseconds 500
    # The driver must be a separate process from the one that started the application.
    $shell = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $audio = if ($SilentAudio) { '-RunAudio' } else { '-NoAudio' }
    & $shell -NoProfile -NonInteractive -File (Join-Path $PSScriptRoot 'Verify-FirstUse.ps1') -AppProcessId $app.Id `
        -ReportPath (Join-Path $OutputPath 'first-use.json') $audio -NoScreenshots -NoKeyboard
    $exit = $LASTEXITCODE
} finally {
    if (-not $app.HasExited) {
        # Close the application's own window; Process.CloseMainWindow targets the same heuristic window as MainWindowHandle.
        $closing = [LocalVerifyWindow]::FindMain([uint32]$app.Id, 'WinUIDesktopWin32WindowClass')
        if ($closing -ne [IntPtr]::Zero) { [void][LocalVerifyWindow]::PostMessage($closing, 0x0010, [IntPtr]::Zero, [IntPtr]::Zero) }
        else { [void]$app.CloseMainWindow() }
        if (-not $app.WaitForExit(6000)) { Stop-Process -Id $app.Id }
    }
}
"verifier exit code: $exit"
exit $exit
