param([string]$From = (Join-Path $PSScriptRoot 'clips'))
# Copies the recorded clips (tools/capture/capture-promo.ps1 writes them to promo/clips) into public/clips, where the Remotion project reads
# them. A <name>-blur.mp4 (a copy with the cover, title and lyrics blurred, for a track you may not publish) wins over <name>.mp4.
$to = Join-Path $PSScriptRoot 'public\clips'
$null = New-Item -ItemType Directory -Force $to
$names = 'promo-hover', 'promo-idle-hover', 'promo-idle-media', 'promo-media', 'promo-stats', 'promo-cpu-ready', 'promo-stats-tour', 'promo-gaming', 'promo-weather', 'promo-prayer', 'promo-fx', 'promo-theme', 'promo-mascot'
$optional = @{
  'promo-gaming' = 'the Gaming mode scene is skipped'
  'promo-stats-tour' = 'the original Stats clip is used'
  'promo-idle-hover' = 'the opening starts on the Stats card'
  'promo-idle-media' = 'the Stats opening is used'
  'promo-cpu-ready' = 'the original CPU shot is used'
}
$have = @()
foreach ($n in $names) {
  $src = @("$From\$n-blur.mp4", "$From\$n.mp4") | Where-Object { Test-Path $_ } | Select-Object -First 1
  if (-not $src) {
    if ($optional.ContainsKey($n)) {
      Write-Host "${n}: no clip, $($optional[$n])"
      $copied = Join-Path $to "$n.mp4"
      if (Test-Path -LiteralPath $copied) { Remove-Item -LiteralPath $copied }
    } else {
      Write-Warning "missing: $n.mp4 (record it with tools/capture/capture-promo.ps1)"
    }
    continue
  }
  Copy-Item $src (Join-Path $to "$n.mp4") -Force; $have += $n
  "{0} <- {1}" -f $n, (Split-Path $src -Leaf)
}
# Remotion uses this to choose the opening footage and include Gaming mode when available.
$have | ConvertTo-Json -AsArray | Set-Content (Join-Path $to 'manifest.json') -Encoding utf8
