$ErrorActionPreference='Stop'
# Tolerances: one device pixel for centring and for the two edges. Even spacing compares four spaces whose eight edges are each
# snapped to the pixel grid (and the marquee snaps the clock text), so the largest and smallest space may differ by up to 1.5 DIPs (at most about one pixel off at 100%).
# Renders the resting capsule [Mochi] clock [weather] with the production arranger (MochiDock.Controls.CapsuleClockWeatherArranger)
# in both spacing modes and checks EqualEdges (clock centred, equal edges) and EvenSpacing (rim, Mochi, clock, weather, rim equally spaced), and that nothing
# is clipped. Never starts MochiDock. Output: MochiDock/obj/clock-weather-balance (one PNG per case plus a contact sheet).
$setup=(Get-Content (Join-Path $PSScriptRoot 'verify-prayer-capsule.ps1') -Raw).Split('$checks=0')[0]
$setup=$setup.Replace('$PSScriptRoot',('{0}{1}{0}' -f [char]39,$PSScriptRoot))
Invoke-Expression $setup
$output=Join-Path $repo 'MochiDock/obj/clock-weather-balance';$null=New-Item -ItemType Directory -Force $output
$arranger=$assembly.GetType('MochiDock.Controls.CapsuleClockWeatherArranger')
$sheet=[Collections.Generic.List[object]]::new();$checked=0
function Get-Bounds($element,$root){$p=$element.TransformToAncestor($root).Transform([Windows.Point]::new(0,0));[Windows.Rect]::new($p.X,$p.Y,$element.ActualWidth,$element.ActualHeight)}
$alignmentType=$assembly.GetType('MochiDock.Core.CapsuleClockAlignment',$false) ?? [Reflection.Assembly]::LoadFrom((Join-Path (Split-Path $assembly.Location) 'MochiDock.Core.dll')).GetType('MochiDock.Core.CapsuleClockAlignment')
foreach($alignmentName in @('EqualEdges','EvenSpacing')){$alignment=[Enum]::Parse($alignmentType,$alignmentName)
foreach($theme in @('Dark','Matcha')){foreach($scale in @(1,1.5,2)){foreach($case in @(@('20:50',11),@('23:58',-12),@('08:08',104),@('11:11',7))){
 $reading=$case[0];$temperature=$case[1]
 $vm=New-Model $true 'Maghrib';Set-Field $vm '_pomodoroService' $null
 $settings=$vmType.GetField('_settings',$flags).GetValue($vm);$settings.EnablePrayerPreNotification=$false;$settings.ShowPrayerInCapsule=$false
 $vmType.GetField('_weatherInfo',$flags).GetValue($vm).Temperature=$temperature
 $grid=[Windows.Markup.XamlReader]::Parse($normal,$context);$grid.DataContext=$vm
 $surface=[Activator]::CreateInstance($assembly.GetType('MochiDock.Controls.IslandChrome'));$surface.Height=36;$surface.Child=$grid;$surface.CornerRadius=[Windows.CornerRadius]::new(18);$surface.BorderThickness=[Windows.Thickness]::new(1.2)
 $surface.SetResourceReference([Windows.Documents.TextElement]::FontFamilyProperty,'IslandUiFont');[Windows.Controls.TextBlock]::SetFontSize($surface,12)
 $fixtureWindow=[Windows.Window]::new();$fixtureWindow.Content=$surface;$assembly.GetType('MochiDock.Services.ThemeService').GetMethod('ApplyTheme').Invoke($null,@($fixtureWindow,$theme));$fixtureWindow.Content=$null
 $surface.Background=$app.Resources['IslandGroundBrush'];$surface.BorderBrush=$app.Resources['IslandBorderBrush'];$surface.UseLayoutRounding=$true
 $parameters=[Windows.Interop.HwndSourceParameters]::new('Isolated clock weather balance');$parameters.WindowStyle=-2147483648;$parameters.PositionX=-32000;$parameters.PositionY=-32000;$parameters.Width=1;$parameters.Height=1
 $native=[Windows.Interop.HwndSource]::new($parameters);$native.RootVisual=$surface
 try{
  [Windows.Media.VisualTreeHelper]::SetRootDpi($surface,[Windows.DpiScale]::new($scale,$scale))
  $surface.Width=300;$surface.Measure([Windows.Size]::new(300,36));$surface.Arrange([Windows.Rect]::new(0,0,300,36));$surface.UpdateLayout()
  [Windows.Threading.Dispatcher]::CurrentDispatcher.Invoke([Windows.Threading.DispatcherPriority]::Loaded,[Action]{})
  $visuals=[Collections.Generic.List[object]]::new();Find-Visuals $grid $visuals
  foreach($visual in $visuals){$assembly.GetType('MochiDock.Services.ThemeService').GetMethod('ApplyElementStyle').Invoke($null,@($visual.PSObject.BaseObject,($theme -eq 'Matcha')))}
  foreach($m in $visuals){if($m.GetType().FullName -eq 'MochiDock.Controls.MascotControl'){$m.ApplyTheme($theme -eq 'Matcha')}}
  if(!$vm.IsWeatherInMainPill){throw 'Fixture is not in the weather capsule state'}
  $marquee=$grid.FindName('CapsuleMarquee');$mascot=$grid.FindName('PillMascot');$badge=$grid.FindName('PillWeatherBadge')
  $border=$surface.BorderThickness.Left+$surface.BorderThickness.Right
  $layout=$arranger.GetMethod('Measure').Invoke($null,@($marquee,$reading,$mascot,$badge,$border,$alignment))
  if($null -eq $layout){throw 'Arranger returned no layout'}
  $arranger.GetMethod('Apply').Invoke($null,@($layout,$grid.ColumnDefinitions[0],$grid.ColumnDefinitions[2],$mascot,$grid.FindName('PillRightAnchors'),$badge,$grid.FindName('PillTickerContent')))
  $width=$layout.PillWidth;$surface.Width=$width;$surface.Measure([Windows.Size]::new($width,36));$surface.Arrange([Windows.Rect]::new(0,0,$width,36));$surface.UpdateLayout()
  $marquee.Text=$reading;$marquee.UpdateMarquee($true);$surface.UpdateLayout()

  # The clock glyphs: the visible TextBlock inside the marquee that shows the reading.
  $clockText=$null;$all=[Collections.Generic.List[object]]::new();Find-Visuals $marquee $all
  foreach($t in $all){if($t -is [Windows.Controls.TextBlock] -and $t.Text -eq $reading -and $t.IsVisible){$clockText=$t;break}}
  if(!$clockText){throw "Clock text $reading not found"}
  $c=Get-Bounds $clockText $surface;$m=Get-Bounds $mascot $surface;$w=Get-Bounds $badge $surface;$col=Get-Bounds $marquee $surface
  $pillCentre=$width/2;$clockCentre=$c.X+$c.Width/2
  $gapLeft=$c.Left-$m.Right;$gapRight=$w.Left-$c.Right
  $leftEdge=$m.Left-$surface.BorderThickness.Left;$rightEdge=($width-$surface.BorderThickness.Right)-$w.Right
  if($alignmentName -eq 'EqualEdges' -and [Math]::Abs($clockCentre-$pillCentre)*$scale -gt 1.01){throw "Clock not centred ($theme x$scale $reading): clock $clockCentre vs pill $pillCentre"}
  if($alignmentName -eq 'EvenSpacing'){$spaces=@($leftEdge,$gapLeft,$gapRight,$rightEdge);if((($spaces|Measure-Object -Maximum).Maximum-($spaces|Measure-Object -Minimum).Minimum) -gt 1.5){throw "Spaces differ ($theme x$scale $reading $temperature): $leftEdge | $gapLeft | $gapRight | $rightEdge"}}
  if($alignmentName -eq 'EqualEdges' -and [Math]::Abs($leftEdge-$rightEdge)*$scale -gt 1.01){throw "Edges differ ($theme x$scale $reading $temperature): Mochi $leftEdge vs weather $rightEdge"}
  if($c.Left -lt $col.Left-0.5 -or $c.Right -gt $col.Right+0.5){throw "Clock clipped ($theme x$scale $reading): text $($c.Left)-$($c.Right), column $($col.Left)-$($col.Right)"}
  if($rightEdge -lt 6 -or $leftEdge -lt 6){throw "Item too close to the capsule edge ($theme x$scale $reading $temperature): left $leftEdge right $rightEdge"}
  $checked++
  Write-Output ('{11,-10} {0,-6} x{1,-4} {2} {3,4}°  width {4,6:N1}  clock centre {5,6:N2}/{6,6:N2}  gaps {7,5:N2} | {8,5:N2}  edges {9,5:N2} | {10,5:N2}' -f $theme,$scale,$reading,$temperature,$width,$clockCentre,$pillCentre,$gapLeft,$gapRight,$leftEdge,$rightEdge,$alignmentName)

  $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new([int][Math]::Ceiling($width*$scale),[int](36*$scale),96*$scale,96*$scale,[Windows.Media.PixelFormats]::Pbgra32);$bitmap.Render($surface)
  $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
  $file=Join-Path $output ("{4}-{0}-{1}-{2}-{3}.png" -f $theme,$scale,$reading.Replace(':',''),$temperature,$alignmentName);$stream=[IO.File]::Create($file);try{$encoder.Save($stream)}finally{$stream.Dispose()}
  if($scale -eq 2){$sheet.Add([pscustomobject]@{Alignment=$alignmentName;Border=$surface.BorderThickness.Left;Theme=$theme;File=$file;Width=$width;ClockCentre=$clockCentre;Mascot=$m;Clock=$c;Weather=$w})}
 }finally{$native.Dispose()}
}}}}

# Contact sheet at 2x: each capsule with its centre line (red) and its four spaces (blue).
$visual=[Windows.Media.DrawingVisual]::new();$dc=$visual.RenderOpen();$y=10;$sheetWidth=0
$dc.DrawRectangle([Windows.Media.Brushes]::White,$null,[Windows.Rect]::new(0,0,1000,10+$sheet.Count*92))
foreach($row in $sheet){
 $img=[Windows.Media.Imaging.BitmapImage]::new();$img.BeginInit();$img.CacheOption='OnLoad';$img.UriSource=[Uri]::new($row.File);$img.EndInit()
 $x=20;$dc.DrawImage($img,[Windows.Rect]::new($x,$y,$row.Width*2,72))
 $red=[Windows.Media.Pen]::new([Windows.Media.Brushes]::Red,1);$blue=[Windows.Media.Pen]::new([Windows.Media.Brushes]::DodgerBlue,2)
 $cx=$x+$row.ClockCentre*2;$dc.DrawLine($red,[Windows.Point]::new($cx,$y-4),[Windows.Point]::new($cx,$y+76))
 $px=$x+$row.Width;$dc.DrawLine([Windows.Media.Pen]::new([Windows.Media.Brushes]::Gray,1),[Windows.Point]::new($px,$y+74),[Windows.Point]::new($px,$y+80))
 $by=$y+80;$inL=$x+$row.Border*2;$inR=$x+($row.Width-$row.Border)*2
 foreach($pair in @(@($inL,($x+$row.Mascot.Left*2)),@(($x+$row.Mascot.Right*2),($x+$row.Clock.Left*2)),@(($x+$row.Clock.Right*2),($x+$row.Weather.Left*2)),@(($x+$row.Weather.Right*2),$inR))){$dc.DrawLine($blue,[Windows.Point]::new($pair[0]+1,$by),[Windows.Point]::new($pair[1]-1,$by))}
 $sheetWidth=[Math]::Max($sheetWidth,$x+$row.Width*2+20);$y+=92
}
$dc.Close()
$bmp=[Windows.Media.Imaging.RenderTargetBitmap]::new([int]$sheetWidth,[int]$y,96,96,[Windows.Media.PixelFormats]::Pbgra32);$bmp.Render($visual)
$enc=[Windows.Media.Imaging.PngBitmapEncoder]::new();$enc.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bmp));$fs=[IO.File]::Create((Join-Path $output 'contact-sheet.png'));try{$enc.Save($fs)}finally{$fs.Dispose()}
Write-Output "Verified $checked clock/weather capsules in both spacing modes: centred clock with equal edges, or four equal spaces; no clipping. Sheet: $(Join-Path $output 'contact-sheet.png')"
$app.Shutdown()
