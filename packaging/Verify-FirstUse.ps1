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
    [switch]$NoKeyboard,
    # The caller (Verify-Local.ps1 -UiTestHooks) asked for the checks that need the UI-test hooks of a -p:UiTest=true build: if one of them
    # cannot run (no hooks, no audio, no output), the run fails instead of reporting it as not run.
    [switch]$RequireUiTestHooks
)
$ErrorActionPreference = 'Stop'
$onCi = $env:GITHUB_ACTIONS -eq 'true'
$playAudio = (-not $NoAudio) -and ($RunAudio -or $onCi)
$takeScreenshots = (-not $NoScreenshots) -and ($Screenshots -or $onCi)
$sendKeys = (-not $NoKeyboard) -and ($Keyboard -or $onCi)
if ($RequireUiTestHooks -and -not $playAudio) { throw '-RequireUiTestHooks needs the audio checks (-RunAudio): the checks that use the hooks play, at an inaudible gain.' }
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
# "Tap to start" sits with the tempo controls and takes nothing from the dial (docs/design/pages-design.md, Play): beside them with
# its words, beside them as an icon, appended to them as an icon (the two together are then what is centred), or, only when none of
# those fits, on a row of its own under them. It never overlaps a control, it is level with the controls it sits beside, and under
# the dial either the tempo controls or the group of them and the switch are centred. Measured only once both have stopped moving.
function Test-TapStartRow([string]$where) {
    Wait-Settled 'TapStart'
    Wait-Settled 'PlayQuickControls'
    $dial = (Find-Control 'PhaseInstrument').Current.BoundingRectangle
    $cluster = (Find-Control 'PlayQuickControls').Current.BoundingRectangle
    $toggle = (Find-Control 'TapStart').Current.BoundingRectangle
    $page = (Get-CurrentWindow).Current.BoundingRectangle
    if ($toggle.IsEmpty -or $cluster.IsEmpty) { throw "${where}: Tap to start or the tempo controls have no rectangle ($toggle, $cluster)." }
    Test-Visible $toggle $page "${where}: Tap to start"
    # "Inside the window" is not enough: a switch pushed past the edge of the page is clipped to what is left of it, and the rectangle
    # that remains lies inside the window. The hosted runner (100 % DPI) showed 2 DIP of it at 320x900 and this check passed. Whatever
    # its form, the switch keeps its own size: at least its icon, which is 46 DIP wide and 44 high.
    if ($toggle.Width -lt 40 * $scale -or $toggle.Height -lt 40 * $scale) {
        throw "${where}: Tap to start is squeezed to $([Math]::Round($toggle.Width / $scale, 1)) x $([Math]::Round($toggle.Height / $scale, 1)) DIP by the edge of the page (its smallest form is 46 x 44): $toggle, tempo controls $cluster."
    }
    $one = 1 * $scale
    if ($toggle.Left -lt $cluster.Right - $one -and $toggle.Right -gt $cluster.Left + $one -and
        $toggle.Top -lt $cluster.Bottom - $one -and $toggle.Bottom -gt $cluster.Top + $one) {
        throw "${where}: Tap to start overlaps the tempo controls ($toggle over $cluster)."
    }
    $dialCentre = $dial.X + $dial.Width / 2
    $clusterCentre = $cluster.X + $cluster.Width / 2
    $groupCentre = ([Math]::Min($cluster.Left, $toggle.Left) + [Math]::Max($cluster.Right, $toggle.Right)) / 2
    $beside = $toggle.Left -ge $cluster.Right - $one
    $tolerance = 3 * $scale
    if ([Math]::Abs($clusterCentre - $dialCentre) -gt $tolerance -and -not ($beside -and [Math]::Abs($groupCentre - $dialCentre) -le $tolerance)) {
        throw "${where}: neither the tempo controls nor the tempo controls with Tap to start are centred under the dial (dial $dial, controls $cluster, switch $toggle)."
    }
    if ($beside) {
        Test-OneLine "${where}: Tap to start and the tempo controls" $toggle $cluster
    } else {
        if ($toggle.Top -lt $cluster.Bottom - $one) { throw "${where}: Tap to start is neither beside nor under the tempo controls ($toggle, $cluster)." }
        if ([Math]::Abs(($toggle.X + $toggle.Width / 2) - $clusterCentre) -gt $tolerance) {
            throw "${where}: Tap to start is not centred under the tempo controls ($toggle, $cluster)."
        }
    }
    $form = if (-not $beside) { 'own-row' } elseif ($toggle.Width -gt 60 * $scale) { 'beside-with-words' }
        elseif ($toggle.Left - $cluster.Right -lt 20 * $scale) { 'appended-icon' } else { 'beside-icon' }
    return @{ form = $form; switchWidth = [Math]::Round($toggle.Width / $scale, 1); dialWidth = [Math]::Round($dial.Width / $scale, 1) }
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
$primary = @('PhaseInstrument', 'TempoEdit', 'TempoDown', 'TapTempo', 'TempoUp', 'TapStart', 'QuickMeter', 'MainSound', 'MainAccent0',
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
# Tap to start is never remembered: the application always opens stopped, silent and not listening for taps.
$tapStartSwitch = (Find-Control 'TapStart').GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern)
if ($tapStartSwitch.Current.ToggleState -ne [Windows.Automation.ToggleState]::Off) { throw 'Tap to start is on when the application opens.' }
$tapStartEvidence = @{ firstUse = Test-TapStartRow 'First-use window' }
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
    # At the sizes people use, the switch has its words and the dial keeps all of its room (Test-InstrumentHierarchy holds it to 220).
    $tapStartRow = Test-TapStartRow "Play at $($size[0])x$($size[1])"
    if ($tapStartRow.form -ne 'beside-with-words') { throw "Tap to start should sit beside the tempo controls with its words at $($size[0])x$($size[1]): $($tapStartRow.form)" }
    $tapStartEvidence["$($size[0])x$($size[1])"] = $tapStartRow
    $numeralOffset = Test-NumeralCentering $screenshot
    $resizeEvidence += @{width=$size[0];height=$size[1];settingsVisible=$true;primaryControlsVisible=$true;instrument=$composition;
        tools=$tools;numeralOffsetPixels=$numeralOffset;tapStart=$tapStartRow}
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
# The number of taps that start the metronome is a setting of the application, kept in the Tempo panel beside the tempo it is for:
# Cancel discards what was typed, Done keeps it, and the field shows it again whenever the panel is opened.
function Get-TapCount { (Find-Control 'TapInCount').GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern).Current.Value }
function Open-TapCount {
    Open-Panel @('TempoEdit') 'TapInCount'
    Wait-Settled 'TapInCount'
}
Open-TapCount
if ((Get-TapCount) -ne 4) { throw "The number of taps to start does not begin at 4: $(Get-TapCount)" }
Set-Number 'TapInCount' 7
Invoke-Control 'TempoOptionsCancel'
Wait-Absent 'TempoOptionsCancel'
Open-TapCount
if ((Get-TapCount) -ne 4) { throw "Cancel kept a draft number of taps to start: $(Get-TapCount)" }
Set-Number 'TapInCount' 7
Invoke-Control 'TempoOptionsDone'
Wait-Absent 'TempoOptionsDone'
Open-TapCount
Wait-Until { (Get-TapCount) -eq 7 } 'Done did not keep the number of taps to start.'
Set-Number 'TapInCount' 4
Invoke-Control 'TempoOptionsDone'
Wait-Absent 'TempoOptionsDone'
Open-TapCount
Wait-Until { (Get-TapCount) -eq 4 } 'The number of taps to start did not return to 4.'
Invoke-Control 'TempoOptionsCancel'
Wait-Absent 'TempoOptionsCancel'
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
    foreach ($id in 'TransportStartPause', 'TransportStop', 'EmergencyMute', 'AudioOptions', 'TempoDown', 'TapTempo', 'TempoUp', 'TapStart', 'QuickMeter') {
        $element = Find-Control $id
        if ($element.Current.IsOffscreen -or -not $currentBounds.Contains($element.Current.BoundingRectangle)) {
            throw "Command is inaccessible at $($size[0])x$($size[1]): $id"
        }
    }
    $tapStartRow = Test-TapStartRow "Play at $($size[0])x$($size[1])"
    $tapStartEvidence["$($size[0])x$($size[1])"] = $tapStartRow
    # The switch gives way before the dial does: in a narrow window it shrinks to its icon and sits at the end of the tempo controls
    # rather than take a row of its own (a row costs the dial about 50 DIP of height, and the dial is what the page is for).
    if ($size[0] -eq 400 -and ($tapStartRow.form -eq 'own-row' -or $tapStartRow.dialWidth -lt 120)) {
        throw "At 400x720 Tap to start took room from the dial: $($tapStartRow.form), dial $($tapStartRow.dialWidth) DIP across."
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
# Waits for something on a page other than Play, where the dial that Wait-Until reports in its message does not exist. It gives a
# page 30 seconds: Studio rebuilds its page on the UI thread, and a Convert or an Undo there took from 1.4 seconds to more than 8 on
# the private desktop of the local gate (the 8-second wait ran out on 4 of 16 steps on preview.10 and on 2 of 16 on preview.11), with
# the lane's fields off and every UI Automation call blocked meanwhile. The wait only ends sooner when the page is ready, so a page
# that does get ready costs nothing, and one that never does still fails, with the same words.
function Wait-Condition([scriptblock]$condition, [string]$failure, [int]$seconds = 30) {
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
# Help is a read-only panel on Play with a quick start and a musical reference (docs/design/pages-design.md, Play panels). It is opened
# and read without changing anything. Whatever the window size its text stays between the edges of the text area, a step of the quick
# start is not squeezed to a sliver, the first step is on screen without scrolling, Done is whole, on screen and below the text, the
# text can be scrolled to its last line, and where keys can be sent the keyboard reads it. The ids are HelpContent's (a unit test pins
# them); what each one says, in every language, is checked by the unit tests, not here.
$helpWide = @('HelpStep1', 'HelpStep2', 'HelpStep3', 'HelpStep4', 'HelpStep5', 'HelpTapStartText', 'HelpTapStartLimits', 'HelpPlaying',
    'HelpLive', 'HelpProgram', 'HelpShortcutScope', 'HelpBpm', 'HelpBpmExample', 'HelpMeter', 'HelpMeterExample', 'HelpFeel',
    'HelpFeelExample', 'HelpStrength', 'HelpStrengthExample')
$helpIds = @('HelpQuickStartHeading', 'HelpTapStartHeading', 'HelpPlayingHeading', 'HelpShortcutsHeading', 'HelpReferenceHeading',
    'HelpBpmTitle', 'HelpMeterTitle', 'HelpFeelTitle', 'HelpStrengthTitle') + $helpWide
$helpLastLine = 'HelpStrengthExample'
function Get-HelpScroll { (Find-Control 'PlayHelpPanelScroll').GetCurrentPattern([Windows.Automation.ScrollPattern]::Pattern) }
function Get-FocusedAutomationId { [Windows.Automation.AutomationElement]::FocusedElement.Current.AutomationId }
# Keys go to the application only while it owns the keyboard, so nobody working in another window can receive them.
function Send-HelpKeys([string]$keys) {
    Add-Type -AssemblyName System.Windows.Forms
    if ([Windows.Automation.AutomationElement]::FocusedElement.Current.ProcessId -ne $AppProcessId) {
        throw "Keyboard focus is not in the application; refusing to send $keys to another window."
    }
    [Windows.Forms.SendKeys]::SendWait($keys)
    Start-Sleep -Milliseconds 300
}
function Test-HelpPanel([string]$where, [string]$shot = '', [bool]$shotEnd = $false) {
    $transportBefore = (Find-Control 'TransportStatus').Current.Name
    $dialBefore = Get-Dial
    $dialRectBefore = (Find-Control 'PhaseInstrument').Current.BoundingRectangle
    # Open-Panel and Invoke-Control say what they do on the output stream; that must not become part of what this returns.
    $null = Open-Panel @('PlayHelp') 'PlayHelpPanelDone'
    Wait-Settled 'PlayHelpPanelDone'
    Wait-Settled 'HelpStep1'
    $windowRect = (Get-CurrentWindow).Current.BoundingRectangle
    $viewport = (Find-Control 'PlayHelpPanelScroll').Current.BoundingRectangle
    $done = (Find-Control 'PlayHelpPanelDone').Current.BoundingRectangle
    $one = 1 * $scale
    # The text area and Done are whole, inside the window, and Done is the size of every other panel's (120 x 44), below the text.
    Test-Visible $viewport $windowRect "${where}: the text area"
    Test-Visible $done $windowRect "${where}: Done"
    if ($done.Width -lt 100 * $scale -or $done.Height -lt 40 * $scale) {
        throw "${where}: Done is squeezed to $([Math]::Round($done.Width / $scale, 1)) x $([Math]::Round($done.Height / $scale, 1)) DIP (a panel's is 120 x 44): $done."
    }
    if ($viewport.Height -lt 200 * $scale) {
        throw "${where}: the text area is only $([Math]::Round($viewport.Height / $scale)) DIP high, too little to read a quick start in: $viewport."
    }
    if ($viewport.Bottom -gt $done.Top + $one) { throw "${where}: the text runs under Done ($viewport over $done)." }
    # Every entry is there with its text. One that has a rectangle (the others are scrolled out of view) lies between the edges of the
    # text area, and a step or paragraph is wide enough to read: a Stretch element wider than its slot is clipped, not shrunk.
    foreach ($id in $helpIds) {
        $element = Find-Control $id
        if ([string]::IsNullOrWhiteSpace($element.Current.Name)) { throw "${where}: $id has no text." }
        $rect = $element.Current.BoundingRectangle
        if ($rect.IsEmpty) { continue }
        if ($rect.Left -lt $viewport.Left - $one -or $rect.Right -gt $viewport.Right + $one) {
            throw "${where}: $id is clipped sideways ($rect in a text area of $viewport)."
        }
        if ($helpWide -contains $id -and $rect.Width -lt 150 * $scale) {
            throw "${where}: $id is squeezed to $([Math]::Round($rect.Width / $scale, 1)) DIP wide: $rect."
        }
    }
    $step1 = (Find-Control 'HelpStep1').Current.BoundingRectangle
    Test-Visible $step1 $viewport "${where}: the first step of the quick start (the panel opens at its first line)"
    Test-NoApplyButtons "${where}"
    # The panel as a person sees it, kept with the run (a runner takes screenshots; a local run does not).
    if ($shot) { $null = Save-PlayScreenshot "$shot.png" }
    # The text can be scrolled to its last line, and back.
    $scroll = Get-HelpScroll
    $scrollable = $scroll.Current.VerticallyScrollable
    if ($scrollable) {
        $scroll.SetScrollPercent([Windows.Automation.ScrollPattern]::NoScroll, 100)
        Wait-Settled $helpLastLine
    }
    Test-Visible (Find-Control $helpLastLine).Current.BoundingRectangle $viewport "${where}: the last line of the text"
    if ($shot -and $shotEnd) { $null = Save-PlayScreenshot "$shot-end.png" }
    $scroll = Get-HelpScroll
    if ($scrollable) {
        $scroll.SetScrollPercent([Windows.Automation.ScrollPattern]::NoScroll, 0)
        Wait-Settled 'HelpStep1'
        Test-Visible (Find-Control 'HelpStep1').Current.BoundingRectangle $viewport "${where}: the first step after scrolling back"
    }
    # A keyboard reads it: the text area is the tab stop, the keys scroll it, and Tab goes on to Done. Keys can be sent only on a
    # desktop that nobody else is using (a hosted runner); the private desktop of a local run cannot send them.
    $keyboard = 'not-run-keyboard-disabled'
    if ($sendKeys -and $scrollable) {
        (Find-Control 'PlayHelpPanelScroll').SetFocus()
        Wait-Condition { (Get-FocusedAutomationId) -eq 'PlayHelpPanelScroll' } "${where}: the text area did not take the keyboard focus (it is on '$(Get-FocusedAutomationId)')."
        Send-HelpKeys '{PGDN}'
        Wait-Condition { (Get-HelpScroll).Current.VerticalScrollPercent -gt 5 } "${where}: Page Down did not scroll the Help."
        Send-HelpKeys '{END}'
        Wait-Condition { (Get-HelpScroll).Current.VerticalScrollPercent -gt 95 } "${where}: End did not reach the last line of the Help."
        Send-HelpKeys '{HOME}'
        Wait-Condition { (Get-HelpScroll).Current.VerticalScrollPercent -lt 1 } "${where}: Home did not return to the first line of the Help."
        Send-HelpKeys '{TAB}'
        Wait-Condition { (Get-FocusedAutomationId) -eq 'PlayHelpPanelDone' } "${where}: Tab did not go on to Done (the focus is on '$(Get-FocusedAutomationId)')."
        $keyboard = 'page-down-end-home-and-tab-to-done'
    }
    # The panel opens at its first line whatever was read before. Every check above ends at the first line (the text is scrolled back
    # and Home is pressed), so none of them could see a panel that opens where it was left: close it at its last line, open it
    # again, and require the first step on screen before anything asks for any scrolling.
    $reopens = 'not-scrollable'
    if ($scrollable) {
        (Get-HelpScroll).SetScrollPercent([Windows.Automation.ScrollPattern]::NoScroll, 100)
        Wait-Settled $helpLastLine
        $null = Invoke-Control 'PlayHelpPanelDone'
        Wait-Absent 'PlayHelpPanelDone'
        $null = Open-Panel @('PlayHelp') 'PlayHelpPanelDone'
        Wait-Settled 'PlayHelpPanelDone'
        Wait-Settled 'HelpStep1'
        Test-Visible (Find-Control 'HelpStep1').Current.BoundingRectangle (Find-Control 'PlayHelpPanelScroll').Current.BoundingRectangle "${where}: the first step after the panel was closed at its last line and opened again (it opens at its first line)"
        $reopenedAt = (Get-HelpScroll).Current.VerticalScrollPercent
        if ($reopenedAt -gt 1) { throw "${where}: the panel opened again $([Math]::Round($reopenedAt)) percent down its text, not at its first line." }
        $reopens = 'at-first-line-after-closing-at-the-last'
    }
    $evidence = @{
        viewportHeight = [Math]::Round($viewport.Height / $scale, 1); step1Width = [Math]::Round($step1.Width / $scale, 1)
        doneWidth = [Math]::Round($done.Width / $scale, 1); doneHeight = [Math]::Round($done.Height / $scale, 1)
        scrollable = $scrollable; keyboard = $keyboard; reopens = $reopens
    }
    $null = Invoke-Control 'PlayHelpPanelDone'
    Wait-Absent 'PlayHelpPanelDone'
    # Reading the Help changes nothing: the transport is as it was, the dial is where it was and says what it said, no error is open.
    if ((Find-Control 'TransportStatus').Current.Name -ne $transportBefore) {
        throw "${where}: the Help changed the transport ('$transportBefore' then '$((Find-Control 'TransportStatus').Current.Name)')."
    }
    if ((Get-Dial) -ne $dialBefore) { throw "${where}: the Help changed the tempo readout ('$dialBefore' then '$(Get-Dial)')." }
    $dialRectAfter = (Find-Control 'PhaseInstrument').Current.BoundingRectangle
    if ([Math]::Abs($dialRectAfter.Width - $dialRectBefore.Width) -gt $one -or [Math]::Abs($dialRectAfter.Height - $dialRectBefore.Height) -gt $one) {
        throw "${where}: the dial is not the size it was before the Help was opened ($dialRectBefore, then $dialRectAfter)."
    }
    Assert-NoErrorNotice $where
    return $evidence
}
# The tempo guide is a line beside the title of the Tempo panel (docs/design/pages-design.md, Play panels). It names the approximate
# Italian markings of the speed that the panel holds (Moderato / Allegro) and follows the number and the note value in the panel before
# anything is applied. The markings are about a speed in quarter notes a minute whatever note value is chosen: counted in quarter notes
# the line only names them (the note value beside the box says what the number counts), counted in any other note it also says the
# speed in quarter notes (80 dotted quarters are 120 BPM in quarter notes, and picking 6/8 counts eighth notes). The names are the same
# in every language; the sentences around them are translated and what they say is checked by the unit tests, not here: this reads the
# line on the real window, in the panel, at every size, and requires the panel's own fields to stay whole under it.
$tempoGuideNames = @('Larghissimo', 'Grave', 'Largo', 'Larghetto', 'Adagio', 'Andante', 'Moderato', 'Allegro', 'Vivace', 'Presto', 'Prestissimo')
function Get-TempoGuideMarkings([string]$text) {
    @($tempoGuideNames | Where-Object { $text -match "(?<!\p{L})$_(?!\p{L})" })
}
# Waits for the guide to name exactly these markings. With a rate it is the line for a note that is not a quarter: it says that speed
# in quarter notes a minute, as the sentence prints it, with BPM; without one it is the line for a quarter note, which says no BPM.
function Wait-TempoGuide([string[]]$names, [string]$rate, [string]$what) {
    $limit = [DateTime]::UtcNow.AddSeconds(15)
    do {
        $text = (Find-Control 'TempoGuide').Current.Name
        $saysRate = if ($rate) { $text -match "(?<![\d.,])$([regex]::Escape($rate))(?!\d)" -and $text -match 'BPM' } else { $text -notmatch 'BPM' }
        if (((Get-TempoGuideMarkings $text) -join ' ') -eq ($names -join ' ') -and $saysRate) { return $text }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $limit)
    $wanted = if ($rate) { "at $rate BPM in quarter notes" } else { 'for a tempo counted in quarter notes, without a conversion' }
    throw "${what}: the tempo guide says '$text', but it should name $($names -join ' / ') $wanted."
}
function Test-TempoGuide([string]$where, [string]$shot = '', [switch]$Interact, [string]$eighth = 'Eighth note', [string]$quarter = 'Quarter note', [bool]$tapsWhole = $true) {
    $transportBefore = (Find-Control 'TransportStatus').Current.Name
    $dialBefore = Get-Dial
    $dialRectBefore = (Find-Control 'PhaseInstrument').Current.BoundingRectangle
    $null = Open-Panel @('TempoEdit') 'TempoGuide'
    Wait-Settled 'TempoGuide'
    Wait-Settled 'TempoOptionsDone'
    $windowRect = (Get-CurrentWindow).Current.BoundingRectangle
    $one = 1 * $scale
    $guide = (Find-Control 'TempoGuide').Current.BoundingRectangle
    $tempoBox = (Find-Control 'Tempo').Current.BoundingRectangle
    $taps = (Find-Control 'TapInCount').Current.BoundingRectangle
    $done = (Find-Control 'TempoOptionsDone').Current.BoundingRectangle
    # Whole and inside the window, and as large as a line of text that can be read: a Stretch element wider than its slot is clipped,
    # not shrunk, and a line squeezed by a neighbour is a sliver (the Tap to start switch once showed 2 DIP of itself). Beside the title
    # of a panel 240 DIP wide the guide has about 166 DIP, and wraps in them.
    Test-Visible $guide $windowRect "${where}: the tempo guide"
    if ($guide.Width -lt 100 * $scale -or $guide.Height -lt 14 * $scale) {
        throw "${where}: the tempo guide is squeezed to $([Math]::Round($guide.Width / $scale, 1)) x $([Math]::Round($guide.Height / $scale, 1)) DIP (a line of it is at least 100 DIP wide and 14 high): $guide."
    }
    # It sits above the number it describes (beside the title, which is above everything the panel edits) and between the edges of the panel.
    if ($guide.Bottom -gt $tempoBox.Top + $one) { throw "${where}: the tempo guide is not above the tempo box ($guide, tempo $tempoBox)." }
    if ($guide.Left -lt $tempoBox.Left - 2 * $scale -or $guide.Right -gt $done.Right + 2 * $scale) {
        throw "${where}: the tempo guide is clipped sideways ($guide between $($tempoBox.Left) and $($done.Right))."
    }
    # What it sits beside costs the panel's fields nothing: the box for the number of taps is whole above the footer. A panel is only
    # as tall as the room under the dial's readout, and a line of its own under the note value took the lower half of this box out of
    # view at 400x720 and at the first-use size of a hosted runner (the first version of the guide did).
    if ($tapsWhole) {
        if ($taps.IsEmpty -or $taps.Height -lt 40 * $scale -or $taps.Bottom -gt $done.Top + $one) {
            throw "${where}: the box for the number of taps is not whole above Done ($taps, Done $done): the tempo guide took room from the fields of the panel."
        }
    }
    Test-NoApplyButtons $where
    # The project holds 120 BPM counted in quarter notes here, and 120 quarter notes a minute is where two markings meet.
    $initial = Wait-TempoGuide @('Moderato', 'Allegro') '' "${where}: the tempo of the project"
    if ($shot) { $null = Save-PlayScreenshot "$shot.png" }
    $follows = 'not-run'
    $emptied = 'not-run'
    if ($Interact) {
        # The guide follows what the panel holds, before Done: the number (committed in the box by Enter, a stepper or leaving it) ...
        Set-Number 'Tempo' 90
        $null = Wait-TempoGuide @('Andante') '' "${where}: a tempo of 90"
        Set-Number 'Tempo' 42
        $null = Wait-TempoGuide @('Grave', 'Largo') '' "${where}: a tempo of 42, where two markings overlap"
        Set-Number 'Tempo' 120
        $null = Wait-TempoGuide @('Moderato', 'Allegro') '' "${where}: a tempo typed back to 120"
        # ... and the note value: 120 counted in eighth notes is 60 quarter notes a minute, which is another speed and other names.
        Select-Combo 'TempoUnit' $eighth
        $null = Wait-TempoGuide @('Largo', 'Larghetto') '60' "${where}: 120 BPM counted in eighth notes"
        Select-Combo 'TempoUnit' $quarter
        $null = Wait-TempoGuide @('Moderato', 'Allegro') '' "${where}: the note value chosen back to a quarter note"
        $follows = 'the-number-and-the-note-value'
        # An emptied box is not a tempo: the guide says what to enter. The box hands its text over when it loses the focus, and a
        # keyboard is not needed for that; where the box cannot be reached the check says so instead of passing.
        $edit = (Find-Control 'Tempo').FindFirst([Windows.Automation.TreeScope]::Descendants,
            [Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ControlTypeProperty, [Windows.Automation.ControlType]::Edit))
        [object]$valuePattern = $null
        if ($null -ne $edit -and $edit.TryGetCurrentPattern([Windows.Automation.ValuePattern]::Pattern, [ref]$valuePattern)) {
            $valuePattern.SetValue('')
            (Find-Control 'TempoUnit').SetFocus()
            Wait-Condition { (Get-TempoGuideMarkings (Find-Control 'TempoGuide').Current.Name).Count -eq 0 } "${where}: an emptied tempo box still has markings in the guide."
            if ((Find-Control 'TempoGuide').Current.Name -notmatch '1000') { throw "${where}: the guide does not say which tempos it can name when the box is empty: '$((Find-Control 'TempoGuide').Current.Name)'." }
            $emptied = 'says-what-to-enter'
            Set-Number 'Tempo' 120
            $null = Wait-TempoGuide @('Moderato', 'Allegro') '' "${where}: a tempo typed after the box was emptied"
        } else { $emptied = 'not-run-the-box-has-no-text-to-set' }
        # A draft left in the panel is only a draft: 90 BPM counted in eighth notes is 45 quarter notes a minute (two markings meet there).
        Set-Number 'Tempo' 90
        Select-Combo 'TempoUnit' $eighth
        $null = Wait-TempoGuide @('Grave', 'Largo') '45' "${where}: a draft of 90 BPM counted in eighth notes, left in the panel"
    }
    $evidence = @{
        guideWidth = [Math]::Round($guide.Width / $scale, 1); guideHeight = [Math]::Round($guide.Height / $scale, 1)
        tapsHeight = [Math]::Round($taps.Height / $scale, 1)
        text = $initial; follows = $follows; emptiedBox = $emptied
    }
    $null = Invoke-Control 'TempoOptionsCancel'
    Wait-Absent 'TempoOptionsCancel'
    # Reading the guide, and trying other tempos and note values in the panel, changes nothing: Cancel leaves the transport, the dial
    # and what it says as they were, and no error is open. The guide never writes anything.
    if ((Find-Control 'TransportStatus').Current.Name -ne $transportBefore) {
        throw "${where}: the Tempo panel changed the transport ('$transportBefore' then '$((Find-Control 'TransportStatus').Current.Name)')."
    }
    if ((Get-Dial) -ne $dialBefore) { throw "${where}: the tempo guide changed the tempo readout ('$dialBefore' then '$(Get-Dial)')." }
    $dialRectAfter = (Find-Control 'PhaseInstrument').Current.BoundingRectangle
    if ([Math]::Abs($dialRectAfter.Width - $dialRectBefore.Width) -gt $one -or [Math]::Abs($dialRectAfter.Height - $dialRectBefore.Height) -gt $one) {
        throw "${where}: the dial is not the size it was before the Tempo panel was opened ($dialRectBefore, then $dialRectAfter)."
    }
    Assert-NoErrorNotice $where
    if ($Interact) {
        # The panel opens again on what the project holds, and its guide names that, not the draft that was left in it.
        $null = Open-Panel @('TempoEdit') 'TempoGuide'
        Wait-Settled 'TempoGuide'
        $null = Wait-TempoGuide @('Moderato', 'Allegro') '' "${where}: the panel opened again after Cancel"
        $null = Invoke-Control 'TempoOptionsCancel'
        Wait-Absent 'TempoOptionsCancel'
        $evidence['discard'] = 'cancel-leaves-the-project-and-the-panel-opens-on-it-again'
    }
    return $evidence
}
# The export of a finished timing check (docs/design/pages-design.md, Train) is one more command in the row of the timing check, after
# Finish, with a hint under the result. It is read at the sizes the layout gates use and in Dutch: whole and as large as a command, after
# the commands that come before it and between the edges of its card; those commands as whole as they are without it (a command that is
# squeezed or pushed over by a new neighbour is the defect this guards); the hint readable (not a sliver, not clipped, below the
# commands); and off until a check has finished. A hosted runner has no output, so no check can finish there: Test-TimingExportWithOutput
# finishes one in the local gate, and this function then reads the finished state (-finished) too.
$timingCommands = @('BeginKeyboardTiming', 'KeyboardAttack', 'FinishKeyboardTiming', 'ExportTimingCsv')
$timingCommandMinimum = @{ BeginKeyboardTiming = @(100, 40); KeyboardAttack = @(70, 40); FinishKeyboardTiming = @(150, 40); ExportTimingCsv = @(100, 40) }
function Test-TimingExport([string]$where, [string]$shot = '', [bool]$finished = $false, [string]$mainBeat = 'Main beat', [string]$project = '') {
    $transportBefore = (Find-Control 'TransportStatus').Current.Name
    Select-Destination 'DestinationTrain'
    Wait-Settled 'TimingTolerance'
    $null = Find-Control 'ExportTimingCsv'
    # A person scrolls to the timing check: down to the export, then up to the command that starts a check, so that all four are on screen.
    Show-Control 'ExportTimingCsv'
    Show-Control 'BeginKeyboardTiming'
    foreach ($id in $timingCommands) { Wait-Settled $id }
    $windowRect = (Get-CurrentWindow).Current.BoundingRectangle
    $one = 1 * $scale
    # The card keeps 21 DIP of padding at every size. The card cuts off what is wider than its slot, and what it cuts off is not in the
    # rectangle that UI Automation reports, so a rectangle that reaches into the padding is one that was cut (or pushed to the edge).
    $padding = 10 * $scale
    $card = (Find-Control 'TimingCard').Current.BoundingRectangle
    $rects = @{}
    foreach ($id in $timingCommands) {
        $rects[$id] = (Find-Control $id).Current.BoundingRectangle
        Test-Visible $rects[$id] $windowRect "${where}: $id"
        $least = $timingCommandMinimum[$id]
        if ($rects[$id].Width -lt $least[0] * $scale -or $rects[$id].Height -lt $least[1] * $scale) {
            throw "${where}: $id is squeezed to $([Math]::Round($rects[$id].Width / $scale, 1)) x $([Math]::Round($rects[$id].Height / $scale, 1)) DIP (a command of the timing check is at least $($least[0]) x $($least[1]) DIP): $($rects[$id])."
        }
        if ($rects[$id].Left -lt $card.Left + $padding -or $rects[$id].Right -gt $card.Right - $padding) {
            throw "${where}: $id is clipped sideways or pushed into the edge of its card ($($rects[$id]) in the card $card)."
        }
    }
    # No two commands overlap: each is a whole command of its own, on one row or in a row under the one before.
    for ($first = 0; $first -lt $timingCommands.Count; $first++) {
        for ($second = $first + 1; $second -lt $timingCommands.Count; $second++) {
            $a = $rects[$timingCommands[$first]]; $b = $rects[$timingCommands[$second]]
            $a.Inflate(-$one, -$one); $b.Inflate(-$one, -$one)
            if ($a.IntersectsWith($b)) { throw "${where}: $($timingCommands[$first]) and $($timingCommands[$second]) overlap ($($rects[$timingCommands[$first]]) and $($rects[$timingCommands[$second]]))." }
        }
    }
    # The commands come in the order a check goes: start, press, finish, then export. Each is after the one before it: to its right on the
    # same row or on a row under it.
    $finish = $rects['FinishKeyboardTiming']; $export = $rects['ExportTimingCsv']
    for ($index = 1; $index -lt $timingCommands.Count; $index++) {
        $before = $rects[$timingCommands[$index - 1]]; $after = $rects[$timingCommands[$index]]
        $onOneRow = [Math]::Abs(($after.Top + $after.Height / 2) - ($before.Top + $before.Height / 2)) -le 4 * $scale
        if (($onOneRow -and $after.Left -lt $before.Right - $one) -or (-not $onOneRow -and $after.Top -lt $before.Bottom - $one)) {
            throw "${where}: $($timingCommands[$index]) does not come after $($timingCommands[$index - 1]) ($after, $before)."
        }
    }
    $layout = if ([Math]::Abs(($export.Top + $export.Height / 2) - ($finish.Top + $finish.Height / 2)) -le 4 * $scale) { 'after-finish-on-its-row' } else { 'under-finish' }
    # The export is off until a check has finished, and then on.
    $enabled = (Find-Control 'ExportTimingCsv').Current.IsEnabled
    if ($finished -and -not $enabled) { throw "${where}: the export is off although a check has finished." }
    if (-not $finished -and $enabled) { throw "${where}: the export is on although no check has finished." }
    # The hint is readable and under the commands, and the line that names what is exported is there only for a finished check.
    Show-Control 'TimingCsvHint'
    Wait-Settled 'TimingCsvHint'
    $hintElement = Find-Control 'TimingCsvHint'
    $hint = $hintElement.Current.BoundingRectangle
    $export = (Find-Control 'ExportTimingCsv').Current.BoundingRectangle
    $card = (Find-Control 'TimingCard').Current.BoundingRectangle
    Test-Visible $hint $windowRect "${where}: the hint under the export"
    # The export and the hint are on screen together at every size (a rectangle that is not on screen is empty, and would pass any
    # comparison below it), so that what lies between them is read, not assumed.
    if ($export.IsEmpty) { throw "${where}: the export and its hint are not on screen together." }
    # The export is still whole there: an edge of the window that cuts it leaves a rectangle that is not empty but smaller than the command.
    $leastExport = $timingCommandMinimum['ExportTimingCsv']
    if ($export.Width -lt $leastExport[0] * $scale -or $export.Height -lt $leastExport[1] * $scale) {
        throw "${where}: the export is cut by the edge of the window while its hint is on screen: $([Math]::Round($export.Width / $scale, 1)) x $([Math]::Round($export.Height / $scale, 1)) DIP (a command of the timing check is at least $($leastExport[0]) x $($leastExport[1]) DIP): $export."
    }
    Test-Visible $export $windowRect "${where}: the export beside its hint"
    if ($hintElement.Current.Name.Length -lt 80 -or $hintElement.Current.Name -notmatch 'CSV') { throw "${where}: the hint under the export does not say what it saves: '$($hintElement.Current.Name)'." }
    if ($hint.Width -lt 150 * $scale -or $hint.Height -lt 14 * $scale) {
        throw "${where}: the hint is squeezed to $([Math]::Round($hint.Width / $scale, 1)) x $([Math]::Round($hint.Height / $scale, 1)) DIP (a line of it is at least 150 DIP wide and 14 high): $hint."
    }
    if ($hint.Left -lt $card.Left + $padding -or $hint.Right -gt $card.Right - $padding) { throw "${where}: the hint is clipped sideways or pushed into the edge of its card ($hint in the card $card)." }
    if ($hint.Top -lt $export.Bottom - $one) { throw "${where}: the hint is not under the export ($hint, export $export)." }
    $readyEvidence = 'none'
    $ready = Find-Optional 'TimingCsvReady'
    if ($finished) {
        if ($null -eq $ready) { throw "${where}: a check has finished but no line says what the export holds." }
        $readyText = $ready.Current.Name
        if ($project -and $readyText -notlike "*$project*") { throw "${where}: the line for the export does not name the project '$project': '$readyText'." }
        if ($readyText -notlike "*$mainBeat*") { throw "${where}: the line for the export does not name the rhythm '$mainBeat': '$readyText'." }
        if ($readyText -notmatch '\d{1,2}[:.]\d{2}') { throw "${where}: the line for the export does not say when the check finished: '$readyText'." }
        Show-Control 'TimingCsvReady'
        Wait-Settled 'TimingCsvReady'
        $line = (Find-Control 'TimingCsvReady').Current.BoundingRectangle
        Test-Visible $line $windowRect "${where}: the line for the export"
        if ($line.Width -lt 150 * $scale -or $line.Height -lt 14 * $scale) { throw "${where}: the line for the export is squeezed to $line." }
        if ($line.Left -lt $card.Left + $padding -or $line.Right -gt $card.Right - $padding) { throw "${where}: the line for the export is clipped sideways or pushed into the edge of its card ($line in the card $card)." }
        $readyEvidence = "$([Math]::Round($line.Width / $scale, 1)) x $([Math]::Round($line.Height / $scale, 1))"
    } elseif ($null -ne $ready) { throw "${where}: the line for the export is shown although no check has finished: '$($ready.Current.Name)'." }
    Test-NoApplyButtons "${where}: Train"
    Assert-NoErrorNotice $where
    if ((Find-Control 'TransportStatus').Current.Name -ne $transportBefore) {
        throw "${where}: reading the export controls changed the transport ('$transportBefore' then '$((Find-Control 'TransportStatus').Current.Name))')."
    }
    if ($shot) { $null = Save-PlayScreenshot "$shot.png" }
    $evidence = @{
        layout = $layout; finished = $finished
        begin = "$([Math]::Round($rects['BeginKeyboardTiming'].Width / $scale, 1)) x $([Math]::Round($rects['BeginKeyboardTiming'].Height / $scale, 1))"
        hit = "$([Math]::Round($rects['KeyboardAttack'].Width / $scale, 1)) x $([Math]::Round($rects['KeyboardAttack'].Height / $scale, 1))"
        finish = "$([Math]::Round($rects['FinishKeyboardTiming'].Width / $scale, 1)) x $([Math]::Round($rects['FinishKeyboardTiming'].Height / $scale, 1))"
        export = "$([Math]::Round($export.Width / $scale, 1)) x $([Math]::Round($export.Height / $scale, 1))"
        hint = "$([Math]::Round($hint.Width / $scale, 1)) x $([Math]::Round($hint.Height / $scale, 1))"
        ready = $readyEvidence
    }
    Show-Control 'EditProgram'
    return $evidence
}
# What the export of a finished check depends on, with the real transport (a hosted runner has no output, so this is the local gate only,
# and the project it plays is silent). Nothing to export before the first check ends, and none while one runs; after one ends, a line that
# says what is exported (project, rhythm, when) and the command on; kept through Stop, another page and a rename of the project (the line
# still names the project as it was); gone when another check starts, and when a change of playback ends the check that runs. It leaves a
# finished check behind, which the Dutch checks then find, in Dutch.
$script:timingExportFinished = $false
$script:timingExportProject = ''
function Test-TimingExportWithOutput {
    $script:timingExportDone = [System.Collections.Generic.List[string]]::new()
    function Get-ExportOn { (Find-Control 'ExportTimingCsv').Current.IsEnabled }
    function Start-PlayingOnTrain {
        Select-Destination 'DestinationPlay'
        Wait-Settled 'QuickMeter'
        Invoke-Control 'TransportStartPause'
        $null = Wait-Transport 'Playing' $true
        Select-Destination 'DestinationTrain'
        Wait-Settled 'TimingTolerance'
    }
    function Begin-Check([string]$what) {
        Invoke-Control 'BeginKeyboardTiming'
        Wait-Condition { (Find-Control 'KeyboardAttack').Current.IsEnabled } "${what}: the check did not start."
        if (Get-ExportOn) { throw "${what}: the export is on while a check is running." }
        if ($null -ne (Find-Optional 'TimingCsvReady')) { throw "${what}: a line says what is exported while a check is running." }
    }
    function Finish-Check([string]$what) {
        Invoke-Control 'FinishKeyboardTiming'
        Wait-Condition { Get-ExportOn } "${what}: the export did not turn on when the check finished."
        Wait-Condition { $null -ne (Find-Optional 'TimingCsvReady') } "${what}: no line says what is exported once the check has finished."
    }
    function Assert-Kept([string]$what, [string]$project) {
        if (-not (Get-ExportOn)) { throw "${what}: the finished check can no longer be exported." }
        # A person waits for the line a moment; so does this check, so that a busy machine is not mistaken for a line that is gone.
        $limit = [DateTime]::UtcNow.AddSeconds(5)
        do { $line = Find-Optional 'TimingCsvReady'; if ($null -ne $line) { break }; Start-Sleep -Milliseconds 100 } while ([DateTime]::UtcNow -lt $limit)
        if ($null -eq $line) { throw "${what}: no line says what is exported any more." }
        if ($line.Current.Name -notlike "*$project*") { throw "${what}: the line for the export does not name the project '$project': '$($line.Current.Name)'." }
    }
    # The name of the project as the person sees it, read from the panel that edits it.
    Select-Destination 'DestinationPlay'
    Wait-Settled 'QuickMeter'
    Open-Panel @('ProjectOptions') 'DocumentName'
    $project = (Find-Control 'DocumentName').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value
    Invoke-Control 'ProjectOptionsPanelCancel'
    Wait-Absent 'DocumentName'
    if ([string]::IsNullOrWhiteSpace($project)) { throw 'The project has no name to look for in the line for the export.' }

    # 1. Nothing has finished yet: the command is off and nothing says what would be exported.
    Start-PlayingOnTrain
    if (Get-ExportOn) { throw 'The export is on before any check has finished.' }
    if ($null -ne (Find-Optional 'TimingCsvReady')) { throw 'A line says what is exported before any check has finished.' }
    $script:timingExportDone.Add('off-before-the-first-check')
    # 2. A check that runs has no result yet.
    Begin-Check 'A running check'
    $script:timingExportDone.Add('off-while-a-check-runs')
    foreach ($press in 1..3) { Invoke-Control 'KeyboardAttack'; Start-Sleep -Milliseconds 700 }
    # 3. When it finishes, the line says what is exported: project, rhythm and when.
    Finish-Check 'A finished check'
    $ready = (Find-Control 'TimingCsvReady').Current.Name
    if ($ready -notlike "*$project*") { throw "The line for the export does not name the project '$project': '$ready'." }
    if ($ready -notlike '*Main beat*') { throw "The line for the export does not name the rhythm 'Main beat': '$ready'." }
    if ($ready -notmatch '\d{1,2}[:.]\d{2}') { throw "The line for the export does not say when the check finished: '$ready'." }
    $script:timingExportDone.Add('on-after-a-check-with-a-line-naming-project-rhythm-and-time')
    # 4. It is kept through Stop and through another page.
    Invoke-Control 'TransportStop'
    $null = Wait-Transport 'Stopped'
    Assert-Kept 'After Stop' $project
    Select-Destination 'DestinationPlay'
    Wait-Settled 'QuickMeter'
    Select-Destination 'DestinationTrain'
    Wait-Settled 'TimingTolerance'
    Assert-Kept 'After another page' $project
    $script:timingExportDone.Add('kept-through-stop-and-another-page')
    # 5. It is kept through a rename of the project, and still says the project's name as it was.
    Select-Destination 'DestinationPlay'
    Wait-Settled 'QuickMeter'
    $renamed = 'Renamed after the check'
    Open-Panel @('ProjectOptions') 'DocumentName'
    Wait-Settled 'DocumentName'
    Set-Text 'DocumentName' $renamed
    Invoke-Control 'ProjectOptionsPanelDone'
    Wait-Absent 'DocumentName'
    Open-Panel @('ProjectOptions') 'DocumentName'
    if ((Find-Control 'DocumentName').GetCurrentPattern([Windows.Automation.ValuePattern]::Pattern).Current.Value -ne $renamed) { throw 'The project was not renamed, so a rename cannot be shown not to reach the line for the export.' }
    Invoke-Control 'ProjectOptionsPanelCancel'
    Wait-Absent 'DocumentName'
    Select-Destination 'DestinationTrain'
    Wait-Settled 'TimingTolerance'
    Assert-Kept 'After the project was renamed' $project
    if ((Find-Control 'TimingCsvReady').Current.Name -like "*$renamed*") { throw "The line for the export names the project as it is now ('$renamed'), not as it was when the check finished." }
    $script:timingExportDone.Add('names-the-project-as-it-was-after-a-rename')
    Select-Destination 'DestinationPlay'
    Wait-Settled 'QuickMeter'
    Open-Panel @('ProjectOptions') 'DocumentName'
    Wait-Settled 'DocumentName'
    Set-Text 'DocumentName' $project
    Invoke-Control 'ProjectOptionsPanelDone'
    Wait-Absent 'DocumentName'
    # 6. Another check ends with a result of its own (no press at all is a finished check too).
    Start-PlayingOnTrain
    Begin-Check 'A new check'
    Finish-Check 'The new check'
    Assert-Kept 'After the new check' $project
    $script:timingExportDone.Add('a-new-check-ends-with-a-result-of-its-own')
    # 6b. The project is renamed while it plays. A rename does not reach the audio, so the revision that plays still holds the name from
    # when playback last started; a check that begins after the rename goes by the name the person sees.
    $renamedWhilePlaying = 'Renamed while playing'
    Select-Destination 'DestinationPlay'
    Wait-Settled 'QuickMeter'
    Open-Panel @('ProjectOptions') 'DocumentName'
    Wait-Settled 'DocumentName'
    Set-Text 'DocumentName' $renamedWhilePlaying
    Invoke-Control 'ProjectOptionsPanelDone'
    Wait-Absent 'DocumentName'
    Select-Destination 'DestinationTrain'
    Wait-Settled 'TimingTolerance'
    Begin-Check 'A check after the project was renamed while it plays'
    Finish-Check 'The check after the project was renamed while it plays'
    $lineAfterRename = (Find-Control 'TimingCsvReady').Current.Name
    if ($lineAfterRename -notlike "*$renamedWhilePlaying*") { throw "The line for the export does not name the project as it is called now ('$renamedWhilePlaying'), only as it was when playback last started: '$lineAfterRename'." }
    $script:timingExportDone.Add('names-the-project-as-renamed-while-playing')
    Select-Destination 'DestinationPlay'
    Wait-Settled 'QuickMeter'
    Open-Panel @('ProjectOptions') 'DocumentName'
    Wait-Settled 'DocumentName'
    Set-Text 'DocumentName' $project
    Invoke-Control 'ProjectOptionsPanelDone'
    Wait-Absent 'DocumentName'
    # The next step goes on from the Train page, where the check before it was left.
    Select-Destination 'DestinationTrain'
    Wait-Settled 'TimingTolerance'
    # 7. A new check took the old result away when it began (nothing shows that while it runs), so a change of playback that ends it
    # leaves nothing to export: not the new result, which never came, and not the old one.
    Begin-Check 'A check that is about to be ended'
    Invoke-Control 'TransportStop'
    $null = Wait-Transport 'Stopped'
    Wait-Condition { -not (Get-ExportOn) } 'A check that was ended by Stop can be exported.'
    if ($null -ne (Find-Optional 'TimingCsvReady')) { throw 'A line says what is exported after the check was ended by Stop.' }
    $script:timingExportDone.Add('off-after-a-change-of-playback-ends-the-check')
    # 8. One more finished check is left for the Dutch checks.
    Start-PlayingOnTrain
    Begin-Check 'The check that is left behind'
    Finish-Check 'The check that is left behind'
    Invoke-Control 'TransportStop'
    $null = Wait-Transport 'Stopped'
    Assert-Kept 'The check that is left behind' $project
    $script:timingExportFinished = $true
    $script:timingExportProject = $project
    Assert-NoErrorNotice 'The export of a finished check'
}
# The export of the separate tracks (docs/WAV-STEMS.md) is the third command of the export card on the Library page, after Export WAV and
# Export MIDI, with a hint under the commands (docs/design/pages-design.md, Library). It is read at the sizes the layout gates use and in
# Dutch: the three commands whole and as large as a command, in the order they come, between the edges of their card and not on top of each
# other (a command that is squeezed or pushed over by a new neighbour is the defect this guards); the hint readable and under the commands.
# Then the command is pressed: it asks first, in words that give the number of tracks, how long each is and what they take unzipped, with a
# Continue and a Cancel that are whole and at least 100 x 32 DIP (the size of the buttons of the system's dialog, which every question of the
# app is); Cancel closes the question and leaves everything as it was.
# The native save dialog cannot be driven on a runner, so the run goes no further than the question (the file itself is read in the
# end-to-end run that the pull request describes). The numbers of the question are compared with what the project gives: the track count with
# the lanes the project has, and the size with the length and the track count (4 bytes a frame, 48,000 frames a second); the first call
# also with the exact numbers of the project that the run starts with. The size is written with a no-break space between the number and
# its unit (U+00A0: the check below refuses an ordinary space), so that a narrow dialog never ends a line between them.
$stemCommands = @('ExportWav', 'ExportMidi', 'ExportWavStems')
$stemCommandMinimum = @{ ExportWav = @(100, 40); ExportMidi = @(100, 40); ExportWavStems = @(150, 40) }
$stemQuestion = @{
    en = @{ tracks = 'Tracks: (?<tracks>\d+), each about (?<length>\d+(?::\d{2}){1,2}) long'; size = 'take about (?<size>[\d.,]+)(?<gap>[ \u00A0])(?<unit>KiB|MiB|GiB)'; thousands = ','; point = '.' }
    nl = @{ tracks = 'Sporen: (?<tracks>\d+), elk ongeveer (?<length>\d+(?::\d{2}){1,2}) lang'; size = 'nemen ze ongeveer (?<size>[\d.,]+)(?<gap>[ \u00A0])(?<unit>KiB|MiB|GiB) in'; thousands = '.'; point = ',' }
}
function Test-StemExport([string]$where, [string]$shot = '', [string]$language = 'en', [int]$tracks = 1, [string]$exactLength = '', [string]$exactSize = '') {
    $transportBefore = (Find-Control 'TransportStatus').Current.Name
    Select-Destination 'DestinationLibrary'
    Wait-Settled 'ExportBars'
    $null = Find-Control 'ExportWavStems'
    # A person scrolls to the export card: down to the hint, then up to the first command, so that the three commands and the hint are
    # on screen together.
    Show-Control 'StemExportHint'
    Show-Control 'ExportWav'
    foreach ($id in $stemCommands + 'StemExportHint') { Wait-Settled $id }
    $windowRect = (Get-CurrentWindow).Current.BoundingRectangle
    $one = 1 * $scale
    # The card keeps 21 DIP of padding at every size. What the card cuts off is not in the rectangle that UI Automation reports, so a
    # rectangle that reaches into the padding is one that was cut (or pushed to the edge).
    $padding = 10 * $scale
    $card = (Find-Control 'ExportCard').Current.BoundingRectangle
    $rects = @{}
    foreach ($id in $stemCommands) {
        $rects[$id] = (Find-Control $id).Current.BoundingRectangle
        Test-Visible $rects[$id] $windowRect "${where}: $id"
        $least = $stemCommandMinimum[$id]
        if ($rects[$id].Width -lt $least[0] * $scale -or $rects[$id].Height -lt $least[1] * $scale) {
            throw "${where}: $id is squeezed to $([Math]::Round($rects[$id].Width / $scale, 1)) x $([Math]::Round($rects[$id].Height / $scale, 1)) DIP (a command of the export card is at least $($least[0]) x $($least[1]) DIP): $($rects[$id])."
        }
        if ($rects[$id].Left -lt $card.Left + $padding -or $rects[$id].Right -gt $card.Right - $padding) {
            throw "${where}: $id is clipped sideways or pushed into the edge of its card ($($rects[$id]) in the card $card)."
        }
    }
    # No two commands overlap: each is a whole command of its own, on one row or in a row under the one before.
    for ($first = 0; $first -lt $stemCommands.Count; $first++) {
        for ($second = $first + 1; $second -lt $stemCommands.Count; $second++) {
            $a = $rects[$stemCommands[$first]]; $b = $rects[$stemCommands[$second]]
            $a.Inflate(-$one, -$one); $b.Inflate(-$one, -$one)
            if ($a.IntersectsWith($b)) { throw "${where}: $($stemCommands[$first]) and $($stemCommands[$second]) overlap ($($rects[$stemCommands[$first]]) and $($rects[$stemCommands[$second]]))." }
        }
    }
    # The commands come in the order Export WAV, Export MIDI, separate tracks. Each is after the one before it: to its right on the same
    # row or on a row under it.
    for ($index = 1; $index -lt $stemCommands.Count; $index++) {
        $before = $rects[$stemCommands[$index - 1]]; $after = $rects[$stemCommands[$index]]
        $onOneRow = [Math]::Abs(($after.Top + $after.Height / 2) - ($before.Top + $before.Height / 2)) -le 4 * $scale
        if (($onOneRow -and $after.Left -lt $before.Right - $one) -or (-not $onOneRow -and $after.Top -lt $before.Bottom - $one)) {
            throw "${where}: $($stemCommands[$index]) does not come after $($stemCommands[$index - 1]) ($after, $before)."
        }
    }
    $midi = $rects['ExportMidi']; $stems = $rects['ExportWavStems']
    $layout = if ([Math]::Abs(($stems.Top + $stems.Height / 2) - ($midi.Top + $midi.Height / 2)) -le 4 * $scale) { 'after-midi-on-its-row' } else { 'under-midi' }
    # The hint is readable, between the edges of the card and under every command, and it says what the command saves.
    $hintElement = Find-Control 'StemExportHint'
    $hint = $hintElement.Current.BoundingRectangle
    Test-Visible $hint $windowRect "${where}: the hint under the commands"
    if ($hintElement.Current.Name.Length -lt 80 -or $hintElement.Current.Name -notmatch 'ZIP') { throw "${where}: the hint under the commands does not say what it saves: '$($hintElement.Current.Name)'." }
    if ($hint.Width -lt 150 * $scale -or $hint.Height -lt 14 * $scale) {
        throw "${where}: the hint is squeezed to $([Math]::Round($hint.Width / $scale, 1)) x $([Math]::Round($hint.Height / $scale, 1)) DIP (a line of it is at least 150 DIP wide and 14 high): $hint."
    }
    if ($hint.Left -lt $card.Left + $padding -or $hint.Right -gt $card.Right - $padding) { throw "${where}: the hint is clipped sideways or pushed into the edge of its card ($hint in the card $card)." }
    foreach ($id in $stemCommands) {
        if ($hint.Top -lt $rects[$id].Bottom - $one) { throw "${where}: the hint is not under $id ($hint, $id $($rects[$id]))." }
    }
    Test-NoApplyButtons "${where}: Library"
    Assert-NoErrorNotice $where
    if ($shot) { $null = Save-PlayScreenshot "$shot.png" }
    # Pressing the command asks first. The question gives the number of tracks, how long each is and what they take unzipped.
    if (-not (Find-Control 'ExportWavStems').Current.IsEnabled) { throw "${where}: the command is off although no export of tracks is going." }
    # A person who pressed the command with the keyboard had the focus on it. The command goes off at the press, so the system moves the
    # focus on to the next control (the first ready-made rhythm) and the question gives it back to that control when it closes: the
    # command has to take the focus back once it is on again, or a keyboard user has lost their place on the page (CommandFocus). The check
    # puts the focus on the command first, as the person's Tab would have.
    (Find-Control 'ExportWavStems').SetFocus()
    Wait-Condition { (Find-Control 'ExportWavStems').Current.HasKeyboardFocus } "${where}: the command did not take the keyboard focus, so where the focus goes when the question closes cannot be seen."
    $null = Invoke-Control 'ExportWavStems'
    Wait-Condition { $null -ne (Find-DialogButton 'PrimaryButton') } "${where}: the command did not ask for confirmation."
    $messageElement = Find-Control 'ConfirmationMessage'
    # The question slides and fades in: its rectangles are read once it has stopped moving. The command is off while it is open, because an
    # export of tracks counts as running from the press to the end (a second press would be ignored, so a command that looks usable misleads).
    Wait-Settled 'ConfirmationMessage'
    if ((Find-Control 'ExportWavStems').Current.IsEnabled) { throw "${where}: the command is on while the question is open (it is off from the press to the end of an export)." }
    $question = $messageElement.Current.Name
    $words = $stemQuestion[$language]
    if ($question -notmatch $words.tracks) { throw "${where}: the question does not give the number of tracks and how long each is: '$question'." }
    $trackCount = [int]$Matches['tracks']; $lengthText = $Matches['length']
    if ($question -notmatch $words.size) { throw "${where}: the question does not give what the tracks take unzipped: '$question'." }
    $sizeText = $Matches['size']
    $sizeGap = $Matches['gap']
    $sizeUnit = $Matches['unit']
    # Compared by code point: PowerShell's -ne treats a no-break space and an ordinary space as equal (culture-aware comparison).
    if ($sizeGap.Length -ne 1 -or [int][char]$sizeGap -ne 0x00A0) { throw "${where}: the size in the question is separated from its unit by a space that can break a line between them: '$question'." }
    $sizeNumber = [double]::Parse($sizeText.Replace($words.thousands, '').Replace($words.point, '.'), [Globalization.CultureInfo]::InvariantCulture)
    if ($trackCount -ne $tracks) { throw "${where}: the question says $trackCount tracks, but the project has $($tracks - 1) lanes and so $tracks tracks: '$question'." }
    $lengthParts = @($lengthText -split ':' | ForEach-Object { [int]$_ })
    $lengthSeconds = if ($lengthParts.Count -eq 3) { $lengthParts[0] * 3600 + $lengthParts[1] * 60 + $lengthParts[2] } else { $lengthParts[0] * 60 + $lengthParts[1] }
    # The size is the tracks, each as long as the length (rounded up to the next second), at 4 bytes a frame and 48,000 frames a second.
    # It is written to one decimal (KiB without), so the range it stands for is compared with the range the length allows.
    $unitBytes = switch ($sizeUnit) { 'KiB' { 1024.0 } 'MiB' { 1048576.0 } default { 1073741824.0 } }
    $half = if ($sizeUnit -eq 'KiB') { 0.5 } else { 0.05 }
    $shownLow = ($sizeNumber - $half) * $unitBytes; $shownHigh = ($sizeNumber + $half) * $unitBytes
    $allowedLow = $tracks * 192000.0 * ($lengthSeconds - 1); $allowedHigh = $tracks * 192000.0 * $lengthSeconds
    if ($shownHigh -lt $allowedLow -or $shownLow -gt $allowedHigh) {
        throw "${where}: the size in the question ($sizeNumber $sizeUnit) does not fit $tracks tracks of $lengthText (between $([Math]::Round($allowedLow / 1MB, 2)) and $([Math]::Round($allowedHigh / 1MB, 2)) MiB): '$question'."
    }
    if ($exactLength -and $lengthText -ne $exactLength) { throw "${where}: the question says each track is about $lengthText long, not $exactLength as the project that the run starts with gives: '$question'." }
    if ($exactSize -and "$sizeText $sizeUnit" -ne $exactSize) { throw "${where}: the question says the tracks take about $sizeText $sizeUnit, not $exactSize as the project that the run starts with gives: '$question'." }
    # The question is whole, and so are its two buttons: at least 100 DIP wide and as high as a button of the system's dialog, which is 32 DIP
    # (measured here, 231 x 32 at the first-use size; every question of the app is that dialog, so 32 is what a whole button is).
    $questionRect = $messageElement.Current.BoundingRectangle
    Test-Visible $questionRect $windowRect "${where}: the question"
    if ($questionRect.Width -lt 150 * $scale -or $questionRect.Height -lt 14 * $scale) { throw "${where}: the question is squeezed to $questionRect." }
    $buttons = @{}
    foreach ($button in @(@('PrimaryButton', 'Continue'), @('CloseButton', 'Cancel'))) {
        $element = Find-DialogButton $button[0]
        if ($null -eq $element) { throw "${where}: the question has no $($button[1]) button." }
        $rect = $element.Current.BoundingRectangle
        Test-Visible $rect $windowRect "${where}: the $($button[1]) button of the question"
        if ($rect.Width -lt 100 * $scale -or $rect.Height -lt 32 * $scale) {
            throw "${where}: the $($button[1]) button of the question is squeezed to $([Math]::Round($rect.Width / $scale, 1)) x $([Math]::Round($rect.Height / $scale, 1)) DIP (at least 100 x 32 DIP): $rect."
        }
        $buttons[$button[1]] = "$([Math]::Round($rect.Width / $scale, 1)) x $([Math]::Round($rect.Height / $scale, 1))"
    }
    if ($shot) { $null = Save-PlayScreenshot "$shot-question.png" }
    # Cancel closes the question. What a runner can see of "nothing happened": the question is gone, no error appears, the command is on
    # again and the status line reads as before (no file can be checked, because nothing here drives the save dialog).
    Invoke-DialogButton 'CloseButton' "${where}: the question went away before Cancel could be pressed."
    Wait-Condition { $null -eq (Find-Optional 'ConfirmationDialog') } "${where}: Cancel did not close the question."
    Wait-Condition { (Find-Control 'ExportWavStems').Current.IsEnabled } "${where}: the command stayed off after Cancel."
    Wait-Condition { (Find-Control 'ExportWavStems').Current.HasKeyboardFocus } "${where}: the keyboard focus is not on the command after Cancel (it was on it when the command was pressed): a keyboard user has lost their place on the page."
    Start-Sleep -Milliseconds 500
    Assert-NoErrorNotice "${where} after Cancel"
    if ((Find-Control 'TransportStatus').Current.Name -ne $transportBefore) {
        throw "${where}: reading the export of tracks changed the transport ('$transportBefore' then '$((Find-Control 'TransportStatus').Current.Name)')."
    }
    return @{
        layout = $layout; tracks = $trackCount; length = $lengthText; size = "$sizeText $sizeUnit"
        wav = "$([Math]::Round($rects['ExportWav'].Width / $scale, 1)) x $([Math]::Round($rects['ExportWav'].Height / $scale, 1))"
        midi = "$([Math]::Round($midi.Width / $scale, 1)) x $([Math]::Round($midi.Height / $scale, 1))"
        stems = "$([Math]::Round($stems.Width / $scale, 1)) x $([Math]::Round($stems.Height / $scale, 1))"
        hint = "$([Math]::Round($hint.Width / $scale, 1)) x $([Math]::Round($hint.Height / $scale, 1))"
        question = "$([Math]::Round($questionRect.Width / $scale, 1)) x $([Math]::Round($questionRect.Height / $scale, 1))"
        buttons = $buttons
    }
}
$panels = @(
    @{open=@('QuickMeter');                 field='MeterPreset4_4';    close='MeterPreset4_4'; focus='MeterPreset4_4'},
    @{open=@('QuickMeter', 'MeterCustom');  field='MeterBeats';        close='RhythmOptionsCancel'; focus='RhythmOptionsCancel'
      limits=@{MeterBeats=200; MeterDenominator=330; MeterGroups=300; Subdivision=200; Swing=200; CountInBars=200; AudibleBars=200; SilentBars=200}},
    @{open=@('TempoEdit');                  field='TempoUnit';         close='TempoOptionsCancel'; focus='TempoOptionsCancel'
      limits=@{Tempo=260; TempoUnit=330; TapInCount=200}},
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
# Help is read in the sizes that the layout gates use (the first-use size, 800x600, 1000x720) and the two narrowest windows: whole and
# readable in each, with Done on screen, and opening it never changes what Play shows.
$helpEvidence = @{}
$helpEvidence['firstUse'] = Test-HelpPanel 'Help at the first-use size' 'help-first-use'
foreach ($size in @(@(800, 600), @(1000, 720), @(400, 720), @(320, 900))) {
    Resize-To $size[0] $size[1]
    Start-Sleep -Milliseconds 800
    $helpEvidence["$($size[0])x$($size[1])"] = Test-HelpPanel "Help at $($size[0])x$($size[1])" "help-$($size[0])x$($size[1])" ($size[0] -ne 1000)
}
$window.GetCurrentPattern([Windows.Automation.TransformPattern]::Pattern).Resize($bounds.Width, $bounds.Height)
Start-Sleep -Milliseconds 800
# The tempo guide is read in the same windows: the first-use size, 800x600, 1000x720 and the two narrowest. At the first-use size the
# panel is also tried: the guide follows the number and the note value typed in it, names the markings of an emptied box (what to
# enter), and Cancel leaves everything as it was.
$tempoGuideEvidence = @{}
$tempoGuideEvidence['firstUse'] = Test-TempoGuide 'Tempo guide at the first-use size' 'tempo-first-use' -Interact
foreach ($size in @(@(800, 600), @(1000, 720), @(400, 720), @(320, 900))) {
    Resize-To $size[0] $size[1]
    Start-Sleep -Milliseconds 800
    # At 800x600 the panel is only 536 DIP tall and the box for the number of taps is partly out of view with or without the guide
    # (the panel scrolls), so that one size does not ask for it whole.
    $tempoGuideEvidence["$($size[0])x$($size[1])"] = Test-TempoGuide "Tempo guide at $($size[0])x$($size[1])" "tempo-$($size[0])x$($size[1])" -tapsWhole ($size[0] -ne 800)
}
$window.GetCurrentPattern([Windows.Automation.TransformPattern]::Pattern).Resize($bounds.Width, $bounds.Height)
Start-Sleep -Milliseconds 800
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
# Picking 6/8 counts eighth notes, so 120 BPM is 60 quarter notes a minute and not the 120 that the same number means in 4/4; counted
# in dotted quarters (6/8 felt in two) it is 180. The guide names the speed, not the number.
$tempoGuideEvidence['sixEightEighths'] = Wait-TempoGuide @('Largo', 'Larghetto') '60' 'Tempo guide in 6/8 counted in eighth notes'
Select-Combo 'TempoUnit' 'Dotted quarter'
$tempoGuideEvidence['sixEightDottedQuarters'] = Wait-TempoGuide @('Presto') '180' 'Tempo guide in 6/8 counted in dotted quarters'
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
# The dial changes a moment before the page is rebuilt, and the strip has six cells before and after, so neither the dial nor the
# number of cells says the strip is the new one: its names are waited for, and the failure says what they were.
try {
    Wait-Until { (Find-Control 'MainAccent3').Current.Name -eq 'Beat 4, Strong' -and (Find-Control 'MainAccent1').Current.Name -eq 'Beat 2, Medium' } 'The strip was not rebuilt.'
} catch {
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
function Show-Control([string]$id) {
    # Brings a control that is below the first screen into view, the way a person scrolls to it.
    try { (Find-Control $id).GetCurrentPattern([Windows.Automation.ScrollItemPattern]::Pattern).ScrollIntoView() } catch { }
    Start-Sleep -Milliseconds 400
}
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
# The export of the separate tracks, read with the project that the run started with: the Studio checks below add the lanes, so there is no
# lane yet, one track of the default length (16 bars in 4/4 at 120 BPM and a count-in of one bar are 17 bars of 2 s = 34 s = 1,632,000
# frames of 4 bytes = 6,528,000 bytes = 6.2 MiB). A project that an earlier check left changed would give other numbers, and the message
# of the check says so.
$stemExportEvidence = @{}
$stemExportEvidence['firstUse'] = Test-StemExport 'Stem export at the first-use size' 'stems-first-use' 'en' 1 '0:34' '6.2 MiB'
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
# Tap to start puts its state where a person (and a screen reader) can read it: the switch is a toggle, and TAP carries
# "idle", "armed:<taps counted>:<taps asked for>:<1 if the last tap was uneven>" or "preparing" as its status.
function Get-TapStatus { (Find-Control 'TapTempo').Current.ItemStatus }
function Get-TapStartState { (Find-Control 'TapStart').GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Current.ToggleState }
function Wait-TapStartState([bool]$on, [string]$failure) {
    $wanted = if ($on) { [Windows.Automation.ToggleState]::On } else { [Windows.Automation.ToggleState]::Off }
    Wait-Condition { (Get-TapStartState) -eq $wanted } "$failure (the switch is $(Get-TapStartState); TAP reports '$(Get-TapStatus)')" 10
}
function Switch-TapStart([bool]$on, [string]$failure) {
    $pattern = (Find-Control 'TapStart').GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern)
    $wanted = if ($on) { [Windows.Automation.ToggleState]::On } else { [Windows.Automation.ToggleState]::Off }
    if ($pattern.Current.ToggleState -ne $wanted) { $pattern.Toggle() }
    Wait-TapStartState $on $failure
}
function Wait-TapStatus([string]$expected, [string]$what, [int]$seconds = 10) {
    $limit = [DateTime]::UtcNow.AddSeconds($seconds)
    do {
        if ((Get-TapStatus) -like $expected) { return }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $limit)
    throw "$what (TAP reports '$(Get-TapStatus)', expected '$expected'; the switch is $(Get-TapStartState); $((Find-Control 'TransportStatus').Current.Name))"
}
function Get-DialNumber { [double]::Parse(((Get-Dial) -split ' ')[0], [Globalization.CultureInfo]::InvariantCulture) }
function Set-TapCount([int]$count) {
    Open-Panel @('TempoEdit') 'TapInCount'
    Wait-Settled 'TapInCount'
    Set-Number 'TapInCount' $count
    Invoke-Control 'TempoOptionsDone'
    Wait-Absent 'TempoOptionsDone'
}
# The wiring of Tap to start with the real transport, on a machine that has an output (the local gate; a hosted runner has none). The
# estimator and the session are held by unit tests with exact timings; here taps are made through UI Automation, whose timing is
# not exact, so nothing below depends on how evenly they are spaced except the one check that reads the interval from the dial first.
function Test-TapStartWithOutput {
    $script:tapStartDone = [System.Collections.Generic.List[string]]::new()
    if (-not (Find-Control 'TapStart').Current.IsEnabled) { throw 'Tap to start is off although an output is selected and nothing plays.' }
    if ((Get-TapStartState) -ne [Windows.Automation.ToggleState]::Off -or (Get-TapStatus) -ne 'idle') {
        throw "Tap to start is not idle at the start of the checks: $(Get-TapStatus)"
    }
    if ((Find-Control 'TransportStop').Current.IsEnabled) { throw 'Stop is available while nothing plays and nothing listens.' }
    # Switching it on listens, counts nothing yet and makes Stop available (it cancels); switching it off ends it.
    Switch-TapStart $true 'Tap to start did not switch on.'
    Wait-TapStatus 'armed:0:4:0' 'Tap to start did not listen for four taps.'
    if (-not (Find-Control 'TransportStop').Current.IsEnabled) { throw 'Stop is not available while Tap to start listens.' }
    if (-not (Find-Control 'TapTempo').Current.IsEnabled) { throw 'TAP is off while Tap to start listens.' }
    Switch-TapStart $false 'Tap to start did not switch off.'
    Wait-TapStatus 'idle' 'Switching Tap to start off left TAP counting.'
    $script:tapStartDone.Add('switch-on-and-off')
    # Stop cancels it.
    Switch-TapStart $true 'Tap to start did not switch on again.'
    Wait-TapStatus 'armed:0:4:0' 'Tap to start did not listen again.'
    Invoke-Control 'TransportStop'
    Wait-TapStartState $false 'Stop did not cancel Tap to start.'
    Wait-TapStatus 'idle' 'Stop left TAP counting.'
    $script:tapStartDone.Add('stop-cancels')
    # So does starting by hand, and while the metronome plays the switch is off (a tap-in cannot start what already plays).
    Switch-TapStart $true 'Tap to start did not switch on for the manual start.'
    Wait-TapStatus 'armed:0:4:0' 'Tap to start did not listen before the manual start.'
    Invoke-Control 'TransportStartPause'
    $null = Wait-Transport 'Playing'
    Wait-TapStartState $false 'Starting by hand did not end Tap to start.'
    Wait-TapStatus 'idle' 'Starting by hand left TAP counting.'
    if ((Find-Control 'TapStart').Current.IsEnabled) { throw 'Tap to start can be switched on while the metronome plays.' }
    Invoke-Control 'TransportStop'
    $null = Wait-Transport 'Stopped'
    $script:tapStartDone.Add('manual-start-cancels-and-the-switch-is-off-while-playing')
    # Leaving the page ends it: the page that is built again has no intent.
    Switch-TapStart $true 'Tap to start did not switch on before leaving Play.'
    Select-Destination 'DestinationStudio'
    Wait-Settled 'AddLane'
    Select-Destination 'DestinationPlay'
    Wait-Settled 'QuickMeter'
    Wait-TapStartState $false 'Leaving Play did not end Tap to start.'
    Wait-TapStatus 'idle' 'Leaving Play left TAP counting.'
    $script:tapStartDone.Add('leaving-the-page-cancels')
    # A tempo chosen by hand ends it too: the tempo its taps would save is not the one just asked for.
    Switch-TapStart $true 'Tap to start did not switch on before a tempo was chosen.'
    Invoke-Control 'TempoUp'
    Wait-TapStartState $false 'Choosing a tempo by hand did not end Tap to start.'
    Wait-Until { (Get-Dial) -like '121*BPM*' } 'Tempo + did not change the tempo after it ended Tap to start.'
    Invoke-Control 'TempoDown'
    Wait-Until { (Get-Dial) -like '120*BPM*' } 'The tempo did not return to 120.'
    $script:tapStartDone.Add('tempo-by-hand-cancels')
    # An uneven tap starts the count again and the dial goes back to the project's tempo (the estimate it showed is not what a start
    # would use). The taps are made through one button that is found once, and nothing is read in between: finding a control and
    # pressing a button each take as long as UI Automation takes on this desktop (a press took 2.0 seconds on the private desktop of
    # the local gate), and the taps are as late as the presses are. So the pauses follow what was measured: the second tap comes
    # as soon as a beat of 0.3 seconds allows, and the third about 1.3 beats plus 0.7 seconds after it, which is more than 30 % off
    # the beat (so not steady) and still inside the four seconds after which Tap to start begins a new count, whatever the beat was
    # (3.3 seconds for the 2.0-second beat of a slow desktop; a longer wait would be an idle gap, which starts a new count instead).
    # A run whose timing cannot tell the two apart is reported as not run, not as passed.
    Switch-TapStart $true 'Tap to start did not switch on for the uneven tap.'
    Wait-TapStatus 'armed:0:4:0' 'Tap to start did not listen for the uneven tap.'
    $tapButton = (Find-Control 'TapTempo').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $tapButton.Invoke(); $firstTap = $clock.Elapsed.TotalSeconds
    $pause = [Math]::Max(0, 0.3 - $firstTap)
    Start-Sleep -Milliseconds ([int]($pause * 1000))
    $tapButton.Invoke(); $secondTap = $clock.Elapsed.TotalSeconds
    $beat = $secondTap - $firstTap
    $pressTime = [Math]::Max(0, $beat - $pause)
    Start-Sleep -Milliseconds ([int]([Math]::Max(0, 1.3 * $beat + 0.7 - $pressTime) * 1000))
    $tapButton.Invoke(); $thirdTap = $clock.Elapsed.TotalSeconds
    $late = $thirdTap - $secondTap
    if ($beat -ge 0.08 -and $late -gt 1.3 * $beat + 0.25 -and $late -lt 4 - 0.4) {
        Wait-TapStatus 'armed:0:4:1' "An uneven tap did not start the count again (the taps were $([Math]::Round($beat, 2)) and $([Math]::Round($late, 2)) seconds apart)."
        Wait-Until { (Get-Dial) -like '120*BPM*' } 'The dial kept an estimate after an uneven tap.'
        $script:tapStartDone.Add("uneven-tap-restarts-the-count-taps-$([Math]::Round($beat, 2))-and-$([Math]::Round($late, 2))-seconds-apart")
    } else {
        $script:tapStartDone.Add("uneven-tap-not-run-the-taps-were-$([Math]::Round($beat, 2))-and-$([Math]::Round($late, 2))-seconds-apart")
    }
    Invoke-Control 'TransportStop'
    Wait-TapStartState $false 'Stop did not cancel the count after an uneven tap.'
    # The start: two taps are the fewest that make a tempo, and the first interval is always taken, so this does not depend on how
    # evenly the taps are spaced, only on their being less than four seconds apart (hence the one button found beforehand). The
    # tempo that starts is the tempo the project keeps.
    Set-TapCount 2
    try {
        Switch-TapStart $true 'Tap to start did not switch on for the start.'
        Wait-TapStatus 'armed:0:2:0' 'The number of taps that was chosen is not the one asked for.'
        $tapButton = (Find-Control 'TapTempo').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)
        $tapButton.Invoke()
        Start-Sleep -Milliseconds 300
        $tapButton.Invoke()
        $null = Wait-Transport 'Playing'
        Wait-TapStartState $false 'Tap to start stayed on after it started the metronome.'
        Wait-TapStatus 'idle' 'TAP kept counting after the start.'
        $playingStatus = (Find-Control 'TransportStatus').Current.ItemStatus
        if ($playingStatus -match ';tempo=([0-9.]+);') { $played = [double]::Parse($Matches[1], [Globalization.CultureInfo]::InvariantCulture) }
        else { throw "The playing revision reports no tempo: $playingStatus" }
        if ($played -lt 15 -or $played -gt 1000) { throw "Tap to start played an impossible tempo: $played" }
        if ([Math]::Abs((Get-DialNumber) - $played) -gt 0.05) { throw "The dial shows $(Get-Dial), but the audio plays at $played." }
        Invoke-Control 'TransportStop'
        $null = Wait-Transport 'Stopped'
        Wait-Until { [Math]::Abs((Get-DialNumber) - $played) -le 0.05 } "The tempo that started ($played) is not the tempo the project kept ($(Get-Dial))."
        $script:tapStartDone.Add("two-taps-start-the-metronome-at-the-tapped-tempo-$played")
        # The Tempo panel can be open when the last tap saves its tempo: a person who finds four taps too many changes the number
        # there and taps. Done writes only what was changed in the panel, so the tempo that the panel showed when it opened must
        # not be put back over the tapped one, on the dial, in the audio and in the project.
        Open-Panel @('TempoEdit') 'Tempo'
        Wait-Settled 'Tempo'
        Set-Number 'Tempo' 120
        Invoke-Control 'TempoOptionsDone'
        Wait-Absent 'TempoOptionsDone'
        Wait-Until { (Get-Dial) -like '120*BPM*' } 'The tempo did not return to 120 before the check of the Tempo panel.'
        Switch-TapStart $true 'Tap to start did not switch on for the check of the Tempo panel.'
        Wait-TapStatus 'armed:0:2:0' 'Tap to start did not listen for two taps before the Tempo panel was opened.'
        Open-TapCount
        Set-Number 'TapInCount' 3
        $tapButton = (Find-Control 'TapTempo').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern)
        $tapButton.Invoke()
        # At least 0.7 seconds between the taps is at most 85 BPM, however quickly the host answers (a press took 2.0 seconds on the
        # private desktop of the local gate and takes a few hundredths on a desktop that answers at once): the tapped tempo is then
        # never near the 120 that the panel showed when it opened, so a tempo put back from the panel can be told from it.
        Start-Sleep -Milliseconds 700
        $tapButton.Invoke()
        $null = Wait-Transport 'Playing'
        Wait-TapStartState $false 'Tap to start stayed on after it started the metronome behind the Tempo panel.'
        $tappedTempo = Get-DialNumber
        if ($tappedTempo -lt 15 -or $tappedTempo -gt 100) { throw "Two taps behind the Tempo panel did not set a tempo of their own: $(Get-Dial)" }
        Invoke-Control 'TempoOptionsDone'
        Wait-Absent 'TempoOptionsDone'
        Open-TapCount
        Wait-Until { (Get-TapCount) -eq 3 } 'Done did not keep the number of taps that was changed behind Tap to start.'
        Invoke-Control 'TempoOptionsCancel'
        Wait-Absent 'TempoOptionsCancel'
        if ([Math]::Abs((Get-DialNumber) - $tappedTempo) -gt 0.05) {
            throw "Done in the Tempo panel put the tempo it showed when it opened over the tapped one: the dial shows $(Get-Dial), the taps made $tappedTempo."
        }
        $playingStatus = (Find-Control 'TransportStatus').Current.ItemStatus
        if ($playingStatus -match ';tempo=([0-9.]+);') { $playedBehindPanel = [double]::Parse($Matches[1], [Globalization.CultureInfo]::InvariantCulture) }
        else { throw "The playing revision reports no tempo after Done in the Tempo panel: $playingStatus" }
        if ([Math]::Abs($playedBehindPanel - $tappedTempo) -gt 0.05) {
            throw "The metronome plays $playedBehindPanel, not the tempo that was tapped behind the Tempo panel ($tappedTempo)."
        }
        Invoke-Control 'TransportStop'
        $null = Wait-Transport 'Stopped'
        Wait-Until { [Math]::Abs((Get-DialNumber) - $tappedTempo) -le 0.05 } "The project did not keep the tempo that was tapped behind the Tempo panel ($tappedTempo): $(Get-Dial)."
        $script:tapStartDone.Add("a-number-of-taps-changed-behind-a-saved-tap-in-leaves-the-tapped-tempo-$tappedTempo")
    } finally {
        if ((Get-TapStartState) -eq [Windows.Automation.ToggleState]::On) { Switch-TapStart $false 'Tap to start could not be switched off.' }
        if ((Find-Control 'TransportStatus').Current.Name -notlike 'Stopped*') {
            Invoke-Control 'TransportStop'
            $null = Wait-Transport 'Stopped'
        }
        Open-Panel @('TempoEdit') 'Tempo'
        Wait-Settled 'Tempo'
        Set-Number 'Tempo' 120
        Invoke-Control 'TempoOptionsDone'
        Wait-Absent 'TempoOptionsDone'
        Wait-Until { (Get-Dial) -like '120*BPM*' } 'The tempo did not return to 120 after the tap-in checks.'
        Set-TapCount 4
    }
    Assert-NoErrorNotice 'Tap to start'
}
# The tempo that is typed in the Tempo panel is the last word. A nudge (the steppers, the wheel, TAP) is committed by a loop that takes one
# value at a time and goes on to the newest, and a Done pressed while that loop was still committing queued its own commit between two of
# the loop's, so the loop then committed its older value over the typed one: the project, the dial and the audio ended on a nudge, not on
# what was typed (the 'Done did not apply the typed tempo' of a runner on which a commit is slow). How slow a commit is depends on the
# machine, so this check does not wait for a slow one: a build with the UI-test hooks (-p:UiTest=true, started by Verify-Local.ps1
# -UiTestHooks) holds the edit queue, the nudges and Done are made, and only then is the queue let go, so the commits run in the order they
# were asked for however slow UI Automation is. A shipped build has no hooks and a hosted runner no output, and the report then says that
# this was not run (a caller that asked for the hooks, -RequireUiTestHooks, gets a failure instead). The first round plays, because the
# audio is part of what must end on the typed tempo; the second is made with the transport stopped, with a changed note value, which Done
# commits as one change with the tempo (so the dial and the project, not the audio, are what it checks).
function Test-TempoTypedAfterNudges {
    $script:tempoNudgeDone = [System.Collections.Generic.List[string]]::new()
    if ($RequireUiTestHooks) {
        # Asked for: the hooks have to be there, and are waited for like any control, not reported as not run.
        $null = Find-Control 'UiTestHoldEdits'
        $null = Find-Control 'UiTestReleaseEdits'
    } elseif ($null -eq (Find-Optional 'UiTestHoldEdits') -or $null -eq (Find-Optional 'UiTestReleaseEdits')) {
        $script:tempoNudgeDone.Add('not-run-no-ui-test-hooks')
        return
    }
    # The tempo of the status is the one the audio plays while the transport plays and the one the project holds while it is stopped.
    function Get-StatusTempo {
        $status = (Find-Control 'TransportStatus').Current.ItemStatus
        if ($status -notmatch ';tempo=([0-9.]+);') { throw "The transport reports no tempo: $status" }
        [double]::Parse($Matches[1], [Globalization.CultureInfo]::InvariantCulture)
    }
    function Get-Shown([string]$source) { "the dial shows $(Get-Dial), $source $(Get-StatusTempo)" }
    function Assert-LastWord([string]$what, [string]$dialLike, [double]$tempo, [string]$source) {
        try { Wait-Until { (Get-Dial) -like $dialLike -and (Get-StatusTempo) -eq $tempo } 'the typed tempo is not where it should be' 30 }
        catch { throw "${what}: the typed tempo ($tempo) is not where the dial and the status end: $(Get-Shown $source)." }
        # Nothing that was asked for before Done may land after it, so the state is watched for a while: a later commit would show as a change.
        $until = [DateTime]::UtcNow.AddSeconds(4)
        do {
            if ((Get-Dial) -notlike $dialLike -or (Get-StatusTempo) -ne $tempo) {
                throw "${what}: a nudge that was still on its way landed after the typed tempo ($tempo): $(Get-Shown $source)."
            }
            Start-Sleep -Milliseconds 250
        } while ([DateTime]::UtcNow -lt $until)
    }
    # Three nudges, then the panel is opened and Done pressed, all while nothing is applied; then the queue is let go.
    function Test-OneRound([string]$what, [string]$dialAfterNudges, [double]$typed, [string]$unitName) {
        Invoke-Control 'UiTestHoldEdits'
        $script:tempoEditsHeld = $true
        foreach ($nudge in 1..3) { Invoke-Control 'TempoUp' }
        Wait-Until { (Get-Dial) -like $dialAfterNudges } "${what}: Tempo + did not show the new tempo at once while the edit queue was held."
        $before = Get-StatusTempo
        Open-Panel @('TempoEdit') 'Tempo'
        Wait-Settled 'Tempo'
        Set-Number 'Tempo' $typed
        if ($unitName) { Select-Combo 'TempoUnit' $unitName }
        Invoke-Control 'TempoOptionsDone'
        if ((Get-StatusTempo) -ne $before) { throw "${what}: something was applied although the edit queue was held ($(Get-Shown 'the status says')), so the check cannot tell what it means to." }
        Invoke-Control 'UiTestReleaseEdits'
        $script:tempoEditsHeld = $false
        Wait-Condition { $null -eq (Find-Optional 'TempoOptionsDone') } "${what}: Done did not close the Tempo panel once the edit queue was let go." 60
    }
    $script:tempoEditsHeld = $false
    try {
        Select-Destination 'DestinationPlay'
        Wait-Settled 'QuickMeter'
        if ((Find-Control 'TransportStatus').Current.Name -notlike 'Stopped*') { throw 'Tempo typed after nudges: the transport is not stopped when the check begins.' }
        if ((Get-Dial) -notlike '120*BPM*Quarter*') { throw "Tempo typed after nudges: the check begins from 120 BPM in quarter notes, not from $(Get-Dial)." }
        # With the audio: after Done and the queue let go, the dial, the audio and (after Stop) the project all hold the typed 90.
        Invoke-Control 'TransportStartPause'
        $null = Wait-Transport 'Playing' $true
        if ((Get-StatusTempo) -ne 120) { throw "Tempo typed after nudges: the audio does not begin at 120 BPM ($(Get-Shown 'the audio plays'))." }
        Test-OneRound 'Tempo typed after nudges (playing)' '123*BPM*' 90 ''
        Assert-LastWord 'Tempo typed after nudges (playing)' '90*BPM*' 90 'the audio plays'
        Invoke-Control 'TransportStop'
        $null = Wait-Transport 'Stopped'
        Assert-LastWord 'Tempo typed after nudges (after Stop)' '90*BPM*' 90 'the project holds'
        $script:tempoNudgeDone.Add('typed-90-after-three-held-nudges-on-the-dial-in-the-audio-and-in-the-project')
        # With a changed note value, which the project takes as one change with the tempo (and a page that is built again).
        Test-OneRound 'Tempo typed after nudges (note value)' '93*BPM*Quarter*' 60 'Eighth note'
        Wait-Settled 'QuickMeter'
        Assert-LastWord 'Tempo typed after nudges (note value)' '60*BPM*Eighth*' 60 'the project holds'
        $script:tempoNudgeDone.Add('typed-60-in-eighth-notes-after-three-held-nudges')
        Open-Panel @('TempoEdit') 'Tempo'
        Wait-Settled 'Tempo'
        Set-Number 'Tempo' 120
        Select-Combo 'TempoUnit' 'Quarter note'
        Invoke-Control 'TempoOptionsDone'
        Wait-Absent 'TempoOptionsDone'
        Wait-Until { (Get-Dial) -like '120*BPM*Quarter*' } 'The tempo and the note value did not return after the checks of a tempo typed after nudges.'
        Assert-NoErrorNotice 'Tempo typed after nudges'
    } finally {
        if ($script:tempoEditsHeld) {
            $script:tempoEditsHeld = $false
            Invoke-Control 'UiTestReleaseEdits'
        }
        if ((Find-Control 'TransportStatus').Current.Name -notlike 'Stopped*') {
            Invoke-Control 'TransportStop'
            $null = Wait-Transport 'Stopped'
        }
    }
}
$start = Find-Control 'TransportStartPause'
$playback = 'not-run-no-output'
$practiceStops = 'not-run-no-output'
$liveSound = 'not-run-no-output'
$liveTempoAndMeter = 'not-run-no-output'
$liveSplitClicks = 'not-run-no-output'
$tapStartSwitching = 'not-run-no-output'
$tempoTypedAfterNudges = 'not-run-no-output'
$timingExportFlow = 'not-run-no-output'
if (-not $playAudio) {
    $playback = 'not-run-audio-disabled'
    $practiceStops = 'not-run-audio-disabled'
    $liveSound = 'not-run-audio-disabled'
    $liveTempoAndMeter = 'not-run-audio-disabled'
    $liveSplitClicks = 'not-run-audio-disabled'
    $tapStartSwitching = 'not-run-audio-disabled'
    $tempoTypedAfterNudges = 'not-run-audio-disabled'
    $timingExportFlow = 'not-run-audio-disabled'
} elseif ($selection.Count -eq 0) {
    if ($start.Current.IsEnabled) { throw 'Start is enabled without a selected output.' }
    # Tap to start starts the metronome, so it needs an output to start on: without one the switch is off and says why.
    $tapStartSwitch = Find-Control 'TapStart'
    if ($tapStartSwitch.Current.IsEnabled) { throw 'Tap to start can be switched on without a selected output.' }
    if ([string]::IsNullOrWhiteSpace($tapStartSwitch.Current.HelpText)) { throw 'Tap to start is off without a selected output and does not say why.' }
    if (-not (Find-Control 'TapTempo').Current.IsEnabled) { throw 'TAP does not work without an output: setting the tempo by tapping needs no sound.' }
    $tapStartSwitching = 'off-without-an-output-and-says-why'
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
    Test-TapStartWithOutput
    $tapStartSwitching = $script:tapStartDone -join ', '
    # Before the export of a finished check: that check is left behind for the Dutch checks, and starting the transport (as this does) ends one.
    Test-TempoTypedAfterNudges
    $tempoTypedAfterNudges = $script:tempoNudgeDone -join ', '
    Test-TimingExportWithOutput
    $timingExportFlow = $script:timingExportDone -join ', '
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
# The export of a finished timing check at the sizes the layout gates use. In the local gate a check has finished by now, and the same
# sizes are read for the finished state (the line that names what is exported is read there too); a hosted runner reads the state
# before any check.
$timingExportEvidence = @{}
$timingExportEvidence['firstUse'] = Test-TimingExport 'Timing export at the first-use size' 'timing-first-use' $script:timingExportFinished 'Main beat' $script:timingExportProject
foreach ($size in @(@(800, 600), @(1000, 720), @(400, 720), @(320, 900))) {
    Resize-To $size[0] $size[1]
    Start-Sleep -Milliseconds 800
    $timingExportEvidence["$($size[0])x$($size[1])"] = Test-TimingExport "Timing export at $($size[0])x$($size[1])" "timing-$($size[0])x$($size[1])" $script:timingExportFinished 'Main beat' $script:timingExportProject
}
$window.GetCurrentPattern([Windows.Automation.TransformPattern]::Pattern).Resize($bounds.Width, $bounds.Height)
Start-Sleep -Milliseconds 800
# The export of the separate tracks once the Studio checks have added two lanes (a plain one and a Euclidean one): three tracks, at the
# same sizes, and the same question whatever the size. It has its own run of sizes, with Library as the page throughout: in a narrow window
# (400x720 and 320x900) the top navigation keeps only the page that is selected and folds the others into its overflow menu, so a check that
# went to the Train page and back to Library at every size could not find Train by name there.
$stemExportEvidence['withLanes'] = Test-StemExport 'Stem export with two lanes at the first-use size' 'stems-lanes-first-use' 'en' 3
foreach ($size in @(@(800, 600), @(1000, 720), @(400, 720), @(320, 900))) {
    Resize-To $size[0] $size[1]
    Start-Sleep -Milliseconds 800
    $stemExportEvidence["$($size[0])x$($size[1])"] = Test-StemExport "Stem export at $($size[0])x$($size[1])" "stems-$($size[0])x$($size[1])" 'en' 3
}
$window.GetCurrentPattern([Windows.Automation.TransformPattern]::Pattern).Resize($bounds.Width, $bounds.Height)
Start-Sleep -Milliseconds 800
Select-Destination 'DestinationSettings'
Wait-Settled 'Language'
Select-Combo 'Language' 'Nederlands'
Wait-Condition { (Find-Control 'DestinationPlay').Current.Name -ne 'Play' } 'The interface did not switch to Dutch.'
Select-Destination 'DestinationPlay'
Wait-Settled 'QuickMeter'
# Tap to start in Dutch: its words are longer, and still it sits beside the tempo controls with them at the sizes people use.
$tapStartEvidence['dutch'] = Test-TapStartRow 'Dutch Play'
Resize-To 800 600
Start-Sleep -Milliseconds 500
$tapStartDutch = Test-TapStartRow 'Dutch Play at 800x600'
if ($tapStartDutch.form -ne 'beside-with-words') { throw "In Dutch Tap to start should have its words beside the tempo controls at 800x600: $($tapStartDutch.form)" }
$tapStartEvidence['dutch800x600'] = $tapStartDutch
$window.GetCurrentPattern([Windows.Automation.TransformPattern]::Pattern).Resize($bounds.Width, $bounds.Height)
Start-Sleep -Milliseconds 800
foreach ($panel in $panels) {
    if ($panel.close -notlike '*Cancel') { continue }
    $before = @(Get-AllButtons | ForEach-Object { $_.Current.AutomationId } | Where-Object { $_ })
    Open-Panel $panel.open $panel.field
    Wait-Settled $panel.field
    Test-NewButtonsOnScreen "Dutch panel $($panel.open[-1])" $before
    Invoke-Control $panel.close
    Wait-Absent $panel.field
}
# The Help in Dutch, whose words are longer: the same rules at the first-use size and at 400x720.
$helpEvidence['dutch'] = Test-HelpPanel 'Dutch Help' 'help-nl'
Resize-To 400 720
Start-Sleep -Milliseconds 800
$helpEvidence['dutch400x720'] = Test-HelpPanel 'Dutch Help at 400x720' 'help-nl-400x720' $true
$window.GetCurrentPattern([Windows.Automation.TransformPattern]::Pattern).Resize($bounds.Width, $bounds.Height)
Start-Sleep -Milliseconds 800
# The tempo guide in Dutch, whose sentence is longer: the same rules, and the note values chosen by their Dutch names.
$tempoGuideEvidence['dutch'] = Test-TempoGuide 'Dutch tempo guide' 'tempo-nl' -Interact -eighth 'Achtste noot' -quarter 'Kwartnoot'
Resize-To 400 720
Start-Sleep -Milliseconds 800
$tempoGuideEvidence['dutch400x720'] = Test-TempoGuide 'Dutch tempo guide at 400x720' 'tempo-nl-400x720'
$window.GetCurrentPattern([Windows.Automation.TransformPattern]::Pattern).Resize($bounds.Width, $bounds.Height)
Start-Sleep -Milliseconds 800
# The export in Dutch, whose words are longer, and where the main beat is Hoofdpuls: the same rules at the first-use size and at 400x720.
# After the local gate's finished check this also shows that the check survived the change of language and is described in Dutch.
$timingExportEvidence['dutch'] = Test-TimingExport 'Dutch timing export' 'timing-nl' $script:timingExportFinished 'Hoofdpuls' $script:timingExportProject
Resize-To 400 720
Start-Sleep -Milliseconds 800
$timingExportEvidence['dutch400x720'] = Test-TimingExport 'Dutch timing export at 400x720' 'timing-nl-400x720' $script:timingExportFinished 'Hoofdpuls' $script:timingExportProject
$window.GetCurrentPattern([Windows.Automation.TransformPattern]::Pattern).Resize($bounds.Width, $bounds.Height)
Start-Sleep -Milliseconds 800
# The export of the separate tracks in Dutch, whose words are longer: the same rules and the Dutch question, at the first-use size and at
# 400x720 (Library stays the page while the window narrows, as above).
$stemExportEvidence['dutch'] = Test-StemExport 'Dutch stem export' 'stems-nl' 'nl' 3
Resize-To 400 720
Start-Sleep -Milliseconds 800
$stemExportEvidence['dutch400x720'] = Test-StemExport 'Dutch stem export at 400x720' 'stems-nl-400x720' 'nl' 3
$window.GetCurrentPattern([Windows.Automation.TransformPattern]::Pattern).Resize($bounds.Width, $bounds.Height)
Start-Sleep -Milliseconds 800
# The checks of each page's first screen below begin with Library: a page that is already the selected one is not built again and keeps the
# place it was scrolled to (the stems checks scroll Library down to its Export card), so they start from Train, where the timing export
# checks leave the app on main.
Select-Destination 'DestinationTrain'
Wait-Settled 'TimingTolerance'
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
  tapStartLayout=$tapStartEvidence;tapStart=$tapStartSwitching;tempoTypedAfterNudges=$tempoTypedAfterNudges;help=$helpEvidence;tempoGuide=$tempoGuideEvidence;
  timingExport=$timingExportEvidence;timingExportFlow=$timingExportFlow;stemExport=$stemExportEvidence;
  popupShortcuts=$(if ($sendKeys) { 'ctrl-m-in-all-seven-panels' } else { 'not-run-keyboard-disabled' });
  limitations='No acoustic accuracy, listening quality or complete accessibility acceptance is established.'} |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
# The report is written first, so that it says why. A caller that asked for the checks that need the hooks (-RequireUiTestHooks) does not
# get a pass for a run in which one of them could not run (a machine with no audio output reports it as not run).
if ($RequireUiTestHooks -and $tempoTypedAfterNudges -like 'not-run*') {
    throw "-RequireUiTestHooks: the check of a tempo typed after nudges did not run ($tempoTypedAfterNudges)."
}
