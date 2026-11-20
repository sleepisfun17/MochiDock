param([switch]$Baseline)
$ErrorActionPreference='Stop'
$setup=(Get-Content (Join-Path $PSScriptRoot 'verify-prayer-capsule.ps1') -Raw).Split('$checks=0')[0]
$setup=$setup.Replace('$PSScriptRoot',('{0}{1}{0}' -f [char]39,$PSScriptRoot));Invoke-Expression $setup
$vm=New-Model $false 'Fajr';Set-Field $vm '_pomodoroService' $null
$settings=$vmType.GetField('_settings',$flags).GetValue($vm);$settings.LastActiveCardType='Media';$settings.ActiveSystemModule='Games';$settings.EnablePrayerPreNotification=$false
Set-Field $vm '_activeCardType' ([Enum]::Parse($assembly.GetType('MochiDock.ViewModels.IslandCardType'),'Hardware'))
$vm.PrepareHoverExpand()
Write-Output "Remembered card=$($settings.LastActiveCardType); ordinary expansion presents=$($vm.ActiveCardType); module=$($vm.ActiveSystemModule)"
if($Baseline){if($vm.ActiveCardType.ToString() -ne 'Hardware'){throw 'Expected old stale-card behavior was not reproduced'}}elseif($vm.ActiveCardType.ToString() -ne 'Media'){throw 'Ordinary expansion did not restore manual card'}
$hostPanel=[Activator]::CreateInstance($assembly.GetType('MochiDock.Controls.TabTransitionHost'))
foreach($key in @('CPU','Games')){$child=[Windows.Controls.Border]::new();$child.Tag=$key;$hostPanel.Children.Add($child)|Out-Null}
$hostPanel.SelectedKey='Games';$hostPanel.SelectedKey='invalid'
$visible=@($hostPanel.Children|Where-Object{$_.Visibility -eq [Windows.Visibility]::Visible})
Write-Output "Host logical key=$($hostPanel.SelectedKey); visible panels=$($visible.Tag -join ',')"
if($Baseline){if($visible.Count -ne 1 -or $visible[0].Tag -ne 'Games'){throw 'Expected old stale-panel behavior was not reproduced'}}elseif($visible.Count -ne 0){throw 'Invalid key retained stale content'}
if($Baseline){$app.Shutdown();exit 0}
Add-Type 'public static class NavigationFixtureSink { public static int Writes; public static void Save(object settings) { Writes++; } }'
$saveType=[Action`1].MakeGenericType($assembly.GetType('MochiDock.Models.AppSettings'))
$save=[NavigationFixtureSink].GetMethod('Save').CreateDelegate($saveType)
foreach($style in $source.SelectNodes('//*[local-name()="ResourceDictionary"]/*[local-name()="Style"]')){
 $key=$style.GetAttribute('Key','http://schemas.microsoft.com/winfx/2006/xaml');$parsed=[Windows.Markup.XamlReader]::Parse($style.OuterXml,$context)
 if($key){$app.Resources[$key]=$parsed}else{$app.Resources[$parsed.TargetType]=$parsed}
}
$output=Join-Path $repo 'MochiDock/obj/navigation';$null=New-Item -ItemType Directory -Force $output
$cardType=$assembly.GetType('MochiDock.ViewModels.IslandCardType');$hostType=$assembly.GetType('MochiDock.Controls.TabTransitionHost')
function Pump([int]$ms){$frame=[Windows.Threading.DispatcherFrame]::new();$timer=[Windows.Threading.DispatcherTimer]::new();$timer.Interval=[TimeSpan]::FromMilliseconds($ms);$timer.Tag=$frame;$timer.Add_Tick({param($sender,$e)$sender.Stop();$sender.Tag.Continue=$false});$timer.Start();try{[Windows.Threading.Dispatcher]::PushFrame($frame)}finally{$timer.Stop()}}
function Assert-Selected($panel,$key){$panel.Settle();$panel.UpdateLayout();$visible=@($panel.Children|Where-Object{$_.Visibility -eq [Windows.Visibility]::Visible});if($visible.Count -ne 1 -or $visible[0].Tag -ne $key){throw "Logical/visible mismatch: requested=$key visible=$($visible.Tag -join ',')"}}
[xml]$hardwareSource=Get-Content (Join-Path $repo 'MochiDock/Views/Cards/HardwareCardView.xaml') -Raw
$hardwareMarkup=$hardwareSource.OuterXml -replace '\s+x:Class="[^"]*"',''
$hardwareMarkup=$hardwareMarkup.Replace('clr-namespace:MochiDock.Controls"','clr-namespace:MochiDock.Controls;assembly=MochiDock"')
$hardwareMarkup=$hardwareMarkup -replace '<local:BatteryModuleView[^>]*/>','<Border Tag="Battery" Visibility="Collapsed"><TextBlock Text="Battery navigation fixture"/></Border>'
$hardwareMarkup=$hardwareMarkup -replace '\s+(Click|MouseLeftButtonUp|MouseLeftButtonDown)="[^"]*"',''
foreach($theme in @('Dark','Matcha')){foreach($scale in @(1,1.25,1.5,2)){
 $themeWindow=[Windows.Window]::new();$assembly.GetType('MochiDock.Services.ThemeService').GetMethod('ApplyTheme').Invoke($null,@($themeWindow,$theme))
 $vm=New-Model $false 'Fajr';Set-Field $vm '_saveNavigationSettings' $save;Set-Field $vm '_autoCollapseTimer' ([Windows.Threading.DispatcherTimer]::new());Set-Field $vm '_pomodoroService' $null
 $settings=$vm.Settings;$settings.EnablePrayerPreNotification=$false;$settings.LastActiveCardType='Media';$settings.ActiveSystemModule='CPU';$settings.ShowTodayTab=$true;Set-Field $vm '_hasSystemBattery' $true # the Battery chip needs a system battery (or wireless batteries) to be visible
 foreach($name in @('ShowStatsTab','ShowWeatherTab','ShowPomodoroTab','ShowFileShelfTab','ShowClipboardTab','ShowFxRatesTab')){$settings.$name=$true}
 $hardwareView=[Windows.Markup.XamlReader]::Parse($hardwareMarkup,$context)
 $cards=[Activator]::CreateInstance($hostType);$cards.DataContext=$vm;$cards.Width=620;$cards.Height=345
 $cardBinding=[Windows.Data.Binding]::new('ActiveCardType');$cardBinding.Source=$vm
 $cards.SetBinding($hostType.GetField('SelectedKeyProperty').GetValue($null),$cardBinding)|Out-Null
 foreach($key in [Enum]::GetNames($cardType)){
  if($key -eq 'Hardware'){$page=$hardwareView}else{$page=[Windows.Controls.Border]::new();$text=[Windows.Controls.TextBlock]::new();$text.Text="$key navigation fixture";$text.SetResourceReference([Windows.Controls.TextBlock]::FontFamilyProperty,'IslandUiFont');$text.SetResourceReference([Windows.Controls.TextBlock]::ForegroundProperty,'IslandInkBrush');$page.Child=$text}
  $page.Tag=$key;$cards.Children.Add($page)|Out-Null
 }
 $surface=[Windows.Controls.Border]::new();$surface.Child=$cards;$surface.Background=$app.Resources['IslandGroundBrush'];$surface.Width=620;$surface.Height=345
 $parameters=[Windows.Interop.HwndSourceParameters]::new('Isolated dock navigation');$parameters.WindowStyle=-2147483648;$parameters.PositionX=-32000;$parameters.PositionY=-32000;$parameters.Width=1;$parameters.Height=1
 $native=[Windows.Interop.HwndSource]::new($parameters);$native.RootVisual=$surface
 try{
 $vm.ActiveCardType=[Enum]::Parse($cardType,'Hardware')
 [Windows.Media.VisualTreeHelper]::SetRootDpi($surface,[Windows.DpiScale]::new($scale,$scale));$surface.Measure([Windows.Size]::new(620,345));$surface.Arrange([Windows.Rect]::new(0,0,620,345));$surface.UpdateLayout();[Windows.Threading.Dispatcher]::CurrentDispatcher.Invoke([Windows.Threading.DispatcherPriority]::Loaded,[Action]{})
 $cards.Settle();$cards.UpdateLayout()
 $visuals=[Collections.Generic.List[object]]::new();Find-Visuals $hardwareView $visuals
 $modules=$visuals|Where-Object{$_.GetType() -eq $hostType}|Select-Object -First 1
 if(!$modules){throw 'Production Stats transition host missing'}
 foreach($module in @('CPU','GPU','Memory','Drives','Network','Games','Battery')){
  $vm.NavigateManually([Enum]::Parse($cardType,'Hardware'));$vm.SelectSystemModule($module);$vm.IsExpanded=$true
  Assert-Selected $cards 'Hardware';Assert-Selected $modules $module
  $vm.IsExpanded=$false;$vm.PrepareHoverExpand();$vm.IsExpanded=$true
  Assert-Selected $cards 'Hardware';Assert-Selected $modules $module
  foreach($gaming in @($true,$false)){
   Set-Field $vm '_wasGaming' $gaming;$vm.HardwareStats=[Activator]::CreateInstance($assembly.GetType('MochiDock.Services.HardwareStats'));$vm.RaiseCapsuleStateChanged()
   if($vm.ActiveCardType.ToString() -ne 'Hardware' -or $vm.ActiveSystemModule -ne $module -or $settings.LastActiveCardType -ne 'Hardware'){throw 'Hardware/gaming changed deliberate selection'}
  }
  $vm.NavigateManually([Enum]::Parse($cardType,'Media'))
  foreach($temporary in @('Hardware','Email','WhatsApp','Discord','Summary','Settings')){
   $vm.ActiveCardType=[Enum]::Parse($cardType,$temporary)
   if($settings.LastActiveCardType -ne 'Media'){throw 'Temporary view overwrote manual preference'}
   $vm.IsExpanded=$false;$vm.PrepareHoverExpand();$vm.IsExpanded=$true;Assert-Selected $cards 'Media'
   if($vm.ActiveSystemModule -ne $module){throw 'Temporary view overwrote Stats module'}
  }
 }
 # Manual clicks on an already presented transient card must still establish ownership.
 $settings.LastActiveCardType='Media';$vm.ActiveCardType=[Enum]::Parse($cardType,'Hardware');$vm.NavigateManually([Enum]::Parse($cardType,'Hardware'))
 if($settings.LastActiveCardType -ne 'Hardware'){throw 'Same-card deliberate navigation was not remembered'}
 # A notification keeps its existing expansion priority, without becoming the manual default.
 $queue=$vmType.GetField('_notificationQueue',$flags).GetValue($vm);$queue.Add([Enum]::Parse($cardType,'Email'));Set-Field $vm '_isNotificationTakeover' $true;$vm.PrepareHoverExpand();Assert-Selected $cards 'Email'
 if($settings.LastActiveCardType -ne 'Hardware'){throw 'Notification expansion became manual default'}
 $queue.Clear();Set-Field $vm '_isNotificationTakeover' $false;$vm.PrepareHoverExpand();Assert-Selected $cards 'Hardware'
 # The Battery page is always available: selecting it sticks and notifies the host.
 $vm.SelectSystemModule('Battery');Assert-Selected $modules 'Battery'
 if($vm.ActiveSystemModule -ne 'Battery' -or $settings.ActiveSystemModule -ne 'Battery'){throw 'Battery selection did not persist coherently'}
 $vm.SelectSystemModule('CPU')
 foreach($bad in @('bad','Summary','999','WhatsApp','Email','Discord')){$settings.LastActiveCardType=$bad;$vm.PrepareHoverExpand();Assert-Selected $cards 'Media'}
 $settings.LastActiveCardType='Hardware';$settings.ShowStatsTab=$false;$vm.PrepareHoverExpand();Assert-Selected $cards 'Media';$settings.ShowStatsTab=$true
 # Pomodoro automatic completion and Summary dismissal must restore manual selection.
 $settings.LastActiveCardType='Hardware';$vm.ShowPomodoro($false);if($settings.LastActiveCardType -ne 'Hardware'){throw 'Transient Pomodoro overwrote manual card'}
 $vm.ActiveCardType=[Enum]::Parse($cardType,'Summary');$vm.DismissSummary();if($vm.ActiveCardType.ToString() -ne 'Hardware'){throw 'DismissSummary did not restore manual card'}
 $vm.SelectSystemModule('invalid');if($vm.ActiveSystemModule -ne 'CPU'){throw 'Invalid Stats key did not fall back'}
 $vm.NavigateManually([Enum]::Parse($cardType,'Hardware'));Assert-Selected $cards 'Hardware';Assert-Selected $modules 'CPU'
 # Exercise the real Games release handler without a prior press; persistence is injected.
 $inputView=[Activator]::CreateInstance($assembly.GetType('MochiDock.Views.Cards.HardwareCardView'));$inputView.DataContext=$vm
 $chip=$hardwareView.FindName('HardwareModuleStrip').Children|Where-Object{$_ -is [Windows.Controls.Border]}|Select-Object -Last 1
 $args=[Windows.Input.MouseButtonEventArgs]::new([Windows.Input.Mouse]::PrimaryDevice,0,[Windows.Input.MouseButton]::Left)
 $inputView.GetType().GetMethod('GamesChip_Click',$flags).Invoke($inputView,@($chip.PSObject.BaseObject,$args))
 if($vm.ActiveSystemModule -ne 'CPU'){throw 'Release from opening gesture activated Games'}
 # Rapid transition interruptions, hide/reopen and invalid key recovery.
 $vm.NavigateManually([Enum]::Parse($cardType,'Hardware'))
 foreach($module in @('Games','GPU','Memory','CPU','Games','Network')){$vm.SelectSystemModule($module)}
 $cards.Visibility=[Windows.Visibility]::Collapsed;$cards.Visibility=[Windows.Visibility]::Visible;Pump 300
 Assert-Selected $cards 'Hardware';Assert-Selected $modules 'Network'
 $modules.SelectedKey='invalid';$modules.Settle();if(@($modules.Children|Where-Object{$_.Visibility -eq [Windows.Visibility]::Visible}).Count){throw 'Invalid key retained a visible Stats module'}
 $modules.SetBinding($hostType.GetField('SelectedKeyProperty').GetValue($null),[Windows.Data.Binding]::new('ActiveSystemModule'))|Out-Null;Assert-Selected $modules 'Network'
 $surface.UpdateLayout();$bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new([int](620*$scale),[int](345*$scale),96*$scale,96*$scale,[Windows.Media.PixelFormats]::Pbgra32);$bitmap.Render($surface)
 $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap));$stream=[IO.File]::Create((Join-Path $output "$theme-$scale.png"));try{$encoder.Save($stream)}finally{$stream.Dispose()}
 }finally{$native.Dispose()}
}}
Write-Output "Verified production navigation/getters/chip handler and transition hosts at four scales in both themes; injected settings saves=$([NavigationFixtureSink]::Writes), no user settings written."
$app.Shutdown()