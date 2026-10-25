[CmdletBinding()]
param([Parameter(Mandatory)][string]$Payload)
$ErrorActionPreference = 'Stop'
$required = @(
    'ResonantPulse.exe',
    'ResonantPulse.dll',
    'ResonantPulse.runtimeconfig.json',
    'ResonantPulse.pri',
    'App.xbf',
    'MainWindow.xbf',
    'Themes\Resonant.xbf',
    'Microsoft.UI.Xaml.dll',
    'coreclr.dll'
)
foreach ($relative in $required) {
    if (-not (Test-Path -LiteralPath (Join-Path $Payload $relative) -PathType Leaf)) {
        throw "Self-contained distribution is incomplete: $relative is missing."
    }
}
Write-Output 'Application, WinUI and .NET runtime payloads are present.'
