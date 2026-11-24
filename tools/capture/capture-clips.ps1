param([Parameter(Mandatory)][ValidateSet('hover', 'tour', 'theme', 'mascot')][string]$Clip,
      [string]$RawDir = (Join-Path $env:TEMP 'mochidock-capture'),
      [string]$OutDir = (Join-Path $PSScriptRoot '..\..\docs\media'))
# Records a short clip of the running dock with ffmpeg gdigrab while driving it, then converts it to a GIF. Needs an ELEVATED shell and
# the backdrop window. The region (660 x 395 around the dock) is fixed so the GIF does not jump between cards.
. (Join-Path $PSScriptRoot 'capture-lib.ps1')
# ffmpeg from PATH (WinGet installs it there), else the classic install folder.
$ffmpeg = (Get-Command ffmpeg -ErrorAction SilentlyContinue | Select-Object -First 1).Source
if (-not $ffmpeg -and (Test-Path 'C:\Program Files\FFmpeg\bin\ffmpeg.exe')) { $ffmpeg = 'C:\Program Files\FFmpeg\bin\ffmpeg.exe' }
if (-not $ffmpeg) { throw 'ffmpeg not found: install it (winget install Gyan.FFmpeg) or put ffmpeg.exe on PATH.' }
$null = New-Item -ItemType Directory -Force $RawDir, $OutDir
$OutDir = (Resolve-Path $OutDir).Path
Raise-Dock
$dock = Get-MainDock
$x =$dock.L + 10; $y = 0; $w = 660; $h = 396   # even dimensions: libx264 refuses odd ones

$quickStatusBlur = '30:70:300:20'   # the "WhatsApp (Server): ... Mail: ..." line under "MochiDock Preferences"

function Start-Recording([string]$name, [int]$seconds) {
  $mp4 = Join-Path $RawDir "$name.mp4"
  if (Test-Path $mp4) { Remove-Item $mp4 }
  $args_ = @('-y', '-f', 'gdigrab', '-framerate', '30', '-offset_x', $x, '-offset_y', $y, '-video_size', "${w}x${h}", '-draw_mouse', '1', '-i', 'desktop',
             '-t', $seconds, '-c:v', 'libx264', '-pix_fmt', 'yuv420p', '-crf', '16', "`"$mp4`"")
  $p = Start-Process $ffmpeg -ArgumentList $args_ -PassThru -WindowStyle Hidden
  Start-Sleep -Milliseconds 900    # let ffmpeg open the grabber before the first move
  return $p
}
# $blur = 'x:y:w:h' in clip pixels: that region is blurred before the GIF is made (used to hide the integration status line in quick settings,
# which names the user's WhatsApp server and accounts).
function Convert-ToGif([string]$name, [int]$fps = 12, [string]$blur = '') {
  $mp4 = Join-Path $RawDir "$name.mp4"; $gif = Join-Path $OutDir "$name.gif"
  $pre = ''
  if ($blur) {
    $bx, $by, $bw, $bh = $blur -split ':'
    $pre = "split[a][b];[b]crop=${bw}:${bh}:${bx}:${by},boxblur=5:4:2:2[bl];[a][bl]overlay=${bx}:${by},"
  }
  $filter = "${pre}fps=$fps,scale=${w}:-1:flags=lanczos,split[s0][s1];[s0]palettegen=stats_mode=diff[p];[s1][p]paletteuse=dither=bayer:bayer_scale=5:diff_mode=rectangle"
  & $ffmpeg -y -loglevel error -i $mp4 -vf $filter -loop 0 $gif
  '{0}: {1:N1} MB' -f $gif, ((Get-Item $gif).Length / 1MB)
}

switch ($Clip) {
  'hover' {
    # Start on the Media card: expand, pick Media, collapse again, then record the real expand and collapse.
    Move-Mouse ($dock.L + 340) 3 15; Start-Sleep -Milliseconds 1800; Click-TabNamed 'Media Player'
    Move-Mouse ($dock.L - 150) 3 15; Move-Mouse ($dock.L - 150) 700 15; Start-Sleep -Milliseconds 2800
    $p = Start-Recording 'hover-expand' 10
    Start-Sleep -Milliseconds 800
    Move-Mouse ($dock.L + 340) 600 25 14; Move-Mouse ($dock.L + 340) 3 20 14; Start-Sleep -Milliseconds 3300
    Move-Mouse ($dock.L - 150) 3 20 14; Move-Mouse ($dock.L - 150) 700 15 12
    $p.WaitForExit(); Convert-ToGif 'hover-expand'
  }
  'tour' {
    Move-Mouse ($dock.L + 340) 12 20; Start-Sleep -Milliseconds 1600
    Click-TabNamed 'Media Player'; Rest-Mouse
    # Weather loads its hourly strip a moment after the page opens, so visit it once beforehand and give it extra time in the clip.
    Click-TabNamed 'Weather Forecast'; Rest-Mouse; Start-Sleep 3
    Click-TabNamed 'System Performance'; Rest-Mouse; Click-At ($dock.L + 85) 62; Rest-Mouse   # Stats on its CPU chip (live hardware page); put the Battery chip back afterwards
    Click-TabNamed 'Media Player'; Rest-Mouse
    $p = Start-Recording 'tab-tour' 17
    # Clipboard (your own history), Today (calendar titles) and AI usage (account line) are left out: a video cannot be redacted like the stills.
    foreach ($tip in 'System Performance', 'Weather Forecast', 'Islamic Prayer Times', 'FX Rates', 'Media Player') {
      Click-TabNamed $tip; Rest-Mouse; if ($tip -eq 'Weather Forecast') { Start-Sleep -Milliseconds 1300 }; Start-Sleep -Milliseconds 200 }
    $p.WaitForExit(); Convert-ToGif 'tab-tour' 10
  }
  'theme' {
    # Default -> Matcha -> Kuro Night -> back to the starting theme, in quick settings. The radios are found by id (their labels repeat
    # in the Mascot row). The theme and mascot in use before are put back afterwards.
    $before = Get-Look
    $p = Start-Recording 'theme-switch' 12
    Start-Sleep -Milliseconds 800
    foreach ($key in 'matcha', 'kuro-night', 'default') { Set-Theme $key; Move-Mouse ($dock.L + 340) 3 10 }
    $p.WaitForExit(); Convert-ToGif 'theme-switch' 12 $quickStatusBlur
    Restore-Look $before
  }
  'mascot' {
    # The mascot picker in quick settings: Mochi spins into Kuro, then Shiro, then back to Mochi (the whirlwind of the mascot change). Set
    # the theme you want first (Kuro Night shows Kuro's glow best); the theme and mascot in use before are put back afterwards.
    $before = Get-Look
    Set-Mascot 'mochi' 1500
    $p = Start-Recording 'mascot-switch' 11
    Start-Sleep -Milliseconds 800
    foreach ($key in 'kuro', 'shiro', 'mochi') { Set-Mascot $key 2600; Move-Mouse ($dock.L + 340) 3 10 }
    $p.WaitForExit(); Convert-ToGif 'mascot-switch' 12 $quickStatusBlur
    Restore-Look $before
  }
}