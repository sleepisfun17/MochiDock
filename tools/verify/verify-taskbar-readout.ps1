param([string]$Configuration='Release')
# Renders the taskbar readout (TaskbarReadoutRenderer, the same drawing the widget uses) without a taskbar and without starting MochiDock:
# sample blocks at 100/125/150/200% on light and dark taskbars, for the 40 px (Windows 10) and 48 px (Windows 11) heights and the small-taskbar
# one-line fallback. Checks that two lines fit, that nothing is clipped or touches the next block, that the hit rectangles are contiguous, match the
# blocks and hit-test back to their kind, that the text keeps 4.5:1 against the Windows taskbar colours and that warnings are drawn in the warning
# colour. Writes PNGs to MochiDock/obj/taskbar-readout.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework
$repo=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$directory=Join-Path $repo "MochiDock/bin/$Configuration/net8.0-windows10.0.19041.0"
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $directory 'MochiDock.dll'))
$core=[Reflection.Assembly]::LoadFrom((Join-Path $directory 'MochiDock.Core.dll'))
[AppDomain]::CurrentDomain.add_AssemblyResolve({param($s,$e) if($e.Name.StartsWith('MochiDock.Core')){return $core} if($e.Name.StartsWith('MochiDock')){return $assembly} return $null})
$app=[Windows.Application]::new();$app.ShutdownMode=[Windows.ShutdownMode]::OnExplicitShutdown
[xml]$source=Get-Content (Join-Path $repo 'MochiDock/App.xaml') -Raw
foreach($node in $source.SelectNodes('//*[local-name()="FontFamily"]')){$app.Resources[$node.GetAttribute('Key','http://schemas.microsoft.com/winfx/2006/xaml')]=[Windows.Media.FontFamily]::new([uri]'pack://application:,,,/',$node.InnerText)}
$app.Resources['IslandUiFont']=$app.Resources['QuicksandFont']   # what ThemeService.ApplyTheme sets in the running app
$output=Join-Path $repo 'MochiDock/obj/taskbar-readout';$null=New-Item -ItemType Directory -Force $output

$blockType=$core.GetType('MochiDock.Core.TaskbarBlock');$snapType=$core.GetType('MochiDock.Core.TaskbarSnapshot');$optType=$core.GetType('MochiDock.Core.TaskbarOptions')
$batType=$core.GetType('MochiDock.Core.TaskbarBattery');$kindType=$core.GetType('MochiDock.Core.TaskbarBlockKind');$readouts=$core.GetType('MochiDock.Core.TaskbarReadouts')
$renderer=$assembly.GetType('MochiDock.Services.TaskbarReadoutRenderer')
function New-Rec($type,$props){$o=[Activator]::CreateInstance($type);foreach($k in $props.Keys){$o.GetType().GetProperty($k).SetValue($o,$props[$k])};return $o}
function Build-Blocks($snapshot,$options){return ,($readouts.GetMethod('Build').Invoke($null,@($snapshot,$options)))}
function Lum($c){$f={param($v)$x=$v/255.0;if($x -le 0.03928){$x/12.92}else{[math]::Pow(($x+0.055)/1.055,2.4)}};return 0.2126*(& $f $c.R)+0.7152*(& $f $c.G)+0.0722*(& $f $c.B)}
function Contrast($a,$b){$x=Lum $a;$y=Lum $b;$hi=[math]::Max($x,$y);$lo=[math]::Min($x,$y);return ($hi+0.05)/($lo+0.05)}

# ---- sample data -----------------------------------------------------------------------------------------------------------------------
$now=[DateTimeOffset]::UtcNow
$mouse=[Activator]::CreateInstance($batType,@([string][char]0xE962,'Mouse','2.4G',[Nullable[int]]64,$false,$false,$now))
$keys=[Activator]::CreateInstance($batType,@([string][char]0xE765,'Keyboard','Bluetooth',[Nullable[int]]82,$true,$false,$now))
$low=[Activator]::CreateInstance($batType,@([string][char]0xE961,'Headset','HID',[Nullable[int]]9,$false,$true,$now))
$pad=[Activator]::CreateInstance($batType,@([string][char]0xE7FC,'Gamepad','Bluetooth',[Nullable[int]]55,$false,$false,$now))
$batteries=[Array]::CreateInstance($batType,4);$batteries[0]=$mouse;$batteries[1]=$keys;$batteries[2]=$low;$batteries[3]=$pad
$all=New-Rec $optType @{System=$true;MemoryDisk=$true;Network=$true;Prayer=$true;Weather=$true;Batteries=$true}
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

# ---- rendering and checks --------------------------------------------------------------------------------------------------------------
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
    $render=$renderer.GetMethod('Render').Invoke($null,@($blocks,$dpi,$dark,$heightPx,$false))
    if($null -eq $render){throw "${label}: nothing rendered"}
    if($render.OneLine -ne $oneLine){throw "${label}: one-line layout should be $oneLine"}
    if($render.Height -ne $heightPx -or $render.Pixels.Length -ne $render.Width*$render.Height*4){throw "${label}: the bitmap size is wrong"}
    $rects=@($render.Blocks)
    if($rects.Count -ne $blocks.Count){throw "${label}: $($rects.Count) rectangles for $($blocks.Count) blocks"}
    $stride=$render.Width*4;$px=$render.Pixels
    # the hit rectangles: in block order, contiguous, the full height, from the left edge to the right edge, and they hit-test back to their block
    for($i=0;$i -lt $rects.Count;$i++){
     $r=$rects[$i].Pixels
     if($rects[$i].Kind -ne $blocks[$i].Kind){throw "${label}: rectangle $i is for $($rects[$i].Kind), the block is $($blocks[$i].Kind)"}
     if([math]::Abs($r.Height-$render.Height) -gt 0.5 -or $r.Top -ne 0){throw "${label}: rectangle $i is not the full height"}
     if($i -eq 0 -and [math]::Abs($r.Left) -gt 0.5){throw "${label}: the first rectangle does not start at the left edge"}
     if($i -gt 0 -and [math]::Abs($r.Left-$rects[$i-1].Pixels.Right) -gt 0.5){throw "${label}: rectangles $($i-1) and $i are not contiguous"}
     if($i -eq $rects.Count-1 -and [math]::Abs($r.Right-$render.Width) -gt 0.5){throw "${label}: the last rectangle does not reach the right edge"}
     $hit=$renderer.GetMethod('HitTest').Invoke($null,@($render.Blocks,[double]($r.Left+$r.Width/2)))
     if($hit -ne $blocks[$i].Kind){throw "${label}: a click in the middle of block $i hits $hit, not $($blocks[$i].Kind)"}
     $checks++
    }
    # nothing is clipped: the ink keeps a pixel off the top and bottom and clear of the sides, and nothing is drawn across a block boundary
    $box=Ink-Box $px $stride 0 $render.Width $render.Height 60
    if($box.Count -eq 0){throw "${label}: nothing visible was drawn"}
    if($box.MinY -lt 1 -or $box.MaxY -gt $render.Height-2){throw "${label}: the text touches the top or bottom edge (ink rows $($box.MinY) to $($box.MaxY) of $($render.Height))"}
    if($box.MinX -lt 2 -or $box.MaxX -gt $render.Width-3){throw "${label}: the text touches the left or right edge (ink columns $($box.MinX) to $($box.MaxX) of $($render.Width))"}
    for($i=0;$i -lt $rects.Count;$i++){
     $r=$rects[$i].Pixels;$inner=Ink-Box $px $stride ([math]::Ceiling($r.Left)) ([math]::Floor($r.Right)) $render.Height 60
     if($inner.Count -eq 0){throw "${label}: block $i ($($blocks[$i].Kind)) drew nothing"}
     if($i -gt 0){ # ink within 2 px of a boundary would mean two blocks touch
      $edge=Ink-Box $px $stride ([math]::Round($r.Left)-1) ([math]::Round($r.Left)+1) $render.Height 60
      if($edge.Count -gt 0){throw "${label}: ink on the boundary between block $($i-1) and block $i (blocks touch or overlap)"}
     }
     # two lines fit: a two-line block's ink spans more than one line of text but stays inside the taskbar
     $hasBottom=-not [string]::IsNullOrEmpty($blocks[$i].Bottom)
     $inkHeight=$inner.MaxY-$inner.MinY+1
     if(!$oneLine -and $hasBottom -and $inkHeight -lt 1.5*8*$scale){throw "${label}: block $i should have two lines but its ink is only $inkHeight px high"}
     if($inkHeight -gt $render.Height-2){throw "${label}: block $i is $inkHeight px high in a $($render.Height) px taskbar"}
    }
    # warnings are drawn in the warning colour (and only those blocks)
    foreach($i in 0..($rects.Count-1)){
     $r=$rects[$i].Pixels;$red=0
     for($y=0;$y -lt $render.Height;$y++){for($x=[int][math]::Ceiling($r.Left);$x -lt [int][math]::Floor($r.Right);$x++){$o=$y*$stride+$x*4;if($px[$o+3] -ge 200 -and $px[$o+2] -gt 150 -and $px[$o+2] -gt 2*$px[$o+1] -and $px[$o+2] -gt 2*$px[$o]){$red++}}}
     $expectWarn=$case.Warn -contains $rects[$i].Kind.ToString()
     $isWarnBlock=$blocks[$i].TopWarn -or $blocks[$i].BottomWarn
     if($case.Name -eq 'warnings' -and $isWarnBlock -and $red -eq 0 -and $rects[$i].Kind.ToString() -ne 'Batteries'){throw "${label}: the warning block $i ($($rects[$i].Kind)) has no warning colour"}
     if($case.Name -eq 'full' -and $red -gt 0 -and $rects[$i].Kind.ToString() -ne 'Batteries'){throw "${label}: block $i ($($rects[$i].Kind)) is red but is not a warning"}
    }
    $measure+=("{0}: {1}x{2} px, {3} blocks, ink rows {4}-{5}" -f $label,$render.Width,$render.Height,$rects.Count,$box.MinY,$box.MaxY)
    if($case.Name -in 'full','warnings' -or $heightDip -eq 30){Save-Png $render $dark (Join-Path $output ("readout-{0}-{1}-{2}px-{3}.png" -f $case.Name,$theme,$heightDip,[int]($scale*100)))}
    $checks++
   }
  }
 }
}
$measure|Set-Content (Join-Path $output 'measurements.txt')

# ---- the plate behind the text (the optional background) must not change the block rectangles
$plain=$renderer.GetMethod('Render').Invoke($null,@($cases[0].Blocks,96.0,$true,48,$false));$plate=$renderer.GetMethod('Render').Invoke($null,@($cases[0].Blocks,96.0,$true,48,$true))
if($plain.Width -ne $plate.Width -or @($plain.Blocks).Count -ne @($plate.Blocks).Count){throw 'The background plate changed the readout size'}
$checks++
# ---- nothing to show: nothing is drawn
$none=[Array]::CreateInstance($blockType,0)
if($null -ne $renderer.GetMethod('Render').Invoke($null,@($none,96.0,$true,48,$false))){throw 'An empty readout should render nothing'}
$checks++
Write-Output "Verified $checks taskbar readout checks: two lines fit 40 and 48 px taskbars and a small taskbar falls back to one line at 100/125/150/200%, no clipping or touching blocks, hit rectangles match the blocks, text contrast on light and dark taskbars, warnings in the warning colour. PNGs: $output"
