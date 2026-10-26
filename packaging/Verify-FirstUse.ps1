[CmdletBinding()]
param(
    [Parameter(Mandatory)][int]$AppProcessId,
    [Parameter(Mandatory)][string]$ReportPath
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
$process = [Diagnostics.Process]::GetProcessById($AppProcessId)
$window = [Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
function Find-Control([string]$id) {
    $condition = [Windows.Automation.PropertyCondition]::new(
        [Windows.Automation.AutomationElement]::AutomationIdProperty, $id)
    $limit = [DateTime]::UtcNow.AddSeconds(5)
    do {
        $process.Refresh()
        if ($process.HasExited) { throw "Application exited while finding native control: $id" }
        $currentWindow = [Windows.Automation.AutomationElement]::FromHandle($process.MainWindowHandle)
        $element = $currentWindow.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
        if ($null -ne $element) { return $element }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $limit)
    throw "Required first-use control remained missing: $id"
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
function Set-PracticeNumber([string]$id, [int]$value) {
    $element = Find-Control $id
    [object]$pattern = $null
    if ($element.TryGetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern, [ref]$pattern)) {
        $pattern.SetValue([double]$value)
    } elseif ($element.TryGetCurrentPattern([Windows.Automation.ValuePattern]::Pattern, [ref]$pattern)) {
        $pattern.SetValue([string]$value)
    } else {
        throw "Practice input has no writable numeric automation pattern: $id"
    }
}
function Set-PracticeLimits([int]$seconds, [int]$beats, [int]$bars) {
    (Find-Control 'PracticeTimer').GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    Set-PracticeNumber 'PracticeSeconds' $seconds
    Set-PracticeNumber 'PracticeBeats' $beats
    Set-PracticeNumber 'PracticeBars' $bars
    $before = (Find-Control 'PracticeLimitsStatus').Current.ItemStatus
    Invoke-Control 'ApplyPracticeTimer'
    $expected = ':{0}:{1}:{2}' -f $seconds, $beats, $bars
    $limit = [DateTime]::UtcNow.AddSeconds(15)
    do {
        $committed = (Find-Control 'PracticeLimitsStatus').Current.ItemStatus
        if ($committed -ne $before -and $committed.EndsWith($expected, [StringComparison]::Ordinal)) {
            return
        }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $limit)
    throw "Practice-limit Apply did not confirm successful publication and recovery: $expected (observed $committed)"
}
function Wait-TapStatus([string]$expected) {
    $limit = [DateTime]::UtcNow.AddSeconds(10)
    do {
        $text = (Find-Control 'TapStatus').Current.ItemStatus
        if ($text -like $expected) { return }
        Start-Sleep -Milliseconds 50
    } while ([DateTime]::UtcNow -lt $limit)
    throw "Expected tap-in status $expected, observed $text"
}
$deadline = [DateTime]::UtcNow.AddSeconds(20)
do {
    $status = Find-Control 'TransportStatus'
    if ($status.Current.Name -like 'Stopped*') { break }
    Start-Sleep -Milliseconds 200
} while ([DateTime]::UtcNow -lt $deadline)
if ($status.Current.Name -notlike 'Stopped*') { throw 'First-use initialization did not reach Stopped.' }
$bounds = $window.Current.BoundingRectangle
$controls = @()
foreach ($id in @('Tempo', 'TapTempo', 'QuickMeter', 'AudioOutputPicker', 'TransportStartPause', 'MasterGain')) {
    $element = Find-Control $id
    $rect = $element.Current.BoundingRectangle
    if ($element.Current.IsOffscreen -or $rect.IsEmpty -or -not $bounds.Contains($rect)) {
        throw "Primary control is not visible in the first-use window: $id ($rect)"
    }
    $controls += @{id=$id;name=$element.Current.Name;visible=$true}
}
$picker = Find-Control 'AudioOutputPicker'
$selection = $picker.GetCurrentPattern([Windows.Automation.SelectionPattern]::Pattern).Current.GetSelection()
$start = Find-Control 'TransportStartPause'
$playback = 'not-run-no-output'
$practiceStops = 'not-run-no-output'
$tapIn = 'not-run-no-output'
if ($selection.Count -eq 0) {
    if ($start.Current.IsEnabled) { throw 'Start is enabled without a selected output.' }
    (Find-Control 'TempoOptions').GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
    if ((Find-Control 'ArmTapIn').Current.IsEnabled) { throw 'Tap-in arming is enabled without a selected output.' }
    Wait-TapStatus 'idle'
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
        Start-Sleep -Seconds 2
        $advanced = (Find-Control 'TransportStatus').Current.Name
        if ($advanced -notlike 'Playing*') {
            throw 'Playback failed after the first Start.'
        }
        if ($advanced -eq $firstPlaying) { throw 'Playback state changed but the audio position did not advance.' }
        $playback = 'started-and-audio-position-advanced'
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
    try {
        (Find-Control 'TempoOptions').GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
        Set-PracticeNumber 'TapInCount' 4
        Invoke-Control 'ArmTapIn'
        Wait-TapStatus 'armed:0:4:False'
        Invoke-Control 'TapTempo'
        Wait-TapStatus 'armed:1:4:False'
        Start-Sleep -Milliseconds 500
        Invoke-Control 'TapTempo'
        Wait-TapStatus 'armed:2:4:False'
        $estimate = [regex]::Match((Find-Control 'TapStatus').Current.Name, 'estimate ([0-9.]+) BPM')
        if (-not $estimate.Success) { throw 'Native tap-in did not expose its tempo estimate.' }
        $period = 60 / [double]::Parse($estimate.Groups[1].Value, [Globalization.CultureInfo]::InvariantCulture)
        Start-Sleep -Milliseconds ([int](1700 * $period))
        Invoke-Control 'TapTempo'
        Wait-TapStatus 'armed:0:4:True'
        if ((Find-Control 'TransportStatus').Current.Name -notlike 'Stopped*') {
            throw 'A rejected tap unexpectedly started playback.'
        }
        Invoke-Control 'TransportStop'
        Wait-TapStatus 'idle'
        $null = Wait-Transport 'Stopped'
        Invoke-Control 'ArmTapIn'
        $keyboard = New-Object -ComObject WScript.Shell
        if (-not $keyboard.AppActivate($AppProcessId)) { throw 'Could not activate the owned native app for Escape verification.' }
        (Find-Control 'TapInCount').SetFocus()
        if ([Windows.Automation.AutomationElement]::FocusedElement.Current.ProcessId -ne $AppProcessId) {
            throw 'Editing focus is not in the owned app; no keyboard input was sent.'
        }
        $keyboard.SendKeys('{ESC}')
        Wait-TapStatus 'idle'
        Invoke-Control 'ArmTapIn'
        Invoke-Control 'TapTempo'
        Wait-TapStatus 'armed:1:4:False'
        (Find-Control 'DestinationStudio').GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
        (Find-Control 'DestinationPlay').GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
        Wait-TapStatus 'idle'
        if ((Find-Control 'TransportStatus').Current.Name -notlike 'Stopped*') {
            throw 'Navigating away from tap-in unexpectedly started playback.'
        }
        (Find-Control 'TempoOptions').GetCurrentPattern([Windows.Automation.ExpandCollapsePattern]::Pattern).Expand()
        Set-PracticeNumber 'TapInCount' 2
        Invoke-Control 'ArmTapIn'
        Wait-TapStatus 'armed:0:2:False'
        Invoke-Control 'TapTempo'
        Wait-TapStatus 'armed:1:2:False'
        Start-Sleep -Milliseconds 500
        Invoke-Control 'TapTempo'
        $null = Wait-Transport 'Playing' $true
        Wait-TapStatus 'idle'
        $tapIn = 'rejected-tap-stop-editing-escape-navigation-cancel-and-two-tap-start-passed'
    } finally {
        if ((Find-Control 'TransportStatus').Current.Name -notlike 'Stopped*' -or
            (Find-Control 'TapStatus').Current.ItemStatus -ne 'idle') {
            Invoke-Control 'TransportStop'
        }
        $null = Wait-Transport 'Stopped'
        Wait-TapStatus 'idle'
    }
}
@{controls=$controls;selectedOutputCount=$selection.Count;playback=$playback;practiceStops=$practiceStops;tapIn=$tapIn;
  limitations='No acoustic accuracy, listening quality or complete accessibility acceptance is established.'} |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
Write-Output "First-use controls are visible; playback result: $playback"
Write-Output "Practice stop result: $practiceStops"
