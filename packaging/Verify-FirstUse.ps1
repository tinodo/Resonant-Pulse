[CmdletBinding()]
param(
    [Parameter(Mandatory)][int]$AppProcessId,
    [Parameter(Mandatory)][string]$ReportPath,
    # Playback is audible, screenshots copy the whole screen and the Ctrl+M check types into the focused window, so on a
    # developer machine all three are opt-in (-RunAudio, -Screenshots, -Keyboard). A GitHub Actions runner has no
    # listener, nothing private and no person typing, so it runs them. -NoAudio, -NoScreenshots and -NoKeyboard win.
    [switch]$RunAudio,
    [switch]$Screenshots,
    [switch]$Keyboard,
    [switch]$NoAudio,
    [switch]$NoScreenshots,
    [switch]$NoKeyboard
)
$ErrorActionPreference = 'Stop'
$onCi = $env:GITHUB_ACTIONS -eq 'true'
$playAudio = (-not $NoAudio) -and ($RunAudio -or $onCi)
$takeScreenshots = (-not $NoScreenshots) -and ($Screenshots -or $onCi)
$sendKeys = (-not $NoKeyboard) -and ($Keyboard -or $onCi)
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class RpDpi
{
    [DllImport("user32.dll")] public static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr window);
}
public static class RpWindows
{
    public delegate bool EnumProc(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc callback, IntPtr parameter);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassNameW(IntPtr window, StringBuilder text, int capacity);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    // The visible top-level window of a process with the given class, or zero.
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
# Measure and resize in real pixels, and express the requested sizes in device-independent units, so the same
# scenarios mean the same thing on a 100% hosted runner and on a scaled developer display.
[void][RpDpi]::SetProcessDpiAwarenessContext([IntPtr](-4))
$process = [Diagnostics.Process]::GetProcessById($AppProcessId)
# The application's own window is found by its WinUI class. Process.MainWindowHandle can return an unrelated small
# window of the process instead (for example the input-method indicator Windows adds on a desktop without a shell).
function Get-AppWindowHandle {
    $found = [RpWindows]::FindMain([uint32]$AppProcessId, 'WinUIDesktopWin32WindowClass')
    if ($found -ne [IntPtr]::Zero) { return $found }
    $process.Refresh()
    return $process.MainWindowHandle
}
$mainDeadline = [DateTime]::UtcNow.AddSeconds(10)
while ([RpWindows]::FindMain([uint32]$AppProcessId, 'WinUIDesktopWin32WindowClass') -eq [IntPtr]::Zero -and
       [DateTime]::UtcNow -lt $mainDeadline) { Start-Sleep -Milliseconds 200 }
$scale = [RpDpi]::GetDpiForWindow((Get-AppWindowHandle)) / 96.0
if ($scale -le 0) { $scale = 1.0 }
$window = [Windows.Automation.AutomationElement]::FromHandle((Get-AppWindowHandle))
function Resize-To([double]$width, [double]$height) {
    $pattern = $window.GetCurrentPattern([Windows.Automation.TransformPattern]::Pattern)
    $pattern.Resize([Math]::Round($width * $scale), [Math]::Round($height * $scale))
}
function Get-CurrentWindow {
    $process.Refresh()
    if ($process.HasExited) { throw 'Application exited while verifying native controls.' }
    [Windows.Automation.AutomationElement]::FromHandle((Get-AppWindowHandle))
}
function Find-Optional([string]$id) {
    $condition = [Windows.Automation.PropertyCondition]::new(
        [Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
    (Get-CurrentWindow).FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
}
function Find-Control([string]$id) {
    $limit = [DateTime]::UtcNow.AddSeconds(5)
    do {
        $element = Find-Optional $id
        if ($null -ne $element) { return $element }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $limit)
    $present = (Get-CurrentWindow).FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition) |
        ForEach-Object { $_.Current.AutomationId } | Where-Object { $_ } | Select-Object -First 80
    throw "Required first-use control remained missing: $id (present: $($present -join ', '))"
}
function Wait-Absent([string]$id) {
    $limit = [DateTime]::UtcNow.AddSeconds(5)
    do {
        if ($null -eq (Find-Optional $id)) { return }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $limit)
    throw "Control should not exist: $id"
}
function Invoke-Control([string]$id) {
    $limit = [DateTime]::UtcNow.AddSeconds(10)
    do {
        $element = Find-Control $id
        if ($element.Current.IsEnabled) {
            Write-Output "Invoking native control: $id"
            $element.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
            return
        }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $limit)
    throw "Native control remained disabled: $id"
}
function Wait-Until([scriptblock]$condition, [string]$failure, [int]$seconds = 10) {
    $limit = [DateTime]::UtcNow.AddSeconds($seconds)
    do {
        if (& $condition) { return }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $limit)
    throw "$failure (readout: $(Get-Dial); status: $((Find-Control 'TransportStatus').Current.Name))"
}
function Get-Dial { (Find-Control 'PhaseInstrument').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value }
function Test-InstrumentHierarchy {
    $currentBounds = (Get-CurrentWindow).Current.BoundingRectangle
    $dial = (Find-Control 'PhaseInstrument').Current.BoundingRectangle
    $sound = (Find-Control 'MainSound').Current.BoundingRectangle
    $centerError = [Math]::Abs(($dial.X + $dial.Width / 2) - ($currentBounds.X + $currentBounds.Width / 2))
    if ($dial.Width -lt 220 -or $dial.Height -lt 220 -or
        $dial.Width -lt $sound.Width * 1.35 -or $centerError -gt 4 -or
        -not $currentBounds.Contains($dial)) {
        throw "The metronome is not the large, centered primary surface: dial=$dial, center error=$centerError"
    }
    $value = Get-Dial
    if ($value -notlike '120*BPM*') { throw "The main metronome does not present the tempo: $value" }
    return @{width=$dial.Width;height=$dial.Height;centerError=$centerError;value=$value}
}
function Save-PlayScreenshot([string]$name) {
    if (-not $takeScreenshots) { return }
    Add-Type -AssemblyName System.Drawing
    $rect = (Get-CurrentWindow).Current.BoundingRectangle
    $bitmap = [Drawing.Bitmap]::new([int]$rect.Width, [int]$rect.Height)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $origin = [Drawing.Point]::new([Math]::Max(0, [int]$rect.X), [Math]::Max(0, [int]$rect.Y))
        $graphics.CopyFromScreen($origin, [Drawing.Point]::Empty, $bitmap.Size)
        $image = Join-Path (Split-Path -Parent $ReportPath) $name
        $bitmap.Save($image, [Drawing.Imaging.ImageFormat]::Png)
    } finally { $graphics.Dispose(); $bitmap.Dispose() }
}
function Wait-Transport([string]$prefix, [bool]$afterCountIn = $false) {
    $limit = [DateTime]::UtcNow.AddSeconds(65)
    do {
        $text = (Find-Control 'TransportStatus').Current.Name
        if ($text -like "$prefix*" -and (-not $afterCountIn -or $text -notlike '*Count-in*')) {
            return $text
        }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $limit)
    throw "Expected $prefix transport state, observed: $text"
}
function Set-Number([string]$id, [int]$value) {
    $element = Find-Control $id
    [object]$pattern = $null
    if ($element.TryGetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern, [ref]$pattern)) {
        $pattern.SetValue([double]$value)
    } elseif ($element.TryGetCurrentPattern([Windows.Automation.ValuePattern]::Pattern, [ref]$pattern)) {
        $pattern.SetValue([string]$value)
    } else {
        throw "Input has no writable numeric automation pattern: $id"
    }
}
function Set-Text([string]$id, [string]$value) {
    (Find-Control $id).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($value)
}
function Set-PracticeLimits([int]$seconds, [int]$beats, [int]$bars) {
    Open-Panel @('PracticeTimer') 'PracticeSeconds'
    Set-Number 'PracticeSeconds' $seconds
    Set-Number 'PracticeBeats' $beats
    Set-Number 'PracticeBars' $bars
    Invoke-Control 'PracticeTimerOptionsDone'
    Wait-Absent 'PracticeTimerOptionsDone'
    # Reopen to read the acknowledgement the panel recorded after a successful publication and recovery write.
    $expected = ':{0}:{1}:{2}' -f $seconds, $beats, $bars
    $limit = [DateTime]::UtcNow.AddSeconds(15)
    $committed = ''
    do {
        Open-Panel @('PracticeTimer') 'PracticeLimitsStatus'
        $committed = (Find-Control 'PracticeLimitsStatus').Current.ItemStatus
        Invoke-Control 'PracticeTimerOptionsCancel'
        Wait-Absent 'PracticeTimerOptionsCancel'
        if ($committed.EndsWith($expected, [StringComparison]::Ordinal)) { return }
        Start-Sleep -Milliseconds 200
    } while ([DateTime]::UtcNow -lt $limit)
    throw "Practice-limit Done did not confirm successful publication and recovery: $expected (observed $committed)"
}
function Select-Combo([string]$id, [string]$itemName) {
    $combo = Find-Control $id
    $combo.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    $limit = [DateTime]::UtcNow.AddSeconds(5)
    do {
        $item = (Get-CurrentWindow).FindFirst([Windows.Automation.TreeScope]::Descendants,
            [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty, $itemName))
        if ($null -ne $item) { break }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $limit)
    if ($null -eq $item) { throw "Option is missing in ${id}: $itemName" }
    $item.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Start-Sleep -Milliseconds 200
}
function Open-Panel([string[]]$steps, [string]$field) {
    # A flyout that was just closed can ignore the next open while it finishes closing, and a hosted desktop can
    # briefly take focus. Re-invoke the opening chain a couple of times before reporting the panel as missing.
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        try {
            foreach ($step in $steps) { Invoke-Control $step }
            $limit = [DateTime]::UtcNow.AddSeconds(3)
            do {
                if ($null -ne (Find-Optional $field)) { return }
                Start-Sleep -Milliseconds 100
            } while ([DateTime]::UtcNow -lt $limit)
        } catch {
            if ($attempt -eq 3) { throw }
        }
        Start-Sleep -Milliseconds 400
    }
    $null = Find-Control $field
}
function Open-Meter { Open-Panel @('QuickMeter') 'MeterCustom' }
function Select-Meter([string]$preset, [string]$expectedName) {
    Open-Meter
    Invoke-Control "MeterPreset$preset"
    Wait-Until { (Find-Control 'QuickMeter').Current.Name -like "*$expectedName*" } "Meter did not change to $expectedName."
}
function Test-Visible($rectangle, $bounds, [string]$what) {
    if ($rectangle.IsEmpty -or -not $bounds.Contains($rectangle)) { throw "$what is not fully visible: $rectangle in $bounds" }
}
$deadline = [DateTime]::UtcNow.AddSeconds(20)
do {
    $status = Find-Control 'TransportStatus'
    if ($status.Current.Name -like 'Stopped*') { break }
    Start-Sleep -Milliseconds 200
} while ([DateTime]::UtcNow -lt $deadline)
if ($status.Current.Name -notlike 'Stopped*') { throw 'First-use initialization did not reach Stopped.' }
$bounds = $window.Current.BoundingRectangle
$primary = @('PhaseInstrument', 'TempoEdit', 'TempoDown', 'TapTempo', 'TempoUp', 'QuickMeter', 'MainSound', 'MainAccent0',
    'PracticeTimer', 'ProjectOptions', 'PlayHelp', 'AudioOptions', 'TransportStartPause', 'TransportStop', 'EmergencyMute')
$controls = @()
foreach ($id in $primary) {
    $element = Find-Control $id
    $rect = $element.Current.BoundingRectangle
    if ($element.Current.IsOffscreen) { throw "Primary control is offscreen in the first-use window: $id ($rect)" }
    Test-Visible $rect $bounds "Primary control $id"
    if ($id -like 'MainAccent*' -and $rect.Height -lt 50) { throw "Beat control is clipped: $id ($rect)" }
    $controls += @{id=$id;name=$element.Current.Name;visible=$true;width=$rect.Width;height=$rect.Height}
}
# One way to do each thing: the time signature has no second "Rhythm" button and tempo no second "Tempo" button.
foreach ($duplicate in 'RhythmOptions', 'TempoOptions') {
    if ($null -ne (Find-Optional $duplicate)) { throw "Duplicate entry point is present on the Play surface: $duplicate" }
}
$startRect = (Find-Control 'TransportStartPause').Current.BoundingRectangle
if ($startRect.Width -gt $bounds.Width * 0.35) { throw 'Start still stretches across the window.' }
$instrumentEvidence = Test-InstrumentHierarchy
$resizeEvidence = @()
foreach ($size in @(@(800, 600), @(1000, 720))) {
    Resize-To $size[0] $size[1]
    Start-Sleep -Milliseconds 500
    $currentBounds = (Get-CurrentWindow).Current.BoundingRectangle
    Save-PlayScreenshot "play-$($size[0])x$($size[1]).png"
    foreach ($id in $primary) {
        $element = Find-Control $id
        $rect = $element.Current.BoundingRectangle
        if ($element.Current.IsOffscreen -or -not $currentBounds.Contains($rect) -or
            ($id -like 'MainAccent*' -and $rect.Height -lt 50)) {
            throw "Primary control is hidden at $($size[0])x$($size[1]): $id ($rect)"
        }
    }
    $composition = Test-InstrumentHierarchy
    $resizeEvidence += @{width=$size[0];height=$size[1];settingsVisible=$true;primaryControlsVisible=$true;instrument=$composition}
}
# Live-looking tempo: the readout answers immediately, Cancel discards, Done applies.
Resize-To 800 600
Start-Sleep -Milliseconds 500
Invoke-Control 'TempoUp'
Invoke-Control 'TempoUp'
Wait-Until { (Get-Dial) -like '122*BPM*' } 'Tempo + did not change the BPM readout immediately.'
Invoke-Control 'TempoDown'
Invoke-Control 'TempoDown'
Wait-Until { (Get-Dial) -like '120*BPM*' } 'Tempo - did not change the BPM readout immediately.'
Open-Panel @('TempoEdit') 'Tempo'
Set-Number 'Tempo' 77
Invoke-Control 'TempoOptionsCancel'
Wait-Absent 'TempoOptionsCancel'
if ((Get-Dial) -notlike '120*BPM*') { throw 'Cancel applied a draft tempo.' }
Open-Panel @('TempoEdit') 'Tempo'
Set-Number 'Tempo' 90
Invoke-Control 'TempoOptionsDone'
Wait-Until { (Get-Dial) -like '90*BPM*' } 'Done did not apply the typed tempo.'
Wait-Absent 'TempoOptionsDone'
Open-Panel @('TempoEdit') 'Tempo'
Set-Number 'Tempo' 120
Invoke-Control 'TempoOptionsDone'
Wait-Until { (Get-Dial) -like '120*BPM*' } 'The original tempo did not restore.'
Wait-Absent 'TempoOptionsDone'
# Beat strength: a tap changes only that beat, and the name says what it is now.
$before = (Find-Control 'MainAccent1').Current.Name
Invoke-Control 'MainAccent1'
Wait-Until { (Find-Control 'MainAccent1').Current.Name -ne $before } 'Tapping a beat did not change its strength.'
foreach ($tap in 1..3) { Invoke-Control 'MainAccent1' }
Wait-Until { (Find-Control 'MainAccent1').Current.Name -eq $before } 'Four taps did not return the beat to its original strength.'
# Time signature: sheet-music quick picks. Every written beat is one click (8/8 = eight beats, not four).
$meterEvidence = @()
foreach ($case in @(@('6_8', '6/8', 6), @('3_4', '3/4', 3), @('12_8', '12/8', 12), @('4_4', '4/4', 4))) {
    Select-Meter $case[0] $case[1]
    $last = $case[2] - 1
    $null = Find-Control "MainAccent$last"
    Wait-Absent "MainAccent$($case[2])"
    $dialText = Get-Dial
    if ($case[1] -in @('6/8', '12/8')) {
        Wait-Until { (Get-Dial) -like '*BPM*Eighth*' } "A /8 meter must count eighth notes: $dialText"
        $dialText = Get-Dial
    }
    $meterEvidence += @{meter=$case[1];beats=$case[2];dial=$dialText}
}
Wait-Until { (Get-Dial) -like '120*BPM*Quarter*' } 'Quarter-note BPM did not return with 4/4.'
# The custom builder is the advanced path, behind the same control. 8/8 must give eight clicks, not four.
Open-Panel @('QuickMeter', 'MeterCustom') 'MeterBeats'
Set-Number 'MeterBeats' 8
Select-Combo 'MeterDenominator' 'Eighth note (8)'
Set-Text 'MeterGroups' ''
Invoke-Control 'RhythmOptionsDone'
Wait-Absent 'RhythmOptionsDone'
Wait-Until { (Find-Control 'QuickMeter').Current.Name -like '*8/8*' } 'The custom 8/8 meter was not applied.'
$null = Find-Control 'MainAccent7'
Wait-Absent 'MainAccent8'
Wait-Until { (Get-Dial) -like '*BPM*Eighth*' } 'Custom 8/8 must count eighth notes.'
$dialText = Get-Dial
$meterEvidence += @{meter='custom 8/8';beats=8;dial=$dialText}
Open-Panel @('QuickMeter', 'MeterCustom') 'MeterBeats'
Set-Number 'MeterBeats' 64
Set-Text 'MeterGroups' ''
Invoke-Control 'RhythmOptionsDone'
Wait-Absent 'RhythmOptionsDone'
$lastPulse = Find-Control 'MainAccent63'
Start-Sleep -Milliseconds 500
Save-PlayScreenshot 'play-many-pulses.png'
$manyPulseInstrument = $null
$hierarchy = Test-InstrumentHierarchy
$manyPulseInstrument = $hierarchy
if ((Find-Control 'PlayScrollHint').Current.IsOffscreen) { throw 'Long beat strip has no visible overflow cue.' }
(Find-Control 'PulseAccentStrip').GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern).SetScrollPercent(100, -1)
Start-Sleep -Milliseconds 300
$lastPulse = Find-Control 'MainAccent63'
if ($lastPulse.Current.IsOffscreen -or $lastPulse.Current.BoundingRectangle.Height -lt 50) {
    throw 'The last beat is not fully reachable in the bounded strip.'
}
Select-Meter '4_4' '4/4'
$window.GetCurrentPattern([Windows.Automation.TransformPattern]::Pattern).Resize($bounds.Width, $bounds.Height)
foreach ($size in @(@(400, 720), @(320, 900))) {
    Resize-To $size[0] $size[1]
    Start-Sleep -Milliseconds 500
    $currentBounds = (Get-CurrentWindow).Current.BoundingRectangle
    Save-PlayScreenshot "play-$($size[0])x$($size[1]).png"
    foreach ($id in 'TransportStartPause', 'TransportStop', 'EmergencyMute', 'AudioOptions', 'TempoDown', 'TapTempo', 'TempoUp', 'QuickMeter') {
        $element = Find-Control $id
        if ($element.Current.IsOffscreen -or -not $currentBounds.Contains($element.Current.BoundingRectangle)) {
            throw "Command is inaccessible at $($size[0])x$($size[1]): $id"
        }
    }
    $soundControl = Find-Control 'MainSound'
    if ($soundControl.Current.IsOffscreen -or -not $currentBounds.Contains($soundControl.Current.BoundingRectangle) -or
        $soundControl.Current.BoundingRectangle.Height -lt 44) {
        throw "Sound selector is not fully reachable at $($size[0])x$($size[1])."
    }
    $firstBeat = Find-Control 'MainAccent0'
    if ($firstBeat.Current.IsOffscreen -or -not $currentBounds.Contains($firstBeat.Current.BoundingRectangle)) {
        throw "Beat strip is not reachable at $($size[0])x$($size[1])."
    }
}
$window.GetCurrentPattern([Windows.Automation.TransformPattern]::Pattern).Resize($bounds.Width, $bounds.Height)
Start-Sleep -Milliseconds 500
# Every panel: opens with its fields visible, and an emergency mute (Ctrl+M) works while it owns the keyboard.
$panels = @(
    @{open=@('QuickMeter');                 field='MeterPreset4_4';    close='MeterPreset4_4'; focus='MeterPreset4_4'},
    @{open=@('QuickMeter', 'MeterCustom');  field='MeterBeats';        close='RhythmOptionsCancel'; focus='RhythmOptionsCancel'},
    @{open=@('TempoEdit');                  field='TempoUnit';         close='TempoOptionsCancel'; focus='TempoOptionsCancel'},
    @{open=@('PracticeTimer');              field='PracticeSeconds';   close='PracticeTimerOptionsCancel'; focus='PracticeTimerOptionsCancel'},
    @{open=@('ProjectOptions');             field='DocumentName';      close='ProjectOptionsPanelCancel'; focus='ProjectOptionsPanelCancel'},
    @{open=@('PlayHelp');                   field='PlayHelpPanelDone'; close='PlayHelpPanelDone'; focus='PlayHelpPanelDone'},
    @{open=@('AudioOptions');               field='AudioOutputPicker'; close='AudioOptionsClose'; focus='AudioOptionsClose'}
)
Add-Type -AssemblyName System.Windows.Forms
# Keystrokes go to whichever window has the keyboard. Never send them unless the application owns it right now,
# so a person working in another window can never receive them.
function Send-CtrlM {
    if ([Windows.Automation.AutomationElement]::FocusedElement.Current.ProcessId -ne $AppProcessId) {
        throw 'Keyboard focus is not in the application; refusing to send Ctrl+M to another window.'
    }
    [Windows.Forms.SendKeys]::SendWait('^m')
}
foreach ($panel in $panels) {
    Open-Panel $panel.open $panel.field
    $element = Find-Control $panel.field
    if ($element.Current.IsOffscreen) { throw "Opened settings control is not visible: $($panel.field)" }
    if ($panel.open[0] -eq 'TempoEdit' -and (Find-Control 'Tempo').Current.IsOffscreen) {
        throw 'Precise tempo input is not accessible in the tempo editor.'
    }
    if ($panel.open[0] -eq 'AudioOptions' -and (Find-Control 'MasterGain').Current.IsOffscreen) {
        throw 'Volume is not accessible in Audio.'
    }
    if ($panel.open[0] -ne 'AudioOptions' -and $panel.close -like '*Cancel') {
        try { $null = Find-Control ($panel.close -replace 'Cancel$', 'Done') }
        catch { throw "Panel has no Done beside Cancel: $($panel.close)" }
    }
    if (-not $sendKeys) {
        Invoke-Control $panel.close
        Wait-Absent $panel.field
        continue
    }
    $focused = Find-Control $panel.focus
    $focused.SetFocus()
    if ([Windows.Automation.AutomationElement]::FocusedElement.Current.ProcessId -ne $AppProcessId) {
        throw 'Popup shortcut verification does not own keyboard focus.'
    }
    $muteName = (Find-Control 'EmergencyMute').Current.Name
    Send-CtrlM
    Start-Sleep -Milliseconds 250
    if ((Find-Control 'EmergencyMute').Current.Name -eq $muteName) { throw "Ctrl+M did not reach the popup: $($panel.field)" }
    Send-CtrlM
    Start-Sleep -Milliseconds 250
    if ((Find-Control 'EmergencyMute').Current.Name -ne $muteName) { throw "Ctrl+M did not restore mute state: $($panel.field)" }
    Invoke-Control $panel.close
    Wait-Absent $panel.field
}
Invoke-Control 'AudioOptions'
$picker = Find-Control 'AudioOutputPicker'
$selection = $picker.GetCurrentPattern([Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()
Invoke-Control 'AudioOptionsClose'
$start = Find-Control 'TransportStartPause'
$playback = 'not-run-no-output'
$practiceStops = 'not-run-no-output'
$liveSound = 'not-run-no-output'
$liveTempoAndMeter = 'not-run-no-output'
if (-not $playAudio) {
    $playback = 'not-run-audio-disabled'
    $practiceStops = 'not-run-audio-disabled'
    $liveSound = 'not-run-audio-disabled'
    $liveTempoAndMeter = 'not-run-audio-disabled'
} elseif ($selection.Count -eq 0) {
    if ($start.Current.IsEnabled) { throw 'Start is enabled without a selected output.' }
} else {
    if (-not $start.Current.IsEnabled) { throw 'Start is disabled despite a selected output.' }
    try {
        Invoke-Control 'TransportStartPause'
        $deadline = [DateTime]::UtcNow.AddSeconds(20)
        do {
            Start-Sleep -Milliseconds 200
            $status = Find-Control 'TransportStatus'
            if ($status.Current.Name -like 'Playing*') { break }
        } while ([DateTime]::UtcNow -lt $deadline)
        if ($status.Current.Name -notlike 'Playing*') {
            throw "First Start did not enter Playing: $($status.Current.Name)"
        }
        $firstPlaying = $status.Current.Name
        # The count-in shows one fixed text for its whole bar (two seconds at 120 BPM), so a fixed sleep races its length.
        # Wait for the text to move on instead; a position that never advances still fails after the deadline.
        $advanceDeadline = [DateTime]::UtcNow.AddSeconds(12)
        do {
            Start-Sleep -Milliseconds 250
            $advanced = (Find-Control 'TransportStatus').Current.Name
        } while ($advanced -eq $firstPlaying -and [DateTime]::UtcNow -lt $advanceDeadline)
        if ($advanced -notlike 'Playing*') {
            throw 'Playback failed after the first Start.'
        }
        if ($advanced -eq $firstPlaying) { throw 'Playback state changed but the audio position did not advance.' }
        $playback = 'started-and-audio-position-advanced'
        $null = Wait-Transport 'Playing' $true
        $soundPicker = Find-Control 'MainSound'
        $soundPicker.GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
        $glass = (Get-CurrentWindow).FindFirst([Windows.Automation.TreeScope]::Descendants,
            [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,'Glass'))
        if ($null -eq $glass) { throw 'Glass sound option is missing.' }
        $glass.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
        $deadline = [DateTime]::UtcNow.AddSeconds(10)
        do {
            $activeSound = (Find-Control 'TransportStatus').Current.ItemStatus
            if ($activeSound -like 'sound=Glass;*') { break }
            Start-Sleep -Milliseconds 50
        } while ([DateTime]::UtcNow -lt $deadline)
        if ($activeSound -notlike 'sound=Glass;*') { throw 'Selected sound never reached the active audio revision.' }
        foreach ($accent in @(0,1,2,3)) {
            Invoke-Control 'MainAccent0'
            $deadline = [DateTime]::UtcNow.AddSeconds(10)
            do {
                $activeSound = (Find-Control 'TransportStatus').Current.ItemStatus
                if ($activeSound -like "sound=Glass;accents=$accent,*") { break }
                Start-Sleep -Milliseconds 50
            } while ([DateTime]::UtcNow -lt $deadline)
            if ($activeSound -notlike "sound=Glass;accents=$accent,*") { throw "Beat choice did not reach active audio: $accent" }
            if ($activeSound -notmatch ';manual=0(?:,|;|$)') { throw 'Manual accent precedence did not reach active audio.' }
            if ((Find-Control 'TransportStatus').Current.Name -notlike 'Playing*') { throw 'Live sound edit interrupted playback.' }
        }
        $liveSound = 'sound-and-all-four-beat-choices-published-without-stop'
        # Tempo and time signature are live too: the active revision follows without Stop.
        Invoke-Control 'TempoUp'
        Invoke-Control 'TempoUp'
        Invoke-Control 'TempoUp'
        Wait-Until { (Find-Control 'TransportStatus').Current.ItemStatus -like '*;tempo=123;*' } 'Live tempo never reached the active audio revision.' 15
        if ((Find-Control 'TransportStatus').Current.Name -notlike 'Playing*') { throw 'Live tempo edit interrupted playback.' }
        Select-Meter '3_4' '3/4'
        Wait-Until { (Find-Control 'TransportStatus').Current.ItemStatus -like '*;meter=3/4' } 'Live meter never reached the active audio revision.' 15
        if ((Find-Control 'TransportStatus').Current.Name -notlike 'Playing*') { throw 'Live meter edit interrupted playback.' }
        $liveTempoAndMeter = 'tempo-and-meter-published-without-stop'
        Select-Meter '4_4' '4/4'
        Invoke-Control 'TempoDown'
        Invoke-Control 'TempoDown'
        Invoke-Control 'TempoDown'
        # Stopping mid-commit would leave the later counted-stop checks running at 121 or 122 BPM.
        Wait-Until { (Find-Control 'TransportStatus').Current.ItemStatus -like '*;tempo=120;meter=4/4' } 'Live tempo and meter did not return to 120 BPM, 4/4 before stopping.' 20
    } finally {
        if ((Find-Control 'TransportStatus').Current.Name -notlike 'Stopped*') {
            Invoke-Control 'TransportStop'
        }
    }
    $null = Wait-Transport 'Stopped'
    try {
        Set-PracticeLimits 8 4 2
        Invoke-Control 'TransportStartPause'
        $null = Wait-Transport 'Playing'
        $completed = Wait-Transport 'Completed'
        if ($completed -notlike '*Bar 1*beat 4*00:04*' -or $completed -notlike '*Time remaining: 0:00:00*') {
            throw "Competing practice limits did not end at the four-beat boundary: $completed"
        }
        Start-Sleep -Milliseconds 500
        if ((Find-Control 'TransportStatus').Current.Name -ne $completed) {
            throw 'Completed beat stop continued advancing.'
        }
        Invoke-Control 'TransportStop'
        $null = Wait-Transport 'Stopped'
        Set-PracticeLimits 0 0 17
        Invoke-Control 'TransportStartPause'
        $null = Wait-Transport 'Playing'
        $completed = Wait-Transport 'Completed'
        if ($completed -notlike '*Bar 17*beat 4*00:36*' -or $completed -notlike '*Time remaining: 0:00:00*') {
            throw "Bar stop did not cross the initial/body loop boundary correctly: $completed"
        }
        Invoke-Control 'TransportStop'
        $null = Wait-Transport 'Stopped'
        Set-PracticeLimits 0 16 0
        Invoke-Control 'TransportStartPause'
        $null = Wait-Transport 'Playing' $true
        Start-Sleep -Milliseconds 500
        Invoke-Control 'TransportStartPause'
        $null = Wait-Transport 'Paused'
        Start-Sleep -Milliseconds 250
        $paused = (Find-Control 'TransportStatus').Current.Name
        Start-Sleep -Milliseconds 1500
        if ((Find-Control 'TransportStatus').Current.Name -ne $paused) {
            throw 'Counted practice time or position advanced while paused.'
        }
        Invoke-Control 'TransportStartPause'
        $completed = Wait-Transport 'Completed'
        if ($completed -notlike '*Bar 4*beat 4*00:10*' -or $completed -notlike '*Time remaining: 0:00:00*') {
            throw "Counted practice resume did not complete correctly: $completed"
        }
        $practiceStops = 'competing-limits-loop-seam-pause-resume-passed'
    } finally {
        if ((Find-Control 'TransportStatus').Current.Name -notlike 'Stopped*') {
            Invoke-Control 'TransportStop'
        }
        $null = Wait-Transport 'Stopped'
        Set-PracticeLimits 0 0 0
    }
}
@{controls=$controls;selectedOutputCount=$selection.Count;playback=$playback;practiceStops=$practiceStops;
  resize=$resizeEvidence;instrument=$instrumentEvidence;manyPulseInstrument=$manyPulseInstrument;
  meters=$meterEvidence;liveSound=$liveSound;liveTempoAndMeter=$liveTempoAndMeter;
  popupShortcuts=$(if ($sendKeys) { 'ctrl-m-in-all-seven-panels' } else { 'not-run-keyboard-disabled' });
  limitations='No acoustic accuracy, listening quality or complete accessibility acceptance is established.'} |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
