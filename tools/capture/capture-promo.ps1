param([string]$OutDir = (Join-Path $PSScriptRoot '..\..\promo\clips'),
      [int]$Width = 660, [int]$Height = 420,
      [string[]]$Only = @(), [switch]$Gaming, [int]$GamingCountdown = 20, [int]$GamingSeconds = 12,
      [int]$GamingWidth = 1100, [int]$GamingHeight = 420)
# Records the raw MP4 clips for the vertical promo video (promo/) from the RUNNING dock, in one go. Nothing is converted to GIF and the
# README clips (capture-clips.ps1) are not touched. Needs an ELEVATED shell, the backdrop window (backdrop.ps1) and the tabs used below
# switched on; MochiDock is never launched from here. Keep the mouse still while it runs (about two minutes).
# -Only promo-media,promo-stats records just those clips.
# -Only promo-stats-tour records the live CPU, GPU, Memory, Drives and Power pages in one take.
# -Only promo-idle-hover records the resting capsule opening directly into Stats. Pause media first.
# -Only promo-idle-media,promo-cpu-ready records the resting capsule opening into Media and the loaded CPU page.
# promo-gaming is never part of the default run: record it with -Only promo-gaming (or -Gaming, which records only that clip). Do NOT run the
# backdrop for it (the backdrop would cover the game). It counts down (-GamingCountdown seconds) so a game can be put in borderless windowed
# mode (or fullscreen with fullscreen optimizations; not exclusive fullscreen), then records -GamingSeconds of the top of the screen,
# centred on the gaming dock, with no mouse movement.
. (Join-Path $PSScriptRoot 'capture-lib.ps1')
$ffmpeg = (Get-Command ffmpeg -ErrorAction SilentlyContinue | Select-Object -First 1).Source
if (-not $ffmpeg -and (Test-Path 'C:\Program Files\FFmpeg\bin\ffmpeg.exe')) { $ffmpeg = 'C:\Program Files\FFmpeg\bin\ffmpeg.exe' }
if (-not $ffmpeg) {
  $wg = Get-ChildItem (Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Packages') -Filter ffmpeg.exe -Recurse -ErrorAction SilentlyContinue | Select-Object -First 1
  if ($wg) { $ffmpeg = $wg.FullName }
}
if (-not $ffmpeg) { throw 'ffmpeg not found: install it (winget install Gyan.FFmpeg) or put ffmpeg.exe on PATH.' }
$null = New-Item -ItemType Directory -Force $OutDir
$OutDir = (Resolve-Path $OutDir).Path
$Width -= $Width % 2; $Height -= $Height % 2   # libx264 refuses odd sizes

$gamingOnly = $Gaming -or ($Only.Count -gt 0 -and ($Only | Where-Object { $_ -ne 'promo-gaming' }).Count -eq 0 -and $Only -contains 'promo-gaming')
if ($Gaming -and $Only.Count -eq 0) { $Only = @('promo-gaming') }
if (-not $gamingOnly) { Raise-Dock }
$dock = Get-MainDock
# Same left offset as capture-clips.ps1, so the quick-settings blur rectangle below lines up.
$x = $dock.L + 10; $y = 0
$quickStatusBlur = '30:70:300:20'   # the integration status line under "MochiDock Preferences" (names servers and accounts)

function Start-Clip([string]$name, [int]$seconds, [int]$cursor = 1) {
  $mp4 = Join-Path $OutDir "$name.raw.mp4"
  if (Test-Path $mp4) { Remove-Item $mp4 }
  $a = @('-y', '-f', 'gdigrab', '-framerate', '30', '-offset_x', $x, '-offset_y', $y, '-video_size', "${Width}x${Height}", '-draw_mouse', $cursor, '-i', 'desktop',
         '-t', $seconds, '-c:v', 'libx264', '-pix_fmt', 'yuv420p', '-crf', '12', '-preset', 'slow', "`"$mp4`"")
  $p = Start-Process $ffmpeg -ArgumentList $a -PassThru -WindowStyle Hidden
  Start-Sleep -Milliseconds 900
  return $p
}
# Waits for the recording, then writes <name>.mp4 (blurring $blur = 'x:y:w:h' first, when given) and removes the raw file.
function Finish-Clip($p, [string]$name, [string]$blur = '') {
  $p.WaitForExit()
  $raw = Join-Path $OutDir "$name.raw.mp4"; $out = Join-Path $OutDir "$name.mp4"
  if ($blur) {
    $bx, $by, $bw, $bh = $blur -split ':'
    # Same blur as the README GIFs; the chroma radius must stay under a quarter of the 20 px strip.
    $f = "split[a][b];[b]crop=${bw}:${bh}:${bx}:${by},boxblur=5:4:2:2[bl];[a][bl]overlay=${bx}:${by}"
    & $ffmpeg -y -loglevel error -i $raw -vf $f -c:v libx264 -pix_fmt yuv420p -crf 12 -preset slow $out
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $out) -or (Get-Item $out).Length -lt 10000) {
      Write-Warning "$name blur failed; raw clip kept (not for publishing): $raw"; return
    }
    Remove-Item $raw
  } else { Move-Item -Force $raw $out }
  '{0}: {1:N1} MB' -f $out, ((Get-Item $out).Length / 1MB)
}
function Want([string]$name) {
  if ($Only.Count -eq 0) { return $name -notin 'promo-gaming', 'promo-stats-tour', 'promo-idle-hover', 'promo-idle-media', 'promo-cpu-ready' }
  return $Only -contains $name
}
function Away { Move-Mouse ($dock.L - 150) 3 15; Move-Mouse ($dock.L - 150) 700 15 }
function Get-StatsChipRect([string]$label) {
  $chip = Find-Control $label 'ControlType.Text'
  if (-not $chip -and $label -eq 'Power') { $chip = Find-Control 'Battery' 'ControlType.Text' }
  if (-not $chip -or $chip.Current.IsOffscreen -or $chip.Current.BoundingRectangle.IsEmpty) {
    throw "Could not locate the Stats chip '$label'. Make sure the full Stats card is visible."
  }
  return $chip.Current.BoundingRectangle
}
# Records one card: opens its tab before recording (so the clip starts on it), then holds still.
function Card([string]$name, [string]$tip, [int]$seconds, [scriptblock]$prepare = $null) {
  if (-not (Want $name)) { return }
  try { Click-TabNamed $tip } catch { Write-Warning "$name skipped: $($_.Exception.Message)"; return }
  if ($prepare) { & $prepare }
  Rest-Mouse
  $p = Start-Clip $name $seconds
  Finish-Clip $p $name
}

# 0. Gaming mode: the dock stays at the top of the screen and shows FPS / GPU / CPU. Leaves the mouse alone.
if (Want 'promo-gaming') {
  for ($i = $GamingCountdown; $i -gt 0; $i--) { Write-Host ("Gaming clip in {0} s: switch to a borderless-windowed game now (hands off the mouse once it records)" -f $i); Start-Sleep 1 }
  $gd = Get-MainDock
  $cx = if ($gd) { [int](($gd.L + $gd.R) / 2) } else { [int]([Windows.Forms.Screen]::PrimaryScreen.Bounds.Width / 2) }
  Write-Host ("Gaming dock window: {0}" -f $(if ($gd) { "$($gd.L),$($gd.T) - $($gd.R),$($gd.B)" } else { 'not found, centring on the screen' }))
  $screenW = [Windows.Forms.Screen]::PrimaryScreen.Bounds.Width
  $gw = $GamingWidth - $GamingWidth % 2; $gh = $GamingHeight - $GamingHeight % 2
  $gx = [Math]::Max(0, [Math]::Min($screenW - $gw, $cx - [int]($gw / 2)))
  $mp4 = Join-Path $OutDir 'promo-gaming.raw.mp4'; if (Test-Path $mp4) { Remove-Item $mp4 }
  $a = @('-y', '-f', 'gdigrab', '-framerate', '30', '-offset_x', $gx, '-offset_y', 0, '-video_size', "${gw}x${gh}", '-draw_mouse', 0, '-i', 'desktop',
         '-t', $GamingSeconds, '-c:v', 'libx264', '-pix_fmt', 'yuv420p', '-crf', '12', '-preset', 'slow', "`"$mp4`"")
  $p = Start-Process $ffmpeg -ArgumentList $a -PassThru -WindowStyle Hidden
  Finish-Clip $p 'promo-gaming'
  if ($gamingOnly) { 'Done. Clips are in ' + $OutDir; return }
}

# 1. Hover: the capsule expanding and collapsing, slower than the README clip.
if (Want 'promo-hover') {
  Move-Mouse ($dock.L + 340) 3 15; Start-Sleep -Milliseconds 1800; Click-TabNamed 'Media Player'
  Away; Start-Sleep -Milliseconds 2800
  $p = Start-Clip 'promo-hover' 9
  Start-Sleep -Milliseconds 1200
  Move-Mouse ($dock.L + 340) 600 30 14; Move-Mouse ($dock.L + 340) 3 24 14; Start-Sleep -Milliseconds 3800
  Move-Mouse ($dock.L - 150) 3 24 14; Move-Mouse ($dock.L - 150) 700 15 12
  Finish-Clip $p 'promo-hover'
}

function Record-IdleHover([string]$name, [string]$tab) {
  if (-not (Want $name)) { return }
  $center = [int](($dock.L + $dock.R) / 2)
  Move-Mouse $center ($dock.T + 3) 15
  Start-Sleep -Milliseconds 1600
  Click-TabNamed $tab
  Rest-Mouse
  if ($tab -eq 'System Performance') { Click-Rect (Get-StatsChipRect 'CPU') }
  Away
  Start-Sleep -Milliseconds 2200

  $p = Start-Clip $name 8
  Start-Sleep -Milliseconds 1200
  Move-Mouse $center ($dock.T + 3) 28 13
  Finish-Clip $p $name
}
Record-IdleHover 'promo-idle-hover' 'System Performance'
Record-IdleHover 'promo-idle-media' 'Media Player'

# 2. Cards, each held long enough to cut from. Clipboard, Today, AI usage and the message cards are never recorded (personal data).
if ('promo-media', 'promo-stats', 'promo-cpu-ready', 'promo-weather', 'promo-prayer', 'promo-fx' | Where-Object { Want $_ }) { Move-Mouse ($dock.L + 340) 12 20; Start-Sleep -Milliseconds 1600 }
Card 'promo-media' 'Media Player' 8
Card 'promo-stats' 'System Performance' 7 { Rest-Mouse; Click-At ($dock.L + 85) 62 }   # the CPU chip (live hardware page)
Card 'promo-cpu-ready' 'System Performance' 5 {
  Rest-Mouse
  Click-Rect (Get-StatsChipRect 'CPU')
  Move-Mouse ($dock.L + 340) ($dock.T + 3) 10
  Start-Sleep -Milliseconds 3000
}
Card 'promo-weather' 'Weather Forecast' 7 { Start-Sleep 3 }   # the hourly strip loads a moment after the page opens
Card 'promo-prayer' 'Islamic Prayer Times' 5
Card 'promo-fx' 'FX Rates' 5

if (Want 'promo-stats-tour') {
  Move-Mouse ($dock.L + 340) ($dock.T + 3) 15
  Start-Sleep -Milliseconds 1600
  Click-TabNamed 'System Performance'
  Rest-Mouse

  # Button labels stay in the same row as the dashboard changes. Battery is called Power on desktops.
  $chipRects = foreach ($label in 'CPU', 'GPU', 'Memory', 'Drives', 'Power') {
    Get-StatsChipRect $label
  }
  Click-Rect $chipRects[0]
  Rest-Mouse
  $p = Start-Clip 'promo-stats-tour' 15
  foreach ($rect in $chipRects | Select-Object -Skip 1) {
    Start-Sleep -Milliseconds 2200
    Click-Rect $rect
    Move-Mouse ($dock.L + 340) ($dock.T + 3) 10
  }
  Finish-Clip $p 'promo-stats-tour'
}

# 3. Theme and mascot switches in quick settings; the look in use before is put back afterwards.
if ((Want 'promo-theme') -or (Want 'promo-mascot')) {
  $before = Get-Look
  if (Want 'promo-theme') {
    $p = Start-Clip 'promo-theme' 12
    Start-Sleep -Milliseconds 800
    foreach ($key in 'matcha', 'kuro-night', 'default') { Set-Theme $key; Move-Mouse ($dock.L + 340) 3 10 }
    Finish-Clip $p 'promo-theme' $quickStatusBlur
  }
  if (Want 'promo-mascot') {
    Set-Mascot 'mochi' 1500
    $p = Start-Clip 'promo-mascot' 11
    Start-Sleep -Milliseconds 800
    foreach ($key in 'kuro', 'shiro', 'mochi') { Set-Mascot $key 2600; Move-Mouse ($dock.L + 340) 3 10 }
    Finish-Clip $p 'promo-mascot' $quickStatusBlur
  }
  Restore-Look $before
}
# Leave the dock on Media and collapsed.
try { Click-TabNamed 'Media Player' } catch { }
Away
'Done. Clips are in ' + $OutDir
