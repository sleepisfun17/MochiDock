$ErrorActionPreference='Stop'
# Renders the Battery page's full-width "This PC" power panel (BatteryModuleView.BuildPowerPanel) with sample histories, without
# launching the app: a desktop estimate (stacked other / CPU / GPU) and a laptop battery rate (one area), both themes, four scales.
# Load only resources and service-free view-model test data.
$setup=(Get-Content (Join-Path $PSScriptRoot 'verify-prayer-capsule.ps1') -Raw).Split('$checks=0')[0]
$setup=$setup.Replace('$PSScriptRoot',('{0}{1}{0}' -f [char]39,$PSScriptRoot));Invoke-Expression $setup
$output=Join-Path $repo 'MochiDock/obj/power-chart';$null=New-Item -ItemType Directory -Force $output
$core=[Reflection.Assembly]::LoadFrom((Join-Path (Split-Path $assembly.Location) 'MochiDock.Core.dll'))
$historyType=$core.GetType('MochiDock.Core.PowerHistory');$readingType=$core.GetType('MochiDock.Core.PowerReading');$sourceType=$core.GetType('MochiDock.Core.PowerSource')
$viewType=$assembly.GetType('MochiDock.Views.Cards.BatteryModuleView');$gaugeType=$assembly.GetType('MochiDock.Views.Cards.BatteryGauge')
function Get-InkRect($text,$root){$w=[Math]::Min($text.ActualWidth,$text.DesiredSize.Width-$text.Margin.Left-$text.Margin.Right);$x=switch($text.TextAlignment){'Right'{$text.ActualWidth-$w}'Center'{($text.ActualWidth-$w)/2}default{0}};return $text.TransformToAncestor($root).TransformBounds([Windows.Rect]::new($x,0,$w,$text.ActualHeight))}
function Test-Shown($element,$root){for($e=$element;$e -and $e -ne $root;$e=[Windows.Media.VisualTreeHelper]::GetParent($e)){if($e -is [Windows.UIElement] -and $e.Visibility -ne [Windows.Visibility]::Visible){return $false}};return $true}
$checks=0
foreach($theme in @('Dark','Matcha')){foreach($scale in @(1,1.25,1.5,2)){foreach($kind in @('desktop','laptop','starting')){
 $bootstrap=[Windows.Window]::new();$assembly.GetType('MochiDock.Services.ThemeService').GetMethod('ApplyTheme').Invoke($null,@($bootstrap,$theme))
 $vm=New-Model $false 'Dhuhr'
 # Half a history (25 of 60 readings), so the right-anchored fill is visible too; "starting" has one reading (chart not drawn yet).
 $history=[Activator]::CreateInstance($historyType,@([int]60));$count=if($kind -eq 'starting'){1}else{25}
 for($i=0;$i -lt $count;$i++){
  if($kind -eq 'laptop'){$history.Add(14+6*[Math]::Sin($i/3),$null,$null)}
  else{$cpu=40+25*[Math]::Sin($i/4);$gpu=if($i -gt 12){180+40*[Math]::Sin($i/2)}else{25};$history.Add($cpu+$gpu+35,$cpu,$gpu)}
 }
 Set-Field $vm '_powerHistory' $history
 $last=$history.Points()[$history.Count-1]
 $reading=if($kind -eq 'laptop'){[Activator]::CreateInstance($readingType,@([double]$last.Total,[Enum]::Parse($sourceType,'Measured'),[DateTimeOffset]::Now,'Battery discharge rate',$null,$null))}else{[Activator]::CreateInstance($readingType,@([double]$last.Total,[Enum]::Parse($sourceType,'Estimated'),[DateTimeOffset]::Now,'CPU + GPU + 35 other',$last.CpuW,$last.GpuW))}
 # The page itself is built without its constructor (no timers, power notifications or XAML); only BuildPowerPanel runs.
 $view=[Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject($viewType)
 Set-Field $view 'vm' $vm;Set-Field $view 'power' $reading;Set-Field $view 'powerLaptop' ($kind -eq 'laptop')
 $gauge=[Activator]::CreateInstance($gaugeType)
 if($kind -eq 'laptop'){$gauge.SetValue($gaugeType.GetField("PercentProperty").GetValue($null),[object][int]62);$args2=@('On battery','IslandMutedInkBrush',$gauge,'62%','3h 10m left')}else{$gauge.IsPsu=$true;$args2=@('Wall power','IslandPositiveBrush',$gauge,'','Estimated from CPU + GPU')}
 $panel=$viewType.GetMethod('BuildPowerPanel',$flags).Invoke($view,$args2)
 # The Hardware card body is 620 wide with 12 padding; the page keeps cards at half the page height (84 at the smallest).
 $panel.Height=84;$panel.Margin=[Windows.Thickness]::new(0)
 $surface=[Windows.Controls.Border]::new();$surface.Width=588;$surface.Height=84;$surface.Child=$panel
 $surface.SetResourceReference([Windows.Documents.TextElement]::FontFamilyProperty,'IslandUiFont');$surface.UseLayoutRounding=$true
 [Windows.Media.TextOptions]::SetTextRenderingMode($surface,[Windows.Media.TextRenderingMode]::Grayscale);[Windows.Media.TextOptions]::SetTextFormattingMode($surface,[Windows.Media.TextFormattingMode]::Ideal)
 $themeWindow=[Windows.Window]::new();$assembly.GetType('MochiDock.Services.ThemeService').GetMethod('ApplyTheme').Invoke($null,@($themeWindow,$theme));$surface.Background=$app.Resources["IslandGroundBrush"]
 [Windows.Media.VisualTreeHelper]::SetRootDpi($surface,[Windows.DpiScale]::new($scale,$scale));$surface.Measure([Windows.Size]::new(588,84));$surface.Arrange([Windows.Rect]::new(0,0,588,84));$surface.UpdateLayout()
 # The chart was sized by layout: draw it again with the history now that it has a size (as a real sample would).
 $viewType.GetMethod('ApplyPower',$flags).Invoke($view,@());$surface.UpdateLayout()
 $visuals=[Collections.Generic.List[object]]::new();Find-Visuals $surface $visuals
 $chart=$visuals|Where-Object{$_.GetType().Name -eq 'PowerChartView'}|Select-Object -First 1
 if(!$chart){throw "Power chart missing: $theme/$scale/$kind"}
 if($chart.ActualWidth -lt 200 -or $chart.ActualHeight -lt 30){throw "Power chart too small: $theme/$scale/$kind $($chart.ActualWidth)x$($chart.ActualHeight)"}
 $g=$chart.Geometry
 if($kind -eq 'starting'){
  if($g){throw 'One reading must not draw a chart'}
  if(!($visuals|Where-Object{$_ -is [Windows.Controls.TextBlock] -and $_.Text -like 'Collecting power readings*' -and (Test-Shown $_ $surface)})){throw 'Empty chart state missing'}
 } else {
  if(!$g){throw "Power chart not drawn: $theme/$scale/$kind"}
  if([Math]::Abs($g.TotalLine[$g.TotalLine.Count-1].X-$chart.ActualWidth) -gt 0.01){throw "Newest reading is not at the right edge: $theme/$scale/$kind"}
  if($g.TotalLine[0].X -lt $chart.ActualWidth/2){throw "A half history should fill only the right half: $theme/$scale/$kind"}
  if(($kind -eq 'desktop') -ne $g.HasBreakdown){throw "Breakdown shown for the wrong kind: $kind"}
 }
 # Texts: inside the panel, never overlapping, and readable against the panel.
 $texts=@($visuals|Where-Object{$_ -is [Windows.Controls.TextBlock] -and $_.Text -and (Test-Shown $_ $surface)}|ForEach-Object{[pscustomobject]@{Text=$_.Text;Visual=$_;Rect=(Get-InkRect $_ $surface)}})
 foreach($t in $texts){
  $r=$t.Rect;if($r.Left -lt -0.5 -or $r.Top -lt -0.5 -or $r.Right -gt 588.5 -or $r.Bottom -gt 84.5){throw "Power panel text clips: $theme/$scale/$kind '$($t.Text)' $r"}
  if($t.Visual.TextTrimming -ne 'None' -and $t.Visual.DesiredSize.Width-$t.Visual.Margin.Left-$t.Visual.Margin.Right -gt $t.Visual.ActualWidth+0.5 -and $t.Text -ne $t.Visual.ToolTip){throw "Power panel text is cut: $theme/$scale/$kind '$($t.Text)'"}
 }
 for($i=0;$i -lt $texts.Count;$i++){for($j=$i+1;$j -lt $texts.Count;$j++){$o=[Windows.Rect]::Intersect($texts[$i].Rect,$texts[$j].Rect);if(!$o.IsEmpty -and $o.Width -gt 1 -and $o.Height -gt 1){throw "Power panel texts overlap: $theme/$scale/$kind '$($texts[$i].Text)' and '$($texts[$j].Text)'"}}}
 $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new([int](588*$scale),[int](84*$scale),96*$scale,96*$scale,[Windows.Media.PixelFormats]::Pbgra32);$bitmap.Render($surface)
 $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap));$stream=[IO.File]::Create((Join-Path $output "$theme-$scale-$kind.png"));try{$encoder.Save($stream)}finally{$stream.Dispose()}
 $checks++
}}}
Write-Output "Verified the Power panel ($checks renders: desktop stacked, laptop single area, starting empty state) in both themes at four scales: right-anchored fill, no clipping or overlapping text."
$app.Shutdown()
