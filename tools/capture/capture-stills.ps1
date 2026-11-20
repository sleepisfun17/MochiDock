param([Parameter(Mandatory)][string]$Theme, [string]$OutDir = (Join-Path $PSScriptRoot '..\..\docs\images\live'))
# Captures the running dock (already themed $Theme, backdrop already open) card by card into $OutDir as <card>-<theme>.png.
# Needs an ELEVATED shell. Skips Settings (shows account names) and Clipboard (shows the user's own clipboard history).
. (Join-Path $PSScriptRoot 'capture-lib.ps1')
$null = New-Item -ItemType Directory -Force $OutDir
$OutDir = (Resolve-Path $OutDir).Path
function Shot([string]$name, [string[]]$redact = @()) { Save-Dock (Join-Path $OutDir "$name-$Theme.png") -Redact $redact; Write-Host "saved $name-$Theme" }

# Collapsed capsule first: leave the dock, let it settle, capture, then hover to expand again.
$dock = Get-MainDock
Move-Mouse ($dock.L + 340) 700 20; Start-Sleep -Milliseconds 2500
Shot 'capsule-media'
Move-Mouse ($dock.L + 340) 12 20; Start-Sleep -Milliseconds 1800

# Tabs are found by their ToolTip, so other optional tabs may be on or off.
$tabs = [ordered]@{ media = 'Media Player'; stats = 'System Performance'; weather = 'Weather Forecast'; pomodoro = 'Focus / Pomodoro Timer'
                    prayer = 'Islamic Prayer Times'; today = 'Today calendar'; 'ai-usage' = 'Local AI Coding Plan Usage'; fx = 'FX Rates' }
$redactions = @{ today = @('システム'); 'ai-usage' = @('@') }
foreach ($key in $tabs.Keys) {
  Click-TabNamed $tabs[$key]; Rest-Mouse
  if ($key -eq 'stats') { Click-At ($dock.L + 85) 62; Rest-Mouse }   # the CPU chip, so Stats shows the live hardware page
  if ($key -in 'fx', 'weather', 'ai-usage') { Start-Sleep -Milliseconds 1200 }
  Shot $key $(if ($redactions.ContainsKey($key)) { $redactions[$key] } else { @() })
}
