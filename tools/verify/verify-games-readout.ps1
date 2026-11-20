$ErrorActionPreference='Stop'
# Load only resources and service-free view-model test data.
$setup=(Get-Content (Join-Path $PSScriptRoot 'verify-prayer-capsule.ps1') -Raw).Split('$checks=0')[0]
$setup=$setup.Replace('$PSScriptRoot',('{0}{1}{0}' -f [char]39,$PSScriptRoot));Invoke-Expression $setup
$output=Join-Path $repo 'MochiDock/obj/games-readout';$null=New-Item -ItemType Directory -Force $output
foreach($focus in @('IslandFocusVisual','IslandCheckFocusVisual')){$focusNode=$source.SelectSingleNode('//*[local-name()="Style" and @*[local-name()="Key"]="'+$focus+'"]');$app.Resources[$focus]=[Windows.Markup.XamlReader]::Parse($focusNode.OuterXml,$context)}
$node=$source.SelectSingleNode('//*[local-name()="Style" and @*[local-name()="Key"]="ModernDarkCheckBox"]');$app.Resources['ModernDarkCheckBox']=[Windows.Markup.XamlReader]::Parse($node.OuterXml,$context)
[xml]$hardware=Get-Content (Join-Path $repo 'MochiDock/Views/Cards/HardwareCardView.xaml') -Raw
$markup=$hardware.SelectSingleNode('//*[local-name()="Grid" and @Tag="Games"]').OuterXml
if($markup -match 'Stays|Peeks|SegmentedPill|Alt\+Shift\+F|frame of delay'){throw 'Inactive gaming controls/claims remain'}
function Luminance($color){$v=@($color.R,$color.G,$color.B)|ForEach-Object{$c=$_/255.0;if($c -le 0.04045){$c/12.92}else{[Math]::Pow(($c+0.055)/1.055,2.4)}};return .2126*$v[0]+.7152*$v[1]+.0722*$v[2]}
foreach($theme in @('Dark','Matcha')){foreach($scale in @(1,1.25,1.5,2)){foreach($pin in @($false,$true)){
 $bootstrap=[Windows.Window]::new();$assembly.GetType('MochiDock.Services.ThemeService').GetMethod('ApplyTheme').Invoke($null,@($bootstrap,$theme))
 $vm=New-Model $false 'Dhuhr';Set-Field $vm '_pomodoroService' $null;Set-Field $vm '_isStatsPinnedToCapsule' $pin
 $settings=$vmType.GetField('_settings',$flags).GetValue($vm);$settings.EnablePrayerPreNotification=$false;$settings.ShowPrayerInCapsule=$false
 # Fill the lists a real player would have, in memory only (the view model here is built without its constructor, so give it
 # its collections and a playtime store on a temporary path); this checks the layout with content.
 $coreAssembly=[Reflection.Assembly]::LoadFrom((Join-Path (Split-Path $assembly.Location) "MochiDock.Core.dll"))
 $recordType=$coreAssembly.GetType('MochiDock.Core.GameSessionRecord')
 $sessions=[Activator]::CreateInstance([Collections.ObjectModel.ObservableCollection`1].MakeGenericType($recordType))
foreach($game in 'Valorant','PUBG: BATTLEGROUNDS'){$record=[Activator]::CreateInstance($recordType);$record.Game=$game;$record.AverageFps=143;$record.Low1Fps=98;$record.TrendText="vs your usual (8 sessions): avg +6 FPS, 1% low -3";$sessions.Add($record)}
 $vmType.GetField('<RecentGameSessions>k__BackingField',$flags).SetValue($vm,$sessions)
 $playtime=[Activator]::CreateInstance($assembly.GetType('MochiDock.Services.GamePlaytimeStore'),[object[]]@([string][IO.Path]::Combine([IO.Path]::GetTempPath(),('mochi-games-readout-'+[Guid]::NewGuid().ToString('N')+'.json'))))
 foreach($game in 'Valorant','PUBG: BATTLEGROUNDS','League of Legends'){$playtime.Ledger.Add($game,[DateTime]::Now.AddMinutes(-90),[DateTime]::Now.AddMinutes(-10))}
 Set-Field $vm '_playtime' $playtime
 $vmType.GetMethod('RefreshPlaytime',$flags).Invoke($vm,@())
 $view=[Windows.Markup.XamlReader]::Parse($markup,$context);$view.Visibility=[Windows.Visibility]::Visible;$view.DataContext=$vm
 $surface=[Windows.Controls.Border]::new();$surface.Padding=[Windows.Thickness]::new(12);$surface.Width=620;$surface.Height=270;$surface.Child=$view
 $surface.SetResourceReference([Windows.Documents.TextElement]::FontFamilyProperty,'IslandUiFont')
 [Windows.Media.TextOptions]::SetTextRenderingMode($surface,[Windows.Media.TextRenderingMode]::Grayscale);[Windows.Media.TextOptions]::SetTextFormattingMode($surface,[Windows.Media.TextFormattingMode]::Ideal);[Windows.Media.TextOptions]::SetTextHintingMode($surface,[Windows.Media.TextHintingMode]::Fixed)
 $themeWindow=[Windows.Window]::new();$assembly.GetType('MochiDock.Services.ThemeService').GetMethod('ApplyTheme').Invoke($null,@($themeWindow,$theme));$surface.Background=$app.Resources['IslandGroundBrush'];$surface.UseLayoutRounding=$true
 [Windows.Media.VisualTreeHelper]::SetRootDpi($surface,[Windows.DpiScale]::new($scale,$scale));$surface.Measure([Windows.Size]::new(620,270));$surface.Arrange([Windows.Rect]::new(0,0,620,270));$surface.UpdateLayout()
 $visuals=[Collections.Generic.List[object]]::new();Find-Visuals $surface $visuals
 $labelFound=$false;$helpFound=$false
 foreach($visual in $visuals|Where-Object{$_ -is [Windows.Controls.TextBlock] -and $_.Text}){
  if($visual.Text -eq 'Pin readout'){$labelFound=$true}
  if($visual.Text -like 'FPS mode appears automatically*'){$helpFound=$true}
  $position=$visual.TransformToAncestor($surface).Transform([Windows.Point]::new(0,0))
  if($position.X -lt 0 -or $position.Y -lt 0 -or $position.X+$visual.ActualWidth -gt 620.5 -or $position.Y+$visual.ActualHeight -gt 270.5 -or $visual.DesiredSize.Height-$visual.Margin.Top-$visual.Margin.Bottom -gt $visual.ActualHeight+0.5){throw "Games text clips: $theme/$scale $($visual.Text) pos=$position actual=$($visual.ActualWidth),$($visual.ActualHeight) desired=$($visual.DesiredSize)"}
    $background=$surface.Background;$ancestor=[Windows.Media.VisualTreeHelper]::GetParent($visual)
  while($ancestor){if($ancestor -is [Windows.Controls.Border] -and $ancestor.Background -is [Windows.Media.SolidColorBrush] -and $ancestor.Background.Color.A -eq 255){$background=$ancestor.Background;break};$ancestor=[Windows.Media.VisualTreeHelper]::GetParent($ancestor)}
  $a=Luminance $visual.Foreground.Color;$b=Luminance $background.Color
  if(([Math]::Max($a,$b)+.05)/([Math]::Min($a,$b)+.05) -lt 4.5){throw "Games text contrast failed: $theme $($visual.Text) $($visual.Foreground) / $($surface.Background) a=$a b=$b"}
 }
 if(!$labelFound -or !$helpFound){throw 'Pin label/help missing'}
 $checkbox=$visuals|Where-Object{$_ -is [Windows.Controls.CheckBox]}|Select-Object -First 1
 if($checkbox.IsChecked -ne $pin){throw 'Pin checkbox does not reflect saved state'}
 $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new([int](620*$scale),[int](270*$scale),96*$scale,96*$scale,[Windows.Media.PixelFormats]::Pbgra32);$bitmap.Render($surface)
 $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap));$stream=[IO.File]::Create((Join-Path $output "$theme-$scale-pin$pin.png"));try{$encoder.Save($stream)}finally{$stream.Dispose()}
 # Evaluate the real presentation getters without detection, telemetry, persistence or timers.
 foreach($legacy in @($false,$true)){
  $settings.GameOverlayReadoutPeeks=$legacy
  Set-Field $vm '_wasGaming' $true
  if(!$vm.IsStatsShownInCapsule -or $vm.CompactCapsuleIconType -ne 'Gaming'){throw 'Detected game did not select FPS presentation'}
  Set-Field $vm '_wasGaming' $false
  if($vm.IsStatsShownInCapsule -ne $pin){throw 'Game exit did not restore pin-dependent presentation'}
  if(!$pin -and $vm.CompactCapsuleIconType -eq 'Gaming'){throw 'Unpinned readout remained in FPS mode'}
 }
 # A notification takes the main pill when idle. Prayer remains eligible during a game.
 Set-Field $vm '_isStatsPinnedToCapsule' $false
 $queue=$vmType.GetField('_notificationQueue',$flags).GetValue($vm);$queue.Add([Enum]::Parse($assembly.GetType('MochiDock.ViewModels.IslandCardType'),'Email'))
 if(!$vm.IsNotificationInMainPill){throw 'Notification priority lost'};Set-Field $vm '_wasGaming' $true;Set-Field $vm '_isNotificationTakeover' $true
 if(!$vm.IsNotificationInMainPill -or $vm.IsPrayerReminderVisible){throw 'Gaming notification takeover priority lost'};$queue.Clear();Set-Field $vm '_isNotificationTakeover' $false
 $settings.EnablePrayerPreNotification=$true;$settings.ShowPrayerInCapsule=$true;Set-Field $vm '_wasGaming' $true
 if(!$vm.IsPrayerReminderVisible){throw 'Prayer reminder lost priority during gaming'}
}}}
Write-Output 'Verified Games panel pin states, contrast/bounds at four scales, legacy preference independence and automatic/pinned presentation states.'
$app.Shutdown()