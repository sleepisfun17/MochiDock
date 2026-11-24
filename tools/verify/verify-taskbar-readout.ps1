param([string]$Configuration='Release')
# Renders the taskbar readout (TaskbarReadoutRenderer, the same drawing the widget uses) without a taskbar and without starting MochiDock:
# sample blocks at 100/125/150/200% on light and dark taskbars, for 40 px (Windows 10), 48 px (Windows 11), 64 px (Windhawk tall), and the
# small-taskbar one-line fallback. Also tests item subsets, custom block order, all network arrow styles (including colour emoji pictures),
# explicit/bold fonts, and custom label/value colours. Writes PNGs to MochiDock/obj/taskbar-readout.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework
$repo=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$directory=Join-Path $repo "MochiDock/bin/$Configuration/net8.0-windows10.0.19041.0"
$dllPath=Join-Path $directory 'MochiDock.dll'
$assembly=[Reflection.Assembly]::LoadFrom($dllPath)
$core=[Reflection.Assembly]::LoadFrom((Join-Path $directory 'MochiDock.Core.dll'))
[AppDomain]::CurrentDomain.add_AssemblyResolve({param($s,$e) if($e.Name.StartsWith('MochiDock.Core')){return $core} if($e.Name.StartsWith('MochiDock')){return $assembly} return $null})

# Open Assets\emoji.pak once with APP_CONTEXT_BASE_DIRECTORY pointed at the build output so EmojiImages caches the zip for the run.
$pak = Join-Path $directory 'Assets/emoji.pak'
if(!(Test-Path $pak)){ throw "Build output has no Assets/emoji.pak: $pak" }
$baseKey='APP_CONTEXT_BASE_DIRECTORY'; $baseOld=[AppContext]::GetData($baseKey)
[AppContext]::SetData($baseKey, $directory + [IO.Path]::DirectorySeparatorChar)
try {
    $emojiUp = [MochiDock.Services.EmojiImages]::Get('1f53c')
    $emojiDown = [MochiDock.Services.EmojiImages]::Get('1f53d')
    if($null -eq $emojiUp -or $null -eq $emojiDown){ throw 'emoji.pak did not provide 1f53c/1f53d arrow pictures' }
} finally {
    [AppContext]::SetData($baseKey, $baseOld)
}

$app=[Windows.Application]::new();$app.ShutdownMode=[Windows.ShutdownMode]::OnExplicitShutdown
[xml]$source=Get-Content (Join-Path $repo 'MochiDock/App.xaml') -Raw
foreach($node in $source.SelectNodes('//*[local-name()="FontFamily"]')){$app.Resources[$node.GetAttribute('Key','http://schemas.microsoft.com/winfx/2006/xaml')]=[Windows.Media.FontFamily]::new([uri]'pack://application:,,,/',$node.InnerText)}
$app.Resources['IslandUiFont']=$app.Resources['QuicksandFont']
$output=Join-Path $repo 'MochiDock/obj/taskbar-readout';$null=New-Item -ItemType Directory -Force $output

$blockType=$core.GetType('MochiDock.Core.TaskbarBlock');$snapType=$core.GetType('MochiDock.Core.TaskbarSnapshot');$optType=$core.GetType('MochiDock.Core.TaskbarOptions')
$batType=$core.GetType('MochiDock.Core.TaskbarBattery');$kindType=$core.GetType('MochiDock.Core.TaskbarBlockKind');$readouts=$core.GetType('MochiDock.Core.TaskbarReadouts')
$arrowType=$core.GetType('MochiDock.Core.TaskbarArrowStyle');$styleType=$core.GetType('MochiDock.Core.TaskbarAppearance')
$zoneType=$core.GetType('MochiDock.Core.TaskbarZone');$unitType=$core.GetType('MochiDock.Core.TaskbarNetworkUnit')
$batLayoutType=$core.GetType('MochiDock.Core.TaskbarBatteryLayout')
$prayerDisplayType=$core.GetType('MochiDock.Core.TaskbarPrayerDisplay')
$renderer=$assembly.GetType('MochiDock.Services.TaskbarReadoutRenderer')
$renderMethod=$renderer.GetMethod('Render')

function New-Rec($type,$props){$o=[Activator]::CreateInstance($type);foreach($k in $props.Keys){$o.GetType().GetProperty($k).SetValue($o,$props[$k])};return $o}
function Build-Blocks($snapshot,$options){return ,($readouts.GetMethod('Build').Invoke($null,@($snapshot,$options)))}
function Invoke-Render($blocks,$dpi,$dark,$heightPx,$bg=$false,$style=$null){return $renderMethod.Invoke($null,@($blocks,[double]$dpi,[bool]$dark,[int]$heightPx,[bool]$bg,$style))}
function Lum($c){$f={param($v)$x=$v/255.0;if($x -le 0.03928){$x/12.92}else{[math]::Pow(($x+0.055)/1.055,2.4)}};return 0.2126*(& $f $c.R)+0.7152*(& $f $c.G)+0.0722*(& $f $c.B)}
function Contrast($a,$b){$x=Lum $a;$y=Lum $b;$hi=[math]::Max($x,$y);$lo=[math]::Min($x,$y);return ($hi+0.05)/($lo+0.05)}

# ---- sample data -----------------------------------------------------------------------------------------------------------------------
$now=[DateTimeOffset]::UtcNow
$mouse=[Activator]::CreateInstance($batType,@([string][char]0xE962,'Mouse','2.4G',[Nullable[int]]64,$false,$false,$now))
$keys=[Activator]::CreateInstance($batType,@([string][char]0xE765,'Keyboard','Bluetooth',[Nullable[int]]82,$true,$false,$now))
$low=[Activator]::CreateInstance($batType,@([string][char]0xE961,'Headset','HID',[Nullable[int]]9,$false,$true,$now))
$pad=[Activator]::CreateInstance($batType,@([string][char]0xE7FC,'Gamepad','Bluetooth',[Nullable[int]]55,$false,$false,$now))
$batteries=[Array]::CreateInstance($batType,4);$batteries[0]=$mouse;$batteries[1]=$keys;$batteries[2]=$low;$batteries[3]=$pad
$all=New-Rec $optType @{CpuLoad=$true;CpuTemp=$true;GpuLoad=$true;GpuTemp=$true;Memory=$true;Disk=$true;Upload=$true;Download=$true;Prayer=$true;WeatherTemp=$true;WeatherWind=$true;Batteries=$true}
$full=New-Rec $snapType @{CpuPercent=[Nullable[double]]2;CpuTempC=[Nullable[double]]45;CpuTempMeasured=$false;GpuPercent=[Nullable[double]]5;GpuTempC=[Nullable[double]]37;RamPercent=[Nullable[double]]49;DiskPercent=[Nullable[double]]0;
 NetUpBytesPerSecond=[Nullable[double]](51*1024);NetDownBytesPerSecond=[Nullable[double]](18*1024);PrayerName='Maghrib';PrayerTime='16:40';WeatherTempC=[Nullable[double]]13;WindMetersPerSecond=[Nullable[double]]8;WeatherGlyph=[string][char]0x2601;WeatherCondition='Cloudy';Batteries=$batteries}
$hot=New-Rec $snapType @{CpuPercent=[Nullable[double]]95;CpuTempC=[Nullable[double]]88;CpuTempMeasured=$true;GpuPercent=[Nullable[double]]100;GpuTempC=[Nullable[double]]91;RamPercent=[Nullable[double]]93;DiskPercent=[Nullable[double]]100;
 NetUpBytesPerSecond=[Nullable[double]](1.2*1024*1024);NetDownBytesPerSecond=[Nullable[double]](250*1024*1024);Batteries=$batteries}
$noGpu=New-Rec $snapType @{CpuPercent=[Nullable[double]]10;CpuTempC=[Nullable[double]]50;CpuTempMeasured=$true;RamPercent=[Nullable[double]]60;Batteries=[Array]::CreateInstance($batType,0)}
$cases=@(
 @{Name='full';Blocks=(Build-Blocks $full $all);Warn=@()},
 @{Name='warnings';Blocks=(Build-Blocks $hot $all);Warn=@('System','MemoryDisk')},
 @{Name='no-gpu';Blocks=(Build-Blocks $noGpu $all);Warn=@()}
)
if($cases[0].Blocks.Count -ne 7){throw "The full sample should give 7 blocks (system, memory/disk, network, prayer, weather and two battery blocks), got $($cases[0].Blocks.Count)"}
if(-not $cases[0].Blocks[0].Bottom.StartsWith('GPU ')){throw "The full sample must include the GPU bottom line, got '$($cases[0].Blocks[0].Bottom)'"}

# ---- helpers ---------------------------------------------------------------------------------------------------------------------------
function Ink-Box($px,$stride,$left,$right,$height,$alphaMin){
 $minX=$right;$maxX=-1;$minY=$height;$maxY=-1;$count=0
 for($y=0;$y -lt $height;$y++){for($x=[int]$left;$x -lt [int]$right;$x++){if($px[$y*$stride+$x*4+3] -ge $alphaMin){$count++;if($x -lt $minX){$minX=$x};if($x -gt $maxX){$maxX=$x};if($y -lt $minY){$minY=$y};if($y -gt $maxY){$maxY=$y}}}}
 return @{MinX=$minX;MaxX=$maxX;MinY=$minY;MaxY=$maxY;Count=$count}
}
function Save-Png($render,$darkTaskbar,$path){
 $bg=if($darkTaskbar){[Windows.Media.Color]::FromRgb(0x20,0x20,0x20)}else{[Windows.Media.Color]::FromRgb(0xF3,0xF3,0xF3)}
 $source=[Windows.Media.Imaging.BitmapSource]::Create($render.Width,$render.Height,96,96,[Windows.Media.PixelFormats]::Pbgra32,$null,$render.Pixels,$render.Width*4)
 $visual=[Windows.Media.DrawingVisual]::new()
 $dc=$visual.RenderOpen();$dc.DrawRectangle([Windows.Media.SolidColorBrush]::new($bg),$null,[Windows.Rect]::new(0,0,$render.Width,$render.Height));$dc.DrawImage($source,[Windows.Rect]::new(0,0,$render.Width,$render.Height));$dc.Close()
 $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new($render.Width,$render.Height,96,96,[Windows.Media.PixelFormats]::Pbgra32);$bitmap.Render($visual)
 $enc=[Windows.Media.Imaging.PngBitmapEncoder]::new();$enc.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
 $s=[IO.File]::Create($path);$enc.Save($s);$s.Dispose()
}
function Assert-LayoutValid($render,$blocks,$label,$scale,$oneLine){
 if($null -eq $render){throw "${label}: nothing rendered"}
 if($render.OneLine -ne $oneLine){throw "${label}: one-line layout should be $oneLine"}
 $rects=@($render.Blocks)
 if($rects.Count -ne $blocks.Count){throw "${label}: $($rects.Count) rectangles for $($blocks.Count) blocks"}
 $stride=$render.Width*4;$px=$render.Pixels
 for($i=0;$i -lt $rects.Count;$i++){
  $r=$rects[$i].Pixels
  if($rects[$i].Kind -ne $blocks[$i].Kind){throw "${label}: rectangle $i is for $($rects[$i].Kind), the block is $($blocks[$i].Kind)"}
  if([math]::Abs($r.Height-$render.Height) -gt 0.5 -or $r.Top -ne 0){throw "${label}: rectangle $i is not the full height"}
  if($i -eq 0 -and [math]::Abs($r.Left) -gt 0.5){throw "${label}: the first rectangle does not start at the left edge"}
  if($i -gt 0 -and [math]::Abs($r.Left-$rects[$i-1].Pixels.Right) -gt 0.5){throw "${label}: rectangles $($i-1) and $i are not contiguous"}
  if($i -eq $rects.Count-1 -and [math]::Abs($r.Right-$render.Width) -gt 0.5){throw "${label}: the last rectangle does not reach the right edge"}
  $hit=$renderer.GetMethod('HitTest').Invoke($null,@($render.Blocks,[double]($r.Left+$r.Width/2)))
  if($hit -ne $blocks[$i].Kind){throw "${label}: a click in the middle of block $i hits $hit, not $($blocks[$i].Kind)"}
 }
 $box=Ink-Box $px $stride 0 $render.Width $render.Height 60
 if($box.Count -eq 0){throw "${label}: nothing visible was drawn"}
 if($box.MinY -lt 1 -or $box.MaxY -gt $render.Height-2){throw "${label}: the text touches the top or bottom edge (ink rows $($box.MinY) to $($box.MaxY) of $($render.Height))"}
 if($box.MinX -lt 2 -or $box.MaxX -gt $render.Width-3){throw "${label}: the text touches the left or right edge (ink columns $($box.MinX) to $($box.MaxX) of $($render.Width))"}
 for($i=0;$i -lt $rects.Count;$i++){
  $r=$rects[$i].Pixels;$inner=Ink-Box $px $stride ([math]::Ceiling($r.Left)) ([math]::Floor($r.Right)) $render.Height 60
  if($inner.Count -eq 0){throw "${label}: block $i ($($blocks[$i].Kind)) drew nothing"}
  if($i -gt 0){
   $edge=Ink-Box $px $stride ([math]::Round($r.Left)-1) ([math]::Round($r.Left)+1) $render.Height 60
   if($edge.Count -gt 0){throw "${label}: ink on the boundary between block $($i-1) and block $i (blocks touch or overlap)"}
  }
  $hasBottom=-not [string]::IsNullOrEmpty($blocks[$i].Bottom)
  $inkHeight=$inner.MaxY-$inner.MinY+1
  if(!$oneLine -and $hasBottom -and $inkHeight -lt 1.5*8*$scale){throw "${label}: block $i should have two lines but its ink is only $inkHeight px high"}
  if($inkHeight -gt $render.Height-2){throw "${label}: block $i is $inkHeight px high in a $($render.Height) px taskbar"}
 }
 return $box
}

# ---- standard DPI/height matrix --------------------------------------------------------------------------------------------------------
$checks=0;$measure=@()
foreach($dark in $false,$true){
 $theme=if($dark){'dark'}else{'light'}
 $taskbarBg=if($dark){[Windows.Media.Color]::FromRgb(0x20,0x20,0x20)}else{[Windows.Media.Color]::FromRgb(0xF3,0xF3,0xF3)}
 $text=$renderer.GetMethod('TextColor').Invoke($null,@($dark));$warn=$renderer.GetMethod('WarnColor').Invoke($null,@($dark))
 foreach($pair in @(@('text',$text),@('warning',$warn))){if((Contrast $pair[1] $taskbarBg) -lt 4.5){throw ("The {0} colour on the {1} taskbar is only {2:N2}:1 (needs 4.5:1)" -f $pair[0],$theme,(Contrast $pair[1] $taskbarBg))};$checks++}
 foreach($scale in 1,1.25,1.5,2){
  foreach($heightDip in 48,40,30){
   $dpi=96*$scale;$heightPx=[int][math]::Round($heightDip*$scale)
   $oneLine=$heightDip -lt 36
   foreach($case in $cases){
    $blocks=$case.Blocks
    $label="$($case.Name), $theme taskbar, $heightDip px at $([int]($scale*100))%"
    $render=Invoke-Render $blocks $dpi $dark $heightPx $false $null
    if($render.Height -ne $heightPx -or $render.Pixels.Length -ne $render.Width*$render.Height*4){throw "${label}: the bitmap size is wrong"}
    $box=Assert-LayoutValid $render $blocks $label $scale $oneLine
    $stride=$render.Width*4;$px=$render.Pixels;$rects=@($render.Blocks)
    foreach($i in 0..($rects.Count-1)){
     $r=$rects[$i].Pixels;$red=0
     for($y=0;$y -lt $render.Height;$y++){for($x=[int][math]::Ceiling($r.Left);$x -lt [int][math]::Floor($r.Right);$x++){$o=$y*$stride+$x*4;if($px[$o+3] -ge 200 -and $px[$o+2] -gt 150 -and $px[$o+2] -gt 2*$px[$o+1] -and $px[$o+2] -gt 2*$px[$o]){$red++}}}
     $isWarnBlock=$blocks[$i].TopWarn -or $blocks[$i].BottomWarn
     if($case.Name -eq 'warnings' -and $isWarnBlock -and $red -eq 0 -and $rects[$i].Kind.ToString() -ne 'Batteries'){throw "${label}: the warning block $i ($($rects[$i].Kind)) has no warning colour"}
     if($case.Name -eq 'full' -and $red -gt 0 -and $rects[$i].Kind.ToString() -ne 'Batteries'){throw "${label}: block $i ($($rects[$i].Kind)) is red but is not a warning"}
     $checks++
    }
    $measure+=("{0}: {1}x{2} px, {3} blocks, ink rows {4}-{5}" -f $label,$render.Width,$render.Height,$rects.Count,$box.MinY,$box.MaxY)
    if($case.Name -in 'full','warnings' -or $heightDip -eq 30){Save-Png $render $dark (Join-Path $output ("readout-{0}-{1}-{2}px-{3}.png" -f $case.Name,$theme,$heightDip,[int]($scale*100)))}
    $checks++
   }
  }
 }
}

# ---- Windhawk tall taskbar (64 px DIP) & explicit/bold fonts ---------------------------------------------------------------------------
$r48 = Invoke-Render $cases[0].Blocks 96 $true 48 $false $null
$b48 = Ink-Box $r48.Pixels ($r48.Width*4) 0 $r48.Width $r48.Height 60
foreach($dark in $false,$true){
 $theme=if($dark){'dark'}else{'light'}
 foreach($scale in 1,1.5){
  $dpi=96*$scale;$heightPx=[int][math]::Round(64*$scale)
  $r64=Invoke-Render $cases[0].Blocks $dpi $dark $heightPx $false $null
  $b64=Assert-LayoutValid $r64 $cases[0].Blocks "windhawk 64px $theme $([int]($scale*100))%" $scale $false
  if($scale -eq 1 -and (($b64.MaxY - $b64.MinY) -le ($b48.MaxY - $b48.MinY))){throw 'Auto font on 64px Windhawk taskbar did not scale taller than 48px'}
  $measure+=("windhawk-64px-{0}-{1}: {2}x{3} px, ink rows {4}-{5}" -f $theme,[int]($scale*100),$r64.Width,$r64.Height,$b64.MinY,$b64.MaxY)
  $checks++
 }
}
Save-Png (Invoke-Render $cases[0].Blocks 96 $true 64 $false $null) $true (Join-Path $output 'readout-windhawk-64px-dark-100.png')
Save-Png (Invoke-Render $cases[0].Blocks 144 $false 96 $false $null) $false (Join-Path $output 'readout-windhawk-64px-light-150.png')

$bold18 = New-Rec $styleType @{FontFamily='Quicksand';FontSize=18.0;Bold=$true}
$rBold = Invoke-Render $cases[0].Blocks 96 $true 64 $false $bold18
$null = Assert-LayoutValid $rBold $cases[0].Blocks 'bold 18px on 64px' 1 $false
Save-Png $rBold $true (Join-Path $output 'readout-bold-18px-dark-100.png')
$checks++

$small10 = New-Rec $styleType @{FontSize=10.0;Bold=$false}
$null = Assert-LayoutValid (Invoke-Render $cases[0].Blocks 96 $true 48 $false $small10) $cases[0].Blocks 'explicit 10px on 48px' 1 $false
$oversized = New-Rec $styleType @{FontSize=26.0;Bold=$true}
$null = Assert-LayoutValid (Invoke-Render $cases[0].Blocks 96 $true 40 $false $oversized) $cases[0].Blocks 'oversized 26px clamped on 40px' 1 $false
$checks += 2

# ---- item subsets ----------------------------------------------------------------------------------------------------------------------
$cpuOnly = Build-Blocks $full (New-Rec $optType @{CpuLoad=$true})
if($cpuOnly.Count -ne 1 -or $cpuOnly[0].Top -ne 'CPU 2%' -or $cpuOnly[0].HasBottom){throw 'CPU-load-only subset mismatch'}
$gpuOnly = Build-Blocks $full (New-Rec $optType @{GpuLoad=$true;GpuTemp=$true})
if($gpuOnly.Count -ne 1 -or $gpuOnly[0].Top -ne 'GPU 5% 37°' -or $gpuOnly[0].HasBottom){throw 'GPU-only subset mismatch'}
$gpuTempOnly = Build-Blocks $full (New-Rec $optType @{GpuTemp=$true})
if($gpuTempOnly.Count -ne 1 -or $gpuTempOnly[0].Top -ne 'GPU 37°' -or $gpuTempOnly[0].HasBottom){throw 'GPU-temp-only subset mismatch'}
$mixedOpt = New-Rec $optType @{GpuLoad=$true;GpuTemp=$true;Disk=$true;Download=$true;WeatherWind=$true}
$mixedBlocks = Build-Blocks $full $mixedOpt
if($mixedBlocks.Count -ne 4){throw "Mixed subset expected 4 blocks, got $($mixedBlocks.Count)"}
$rMixed = Invoke-Render $mixedBlocks 96 $true 48 $false $null
$null = Assert-LayoutValid $rMixed $mixedBlocks 'mixed item subsets' 1 $false
Save-Png $rMixed $true (Join-Path $output 'readout-subsets-dark-100.png')
$checks += 4

# ---- custom block order ----------------------------------------------------------------------------------------------------------------
$customOrder = [Array]::CreateInstance($kindType, 6)
$customOrder[0] = [Enum]::Parse($kindType, 'Batteries')
$customOrder[1] = [Enum]::Parse($kindType, 'Network')
$customOrder[2] = [Enum]::Parse($kindType, 'Weather')
$customOrder[3] = [Enum]::Parse($kindType, 'Prayer')
$customOrder[4] = [Enum]::Parse($kindType, 'MemoryDisk')
$customOrder[5] = [Enum]::Parse($kindType, 'System')
$customBlocks = Build-Blocks $full ($all | ForEach-Object { New-Rec $optType @{
 CpuLoad=$true;CpuTemp=$true;GpuLoad=$true;GpuTemp=$true;Memory=$true;Disk=$true;Upload=$true;Download=$true;Prayer=$true;WeatherTemp=$true;WeatherWind=$true;Batteries=$true;BlockOrder=$customOrder
}})
$expectedKinds = @('Batteries','Batteries','Network','Weather','Prayer','MemoryDisk','System')
for($i=0;$i -lt 7;$i++){if($customBlocks[$i].Kind.ToString() -ne $expectedKinds[$i]){throw "Custom order mismatch at $i"}}
$rCustom = Invoke-Render $customBlocks 96 $true 48 $false $null
$null = Assert-LayoutValid $rCustom $customBlocks 'custom block order' 1 $false
Save-Png $rCustom $true (Join-Path $output 'readout-custom-order-dark-100.png')
$checks++

# ---- network arrow styles (classic, triangle, colour emoji) ----------------------------------------------------------------------------
foreach($styleName in 'Arrow','Triangle','Emoji'){
 $arrowVal = [Enum]::Parse($arrowType, $styleName)
 $arrowBlocks = Build-Blocks $full (New-Rec $optType @{CpuLoad=$true;GpuLoad=$true;Upload=$true;Download=$true;NetworkArrows=$arrowVal})
 foreach($dark in $false,$true){
  $theme=if($dark){'dark'}else{'light'}
  $rArrow = Invoke-Render $arrowBlocks 96 $dark 48 $false $null
  $null = Assert-LayoutValid $rArrow $arrowBlocks "arrows $styleName $theme" 1 $false
  if($styleName -eq 'Emoji'){
   $netRect = @($rArrow.Blocks | Where-Object { $_.Kind.ToString() -eq 'Network' })[0].Pixels
   $colored = 0; $stride = $rArrow.Width * 4; $px = $rArrow.Pixels
   for($y=0;$y -lt $rArrow.Height;$y++){
    for($x=[int][math]::Ceiling($netRect.Left);$x -lt [int][math]::Floor($netRect.Right);$x++){
     $o = $y*$stride + $x*4
     $a=$px[$o+3];$b=$px[$o];$g=$px[$o+1];$r=$px[$o+2]
     if($a -ge 120 -and ([math]::Abs($r-$g) -gt 20 -or [math]::Abs($g-$b) -gt 20 -or [math]::Abs($r-$b) -gt 20)){$colored++}
    }
   }
   if($colored -eq 0){throw "Emoji arrows ($theme) drew no coloured pixels inside the Network block"}
  }
  $checks++
 }
}
$emojiBlocksOnly = Build-Blocks $full (New-Rec $optType @{CpuLoad=$true;CpuTemp=$true;GpuLoad=$true;GpuTemp=$true;Upload=$true;Download=$true;NetworkArrows=([Enum]::Parse($arrowType,'Emoji'))})
$triBlocksOnly = Build-Blocks $full (New-Rec $optType @{CpuLoad=$true;CpuTemp=$true;GpuLoad=$true;GpuTemp=$true;Upload=$true;Download=$true;NetworkArrows=([Enum]::Parse($arrowType,'Triangle'))})
Save-Png (Invoke-Render $triBlocksOnly 96 $true 48 $false $null) $true (Join-Path $output 'readout-arrows-triangle-dark-100.png')
Save-Png (Invoke-Render $emojiBlocksOnly 96 $true 48 $false $null) $true (Join-Path $output 'readout-arrows-emoji-dark-100.png')
Save-Png (Invoke-Render $emojiBlocksOnly 144 $false 72 $false $null) $false (Join-Path $output 'readout-arrows-emoji-light-150.png')

# ---- custom label/value colours on dark and light taskbars -----------------------------------------------------------------------------
$customColors = New-Rec $styleType @{LabelColorHex='#80CBC4';ValueColorHex='#FFE082'}
foreach($dark in $false,$true){
 $theme=if($dark){'dark'}else{'light'}
 $rCol = Invoke-Render $cases[0].Blocks 96 $dark 48 $false $customColors
 $null = Assert-LayoutValid $rCol $cases[0].Blocks "custom colors $theme" 1 $false
 Save-Png $rCol $dark (Join-Path $output "readout-colors-$theme-100.png")
 $rWarnCol = Invoke-Render $cases[1].Blocks 96 $dark 48 $false $customColors
 $null = Assert-LayoutValid $rWarnCol $cases[1].Blocks "custom colors warnings $theme" 1 $false
 $checks += 2
}

# ---- Left/Right zone split, compact labels, bits/sec + swapped lines, block spacing & stable width -------------------------------------
$zonesDict = [System.Collections.Generic.Dictionary[MochiDock.Core.TaskbarBlockKind, MochiDock.Core.TaskbarZone]]::new()
$zonesDict[[Enum]::Parse($kindType, 'System')] = [Enum]::Parse($zoneType, 'Left')
$zonesDict[[Enum]::Parse($kindType, 'MemoryDisk')] = [Enum]::Parse($zoneType, 'Left')
$zonesDict[[Enum]::Parse($kindType, 'Network')] = [Enum]::Parse($zoneType, 'Left')
$splitOpt = New-Rec $optType @{CpuLoad=$true;CpuTemp=$true;GpuLoad=$true;GpuTemp=$true;Memory=$true;Disk=$true;Upload=$true;Download=$true;Prayer=$true;WeatherTemp=$true;WeatherWind=$true;Batteries=$true;BlockZones=$zonesDict}
$leftBlocks = $readouts.GetMethod('BuildForZone').Invoke($null, @($full, $splitOpt, [Enum]::Parse($zoneType, 'Left')))
$rightBlocks = $readouts.GetMethod('BuildForZone').Invoke($null, @($full, $splitOpt, [Enum]::Parse($zoneType, 'Right')))
if($leftBlocks.Count -ne 3 -or $rightBlocks.Count -ne 4){throw "Left/Right zone split expected 3+4 blocks, got $($leftBlocks.Count)+$($rightBlocks.Count)"}
$rLeft = Invoke-Render $leftBlocks 96 $true 48 $false $null
$rRight = Invoke-Render $rightBlocks 96 $true 48 $false $null
$null = Assert-LayoutValid $rLeft $leftBlocks 'left zone strip' 1 $false
$null = Assert-LayoutValid $rRight $rightBlocks 'right zone strip' 1 $false
Save-Png $rLeft $true (Join-Path $output 'readout-zone-left-dark-100.png')
Save-Png $rRight $true (Join-Path $output 'readout-zone-right-dark-100.png')
$checks += 2

$compactOpt = New-Rec $optType @{CpuLoad=$true;CpuTemp=$true;GpuLoad=$true;GpuTemp=$true;Memory=$true;Disk=$true;Upload=$true;Download=$true;CompactLabels=$true;SwapNetworkLines=$true;NetworkUnit=([Enum]::Parse($unitType,'Bits'))}
$compactBlocks = Build-Blocks $full $compactOpt
if(-not $compactBlocks[0].Top.StartsWith('C ') -or -not $compactBlocks[2].Top.StartsWith([string][char]0x2193)){throw 'Compact labels or swapped bits network mismatch'}
$rCompact = Invoke-Render $compactBlocks 96 $true 48 $false $null
$null = Assert-LayoutValid $rCompact $compactBlocks 'compact labels + swapped bits' 1 $false
Save-Png $rCompact $true (Join-Path $output 'readout-compact-bits-dark-100.png')
$checks++

$spacedStyle = New-Rec $styleType @{BlockSpacing=24;StableWidth=$false}
$rSpaced = Invoke-Render $cases[0].Blocks 96 $true 48 $false $spacedStyle
$null = Assert-LayoutValid $rSpaced $cases[0].Blocks 'custom 24px block spacing' 1 $false
if($rSpaced.Width -le $r48.Width){throw "24px block spacing should widen the readout ($($rSpaced.Width) vs $($r48.Width))"}
$checks++

$stableStyle = New-Rec $styleType @{StableWidth=$true}
$stableOpt = New-Rec $optType @{CpuLoad=$true;CpuTemp=$true;GpuLoad=$true;GpuTemp=$true;Memory=$true;Disk=$true;Upload=$true;Download=$true}
$rStableFull = Invoke-Render (Build-Blocks $full $stableOpt) 96 $true 48 $false $stableStyle
$rStableHot = Invoke-Render (Build-Blocks $hot $stableOpt) 96 $true 48 $false $stableStyle
$null = Assert-LayoutValid $rStableFull (Build-Blocks $full $stableOpt) 'stable width full' 1 $false
$null = Assert-LayoutValid $rStableHot (Build-Blocks $hot $stableOpt) 'stable width hot' 1 $false
if($rStableFull.Width -ne $rStableHot.Width){throw "Stable width changed between full ($($rStableFull.Width) px) and hot ($($rStableHot.Width) px)"}
$checks++

# ---- device battery layouts (single tight Auto, 2-device stacked Auto, 2-device classic Horizontal) ------------------------------------
$oneMouse = [Array]::CreateInstance($batType, 1)
$oneMouse[0] = [Activator]::CreateInstance($batType,@([string][char]0xE962,'Mouse','2.4G',[Nullable[int]]39,$false,$false,$now))
$oneSnap = New-Rec $snapType @{NetUpBytesPerSecond=[Nullable[double]](9*1024);NetDownBytesPerSecond=[Nullable[double]](16.9*1024);WeatherTempC=[Nullable[double]]13;WindMetersPerSecond=[Nullable[double]]4;WeatherGlyph=[string][char]0x2601;Batteries=$oneMouse}
$oneBatOnly = Build-Blocks $oneSnap (New-Rec $optType @{Batteries=$true})
$rOneBat = Invoke-Render $oneBatOnly 96 $true 48 $false $stableStyle
$null = Assert-LayoutValid $rOneBat $oneBatOnly 'single battery tight width' 1 $false
if($rOneBat.Width -ge 68){throw "Single battery block should stay compact without gap ($($rOneBat.Width) px)"}
$oneCombo = Build-Blocks $oneSnap (New-Rec $optType @{WeatherTemp=$true;WeatherWind=$true;Upload=$true;Download=$true;NetworkArrows=([Enum]::Parse($arrowType,'Triangle'));Batteries=$true})
$rOneCombo = Invoke-Render $oneCombo 96 $true 48 $false $stableStyle
$null = Assert-LayoutValid $rOneCombo $oneCombo 'weather + network + single battery' 1 $false
Save-Png $rOneCombo $true (Join-Path $output 'readout-battery-single-dark-100.png')

$twoDevs = [Array]::CreateInstance($batType, 2); $twoDevs[0] = $oneMouse[0]; $twoDevs[1] = $keys
$twoSnap = New-Rec $snapType @{Batteries=$twoDevs}
$twoStacked = Build-Blocks $twoSnap (New-Rec $optType @{Batteries=$true;BatteryLayout=([Enum]::Parse($batLayoutType,'Auto'))})
$rTwoStacked = Invoke-Render $twoStacked 96 $true 48 $false $stableStyle
$null = Assert-LayoutValid $rTwoStacked $twoStacked 'two batteries stacked' 1 $false
Save-Png $rTwoStacked $true (Join-Path $output 'readout-battery-two-stacked-dark-100.png')

$horizStyle = New-Rec $styleType @{StableWidth=$true;BatteryLayout=([Enum]::Parse($batLayoutType,'Horizontal'))}
$twoHoriz = Build-Blocks $twoSnap (New-Rec $optType @{Batteries=$true;BatteryLayout=([Enum]::Parse($batLayoutType,'Horizontal'))})
if($twoHoriz.Count -ne 2){throw "Horizontal battery layout expected 2 blocks, got $($twoHoriz.Count)"}
$rTwoHoriz = Invoke-Render $twoHoriz 96 $true 48 $false $horizStyle
$null = Assert-LayoutValid $rTwoHoriz $twoHoriz 'two batteries horizontal classic' 1 $false
Save-Png $rTwoHoriz $true (Join-Path $output 'readout-battery-horizontal-dark-100.png')
$checks += 3

# ---- prayer remainder display modes and 10-minute warning highlight ---------------------------------------------------------------------
$prayerSnap = New-Rec $snapType @{PrayerName='Maghrib';PrayerTime='16:40';PrayerRemaining=[Nullable[TimeSpan]][TimeSpan]::FromMinutes(85)}
$prayerBoth = Build-Blocks $prayerSnap (New-Rec $optType @{Prayer=$true;PrayerDisplay=([Enum]::Parse($prayerDisplayType,'TimeAndRemaining'))})
if($prayerBoth[0].Top -ne 'Maghrib 16:40' -or $prayerBoth[0].Bottom -ne 'in 1h 25m'){throw "TimeAndRemaining prayer mismatch: $($prayerBoth[0].Top) / $($prayerBoth[0].Bottom)"}
$rPrayerBoth = Invoke-Render $prayerBoth 96 $true 48 $false $stableStyle
$null = Assert-LayoutValid $rPrayerBoth $prayerBoth 'prayer time and remaining' 1 $false
Save-Png $rPrayerBoth $true (Join-Path $output 'readout-prayer-both-dark-100.png')

$prayerSoonSnap = New-Rec $snapType @{PrayerName='Asr';PrayerTime='15:20';PrayerRemaining=[Nullable[TimeSpan]][TimeSpan]::FromMinutes(8)}
$prayerSoon = Build-Blocks $prayerSoonSnap (New-Rec $optType @{Prayer=$true;PrayerDisplay=([Enum]::Parse($prayerDisplayType,'Remaining'))})
if(-not $prayerSoon[0].BottomWarn -or $prayerSoon[0].Bottom -ne 'in 8m'){throw '10-minute prayer warning state mismatch'}
$rPrayerSoon = Invoke-Render $prayerSoon 96 $true 48 $false $stableStyle
$null = Assert-LayoutValid $rPrayerSoon $prayerSoon 'prayer 10m warning remaining' 1 $false
Save-Png $rPrayerSoon $true (Join-Path $output 'readout-prayer-soon-dark-100.png')
$checks += 2

$measure|Set-Content (Join-Path $output 'measurements.txt')

# ---- plate behind the text & empty readout ---------------------------------------------------------------------------------------------
$plain=Invoke-Render $cases[0].Blocks 96.0 $true 48 $false $null;$plate=Invoke-Render $cases[0].Blocks 96.0 $true 48 $true $null
if($plain.Width -ne $plate.Width -or @($plain.Blocks).Count -ne @($plate.Blocks).Count){throw 'The background plate changed the readout size'}
$checks++
$none=[Array]::CreateInstance($blockType,0)
if($null -ne (Invoke-Render $none 96.0 $true 48 $false $null)){throw 'An empty readout should render nothing'}
$checks++

Write-Output "Verified $checks taskbar readout checks: GPU line present, 40/48/64(Windhawk)/30px heights at 100/125/150/200%, item subsets, custom order, classic/triangle/colour-emoji arrows, bold/explicit fonts, custom light/dark colours, hit rectangles, contrast and warnings. PNGs: $output"
