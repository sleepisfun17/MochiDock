param([string]$Configuration='Release')
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework
$repo=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $repo "MochiDock/bin/$Configuration/net8.0-windows10.0.19041.0/MochiDock.dll"))
[AppDomain]::CurrentDomain.add_AssemblyResolve({param($s,$e) if($e.Name.StartsWith('MochiDock')){return $assembly} return $null})
$app=[Windows.Application]::new();$app.ShutdownMode=[Windows.ShutdownMode]::OnExplicitShutdown
[xml]$source=Get-Content (Join-Path $repo 'MochiDock/App.xaml') -Raw
$context=[Windows.Markup.ParserContext]::new()
$context.XmlnsDictionary.Add('','http://schemas.microsoft.com/winfx/2006/xaml/presentation')
$context.XmlnsDictionary.Add('x','http://schemas.microsoft.com/winfx/2006/xaml')
$context.XmlnsDictionary.Add('controls','clr-namespace:MochiDock.Controls;assembly=MochiDock')
$context.XmlnsDictionary.Add('views','clr-namespace:MochiDock.Views;assembly=MochiDock')
$context.XmlnsDictionary.Add('models','clr-namespace:MochiDock.Models;assembly=MochiDock')
foreach($node in $source.SelectNodes('//*[local-name()="FontFamily"]')){$app.Resources[$node.GetAttribute('Key','http://schemas.microsoft.com/winfx/2006/xaml')]=[Windows.Media.FontFamily]::new([uri]'pack://application:,,,/',$node.InnerText)}
$app.Resources.MergedDictionaries.Add([Windows.Markup.XamlReader]::Parse((Get-Content (Join-Path $repo 'MochiDock/Views/TypographyResources.xaml') -Raw)))
$weatherMarkup=(Get-Content (Join-Path $repo 'MochiDock/Views/WeatherIconResources.xaml') -Raw) -replace 'clr-namespace:MochiDock.Models','clr-namespace:MochiDock.Models;assembly=MochiDock'
$app.Resources.MergedDictionaries.Add([Windows.Markup.XamlReader]::Parse($weatherMarkup,$context))
# The capsule's notification icons (WhatsApp, Discord, email) are the header's line icons.
$app.Resources.MergedDictionaries.Add([Windows.Markup.XamlReader]::Parse(((Get-Content (Join-Path $repo 'MochiDock/Views/NavigationIconResources.xaml') -Raw) -replace ' x:Shared="False"','')))
$app.Resources['BoolToVisibility']=[Windows.Controls.BooleanToVisibilityConverter]::new()
$app.Resources['InverseBoolToVisibility']=[Activator]::CreateInstance($assembly.GetType('MochiDock.Views.InverseBooleanToVisibilityConverter'))
foreach($node in $source.SelectNodes('//*[local-name()="Style" and @*[local-name()="Key"]="IslandFocusVisual"]')){$app.Resources['IslandFocusVisual']=[Windows.Markup.XamlReader]::Parse($node.OuterXml,$context)}
foreach($node in $source.SelectNodes('//*[local-name()="Style" and @*[local-name()="Key"]="ActionButton"]')){$app.Resources['ActionButton']=[Windows.Markup.XamlReader]::Parse($node.OuterXml,$context)}
[xml]$window=Get-Content (Join-Path $repo 'MochiDock/MainWindow.xaml') -Raw
$normal=$window.SelectSingleNode('//*[@*[local-name()="Name"]="PillNormalContent"]').OuterXml
$normal=$normal.Replace('clr-namespace:MochiDock.Controls"','clr-namespace:MochiDock.Controls;assembly=MochiDock"')
$normal=$normal -replace '\s+(Acknowledged|CycleCompleted|Click|MouseLeftButtonDown|MouseLeftButtonUp|MouseEnter|MouseLeave|Loaded|SizeChanged)="[^"]*"',''
$flags=[Reflection.BindingFlags]'Instance,NonPublic'
$vmType=$assembly.GetType('MochiDock.ViewModels.MainViewModel')
function Set-Field($object,$name,$value){$object.GetType().GetField($name,$flags).SetValue($object,$value)}
function New-Model($weatherEnabled,$prayerName){
 # Bypass composition: only production property logic is exercised, with no hardware,
 # audio, settings writes, network, timers, or startup registration.
 $vm=[Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject($vmType)
 $settings=[Activator]::CreateInstance($assembly.GetType('MochiDock.Models.AppSettings'))
 $settings.EnablePrayerTimes=$true;$settings.EnablePrayerPreNotification=$true;$settings.ShowPrayerInCapsule=$true;$settings.ShowWeatherInCapsule=$weatherEnabled;$settings.QuietHoursEnabled=$false
 Set-Field $vm '_settings' $settings
 Set-Field $vm '_currentMedia' ([Activator]::CreateInstance($assembly.GetType('MochiDock.Models.MediaInfo')))
 Set-Field $vm '_hardwareStats' ([Activator]::CreateInstance($assembly.GetType('MochiDock.Services.HardwareStats')))
 $queue=[Activator]::CreateInstance($vmType.GetField('_notificationQueue',$flags).FieldType);Set-Field $vm '_notificationQueue' $queue
 $stateType=$vmType.GetField('_prayerCountdown',$flags).FieldType;Set-Field $vm '_prayerCountdown' ([Activator]::CreateInstance($stateType))
 $weather=[Activator]::CreateInstance($assembly.GetType('MochiDock.Models.WeatherInfo'));$weather.Temperature=16;Set-Field $vm '_weatherInfo' $weather
 $pomodoroType=$assembly.GetType('MochiDock.Services.PomodoroService');$pomodoro=[Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject($pomodoroType)
 Set-Field $pomodoro '_state' ([Enum]::Parse($assembly.GetType('MochiDock.Services.PomodoroState'),'Working'));Set-Field $vm '_pomodoroService' $pomodoro
 $schedule=[Activator]::CreateInstance($assembly.GetType('MochiDock.Models.PrayerSchedule'))
 $schedule.Date=[datetime]'2026-11-07';$schedule.TimeZoneId='UTC';$schedule.NowUtc=[DateTimeOffset]'2026-11-07T12:00:00Z'
 $schedule.NextPrayer=[Activator]::CreateInstance($assembly.GetType('MochiDock.Models.PrayerTimeItem'));$schedule.NextPrayer.Name=$prayerName
 Set-Field $schedule '<NextPrayerInstant>k__BackingField' ([DateTimeOffset]'2026-11-07T12:10:00Z')
 Set-Field $vm '_currentPrayerSchedule' $schedule
 Set-Field $vm '_displayAwake' $true
 return $vm
}
function Find-Visuals($node,$list){$list.Add($node);for($i=0;$i -lt [Windows.Media.VisualTreeHelper]::GetChildrenCount($node);$i++){Find-Visuals ([Windows.Media.VisualTreeHelper]::GetChild($node,$i)) $list}}
$output=Join-Path $repo 'MochiDock/obj/prayer-capsule';$null=New-Item -ItemType Directory -Force $output
$checks=0;$observations=[Collections.Generic.List[object]]::new()
foreach($theme in @('Dark','Matcha')){foreach($scale in @(1,1.25,1.5,2)){foreach($weatherCase in @('Disabled','Unavailable','Warm','Cold')){foreach($prayerName in @('Fajr','Dhuhr','Asr','Maghrib','Isha')){
 $weatherEnabled=$weatherCase -ne 'Disabled';$available=$weatherCase -ne 'Unavailable'
 $vm=New-Model $weatherEnabled $prayerName
 $settings=$vmType.GetField('_settings',$flags).GetValue($vm)
 if(!$available){Set-Field $vm '_weatherInfo' $null}else{$weather=$vmType.GetField('_weatherInfo',$flags).GetValue($vm);$weather.Temperature=if($weatherCase -eq 'Cold'){-12}else{17};$weather.IconKind=if($weatherCase -eq 'Cold'){'Cloudy'}else{'Clear'}}
 $hostWindow=[Windows.Window]::new();$assembly.GetType('MochiDock.Services.ThemeService').GetMethod('ApplyTheme').Invoke($null,@($hostWindow,$theme))
 $grid=[Windows.Markup.XamlReader]::Parse($normal,$context);$grid.DataContext=$vm
 $surface=[Activator]::CreateInstance($assembly.GetType('MochiDock.Controls.IslandChrome'));$surface.Height=36;$surface.IsNotch=$true;$surface.Child=$grid
 $surface.SetResourceReference([Windows.Controls.Border]::BackgroundProperty,'IslandGroundBrush');$surface.SetResourceReference([Windows.Documents.TextElement]::FontFamilyProperty,'IslandUiFont');[Windows.Controls.TextBlock]::SetFontSize($surface,12)
 [Windows.Media.TextOptions]::SetTextRenderingMode($surface,[Windows.Media.TextRenderingMode]::Grayscale);[Windows.Media.TextOptions]::SetTextFormattingMode($surface,[Windows.Media.TextFormattingMode]::Ideal);[Windows.Media.TextOptions]::SetTextHintingMode($surface,[Windows.Media.TextHintingMode]::Fixed)
 $surface.UseLayoutRounding=$true;$surface.SnapsToDevicePixels=$true
 $parameters=[Windows.Interop.HwndSourceParameters]::new('Isolated prayer row');$parameters.WindowStyle=-2147483648;$parameters.PositionX=-32000;$parameters.PositionY=-32000;$parameters.Width=1;$parameters.Height=1
 $native=[Windows.Interop.HwndSource]::new($parameters);$native.RootVisual=$surface
 try{
 [Windows.Media.VisualTreeHelper]::SetRootDpi($surface,[Windows.DpiScale]::new($scale,$scale))
 $row=$grid.FindName('PrayerReminderContent');$label=$row.FindName('PrayerReminderLabel');$ok=$row.FindName('PrayerReminderButton');$weatherSlot=$row.FindName('PrayerWeather');$temperature=$row.FindName('PrayerTemperature')
 $row.add_Acknowledged({param($sender,$eventArgs)$sender.DataContext.AcknowledgePrayerCountdown();$eventArgs.Handled=$true})
 $initialWidth=$null
 foreach($minute in 10..1){
  $vm.CurrentPrayerSchedule.NowUtc=([DateTimeOffset]'2026-11-07T12:10:00Z').AddMinutes(-$minute)
  $vmType.GetMethod('NotifyPrayerPresentation',$flags).Invoke($vm,@())
  [Windows.Threading.Dispatcher]::CurrentDispatcher.Invoke([Windows.Threading.DispatcherPriority]::Loaded,[Action]{})
  $width=$row.MeasureCapsuleWidth();$surface.Width=$width
  $surface.Measure([Windows.Size]::new($width,36));$surface.Arrange([Windows.Rect]::new(0,0,$width,36));$surface.UpdateLayout()
  if($null -eq $initialWidth){$initialWidth=$width}elseif([Math]::Abs($width-$initialWidth) -gt .01){throw "Prayer width moved with countdown: $prayerName $theme $scale"}
  if(!$vm.IsPrayerReminderVisible -or $vm.IsWeatherInMainPill -or $vm.IsPomodoroInMainPill -or $vm.IsWeatherInPrayerReminder -ne ($weatherEnabled -and $available)){throw 'Prayer visibility policy failed'}
  if($label.Text -ne "$prayerName $minute min"){throw 'Countdown did not update'}
  if($grid.FindName('PillTickerContent').Visibility -ne [Windows.Visibility]::Collapsed -or $grid.FindName('PillRightAnchors').Visibility -ne [Windows.Visibility]::Collapsed -or $grid.FindName('PillCol2').ActualWidth -ne 0){throw 'Old ticker/right anchor still reserves reminder space'}
  $a=$label.TransformToAncestor($surface).TransformBounds([Windows.Rect]::new(0,0,$label.ActualWidth,$label.ActualHeight));$b=$ok.TransformToAncestor($surface).TransformBounds([Windows.Rect]::new(0,0,$ok.ActualWidth,$ok.ActualHeight))
  $probe=[Windows.Controls.TextBlock]::new();$probe.Text=$label.Text;$probe.FontFamily=$label.FontFamily;$probe.FontSize=$label.FontSize;$probe.FontWeight=$label.FontWeight;$probe.Typography.NumeralAlignment=[Windows.FontNumeralAlignment]::Tabular;$probe.Measure([Windows.Size]::new([double]::PositiveInfinity,[double]::PositiveInfinity))
  if($a.IntersectsWith($b) -or $a.X -lt 48 -or $b.Right -gt $width-7 -or $probe.DesiredSize.Width -gt $label.ActualWidth+.5 -or $ok.ActualWidth -ne 40){throw "Prayer label/OK bounds failed: $theme/$scale $a $b width=$width"}
  if([Math]::Abs(($b.X-$a.Right-8)*$scale) -gt 1.01){throw 'Prayer-to-OK gap changed'}
  if($weatherEnabled -and $available){
   if($weatherSlot.Visibility -ne [Windows.Visibility]::Visible){throw 'Available prayer weather is hidden'}
   $c=$weatherSlot.TransformToAncestor($surface).TransformBounds([Windows.Rect]::new(0,0,$weatherSlot.ActualWidth,$weatherSlot.ActualHeight))
   if($c.IntersectsWith($b) -or $c.Right -gt $width-7 -or [Math]::Abs(($c.X-$b.Right-8)*$scale) -gt 1.01){throw "Weather overlap/padding failed $b $c width=$width"}
   if($temperature.Text -ne $vm.WeatherTemperatureBadge){throw 'Weather temperature did not bind'}
  }elseif($weatherSlot.Visibility -ne [Windows.Visibility]::Collapsed -or [Math]::Abs(($width-$b.Right-8)*$scale) -gt 1.01){throw "Hidden weather leaves a blank slot: $theme/$scale/$prayerName width=$width b=$b row=$($row.ActualWidth) desired=$($row.DesiredSize.Width) weather=$($weatherSlot.Visibility)"}
  $visuals=[Collections.Generic.List[object]]::new();Find-Visuals $grid $visuals
  foreach($visual in $visuals){if($visual.GetType().FullName -eq 'MochiDock.Controls.MascotControl'){$visual.ApplyTheme($theme -eq 'Matcha')}}
  if($grid.FindName('PillCol0').ActualWidth -ne 48){throw 'Mochi anchor moved'}
  foreach($numeric in @($label,$temperature)){
   if($numeric -eq $temperature -and !($weatherEnabled -and $available)){continue}
   $reference=[Windows.Media.FormattedText]::new('0123456789',[Globalization.CultureInfo]::InvariantCulture,$numeric.FlowDirection,[Windows.Media.Typeface]::new($numeric.FontFamily,$numeric.FontStyle,$numeric.FontWeight,$numeric.FontStretch),$numeric.FontSize,[Windows.Media.Brushes]::Black,$scale)
   $band=$reference.BuildGeometry([Windows.Point]::new(0,0)).Bounds;$position=$numeric.TransformToAncestor($surface).Transform([Windows.Point]::new(0,0))
   if([Math]::Abs(($position.Y+$band.Top+$band.Height/2-18)*$scale) -gt 1.01){throw "Prayer/weather numeral band is not centered: $theme $scale"}
  }
  if($weatherEnabled -and $available){$icon=$weatherSlot.Children[0];$iconY=$icon.TransformToAncestor($surface).Transform([Windows.Point]::new(0,0)).Y;if([Math]::Abs(($iconY+$icon.ActualHeight/2-18)*$scale) -gt 1.01){throw 'Weather artwork is not centered'}}
  if($minute -in @(10,1)){
   $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new([int][Math]::Ceiling($width*$scale),[int](36*$scale),96*$scale,96*$scale,[Windows.Media.PixelFormats]::Pbgra32);$bitmap.Render($surface)
   $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap));$path=Join-Path $output "$theme-$scale-$prayerName-$weatherCase-$minute.png";$stream=[IO.File]::Create($path);try{$encoder.Save($stream)}finally{$stream.Dispose()}
  }
 }
 $observations.Add([pscustomobject]@{Theme=$theme;Scale=$scale;Prayer=$prayerName;Weather=$weatherCase;Width=$width})
 # Weather remains available while the reminder overrides media or gaming.
 foreach($activity in @('Media','Gaming')){
  if($activity -eq 'Media'){$vmType.GetField('_currentMedia',$flags).GetValue($vm).IsPlaying=$true}else{Set-Field $vm '_wasGaming' $true}
  $vm.RaiseCapsuleStateChanged();$surface.UpdateLayout()
  if(!$vm.IsPrayerReminderVisible -or $vm.IsWeatherInPrayerReminder -ne ($weatherEnabled -and $available)){throw "$activity displaced reminder/weather"}
 }
 $vmType.GetField('_currentMedia',$flags).GetValue($vm).IsPlaying=$false;Set-Field $vm '_wasGaming' $false
 # Weather setting changes collapse the slot and reclaim its exact measured space.
 $settings.ShowWeatherInCapsule=$false;$vm.RaiseCapsuleStateChanged();$surface.UpdateLayout();$without=$row.MeasureCapsuleWidth()
 $settings.ShowWeatherInCapsule=$true;$vm.RaiseCapsuleStateChanged();$surface.UpdateLayout();$with=$row.MeasureCapsuleWidth()
 if($available -and $with -le $without){throw 'Weather slot was not measured/reclaimed'}
 $settings.ShowWeatherInCapsule=$weatherEnabled;$vm.RaiseCapsuleStateChanged()
 # Lock/unlock and a notification suppress the reminder without claiming its slot.
 Set-Field $vm '_isWorkstationLocked' $true;$vm.RaiseCapsuleStateChanged();if($vm.IsWeatherInPrayerReminder -or $vm.IsPrayerReminderVisible){throw 'Locked reminder remains visible'}
 Set-Field $vm '_isWorkstationLocked' $false;$vm.RaiseCapsuleStateChanged();if(!$vm.IsPrayerReminderVisible){throw 'Eligible reminder did not resume'}
 $queue=$vmType.GetField('_notificationQueue',$flags).GetValue($vm);$queue.Add([Enum]::Parse($assembly.GetType('MochiDock.ViewModels.IslandCardType'),'Email'));Set-Field $vm '_isNotificationTakeover' $true;$vm.RaiseCapsuleStateChanged()
 if($vm.IsPrayerReminderVisible -or $vm.IsWeatherInPrayerReminder){throw 'Notification priority lost'};$queue.Clear();Set-Field $vm '_isNotificationTakeover' $false;$vm.RaiseCapsuleStateChanged()
 # Exercise the real button forwarding into the real acknowledgement method.
 $ok.RaiseEvent([Windows.RoutedEventArgs]::new([Windows.Controls.Button]::ClickEvent));$surface.UpdateLayout()
 if($vm.IsPrayerReminderVisible -or $vm.IsWeatherInPrayerReminder -or $row.Visibility -ne [Windows.Visibility]::Collapsed -or $vm.IsWeatherInMainPill -ne ($weatherEnabled -and $available) -or !$vm.IsPomodoroInMainPill){throw 'Acknowledgement did not restore normal presentation'}
 # Fresh reminder: expiry also restores the ordinary presentation.
 $stateType=$vmType.GetField('_prayerCountdown',$flags).FieldType;Set-Field $vm '_prayerCountdown' ([Activator]::CreateInstance($stateType));$vm.CurrentPrayerSchedule.NowUtc=[DateTimeOffset]'2026-11-07T12:11:00Z';$vm.RaiseCapsuleStateChanged()
 if($vm.IsPrayerReminderVisible -or $vm.IsWeatherInPrayerReminder){throw 'Expired reminder remains visible'}
 # Exercise prayer arrival notification:
 $vm.ShowPrayerArrival('Asr',[DateTimeOffset]'2026-11-07T14:27:00Z','Tokyo')
 if(!$vm.IsPrayerArrivalVisible -or $vm.PrayerArrivalTitle -ne "It's time for Ashar" -or $vm.PrayerArrivalSubtitle -notlike "*14:27*Tokyo*"){throw 'Prayer arrival activation failed'}
 $vm.DismissPrayerArrival()
 if($vm.IsPrayerArrivalVisible){throw 'Prayer arrival dismissal failed'}
 $checks++
 }finally{$native.Dispose()}
}}}}
$observations|Export-Csv (Join-Path $output 'measured-widths.csv') -NoTypeInformation
Write-Output "Verified $checks production prayer/weather combinations, all countdown minutes, spacing, reclaiming, acknowledgement, expiry, media/gaming, notifications and lock/unlock."
$app.Shutdown()