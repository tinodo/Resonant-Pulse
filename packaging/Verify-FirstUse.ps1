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
    $element = $window.FindFirst([Windows.Automation.TreeScope]::Descendants, $condition)
    if ($null -eq $element) { throw "Required first-use control is missing: $id" }
    return $element
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
if ($selection.Count -eq 0) {
    if ($start.Current.IsEnabled) { throw 'Start is enabled without a selected output.' }
} else {
    if (-not $start.Current.IsEnabled) { throw 'Start is disabled despite a selected output.' }
    try {
        $start.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
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
        (Find-Control 'TransportStop').GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()
    }
}
@{controls=$controls;selectedOutputCount=$selection.Count;playback=$playback;
  limitations='No acoustic accuracy, listening quality or complete accessibility acceptance is established.'} |
    ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $ReportPath -Encoding UTF8
Write-Output "First-use controls are visible; playback result: $playback"
