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
    if (-not $takeScreenshots) { return $null }
    Add-Type -AssemblyName System.Drawing
    $rect = (Get-CurrentWindow).Current.BoundingRectangle
    $bitmap = [Drawing.Bitmap]::new([int]$rect.Width, [int]$rect.Height)
    $graphics = [Drawing.Graphics]::FromImage($bitmap)
    try {
        $origin = [Drawing.Point]::new([Math]::Max(0, [int]$rect.X), [Math]::Max(0, [int]$rect.Y))
        $graphics.CopyFromScreen($origin, [Drawing.Point]::Empty, $bitmap.Size)
        $image = Join-Path (Split-Path -Parent $ReportPath) $name
        $bitmap.Save($image, [Drawing.Imaging.ImageFormat]::Png)
        return $image
    } finally { $graphics.Dispose(); $bitmap.Dispose() }
}
# A flyout slides into place for about 400 ms, and the geometry of its controls changes with every frame until it stops
# (an ease-out whose last one-pixel step lands about 125 ms after the previous one). Two reads that straddle a frame
# differ by the distance travelled, so a panel is measured only after its control has been still for a while.
function Wait-Settled([string]$id, [int]$stillMilliseconds = 500, [int]$seconds = 8) {
    $element = Find-Control $id
    $limit = [DateTime]::UtcNow.AddSeconds($seconds)
    $last = $null
    $stillSince = [DateTime]::UtcNow
    do {
        $now = $element.Current.BoundingRectangle.ToString()
        if ($now -ne $last) { $last = $now; $stillSince = [DateTime]::UtcNow }
        elseif (([DateTime]::UtcNow - $stillSince).TotalMilliseconds -ge $stillMilliseconds) { return }
        Start-Sleep -Milliseconds 40
    } while ([DateTime]::UtcNow -lt $limit)
    throw "$id did not stop moving within $seconds seconds (last position: $last)"
}
# Layout rules a person notices at a glance. Each one failed in a shipped preview, so each is now a gate.
function Test-SameSize([string]$what, $first, $second, [double]$tolerance = 2) {
    if ([Math]::Abs($first.Width - $second.Width) -gt $tolerance -or [Math]::Abs($first.Height - $second.Height) -gt $tolerance) {
        throw "$what are not the same size: $first and $second"
    }
}
function Test-OneLine([string]$what, $first, $second, [double]$tolerance = 2) {
    if ([Math]::Abs(($first.Y + $first.Height / 2) - ($second.Y + $second.Height / 2)) -gt $tolerance) {
        throw "$what are not centred on one line: $first and $second"
    }
}
function Test-Pair([string]$firstId, [string]$secondId, [switch]$HeightOnly) {
    $first = (Find-Control $firstId).Current.BoundingRectangle
    $second = (Find-Control $secondId).Current.BoundingRectangle
    if ($HeightOnly) {
        if ([Math]::Abs($first.Height - $second.Height) -gt 2) {
            throw "$firstId and $secondId are not the same height: $first and $second"
        }
    } else { Test-SameSize "$firstId and $secondId" $first $second }
    Test-OneLine "$firstId and $secondId" $first $second
}
# The time-signature digits must sit at the vertical centre of their chip. The pixels are measured because the control
# tree cannot see where text is drawn; if the capture holds no digits at all the check is skipped, not failed.
function Test-NumeralCentering([string]$image) {
    if (-not $image -or -not (Test-Path -LiteralPath $image)) { return $null }
    $chip = (Find-Control 'QuickMeter').Current.BoundingRectangle
    $windowRect = (Get-CurrentWindow).Current.BoundingRectangle
    $bitmap = [Drawing.Bitmap]::new($image)
    try {
        # Only the middle of the chip is read: that keeps the rounded corners out, and a small vertical margin keeps the
        # top and bottom border lines out. What is left is the digits and the chevron.
        $margin = [int][Math]::Ceiling(4 * $scale)
        $left = [int]($chip.X - $windowRect.X + $chip.Width * 0.2)
        $top = [int]($chip.Y - $windowRect.Y) + $margin
        $width = [int]($chip.Width * 0.6)
        $height = [int]$chip.Height - 2 * $margin
        if ($left -lt 0 -or $top -lt 0 -or $left + $width -gt $bitmap.Width -or $top + $height -gt $bitmap.Height) { return $null }
        $background = $bitmap.GetPixel($left, $top)
        $first = [int]::MaxValue; $last = -1
        for ($y = $top; $y -lt $top + $height; $y++) {
            for ($x = $left; $x -lt $left + $width; $x++) {
                $pixel = $bitmap.GetPixel($x, $y)
                $difference = [Math]::Abs($pixel.R - $background.R) + [Math]::Abs($pixel.G - $background.G) + [Math]::Abs($pixel.B - $background.B)
                if ($difference -gt 120) { if ($y -lt $first) { $first = $y }; if ($y -gt $last) { $last = $y } }
            }
        }
        if ($last -lt 0) { return $null }
        $inkCentre = ($first + $last) / 2
        $chipCentre = ($chip.Y - $windowRect.Y) + $chip.Height / 2
        $offset = [Math]::Abs($inkCentre - $chipCentre)
        if ($offset -gt 2 * $scale) {
            throw "The time-signature digits are not vertically centred in their chip: ink centre $inkCentre, chip centre $chipCentre ($offset px off)."
        }
        return [Math]::Round($offset, 1)
    } finally { $bitmap.Dispose() }
}
# Practice, Project and Help: one row, one height, clear of the dial's ring, and level with the Settings item at the right.
function Test-PlayTools {
    $names = 'PracticeTimer', 'ProjectOptions', 'PlayHelp'
    $rects = $names | ForEach-Object { (Find-Control $_).Current.BoundingRectangle }
    foreach ($index in 1..2) {
        if ([Math]::Abs($rects[0].Height - $rects[$index].Height) -gt 2) {
            throw "$($names[0]) and $($names[$index]) are not the same height: $($rects[0]) and $($rects[$index])"
        }
        Test-OneLine "$($names[0]) and $($names[$index])" $rects[0] $rects[$index]
    }
    # The ring is a circle inside the dial's square: centred 200 of 440 down, its outermost tick 191 from the centre.
    $dial = (Find-Control 'PhaseInstrument').Current.BoundingRectangle
    $centreX = $dial.X + $dial.Width / 2
    $centreY = $dial.Y + $dial.Height * 200 / 440
    $radius = $dial.Height * 191 / 440
    for ($index = 0; $index -lt 3; $index++) {
        $rect = $rects[$index]
        $dx = [Math]::Max([Math]::Max($rect.Left - $centreX, $centreX - $rect.Right), 0)
        $dy = [Math]::Max([Math]::Max($rect.Top - $centreY, $centreY - $rect.Bottom), 0)
        if ([Math]::Sqrt($dx * $dx + $dy * $dy) -lt $radius) { throw "$($names[$index]) covers the metronome's ring: $rect over dial $dial" }
    }
    # A focused item draws its focus ring, and UI Automation then reports the ring's rectangle (4 px wider on each side and 2
    # taller than the item). The application Windows activates (the packaged one) starts with the focus on Settings and its
    # window active, so the ring is there; a launched build, or one that restored a project, has none. This check is about the
    # layout, so a focus that sits on Settings is moved to the mute button (always enabled) before it is measured.
    $settingsItem = Find-Control 'DestinationSettings'
    if ($settingsItem.Current.HasKeyboardFocus) {
        try { (Find-Control 'EmergencyMute').SetFocus() } catch { }
        Start-Sleep -Milliseconds 400
        $settingsItem = Find-Control 'DestinationSettings'
    }
    $settings = $settingsItem.Current.BoundingRectangle
    $labelled = $rects[2].Width -gt 56 * $scale
    if ($labelled -and [Math]::Abs($rects[2].Right - $settings.Right) -gt 3 * $scale) {
        $focusNow = [Windows.Automation.AutomationElement]::FocusedElement
        $focusId = if ($null -eq $focusNow) { 'none' } else { $focusNow.Current.AutomationId }
        throw "Help does not line up with the Settings item at the right edge: $($rects[2]) and $settings (window $((Get-CurrentWindow).Current.BoundingRectangle), focus on '$focusId')"
    }
    return @{ labelled = $labelled; buttonHeight = $rects[0].Height }
}
# Every beat of a quick-pick meter is fully inside the beat strip's own viewport, so the strip needs no scroll bar (a bar is
# drawn over the beat numbers) and shows no "more beats" cue. 12/8, with its three group gaps, is the widest quick pick;
# sizing the cells without the gaps once made it overflow by about 20 DIP, so its last beat was cut off and a scroll bar
# appeared for beats that all fit. A cut-off cell is reported with a clipped (narrower) rectangle.
function Test-StripFits([int]$beats) {
    # The cells are sized after the page's first layout pass, so give a freshly rebuilt strip a moment to settle.
    $limit = [DateTime]::UtcNow.AddSeconds(5)
    do {
        $strip = (Find-Control 'PulseAccentStrip').Current.BoundingRectangle
        $firstCell = (Find-Control 'MainAccent0').Current.BoundingRectangle
        $lastCell = (Find-Control "MainAccent$($beats - 1)").Current.BoundingRectangle
        $cue = Find-Optional 'PlayScrollHint'
        $fits = $lastCell.Right -le $strip.Right + 1 -and $lastCell.Width -ge $firstCell.Width - 3 * $scale
        $cueShown = $null -ne $cue -and -not $cue.Current.IsOffscreen
        if ($fits -and -not $cueShown) { return }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $limit)
    throw "All $beats beats of a quick-pick meter must fit the beat strip without a scroll bar or a 'more beats' cue: first beat $firstCell, last beat $lastCell, strip $strip, cue shown: $cueShown"
}
function Wait-Transport([string]$prefix, [bool]$afterCountIn = $false) {
    $limit = [DateTime]::UtcNow.AddSeconds(65)
    do {
        $text = (Find-Control 'TransportStatus').Current.Name
        if ($text -like "$prefix*" -and (-not $afterCountIn -or $text -notlike '*Count-in*')) {
            # A completed stop is reported a few milliseconds before the position readout lands on its final value (3.99 s
            # reads 00:03 with a second left), so that text is read again until it has been still for a moment.
            if ($prefix -eq 'Completed') { $text = Get-SettledStatus $text }
            return $text
        }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $limit)
    throw "Expected $prefix transport state, observed: $text"
}
function Get-SettledStatus([string]$text) {
    $stillSince = [DateTime]::UtcNow
    $limit = [DateTime]::UtcNow.AddSeconds(6)
    while ([DateTime]::UtcNow -lt $limit) {
        Start-Sleep -Milliseconds 60
        $now = (Find-Control 'TransportStatus').Current.Name
        if ($now -ne $text) { $text = $now; $stillSince = [DateTime]::UtcNow }
        # A finished run has no time left to play, so a readout that still shows some is not final, however long it has stood
        # still: a busy machine can hold the display back for a moment (seen once, as 00:35 with a second left). It is read
        # again until the limit; a readout that never reaches zero still fails the check that called this.
        elseif (([DateTime]::UtcNow - $stillSince).TotalMilliseconds -ge 300 -and $text -notmatch 'Time remaining: (?!0:00:00)') { break }
    }
    return $text
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
# The application opens on Play whichever page was open when it was last closed. The launcher saved Settings as that page; earlier
# builds brought the application back on it, which is not where someone who starts a metronome wants to be.
$openedOn = Find-Control 'DestinationPlay'
if (-not $openedOn.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Current.IsSelected -or $null -eq (Find-Optional 'PhaseInstrument')) {
    throw 'The application did not open on Play, although the page saved last was Settings.'
}
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
$toolsEvidence = Test-PlayTools
$resizeEvidence = @()
foreach ($size in @(@(800, 600), @(1000, 720))) {
    Resize-To $size[0] $size[1]
    Start-Sleep -Milliseconds 500
    $currentBounds = (Get-CurrentWindow).Current.BoundingRectangle
    $screenshot = Save-PlayScreenshot "play-$($size[0])x$($size[1]).png"
    foreach ($id in $primary) {
        $element = Find-Control $id
        $rect = $element.Current.BoundingRectangle
        if ($element.Current.IsOffscreen -or -not $currentBounds.Contains($rect) -or
            ($id -like 'MainAccent*' -and $rect.Height -lt 50)) {
            throw "Primary control is hidden at $($size[0])x$($size[1]): $id ($rect)"
        }
    }
    $composition = Test-InstrumentHierarchy
    $tools = Test-PlayTools
    $numeralOffset = Test-NumeralCentering $screenshot
    $resizeEvidence += @{width=$size[0];height=$size[1];settingsVisible=$true;primaryControlsVisible=$true;instrument=$composition;
        tools=$tools;numeralOffsetPixels=$numeralOffset}
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
    Test-StripFits $case[2]
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
$null = Save-PlayScreenshot 'play-many-pulses.png'
$manyPulseInstrument = $null
$hierarchy = Test-InstrumentHierarchy
$manyPulseInstrument = $hierarchy
if ((Find-Control 'PlayScrollHint').Current.IsOffscreen) { throw 'Long beat strip has no visible overflow cue.' }
# A strip that scrolls keeps room under its beat numbers for the scroll bar, which is drawn over the bottom of the strip.
$scrollingStrip = (Find-Control 'PulseAccentStrip').Current.BoundingRectangle
$firstBeat = (Find-Control 'MainAccent0').Current.BoundingRectangle
if ($scrollingStrip.Bottom - $firstBeat.Bottom -lt 12 * $scale) {
    throw "A scrolling beat strip leaves no room under its beat numbers for the scroll bar: first beat $firstBeat, strip $scrollingStrip"
}
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
    $null = Save-PlayScreenshot "play-$($size[0])x$($size[1]).png"
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
# A field is as wide as what it holds (docs/design/pages-design.md): the box for "100" or "1000" must never stretch to the
# window. Limits are DIPs at 100 % text size and grow with the Windows text-size setting, so a larger text size is never
# mistaken for a stretched field.
$textScale = 1.0
try {
    $configured = (Get-ItemProperty 'HKCU:\SOFTWARE\Microsoft\Accessibility' -ErrorAction Stop).TextScaleFactor
    if ($configured) { $textScale = [Math]::Max(1.0, $configured / 100.0) }
} catch { }
function Test-FieldWidths([string]$where, [hashtable]$limits) {
    foreach ($id in $limits.Keys) {
        $width = (Find-Control $id).Current.BoundingRectangle.Width / $scale
        $limit = $limits[$id] * $textScale
        if ($width -gt $limit) {
            throw "${where}: $id is $([Math]::Round($width)) DIP wide, but a field is as wide as its contents (at most $([Math]::Round($limit)) DIP)."
        }
    }
}
# No page or panel carries an Apply button: a change takes effect as it is made. (Play panels keep Cancel and Done.)
function Test-NoApplyButtons([string]$where, [string]$except = '') {
    $condition = [Windows.Automation.PropertyCondition]::new(
        [Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::Button)
    foreach ($button in (Get-CurrentWindow).FindAll([Windows.Automation.TreeScope]::Descendants, $condition)) {
        $id = $button.Current.AutomationId
        if ($except -and $id -match $except) { continue }
        if ($id -match 'Apply' -or $button.Current.Name -match '^Apply') {
            throw "${where}: an Apply button is present: '$id' '$($button.Current.Name)'"
        }
    }
}
function Wait-ToggleState($pattern, $state, [string]$failure) {
    $limit = [DateTime]::UtcNow.AddSeconds(5)
    do {
        if ($pattern.Current.ToggleState -eq $state) { return }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $limit)
    throw $failure
}
# Waits for something on a page other than Play, where the dial that Wait-Until reports in its message does not exist.
function Wait-Condition([scriptblock]$condition, [string]$failure, [int]$seconds = 8) {
    $limit = [DateTime]::UtcNow.AddSeconds($seconds)
    do {
        if (& $condition) { return }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $limit)
    throw $failure
}
# An error notice stays open until it is closed, and it takes room from the page above it. The one that a refusal asked for by a
# check leaves behind is read, compared with what was expected and closed, so that what is measured next is not squeezed by it; an
# error that nobody asked for fails the run with its words, where it used to turn up as a control that was "not on screen".
function Get-ErrorNoticeText {
    $notice = Find-Optional 'StatusNotice'
    if ($null -eq $notice) { return $null }
    function Get-NoticePart([string]$id) {
        $part = $notice.FindFirst([Windows.Automation.TreeScope]::Descendants,
            [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty, $id))
        if ($null -eq $part) { '' } else { $part.Current.Name }
    }
    if ((Get-NoticePart 'StandardIcon') -notlike 'Error*') { return $null }
    "$(Get-NoticePart 'Title'): $(Get-NoticePart 'Message')"
}
function Assert-NoErrorNotice([string]$where, [string]$detail = '') {
    $text = Get-ErrorNoticeText
    if ($null -ne $text) { throw "${where}: an error is on screen that nobody asked for: $text $detail" }
}
function Close-ErrorNotice([string]$expected, [string]$where) {
    $text = Get-ErrorNoticeText
    if ($null -eq $text) { return }
    if ($text -notlike "*$expected*") { throw "${where}: an error is on screen that nobody asked for: $text" }
    $close = (Find-Optional 'StatusNotice').FindFirst([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty, 'CloseButton'))
    if ($null -eq $close) { throw "${where}: the error notice has no close button." }
    $close.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    Wait-Absent 'StatusNotice'
}
function Select-Destination([string]$id) {
    $item = Find-Control $id
    $pattern = $null
    if ($item.TryGetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern, [ref]$pattern)) { $pattern.Select() }
    else { $item.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke() }
}
$panels = @(
    @{open=@('QuickMeter');                 field='MeterPreset4_4';    close='MeterPreset4_4'; focus='MeterPreset4_4'},
    @{open=@('QuickMeter', 'MeterCustom');  field='MeterBeats';        close='RhythmOptionsCancel'; focus='RhythmOptionsCancel'
      limits=@{MeterBeats=200; MeterDenominator=330; MeterGroups=300; Subdivision=200; Swing=200; CountInBars=200; AudibleBars=200; SilentBars=200}},
    @{open=@('TempoEdit');                  field='TempoUnit';         close='TempoOptionsCancel'; focus='TempoOptionsCancel'
      limits=@{Tempo=260; TempoUnit=330}},
    @{open=@('PracticeTimer');              field='PracticeSeconds';   close='PracticeTimerOptionsCancel'; focus='PracticeTimerOptionsCancel'
      limits=@{PracticeSeconds=200; PracticeBeats=200; PracticeBars=200}},
    @{open=@('ProjectOptions');             field='DocumentName';      close='ProjectOptionsPanelCancel'; focus='ProjectOptionsPanelCancel'
      limits=@{DocumentName=390}},
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
    Wait-Settled $panel.field
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
        # Cancel and Done are one pair: the same size, on one line.
        Test-Pair $panel.close ($panel.close -replace 'Cancel$', 'Done')
    }
    if ($panel.limits) { Test-FieldWidths "Panel $($panel.open[-1])" $panel.limits }
    if ($panel.open[0] -eq 'AudioOptions') {
        # The device list and its refresh button share one height and one centre line.
        Test-Pair 'AudioOutputPicker' 'RefreshAudioOutputs' -HeightOnly
    }
    $footer = if ($panel.close -like '*Cancel') { $panel.close -replace 'Cancel$', 'Done' } else { $panel.close }
    if ($footer -like '*Done' -or $footer -like '*Close') {
        $doneHeight = (Find-Control $footer).Current.BoundingRectangle.Height
        if ($doneHeight -lt 43 * $scale) { throw "The closing button is shorter than its neighbours ($doneHeight px): $footer" }
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
# At the smallest windows a panel still fits: its Cancel/Done pair is on screen, and the pair and the fields stay between the
# panel's own edges. Both failed in a shipped build: a pair wider than a narrow panel poked out and was clipped, and a panel
# that Windows placed above a button low in a short window was cut off above its footer.
foreach ($size in @(@(320, 900), @(400, 720))) {
    Resize-To $size[0] $size[1]
    Start-Sleep -Milliseconds 800
    foreach ($panel in $panels) {
        if ($panel.close -notlike '*Cancel') { continue }
        $where = "Panel $($panel.open[-1]) at $($size[0])x$($size[1])"
        $doneId = $panel.close -replace 'Cancel$', 'Done'
        Open-Panel $panel.open $panel.field
        Wait-Settled $panel.field
        $windowRect = (Get-CurrentWindow).Current.BoundingRectangle
        $edge = (Find-Control $panel.field).Current.BoundingRectangle.Left
        $cancel = (Find-Control $panel.close).Current.BoundingRectangle
        $done = (Find-Control $doneId).Current.BoundingRectangle
        Test-Visible $cancel $windowRect "$where $($panel.close)"
        Test-Visible $done $windowRect "$where $doneId"
        $tolerance = 2 * $scale
        if ($cancel.Left -lt $edge - $tolerance) { throw "${where}: Cancel pokes out of the panel ($($cancel.Left) is left of $edge)." }
        foreach ($id in $panel.limits.Keys) {
            $rect = (Find-Control $id).Current.BoundingRectangle
            # A field scrolled out of view reports no rectangle; its place along the row is checked wherever it has one.
            if ($rect.IsEmpty) { continue }
            if ($rect.Left -lt $edge - $tolerance -or $rect.Right -gt $done.Right + $tolerance) {
                throw "${where}: $id does not fit between the panel's edges ($rect, panel content $edge to $($done.Right))."
            }
        }
        Test-Pair $panel.close $doneId
        Invoke-Control $panel.close
        Wait-Absent $panel.field
    }
}
$window.GetCurrentPattern([Windows.Automation.TransformPattern]::Pattern).Resize($bounds.Width, $bounds.Height)
Start-Sleep -Milliseconds 800
# Every click of the bar has a strength bar. 6/8 felt in two (the "Compound 6/8" ready-made rhythm: counted in dotted quarters,
# each beat split in three) has two beats and six clicks, so six bars: the two beats and the four clicks that split them. The
# clicks that split a beat are named so, and a tap changes that click only. Choosing a time signature afterwards gives one
# click per written beat again; the split clicks were an eighth note apart, which is 6/8's beat, so the tempo rises by the old
# split and the same clicks keep coming at the same speed.
Select-Meter '6_8' '6/8'
Open-Panel @('TempoEdit') 'Tempo'
Select-Combo 'TempoUnit' 'Dotted quarter'
Invoke-Control 'TempoOptionsDone'
Wait-Absent 'TempoOptionsDone'
Wait-Until { (Get-Dial) -like '120*BPM*Dotted quarter*' } '6/8 did not count in dotted quarters.'
$null = Find-Control 'MainAccent1'
Wait-Absent 'MainAccent2'
Open-Panel @('QuickMeter', 'MeterCustom') 'Subdivision'
Set-Number 'Subdivision' 3
Invoke-Control 'RhythmOptionsDone'
Wait-Absent 'RhythmOptionsDone'
$null = Find-Control 'MainAccent5'
Wait-Absent 'MainAccent6'
$splitDial = Get-Dial
$expectedNames = @('Beat 1, Strong', 'Beat 1, click 2 of 3, Soft', 'Beat 1, click 3 of 3, Soft',
    'Beat 4, Strong', 'Beat 4, click 2 of 3, Soft', 'Beat 4, click 3 of 3, Soft')
for ($click = 0; $click -lt $expectedNames.Count; $click++) {
    Wait-Until { (Find-Control "MainAccent$click").Current.Name -eq $expectedNames[$click] } "Click $click of 6/8 felt in two is not named '$($expectedNames[$click])'."
}
Test-StripFits 6
Invoke-Control 'MainAccent1'
Wait-Until { (Find-Control 'MainAccent1').Current.Name -eq 'Beat 1, click 2 of 3, Medium' } 'Tapping a click that splits a beat did not change its strength.'
foreach ($click in 0, 2, 3, 4, 5) {
    if ((Find-Control "MainAccent$click").Current.Name -ne $expectedNames[$click]) {
        throw "A tap on one split click changed click ${click}: '$((Find-Control "MainAccent$click").Current.Name)'."
    }
}
foreach ($tap in 1..3) { Invoke-Control 'MainAccent1' }
Wait-Until { (Find-Control 'MainAccent1').Current.Name -eq $expectedNames[1] } 'Four taps did not return the split click to its original strength.'
Assert-NoErrorNotice 'Split-click strengths'
Select-Meter '6_8' '6/8'
Wait-Until { (Get-Dial) -like '360*BPM*Eighth*' } 'Choosing 6/8 after splitting its beats did not keep the click rate (the tempo rises by the split).'
$null = Find-Control 'MainAccent5'
Wait-Absent 'MainAccent6'
if ((Find-Control 'MainAccent3').Current.Name -ne 'Beat 4, Strong' -or (Find-Control 'MainAccent1').Current.Name -ne 'Beat 2, Medium') {
    throw "Choosing 6/8 after splitting its beats did not give one click per written beat: $((Find-Control 'MainAccent1').Current.Name), $((Find-Control 'MainAccent3').Current.Name)."
}
$meterEvidence += @{meter='6/8 felt in two, each beat split in three';beats=6;dial=$splitDial}
Select-Meter '4_4' '4/4'
Open-Panel @('TempoEdit') 'Tempo'
Set-Number 'Tempo' 120
Invoke-Control 'TempoOptionsDone'
Wait-Absent 'TempoOptionsDone'
Wait-Until { (Get-Dial) -like '120*BPM*Quarter*' } 'The original tempo and unit did not return after the split-click checks.'
Assert-NoErrorNotice 'Split-click strengths (after choosing a time signature)'
# Settings and Library: content-sized fields, a switch that takes effect as it is toggled, and no Apply button.
Select-Destination 'DestinationSettings'
Wait-Settled 'Language'
Test-FieldWidths 'Settings' @{ Language = 330; Theme = 330 }
Test-NoApplyButtons 'Settings'
Wait-Absent 'SaveExportBars'
$motion = (Find-Control 'ReducedMotion').GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern)
$motionBefore = $motion.Current.ToggleState
$motionAfter = if ($motionBefore -eq [Windows.Automation.ToggleState]::On) { [Windows.Automation.ToggleState]::Off } else { [Windows.Automation.ToggleState]::On }
$motion.Toggle()
Wait-ToggleState $motion $motionAfter 'Reduce motion did not change when it was switched.'
$motion.Toggle()
Wait-ToggleState $motion $motionBefore 'Reduce motion did not return to its previous state.'
Select-Destination 'DestinationLibrary'
Wait-Settled 'ExportBars'
Test-FieldWidths 'Library' @{ ExportBars = 200; LibrarySearch = 290 }
Test-NoApplyButtons 'Library'
# Two-pane pages put their cards side by side when the window is wide, and stack them when it is not.
$wideWindow = (Get-CurrentWindow).Current.BoundingRectangle.Width / $scale -ge 1000
function Test-SideBySide([string]$where, [string]$firstId, [string]$secondId) {
    if (-not $wideWindow) { return }
    $first = (Find-Control $firstId).Current.BoundingRectangle
    $second = (Find-Control $secondId).Current.BoundingRectangle
    if ($second.Left -lt $first.Right - 2 * $scale) { throw "${where}: $firstId and $secondId are not side by side on a wide window ($first and $second)." }
}
Test-SideBySide 'Library' 'LibraryProjectsCard' 'ExportCard'
# Nothing is chosen yet, so there is nothing to open. A saved revision appears in the list (the page rebuild that follows once
# crashed the app with a layout cycle when the list had entries), and choosing it makes it openable.
if ((Find-Control 'LoadLibraryProject').Current.IsEnabled) { throw 'Library: Open is enabled before a project is chosen.' }
Invoke-Control 'SaveToLibrary'
Wait-Condition { $null -ne (Find-Optional 'LibraryProject0') } 'Library: the saved project never appeared in the list.'
(Find-Control 'LibraryProject0').GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
Wait-Condition { (Find-Control 'LoadLibraryProject').Current.IsEnabled } 'Library: choosing a project did not enable Open.'
# Stage: the now-playing card and the setlist side by side; Lock editing switches setlist editing off and on at once, and an
# added song appears in the setlist.
Select-Destination 'DestinationStage'
Wait-Settled 'StageLock'
Test-NoApplyButtons 'Stage'
Test-SideBySide 'Stage' 'NowPlayingCard' 'SetlistCard'
if (-not (Find-Control 'AddCurrentSong').Current.IsEnabled) { throw 'Stage: adding a song is disabled while editing is not locked.' }
$stageLock = (Find-Control 'StageLock').GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern)
$stageLock.Toggle()
Wait-ToggleState $stageLock ([Windows.Automation.ToggleState]::On) 'Stage: Lock editing did not switch on.'
Wait-Condition { -not (Find-Control 'AddCurrentSong').Current.IsEnabled } 'Stage: locking did not disable adding a song.'
$stageLock.Toggle()
Wait-ToggleState $stageLock ([Windows.Automation.ToggleState]::Off) 'Stage: Lock editing did not switch off.'
Wait-Condition { (Find-Control 'AddCurrentSong').Current.IsEnabled } 'Stage: unlocking did not enable adding a song again.'
Invoke-Control 'AddCurrentSong'
Wait-Condition { $null -ne (Find-Optional 'StageSong0') } 'Stage: the added song never appeared in the setlist.'
# Notes belong to the song they were typed for. A draft that never lost focus is saved when the selection moves on (the list
# selection is the operation that saves it), the other song shows its own notes, and choosing the first again shows what was saved.
Invoke-Control 'AddCurrentSong'
Wait-Condition { $null -ne (Find-Optional 'StageSong1') } 'Stage: the second song never appeared in the setlist.'
function Get-SongNotes { (Find-Control 'SongNotes').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value }
function Select-Song([string]$id) { (Find-Control $id).GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select() }
$songNotes = 'Verify notes ' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
Select-Song 'StageSong0'
Wait-Condition { (Find-Control 'SongNotes').Current.IsEnabled } 'Stage: the notes box is disabled although a song is selected.'
(Find-Control 'SongNotes').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).SetValue($songNotes)
Select-Song 'StageSong1'
Wait-Condition { (Get-SongNotes) -eq '' } 'Stage: the second song shows the notes of the first.'
Select-Song 'StageSong0'
Wait-Condition { (Get-SongNotes) -eq $songNotes } 'Stage: the notes typed for the first song were not saved before the selection moved on.'
# The setlist is switched off while songs are being added, moved or removed, so a song cannot be chosen from rows that are about
# to be replaced: Remove asks for confirmation inside the operation, so while the question is open the list is off, and after
# Cancel it is on again with every song still in it. (It is not switched off for other operations: that would take the keyboard
# focus away from it each time a song is chosen.) The dialog's buttons are found inside the dialog: the notice bar above the
# transport has a close button with the same id.
function Find-DialogButton([string]$id) {
    $dialog = Find-Optional 'ConfirmationDialog'
    if ($null -eq $dialog) { return $null }
    $dialog.FindFirst([Windows.Automation.TreeScope]::Descendants,
        [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty, $id))
}
# Finds a button of the confirmation dialog and presses it in one step, looking again for a moment if it is not there. Waiting for
# the button and then looking for it a second time raced with the dialog's own opening: the second look came back empty on a
# loaded runner and the run died on a null instead of pressing the button.
function Invoke-DialogButton([string]$id, [string]$failure, [int]$seconds = 8) {
    $limit = [DateTime]::UtcNow.AddSeconds($seconds)
    do {
        $button = Find-DialogButton $id
        if ($null -ne $button) {
            $button.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
            return
        }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $limit)
    throw $failure
}
Invoke-Control 'RemoveSong'
Wait-Condition { $null -ne (Find-DialogButton 'CloseButton') } 'Stage: Remove did not ask for confirmation.'
Wait-Condition { -not (Find-Control 'StageSetlist').Current.IsEnabled } 'Stage: the setlist can still be used while a song is being removed.'
Invoke-DialogButton 'CloseButton' 'Stage: the confirmation went away before Cancel could be pressed.'
Wait-Condition { $null -eq (Find-Optional 'ConfirmationDialog') } 'Stage: Cancel did not close the confirmation.'
Wait-Condition { (Find-Control 'StageSetlist').Current.IsEnabled } 'Stage: the setlist stayed switched off after the removal was cancelled.'
Wait-Condition { $null -ne (Find-Optional 'StageSong1') } 'Stage: cancelling the removal removed a song.'
# Train: content-sized fields; with no program the page says how to get one; a timing check offers only the command that makes
# sense now (nothing to press or finish until one is running).
Select-Destination 'DestinationTrain'
Wait-Settled 'TimingTolerance'
Test-FieldWidths 'Train' @{ TimingTolerance = 200; TimingTarget = 330 }
Test-NoApplyButtons 'Train'
Test-SideBySide 'Train' 'ProgramCard' 'LiveCard'
$null = Find-Control 'EditProgram'
if (-not (Find-Control 'BeginKeyboardTiming').Current.IsEnabled) { throw 'Train: the timing check cannot be started.' }
foreach ($id in 'KeyboardAttack', 'FinishKeyboardTiming') {
    if ((Find-Control $id).Current.IsEnabled) { throw "Train: $id is enabled while no timing check is running." }
}
Select-Destination 'DestinationPlay'
Wait-Settled 'QuickMeter'
Invoke-Control 'AudioOptions'
$picker = Find-Control 'AudioOutputPicker'
$selection = $picker.GetCurrentPattern([Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()
Invoke-Control 'AudioOptionsClose'
$start = Find-Control 'TransportStartPause'
$playback = 'not-run-no-output'
$practiceStops = 'not-run-no-output'
$liveSound = 'not-run-no-output'
$liveTempoAndMeter = 'not-run-no-output'
$liveSplitClicks = 'not-run-no-output'
if (-not $playAudio) {
    $playback = 'not-run-audio-disabled'
    $practiceStops = 'not-run-audio-disabled'
    $liveSound = 'not-run-audio-disabled'
    $liveTempoAndMeter = 'not-run-audio-disabled'
    $liveSplitClicks = 'not-run-audio-disabled'
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
        # The clicks that split a beat are live too: split the beats while playing (spliced in at the next beat), then change the
        # strength of one split click (heard from the next audio quantum). The active revision follows without Stop, and
        # un-splitting the beats ends the strengths with them.
        Open-Panel @('QuickMeter', 'MeterCustom') 'Subdivision'
        Set-Number 'Subdivision' 2
        Invoke-Control 'RhythmOptionsDone'
        Wait-Absent 'RhythmOptionsDone'
        Wait-Until { (Find-Control 'TransportStatus').Current.ItemStatus -like '*;subdivision=2;*' } 'Live subdivision did not reach the active audio revision.' 15
        $null = Find-Control 'MainAccent7'
        Wait-Absent 'MainAccent8'
        Invoke-Control 'MainAccent1'
        Wait-Until { (Find-Control 'TransportStatus').Current.ItemStatus -like '*;splits=2,1,1,1;*' } 'The strength of a split click never reached the active audio revision.' 15
        if ((Find-Control 'TransportStatus').Current.Name -notlike 'Playing*') { throw 'Live split-click edit interrupted playback.' }
        Open-Panel @('QuickMeter', 'MeterCustom') 'Subdivision'
        Set-Number 'Subdivision' 1
        Invoke-Control 'RhythmOptionsDone'
        Wait-Absent 'RhythmOptionsDone'
        Wait-Until { (Find-Control 'TransportStatus').Current.ItemStatus -like '*;splits=;tempo=120;meter=4/4' } 'Un-splitting the beats did not reach the active audio revision.' 15
        Wait-Absent 'MainAccent4'
        if ((Find-Control 'TransportStatus').Current.Name -notlike 'Playing*') { throw 'Live un-split interrupted playback.' }
        $liveSplitClicks = 'split-beats-and-split-click-strength-published-without-stop'
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
# Studio: lanes are added from one menu; every field of a lane applies as it is changed (no Apply button on a lane) and the page is
# not rebuilt, so the focus, the caret and the scroll position stay; each change is one step of Undo. This runs after the playback
# checks, so the lanes it adds cannot disturb them.
Select-Destination 'DestinationStudio'
Wait-Settled 'AddLane'
# This check is a first use, so the project starts with no lanes. One that is already there means an earlier run left its project
# in the data folder (the hosted launchers clear it before each run), which would otherwise show only as a missing control.
if ($null -ne (Find-Optional 'Lane0Card')) {
    throw 'Studio: the project already has lanes, but this check starts from a first use. Run it with an empty data folder (RESONANT_PULSE_HOME, or an empty %LOCALAPPDATA%\ResonantPulse on a disposable runner).'
}
$null = Find-Control 'StudioEmpty'
Invoke-Control 'AddLane'
Invoke-Control 'AddLanePlain'
Wait-Condition { $null -ne (Find-Optional 'Lane0Card') } 'Studio: the new lane never appeared.'
Wait-Absent 'StudioEmpty'
Wait-Settled 'Lane0Volume'
# Adding a lane is an operation of the editor gate: the fields of the page it builds wait for it, then come on.
Wait-Condition { (Find-Control 'Lane0Volume').Current.IsEnabled } 'Studio: the fields of the new lane stayed switched off after it was added.'
Test-FieldWidths 'Studio' @{ Lane0Pulses = 200; Lane0Cycle = 240; Lane0Offset = 250; Lane0Sound = 330; DocumentSeed = 200 }
Test-NoApplyButtons 'Studio'
function Get-LaneValue([string]$id) { (Find-Control $id).GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).Current.Value }
function Get-RuntimeKey([string]$id) { (Find-Control $id).GetRuntimeId() -join '.' }
if ((Get-LaneValue 'Lane0Volume') -ne 70) { throw "Studio: a new lane's volume is $(Get-LaneValue 'Lane0Volume'), not 70." }
$null = Find-Control 'Lane0Step3'
Wait-Absent 'Lane0Step4'
$volumeKey = Get-RuntimeKey 'Lane0Volume'
(Find-Control 'Lane0Volume').GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).SetValue(35)
Wait-Condition { (Get-LaneValue 'Lane0Volume') -eq 35 } 'Studio: the volume slider did not take its value.'
Start-Sleep -Milliseconds 700
if ((Get-RuntimeKey 'Lane0Volume') -ne $volumeKey) { throw 'Studio: changing a field rebuilt the page, so focus, caret and scroll position would be lost.' }
(Find-Control 'Lane0Step1').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
Wait-Condition { (Find-Control 'Lane0Step1').Current.Name -like '*Strong*' } 'Studio: tapping a beat did not make it strong.'
$pulsesKey = Get-RuntimeKey 'Lane0Pulses'
Set-Number 'Lane0Pulses' 6
$null = Find-Control 'Lane0Step5'
Wait-Absent 'Lane0Step6'
if ((Get-RuntimeKey 'Lane0Pulses') -ne $pulsesKey) { throw 'Studio: changing the number of clicks rebuilt the page.' }
$laneMute = (Find-Control 'Lane0Muted').GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern)
$laneMute.Toggle()
Wait-ToggleState $laneMute ([Windows.Automation.ToggleState]::On) 'Studio: Mute did not switch on.'
# One step of Undo for each change, newest first: the page then shows the project as it was.
Wait-Condition { (Find-Control 'StudioUndo').Current.IsEnabled } 'Studio: Undo is not available after changes.'
Invoke-Control 'StudioUndo'
Wait-Condition { (Find-Control 'Lane0Muted').GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Current.ToggleState -eq [Windows.Automation.ToggleState]::Off } 'Studio: Undo did not bring Mute back.'
Invoke-Control 'StudioUndo'
Wait-Condition { $null -ne (Find-Optional 'Lane0Step3') -and $null -eq (Find-Optional 'Lane0Step4') } 'Studio: Undo did not bring back four clicks.'
Invoke-Control 'StudioUndo'
Wait-Condition { (Find-Control 'Lane0Step1').Current.Name -notlike '*Strong*' } 'Studio: Undo did not bring back the beat.'
Invoke-Control 'StudioUndo'
Wait-Condition { (Get-LaneValue 'Lane0Volume') -eq 70 } 'Studio: Undo did not bring back the volume.'
Invoke-Control 'StudioRedo'
Wait-Condition { (Get-LaneValue 'Lane0Volume') -eq 35 } 'Studio: Redo did not bring the volume change back.'
# The Euclidean generator is a panel opened from the same menu; Create adds the lane.
Invoke-Control 'AddLane'
Invoke-Control 'AddEuclidean'
$null = Find-Control 'EuclideanAttacks'
Invoke-Control 'EuclideanOptionsDone'
Wait-Absent 'EuclideanOptionsDone'
Wait-Condition { $null -ne (Find-Optional 'Lane1Card') } 'Studio: the Euclidean lane never appeared.'
Wait-Condition { (Find-Control 'Lane0Volume').Current.IsEnabled } 'Studio: the fields stayed switched off after the Euclidean lane was added.'
Test-NoApplyButtons 'Studio'
# An operation that changes what is on the page takes the fields of every lane with it while it runs, so an edit made meanwhile is
# not queued with the old page and then refused. Remove asks first, inside the operation: while the question is open the fields
# are off and the command that opened it is not (the keyboard focus returns to it); after Cancel everything is back.
$laneFields = @('Lane0Name', 'Lane0Muted', 'Lane0Solo', 'Lane0Pulses', 'Lane0Cycle', 'Lane0Offset', 'Lane0Sound', 'Lane0Volume', 'Lane0Pan', 'Lane0Probability', 'Lane0Step0', 'DocumentSeed')
Invoke-Control 'Lane1More'
Invoke-Control 'Lane1Remove'
Wait-Condition { $null -ne (Find-DialogButton 'CloseButton') } 'Studio: Remove did not ask for confirmation.'
foreach ($id in $laneFields) {
    Wait-Condition { -not (Find-Control $id).Current.IsEnabled } "Studio: $id can still be edited while a lane is being removed."
}
if (-not (Find-Control 'Lane0More').Current.IsEnabled) { throw 'Studio: the lane menu is switched off while a lane is being removed, so the keyboard focus has nowhere to return to.' }
Invoke-DialogButton 'CloseButton' 'Studio: the confirmation went away before Cancel could be pressed.'
Wait-Condition { $null -eq (Find-Optional 'ConfirmationDialog') } 'Studio: Cancel did not close the confirmation.'
foreach ($id in $laneFields) {
    Wait-Condition { (Find-Control $id).Current.IsEnabled } "Studio: $id stayed switched off after the removal was cancelled."
}
Wait-Condition { $null -ne (Find-Optional 'Lane1Card') } 'Studio: cancelling the removal removed the lane.'
function Get-Box([string]$id) { (Find-Control $id).GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value }
function Get-StepValue([string]$id) { (Find-Control $id).GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).Current.Value }
function Show-Control([string]$id) {
    # Brings a control that is below the first screen into view, the way a person scrolls to it.
    try { (Find-Control $id).GetCurrentPattern([Windows.Automation.ScrollItemPattern]::Pattern).ScrollIntoView() } catch { }
    Start-Sleep -Milliseconds 400
}
# The exact positions of the Euclidean lane are rows that open to fields applying as they are changed: no Apply button, the row is
# not rebuilt, a typed position is read exactly, one outside the cycle is refused and the box goes back.
Show-Control 'Lane1Positions'
Wait-Condition { $null -ne (Find-Optional 'Lane1Event7') } 'Studio: the Euclidean lane lists fewer than eight positions.'
Wait-Absent 'Lane1Event1Position'
(Find-Control 'Lane1Event1').GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
Wait-Condition { $null -ne (Find-Optional 'Lane1Event1Position') } 'Studio: opening a position did not show its fields.'
if ((Get-Box 'Lane1Event1Position') -ne '0.5') { throw "Studio: the second position shows '$(Get-Box 'Lane1Event1Position')', not 0.5." }
Test-FieldWidths 'Studio' @{ Lane1Event1Position = 190; Lane1Event1Accent = 330 }
Test-NoApplyButtons 'Studio'
$positionKey = Get-RuntimeKey 'Lane1Event1Position'
# A box commits when it loses focus, and setting its text through automation does not give it the focus: it is focused first, the
# way a person clicks into it, and left by moving the focus on.
(Find-Control 'Lane1Event1Position').SetFocus()
Set-Text 'Lane1Event1Position' '0.75'
(Find-Control 'Lane1AddEvent').SetFocus()
Wait-Condition { (Find-Control 'Lane1Event1').Current.Name -like '*0.75*' } 'Studio: a typed position did not reach the title of its row.'
if ((Get-RuntimeKey 'Lane1Event1Position') -ne $positionKey) { throw 'Studio: changing a position rebuilt its row.' }
(Find-Control 'Lane1Event1Position').SetFocus()
Set-Text 'Lane1Event1Position' '4'
(Find-Control 'Lane1AddEvent').SetFocus()
Wait-Condition { (Get-Box 'Lane1Event1Position') -eq '0.75' } 'Studio: a position outside the cycle was kept in the box.'
Start-Sleep -Milliseconds 700
if ((Get-Box 'Lane1Event1Position') -ne '0.75') { throw "Studio: a position outside the cycle came back into the box ('$(Get-Box 'Lane1Event1Position')')." }
# The refusal says why, and its notice stays until it is closed: it is the one error this run asks for.
Close-ErrorNotice 'A position must be at least 0' 'Studio'
Invoke-Control 'Lane1AddEvent'
Wait-Condition { $null -ne (Find-Optional 'Lane1Event8') } 'Studio: Add position did not add a ninth row.'
# A lane that plays a pattern is converted to positions with one command, and Undo brings the pattern back.
Show-Control 'Lane0Positions'
(Find-Control 'Lane0Positions').GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
Wait-Condition { $null -ne (Find-Optional 'Lane0ConvertEvents') } 'Studio: a pattern lane offers no way to convert it to positions.'
Invoke-Control 'Lane0ConvertEvents'
Wait-Condition { $null -ne (Find-Optional 'Lane0Event3') -and $null -eq (Find-Optional 'Lane0ConvertEvents') } 'Studio: converting the pattern did not list its positions.'
Invoke-Control 'StudioUndo'
# The lane has no positions again, so its list starts closed (as it does for a new project) and the command is inside it.
Wait-Condition { $null -eq (Find-Optional 'Lane0Event3') -and $null -ne (Find-Optional 'Lane0Positions') } 'Studio: Undo did not bring the pattern back.'
(Find-Control 'Lane0Positions').GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
Wait-Condition { $null -ne (Find-Optional 'Lane0ConvertEvents') } 'Studio: Undo did not offer to convert the pattern again.'
# The practice program: one card for each step. Every field applies as it is changed (the page is not rebuilt), the tempo behaviour
# decides which tempo boxes show, leaving bars silent shows a sentence with two boxes, and steps can be moved, copied and removed.
Show-Control 'AddProgramStep'
$null = Find-Control 'ProgramCard'
Wait-Absent 'Program0Card'
# Said when a click that was accepted adds nothing: what the page shows, and whether a second click does what the first should have.
function Get-AddStepDiagnostics {
    $present = @((Get-CurrentWindow).FindAll([Windows.Automation.TreeScope]::Descendants, [Windows.Automation.Condition]::TrueCondition) |
        ForEach-Object { $_.Current.Name } | Where-Object { $_ -like '*operation*' -or $_ -like '*document changed*' -or $_ -like '*could not*' } | Select-Object -Unique)
    function State([string]$id) { $e = Find-Optional $id; if ($null -eq $e) { 'missing' } else { "enabled=$($e.Current.IsEnabled)" } }
    $focus = [Windows.Automation.AutomationElement]::FocusedElement
    $second = 'not tried'
    try {
        if ((Find-Optional 'AddProgramStep').Current.IsEnabled) {
            (Find-Optional 'AddProgramStep').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
            Start-Sleep -Seconds 3
            $second = "step card after a second click: $($null -ne (Find-Optional 'Program0Card'))"
        }
    } catch { $second = "second click failed: $($_.Exception.Message)" }
    "(add step: $(State 'AddProgramStep'); undo: $(State 'StudioUndo'); program card: $($null -ne (Find-Optional 'ProgramCard')); focus: $($focus.Current.AutomationId); notices: $($present -join ' | '); $second)"
}
Invoke-Control 'AddProgramStep'
try { Wait-Condition { $null -ne (Find-Optional 'Program0Card') } 'Studio: Add step did not add a step card.' }
catch { throw "$($_.Exception.Message) $(Get-AddStepDiagnostics)" }
Wait-Settled 'Program0Bars'
Test-FieldWidths 'Studio' @{ Program0Name = 300; Program0Ramp = 330; Program0Tempo = 200; Program0Bars = 200; Program0Repeats = 200; Program0Beats = 200; Program0Denominator = 330 }
Test-NoApplyButtons 'Studio'
if ($null -eq (Find-Optional 'Program0Tempo') -or $null -ne (Find-Optional 'Program0StartTempo')) { throw 'Studio: a steady step must show one tempo, not from and to.' }
$barsKey = Get-RuntimeKey 'Program0Bars'
Set-Number 'Program0Bars' 12
Wait-Condition { (Get-StepValue 'Program0Bars') -eq 12 } 'Studio: the bars box did not take its value.'
Start-Sleep -Milliseconds 700
# A box shows what was typed until the project refuses it, and then goes back: read again after the project had its say.
if ((Get-StepValue 'Program0Bars') -ne 12) { throw "Studio: the bars box went back to $(Get-StepValue 'Program0Bars'), so the project did not take 12." }
if ((Get-RuntimeKey 'Program0Bars') -ne $barsKey) { throw 'Studio: changing a step field rebuilt the page, so the focus and the scroll position would be lost.' }
(Find-Control 'Program0Tempo').GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).SetValue([double]92.5)
Select-Combo 'Program0Ramp' 'Linear in seconds'
Wait-Condition { $null -ne (Find-Optional 'Program0StartTempo') -and $null -ne (Find-Optional 'Program0EndTempo') -and $null -eq (Find-Optional 'Program0Tempo') } 'Studio: a ramp must show a start and an end tempo instead of one.'
if ((Get-StepValue 'Program0StartTempo') -ne 92.5) { throw "Studio: the start tempo of the ramp shows $(Get-StepValue 'Program0StartTempo'), not the 92.5 that was set." }
Set-Number 'Program0EndTempo' 140
Select-Combo 'Program0Ramp' 'Constant'
Wait-Condition { $null -ne (Find-Optional 'Program0Tempo') -and $null -eq (Find-Optional 'Program0EndTempo') } 'Studio: a steady step must show one tempo again.'
if ((Get-StepValue 'Program0Tempo') -ne 92.5) { throw "Studio: back to steady, the tempo shows $(Get-StepValue 'Program0Tempo'), not 92.5." }
$stepRest = (Find-Control 'Program0Rest').GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern)
if ($null -ne (Find-Optional 'Program0Audible')) { throw 'Studio: the silent-bars boxes show while the switch is off.' }
$stepRest.Toggle()
Wait-Condition { $null -ne (Find-Optional 'Program0Audible') -and $null -ne (Find-Optional 'Program0Silent') } 'Studio: leaving bars silent did not show its two boxes.'
Set-Number 'Program0Audible' 3
Set-Number 'Program0Silent' 5
Wait-Condition { (Get-StepValue 'Program0Silent') -eq 5 } 'Studio: the silent-bars box did not take its value.'
Start-Sleep -Milliseconds 700
if ((Get-StepValue 'Program0Silent') -ne 5 -or (Get-StepValue 'Program0Audible') -ne 3) { throw "Studio: the silent-bars boxes went back to $(Get-StepValue 'Program0Audible') and $(Get-StepValue 'Program0Silent'), so the project did not take 3 and 5." }
$stepRest.Toggle()
Wait-Absent 'Program0Audible'
# Steps are added at the end, moved, copied, and removed after a question.
Invoke-Control 'AddProgramStep'
Wait-Condition { $null -ne (Find-Optional 'Program1Card') } 'Studio: the second step never appeared.'
Wait-Condition { -not (Find-Control 'Program0MoveUp').Current.IsEnabled -and (Find-Control 'Program1MoveUp').Current.IsEnabled } 'Studio: only a step that is not first can move up.'
$secondName = Get-Box 'Program1Name'
$focusBeforeMoveUp = [Windows.Automation.AutomationElement]::FocusedElement.Current.AutomationId
Invoke-Control 'Program1MoveUp'
Wait-Condition { (Get-Box 'Program0Name') -eq $secondName } 'Studio: Move up did not put the second step first.'
Invoke-Control 'Program0More'
Invoke-Control 'Program0Duplicate'
Wait-Condition { $null -ne (Find-Optional 'Program2Card') } 'Studio: Duplicate did not add a copy.'
Wait-Condition { (Get-Box 'Program1Name') -like '*(copy)' } 'Studio: the copy is not named after its original.'
Show-Control 'Program2More'
Invoke-Control 'Program2More'
Invoke-Control 'Program2Remove'
Wait-Condition { $null -ne (Find-DialogButton 'CloseButton') } 'Studio: Remove did not ask for confirmation.'
Invoke-DialogButton 'CloseButton' 'Studio: the confirmation went away before Cancel could be pressed.'
Wait-Condition { $null -eq (Find-Optional 'ConfirmationDialog') } 'Studio: Cancel did not close the confirmation.'
$null = Find-Control 'Program2Card'
Invoke-Control 'Program2More'
Invoke-Control 'Program2Remove'
Wait-Condition { $null -ne (Find-DialogButton 'PrimaryButton') } 'Studio: Remove did not ask again.'
Invoke-DialogButton 'PrimaryButton' 'Studio: the confirmation went away before Remove could be confirmed.'
Wait-Condition { $null -eq (Find-Optional 'Program2Card') } 'Studio: removing a step did not take its card away.'
Test-NoApplyButtons 'Studio'
# The remaining steps are taken away too: with a program the Play page leaves the tempo and the time signature to the program, and
# the checks that follow use those controls.
foreach ($stepIndex in 1, 0) {
    Invoke-Control "Program${stepIndex}More"
    Invoke-Control "Program${stepIndex}Remove"
    Wait-Condition { $null -ne (Find-DialogButton 'PrimaryButton') } "Studio: Remove did not ask about step $($stepIndex + 1)."
    Invoke-DialogButton 'PrimaryButton' "Studio: the confirmation about step $($stepIndex + 1) went away before Remove could be confirmed."
    Wait-Condition { $null -eq (Find-Optional "Program${stepIndex}Card") } "Studio: removing step $($stepIndex + 1) did not take its card away."
}
Wait-Condition { $null -ne (Find-Optional 'AddProgramStep') -and $null -ne (Find-Optional 'ProgramCard') } 'Studio: the program card did not come back to its empty state.'
Assert-NoErrorNotice 'Studio program' "(the focus was on '$focusBeforeMoveUp' when the second step was moved up)"
if ($sendKeys) {
    # A beat strip is one tab stop; the arrow keys move between its beats and Up and Down change a beat. Keys are sent only while
    # the application owns the keyboard.
    function Send-StripKeys([string]$keys) {
        if ([Windows.Automation.AutomationElement]::FocusedElement.Current.ProcessId -ne $AppProcessId) {
            throw "Keyboard focus is not in the application; refusing to send $keys to another window."
        }
        [Windows.Forms.SendKeys]::SendWait($keys)
        Start-Sleep -Milliseconds 300
    }
    function Get-FocusedId { [Windows.Automation.AutomationElement]::FocusedElement.Current.AutomationId }
    function Wait-Focus([string]$expected, [string]$what) {
        Wait-Condition { (Get-FocusedId) -eq $expected } "Studio: $what (focus is on '$(Get-FocusedId)', expected '$expected')."
    }
    (Find-Control 'Lane0Step0').SetFocus()
    Wait-Focus 'Lane0Step0' 'the first beat did not take the focus'
    Send-StripKeys '{RIGHT}'
    Wait-Focus 'Lane0Step1' 'Right did not move to the next beat'
    Send-StripKeys '{END}'
    Wait-Focus 'Lane0Step3' 'End did not move to the last beat'
    Send-StripKeys '{HOME}'
    Wait-Focus 'Lane0Step0' 'Home did not move to the first beat'
    Send-StripKeys '{LEFT}'
    Wait-Focus 'Lane0Step0' 'Left at the first beat moved the focus'
    Send-StripKeys '{RIGHT}{RIGHT}'
    Wait-Focus 'Lane0Step2' 'Right twice did not reach the third beat'
    Send-StripKeys '{UP}'
    Wait-Condition { (Find-Control 'Lane0Step2').Current.Name -like '*Strong*' } 'Studio: Up did not make the beat stronger.'
    Send-StripKeys '{TAB}'
    Wait-Condition { (Get-FocusedId) -notlike 'Lane0Step*' } 'Studio: Tab did not leave the strip (each beat is a tab stop).'
}
# Every language keeps its controls on screen. The panels are opened again in Dutch (its labels are the longest of the five), and
# every button a panel adds must be on screen. A row of buttons that fits in English can lose its last button in another
# language when layout rounding tips it over the edge: the child is then placed below the row, out of sight, and the checks
# above (English only) would never notice.
function Get-AllButtons {
    $condition = [Windows.Automation.PropertyCondition]::new(
        [Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::Button)
    @((Get-CurrentWindow).FindAll([Windows.Automation.TreeScope]::Descendants, $condition))
}
function Test-NewButtonsOnScreen([string]$where, [string[]]$before) {
    foreach ($button in (Get-AllButtons)) {
        $id = $button.Current.AutomationId
        if (-not $id -or $before -contains $id) { continue }
        if ($button.Current.IsOffscreen -or $button.Current.BoundingRectangle.IsEmpty) {
            throw "${where}: the button '$id' ('$($button.Current.Name)') is not on screen."
        }
    }
}
Assert-NoErrorNotice 'Before the Dutch checks'
Select-Destination 'DestinationSettings'
Wait-Settled 'Language'
Select-Combo 'Language' 'Nederlands'
Wait-Condition { (Find-Control 'DestinationPlay').Current.Name -ne 'Play' } 'The interface did not switch to Dutch.'
Select-Destination 'DestinationPlay'
Wait-Settled 'QuickMeter'
foreach ($panel in $panels) {
    if ($panel.close -notlike '*Cancel') { continue }
    $before = @(Get-AllButtons | ForEach-Object { $_.Current.AutomationId } | Where-Object { $_ })
    Open-Panel $panel.open $panel.field
    Wait-Settled $panel.field
    Test-NewButtonsOnScreen "Dutch panel $($panel.open[-1])" $before
    Invoke-Control $panel.close
    Wait-Absent $panel.field
}
# The same for the commands each page shows on its first screen (the rest is reached by scrolling).
function Test-OnScreen([string]$where, [string]$id) {
    $element = Find-Control $id
    if ($element.Current.IsOffscreen -or $element.Current.BoundingRectangle.IsEmpty) {
        # An error notice open under the page takes room from it, so it is named when it is the likely reason.
        $notice = Get-ErrorNoticeText
        throw "${where}: '$id' ('$($element.Current.Name)') is not on screen.$(if ($notice) { " An error notice is open: $notice" })"
    }
}
foreach ($page in @(
    @{destination='DestinationLibrary'; settle='ExportBars'
      ids=@('NewProject', 'LibraryImport', 'LibraryExportProject', 'LoadLibraryProject', 'SaveToLibrary', 'ExportWav', 'ExportMidi')},
    @{destination='DestinationStage'; settle='StageLock'; ids=@('StageLock', 'AlwaysOnTop', 'AddCurrentSong')},
    @{destination='DestinationStudio'; settle='AddLane'; ids=@('AddLane', 'StudioUndo', 'StudioRedo', 'Lane0Muted', 'Lane0Solo', 'Lane0More')},
    @{destination='DestinationTrain'; settle='TimingTolerance'; ids=@('EditProgram')})) {
    Select-Destination $page.destination
    Wait-Settled $page.settle
    foreach ($id in $page.ids) { Test-OnScreen "Dutch page $($page.destination)" $id }
}
# The program is below the lanes: it is scrolled to, and the commands of its steps must be on screen in Dutch too. The English
# checks took their steps away (with a program the Play page leaves the tempo and the time signature to the program), so a step
# is added for this check and taken away again.
Select-Destination 'DestinationStudio'
Wait-Settled 'AddLane'
Show-Control 'AddProgramStep'
Test-OnScreen 'Dutch Studio program' 'AddProgramStep'
Invoke-Control 'AddProgramStep'
Wait-Condition { $null -ne (Find-Optional 'Program0Card') } 'Dutch Studio program: Add step did not add a step card.'
Show-Control 'Program0Card'
foreach ($id in 'Program0Name', 'Program0MoveUp', 'Program0MoveDown', 'Program0More') { Test-OnScreen 'Dutch Studio program' $id }
Invoke-Control 'Program0More'
Invoke-Control 'Program0Remove'
Wait-Condition { $null -ne (Find-DialogButton 'PrimaryButton') } 'Dutch Studio program: Remove did not ask for confirmation.'
Invoke-DialogButton 'PrimaryButton' 'Dutch Studio program: the confirmation went away before Remove could be confirmed.'
Wait-Condition { $null -eq (Find-Optional 'Program0Card') } 'Dutch Studio program: removing the step did not take its card away.'
# A project that groups its beats (7/8 as 2+2+3) gives every step the project's own time signature, so the two fields are off, and
# Play offers no time signature while there is a program: the card itself offers Clear grouping, which keeps the program. The
# command must be on screen in Dutch too. The step is taken away and 4/4 chosen again, so nothing is left behind.
Select-Destination 'DestinationPlay'
Wait-Settled 'QuickMeter'
Select-Meter '7_8' '7/8'
Select-Destination 'DestinationStudio'
Wait-Settled 'AddLane'
Show-Control 'AddProgramStep'
Invoke-Control 'AddProgramStep'
Wait-Condition { $null -ne (Find-Optional 'Program0Card') } 'Grouped Studio program: Add step did not add a step card.'
Show-Control 'Program0Card'
Wait-Condition { $null -ne (Find-Optional 'Program0ClearGrouping') } 'Grouped Studio program: the card does not offer to clear the grouping.'
foreach ($id in 'Program0Beats', 'Program0Denominator') {
    if ((Find-Control $id).Current.IsEnabled) { throw "Grouped Studio program: $id is on while the project groups its beats." }
}
Show-Control 'Program0ClearGrouping'
Test-OnScreen 'Grouped Studio program' 'Program0ClearGrouping'
Invoke-Control 'Program0ClearGrouping'
Wait-Condition { $null -eq (Find-Optional 'Program0ClearGrouping') } 'Grouped Studio program: Clear grouping is still offered after it was used.'
Wait-Condition { (Find-Control 'Program0Beats').Current.IsEnabled -and (Find-Control 'Program0Denominator').Current.IsEnabled } 'Grouped Studio program: clearing the grouping did not turn the time signature fields on.'
Wait-Condition { $null -ne (Find-Optional 'Program0Card') } 'Grouped Studio program: clearing the grouping took the step away.'
Invoke-Control 'Program0More'
Invoke-Control 'Program0Remove'
Wait-Condition { $null -ne (Find-DialogButton 'PrimaryButton') } 'Grouped Studio program: Remove did not ask for confirmation.'
Invoke-DialogButton 'PrimaryButton' 'Grouped Studio program: the confirmation went away before Remove could be confirmed.'
Wait-Condition { $null -eq (Find-Optional 'Program0Card') } 'Grouped Studio program: removing the step did not take its card away.'
Select-Destination 'DestinationPlay'
Wait-Settled 'QuickMeter'
Select-Meter '4_4' '4/4'
Select-Destination 'DestinationSettings'
Wait-Settled 'Language'
Select-Combo 'Language' 'English'
Wait-Condition { (Find-Control 'DestinationPlay').Current.Name -eq 'Play' } 'The interface did not switch back to English.'
Select-Destination 'DestinationPlay'
Wait-Settled 'QuickMeter'
@{controls=$controls;selectedOutputCount=$selection.Count;playback=$playback;practiceStops=$practiceStops;
  resize=$resizeEvidence;instrument=$instrumentEvidence;tools=$toolsEvidence;manyPulseInstrument=$manyPulseInstrument;
  meters=$meterEvidence;liveSound=$liveSound;liveTempoAndMeter=$liveTempoAndMeter;liveSplitClicks=$liveSplitClicks;
  popupShortcuts=$(if ($sendKeys) { 'ctrl-m-in-all-seven-panels' } else { 'not-run-keyboard-disabled' });
  limitations='No acoustic accuracy, listening quality or complete accessibility acceptance is established.'} |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
