param([string]$From = (Join-Path $PSScriptRoot 'clips'))
# Copies the recorded clips (tools/capture/capture-promo.ps1 writes them to promo/clips) into public/clips, where the Remotion project reads
# them. A <name>-blur.mp4 (a copy with the cover, title and lyrics blurred, for a track you may not publish) wins over <name>.mp4.
$to = Join-Path $PSScriptRoot 'public\clips'
$null = New-Item -ItemType Directory -Force $to
$names = 'promo-hover', 'promo-media', 'promo-stats', 'promo-gaming', 'promo-weather', 'promo-prayer', 'promo-fx', 'promo-theme', 'promo-mascot'
$have = @()
foreach ($n in $names) {
  $src = @("$From\$n-blur.mp4", "$From\$n.mp4") | Where-Object { Test-Path $_ } | Select-Object -First 1
  if (-not $src) { if ($n -eq 'promo-gaming') { Write-Host 'promo-gaming: no clip, the Gaming mode scene is skipped'; continue }; Write-Warning "missing: $n.mp4 (record it with tools/capture/capture-promo.ps1)"; continue }
  Copy-Item $src (Join-Path $to "$n.mp4") -Force; $have += $n
  "{0} <- {1}" -f $n, (Split-Path $src -Leaf)
}
# Clips that exist; optional scenes (Gaming mode) without a clip are left out of the video.
if (Test-Path (Join-Path $to 'promo-gaming.mp4')) { if ($have -notcontains 'promo-gaming') { Remove-Item (Join-Path $to 'promo-gaming.mp4') } }
$have | ConvertTo-Json -AsArray | Set-Content (Join-Path $to 'manifest.json') -Encoding utf8
