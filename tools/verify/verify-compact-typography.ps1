param([switch]$Baseline)
$ErrorActionPreference='Stop'
# Reuse resource loading and service-free production view-model fixtures.
$setup=(Get-Content (Join-Path $PSScriptRoot 'verify-prayer-capsule.ps1') -Raw).Split('$checks=0')[0]
$setup=$setup.Replace('$PSScriptRoot',('{0}{1}{0}' -f [char]39,$PSScriptRoot))
Invoke-Expression $setup
$output=Join-Path $repo 'MochiDock/obj/compact-typography';$null=New-Item -ItemType Directory -Force $output
$reports=[Collections.Generic.List[object]]::new()
foreach($theme in @('Dark','Matcha')){foreach($scale in @(1,1.25,1.5,2)){foreach($mode in @('Idle','Weather','Timer','Prayer','Media','Notification','TwoLine','Gaming')){
 $vm=New-Model ($mode -in @('Weather','Gaming')) 'Maghrib'
 $settings=$vmType.GetField('_settings',$flags).GetValue($vm);$settings.EnablePrayerPreNotification=$mode -eq 'Prayer';$settings.ShowPrayerInCapsule=$mode -eq 'Prayer'
 if($mode -ne 'Timer'){Set-Field $vm '_pomodoroService' $null}
 if($mode -in @('Media','TwoLine')){$vmType.GetField('_currentMedia',$flags).GetValue($vm).Title='Now playing';$vmType.GetField('_currentMedia',$flags).GetValue($vm).IsPlaying=$true;if($mode -eq 'TwoLine'){Set-Field $vm '_currentLyricLine' 'A line of music';Set-Field $vm '_currentLyricRomaji' 'Supporting pronunciation'}}
 if($mode -eq 'Notification'){$mail=[Activator]::CreateInstance($assembly.GetType('MochiDock.Models.EmailNotification'));$mail.Sender='Alex';$mail.Subject='Your message is ready';Set-Field $vm '_currentEmail' $mail;$vmType.GetField('_notificationQueue',$flags).GetValue($vm).Add([Enum]::Parse($assembly.GetType('MochiDock.ViewModels.IslandCardType'),'Email'))}
 $grid=[Windows.Markup.XamlReader]::Parse($normal,$context);$grid.DataContext=$vm
 $width=switch($mode){Idle{125} Weather{195} Timer{260} Prayer{260} Media{340} Notification{315} TwoLine{380} Gaming{430}}
 $height=if($mode -in @('Notification','TwoLine')){48}elseif($mode -eq 'Gaming'){32}else{36}
 if($mode -eq 'Gaming'){
  $markup=$window.SelectSingleNode('//*[@*[local-name()="Name"]="GamingCapsuleContent"]').OuterXml.Replace('clr-namespace:MochiDock.Controls"','clr-namespace:MochiDock.Controls;assembly=MochiDock"')
  $grid=[Windows.Markup.XamlReader]::Parse($markup,$context);$grid.Visibility=[Windows.Visibility]::Visible;$grid.DataContext=$vm
  $stats=$vmType.GetField('_hardwareStats',$flags).GetValue($vm);$stats.CurrentFps=144;$stats.GpuUsage=97;$stats.CpuUsage=38;$stats.GpuTemp=71
  $grid.FindName('CompactGamingReadout').Update($stats,$vm.WeatherInfo)
 }
 $surface=[Activator]::CreateInstance($assembly.GetType('MochiDock.Controls.IslandChrome'));$surface.Width=$width;$surface.Height=$height;$surface.Child=$grid;$surface.CornerRadius=[Windows.CornerRadius]::new(18)
 $surface.SetResourceReference([Windows.Documents.TextElement]::FontFamilyProperty,'IslandUiFont');[Windows.Controls.TextBlock]::SetFontSize($surface,12)
 $fixtureWindow=[Windows.Window]::new();$fixtureWindow.Content=$surface;$assembly.GetType('MochiDock.Services.ThemeService').GetMethod('ApplyTheme').Invoke($null,@($fixtureWindow,$theme));$fixtureWindow.Content=$null
 $surface.Background=$app.Resources['IslandGroundBrush'];$surface.UseLayoutRounding=$true
 [Windows.Media.TextOptions]::SetTextRenderingMode($surface,[Windows.Media.TextRenderingMode]::Grayscale);[Windows.Media.TextOptions]::SetTextFormattingMode($surface,[Windows.Media.TextFormattingMode]::Ideal);[Windows.Media.TextOptions]::SetTextHintingMode($surface,[Windows.Media.TextHintingMode]::Fixed)
 $parameters=[Windows.Interop.HwndSourceParameters]::new('Isolated compact typography');$parameters.WindowStyle=-2147483648;$parameters.PositionX=-32000;$parameters.PositionY=-32000;$parameters.Width=1;$parameters.Height=1
 $native=[Windows.Interop.HwndSource]::new($parameters);$native.RootVisual=$surface
 try{
 [Windows.Media.VisualTreeHelper]::SetRootDpi($surface,[Windows.DpiScale]::new($scale,$scale));$surface.Measure([Windows.Size]::new($width,$height));$surface.Arrange([Windows.Rect]::new(0,0,$width,$height));$surface.UpdateLayout()
 [Windows.Threading.Dispatcher]::CurrentDispatcher.Invoke([Windows.Threading.DispatcherPriority]::Loaded,[Action]{})
 $marquee=$grid.FindName('CapsuleMarquee');if($marquee){$marquee.UpdateMarquee($true)}
 $surface.UpdateLayout()
 if($mode -eq 'Gaming'){
  # The capsule must not change width as telemetry and weather change (it is centered, so a lagging pill clips the Mochi and the last value).
  $readout=$grid.FindName('CompactGamingReadout');$widths=[Collections.Generic.List[double]]::new()
  $readout.Update($stats,$vm.WeatherInfo);$surface.UpdateLayout() # warm-up: fonts and weights resolve on the first layout
  foreach($sample in @(@(144,97,38,71,17),@(679,85,33,54,-12),@(60,5,3,40,9),@(0,0,0,0,100),@(240,100,100,100,-5))){
   $stats.CurrentFps=$sample[0];$stats.GpuUsage=$sample[1];$stats.CpuUsage=$sample[2];$stats.GpuTemp=$sample[3];$vm.WeatherInfo.Temperature=$sample[4]
   $readout.Update($stats,$vm.WeatherInfo);$surface.UpdateLayout();$grid.Measure([Windows.Size]::new([double]::PositiveInfinity,32));$widths.Add($grid.DesiredSize.Width)
  }
  if(($widths|Measure-Object -Maximum -Minimum|ForEach-Object{$_.Maximum-$_.Minimum}) -gt 1){throw "Gaming capsule width changes with telemetry: $($widths -join ', ')"}
  $readout.Reset();$readout.Update($stats,$vm.WeatherInfo);$surface.UpdateLayout()
 }
 if($mode -eq 'Prayer'){$width=$grid.FindName('PrayerReminderContent').MeasureCapsuleWidth();$surface.Width=$width;$surface.Measure([Windows.Size]::new($width,$height));$surface.Arrange([Windows.Rect]::new(0,0,$width,$height));$surface.UpdateLayout()}
 $visuals=[Collections.Generic.List[object]]::new();Find-Visuals $grid $visuals
 foreach($visual in $visuals){$assembly.GetType('MochiDock.Services.ThemeService').GetMethod('ApplyElementStyle').Invoke($null,@($visual.PSObject.BaseObject,($theme -eq 'Matcha')))} 
 foreach($m in $visuals){if($m.GetType().FullName -eq 'MochiDock.Controls.MascotControl'){$m.ApplyTheme($theme -eq 'Matcha')}}
 foreach($reading in @('12:55','11:11','08:08')){
  if($mode -in @('Idle','Weather')){$marquee.Text=$reading;$marquee.UpdateMarquee($true)}
  if($mode -eq 'Weather'){$vmType.GetField('_weatherInfo',$flags).GetValue($vm).Temperature=if($reading -eq '11:11'){-12}else{17};$vmType.GetMethod('OnPropertyChanged',$flags).Invoke($vm,@('WeatherTemperatureBadge'))|Out-Null}
  $surface.UpdateLayout()
  if(!$Baseline -and $mode -eq 'TwoLine'){
   $group=$marquee.Parent;$groupY=$group.TransformToAncestor($surface).Transform([Windows.Point]::new(0,0)).Y
   if([Math]::Abs(($groupY+$group.ActualHeight/2-$height/2)*$scale) -gt 1){throw 'Two-line group is not centered'}
  }
  foreach($text in $visuals|Where-Object{$_ -is [Windows.Controls.TextBlock] -and $_.Text -and $_.Visibility -eq [Windows.Visibility]::Visible}){
   $parent=$text;$hidden=$false;while($parent -and $parent -ne $surface){if($parent.Visibility -ne [Windows.Visibility]::Visible){$hidden=$true;break};$parent=[Windows.Media.VisualTreeHelper]::GetParent($parent)};if($hidden){continue}
   $format=[Windows.Media.FormattedText]::new('0123456789',[Globalization.CultureInfo]::InvariantCulture,$text.FlowDirection,[Windows.Media.Typeface]::new($text.FontFamily,$text.FontStyle,$text.FontWeight,$text.FontStretch),$text.FontSize,[Windows.Media.Brushes]::Black,$scale)
   $ink=$format.BuildGeometry([Windows.Point]::new(0,0)).Bounds;$origin=$text.TransformToAncestor($surface).Transform([Windows.Point]::new(0,0));$center=$origin.Y+$ink.Top+$ink.Height/2
   if(!$Baseline -and $text.Text -match '^\d\d:\d\d$' -and $mode -in @('Idle','Weather') -and $text.FontWeight -ne [Windows.Application]::Current.Resources['IslandPrimaryReadingWeight']){throw 'Clock must use the primary-reading weight'}
   if(!$Baseline -and $text.Text -match '°C$' -and $text.FontWeight -ne [Windows.Application]::Current.Resources['IslandSmallLabelWeight']){throw 'Weather must use the small-label weight'}
   if(!$Baseline -and $mode -eq 'Gaming' -and $text.Text -match '^\d+(%|°)?$' -and @([Windows.Application]::Current.Resources['IslandPrimaryReadingWeight'],[Windows.Application]::Current.Resources['IslandSmallLabelWeight']) -notcontains $text.FontWeight){throw 'Gaming value must use a primary-reading or small-label weight'}
   if(!$Baseline){
    $actual=[Windows.Media.FormattedText]::new($text.Text,[Globalization.CultureInfo]::InvariantCulture,$text.FlowDirection,[Windows.Media.Typeface]::new($text.FontFamily,$text.FontStyle,$text.FontWeight,$text.FontStretch),$text.FontSize,[Windows.Media.Brushes]::Black,$scale)
    $actualBounds=$actual.BuildGeometry([Windows.Point]::new(0,0)).Bounds
    if(!$actualBounds.IsEmpty -and (($origin.Y+$actualBounds.Top)*$scale -lt -0.5 -or ($origin.Y+$actualBounds.Bottom-$height)*$scale -gt 0.5)){throw "Vertical glyph clipping: $mode $($text.Text)"}
   }
   $reports.Add([pscustomobject]@{Theme=$theme;Scale=$scale;Mode=$mode;Text=$text.Text;Weight="$($text.FontWeight)";OriginY=$origin.Y;NumeralCenter=$center;Target=$height/2;ErrorPixels=($center-$height/2)*$scale})
   if(!$Baseline -and $mode -in @('Idle','Weather') -and $text.Text -match '^[0-9:°C−-]+$' -and [Math]::Abs(($center-$height/2)*$scale) -gt 1){throw "Numeric centering failed $mode $scale $($text.Text) $center"}
  }
  $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new([int]($width*$scale),[int]($height*$scale),96*$scale,96*$scale,[Windows.Media.PixelFormats]::Pbgra32);$bitmap.Render($surface)
  $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap));$file=Join-Path $output "$theme-$scale-$mode-$($reading.Replace(':',''))-baseline$Baseline.png";$stream=[IO.File]::Create($file);try{$encoder.Save($stream)}finally{$stream.Dispose()}
 }
 if(!$Baseline -and $mode -eq 'Idle'){
  $vmType.GetField('_currentMedia',$flags).GetValue($vm).IsPlaying=$true;$vm.RaiseCapsuleStateChanged()
  [Windows.Threading.Dispatcher]::CurrentDispatcher.Invoke([Windows.Threading.DispatcherPriority]::Loaded,[Action]{})
  if($marquee.FontWeight -ne [Windows.Application]::Current.Resources['IslandSmallLabelWeight'] -or $marquee.CenterNumerals){throw 'Idle-to-media role did not update'}
  $vmType.GetField('_currentMedia',$flags).GetValue($vm).IsPlaying=$false;$vm.RaiseCapsuleStateChanged()
  [Windows.Threading.Dispatcher]::CurrentDispatcher.Invoke([Windows.Threading.DispatcherPriority]::Loaded,[Action]{})
  if($marquee.FontWeight -ne [Windows.Application]::Current.Resources['IslandPrimaryReadingWeight'] -or !$marquee.CenterNumerals){throw 'Media-to-idle role did not update'}
 }
 }finally{$native.Dispose()}
}}}
$reports|Export-Csv (Join-Path $output "measurements-baseline$Baseline.csv") -NoTypeInformation
$reports|Where-Object{$_.Mode -eq 'Weather' -and $_.Scale -eq 1}|Select-Object -First 6|Format-Table
$app.Shutdown()