$ErrorActionPreference='Stop'
$setup=(Get-Content (Join-Path $PSScriptRoot 'verify-prayer-capsule.ps1') -Raw).Split('$checks=0')[0]
$setup=$setup.Replace('$PSScriptRoot',('{0}{1}{0}' -f [char]39,$PSScriptRoot));Invoke-Expression $setup
# EmailCardView uses IconClipboard from the flat colour icon dictionary, which App.xaml merges at runtime.
$app.Resources.MergedDictionaries.Add([Windows.Markup.XamlReader]::Parse((Get-Content (Join-Path $repo 'MochiDock/Views/ColorIconResources.xaml') -Raw)))
foreach($style in $source.SelectNodes('//*[local-name()="ResourceDictionary"]/*[local-name()="Style"]')){
 $key=$style.GetAttribute('Key','http://schemas.microsoft.com/winfx/2006/xaml');$parsed=[Windows.Markup.XamlReader]::Parse($style.OuterXml,$context)
 if($key){$app.Resources[$key]=$parsed}else{$app.Resources[$parsed.TargetType]=$parsed}
}
$output=Join-Path $repo 'MochiDock/obj/email-theme';$null=New-Item -ItemType Directory -Force $output
$viewType=$assembly.GetType('MochiDock.Views.Cards.EmailCardView');$feedback=$viewType.GetMethod('SetCopyFeedback',$flags)
$hoverKey=[Windows.UIElement].GetField('IsMouseOverPropertyKey',[Reflection.BindingFlags]'NonPublic,Static').GetValue($null)
function Lum($color){$v=@($color.R,$color.G,$color.B)|ForEach-Object{$c=$_/255.0;if($c -le .04045){$c/12.92}else{[Math]::Pow(($c+.055)/1.055,2.4)}};return .2126*$v[0]+.7152*$v[1]+.0722*$v[2]}
function Pump([int]$milliseconds){$frame=[Windows.Threading.DispatcherFrame]::new();$timer=[Windows.Threading.DispatcherTimer]::new();$timer.Interval=[TimeSpan]::FromMilliseconds($milliseconds);$timer.Tag=$frame;$timer.Add_Tick({param($sender,$eventArgs)$sender.Stop();$sender.Tag.Continue=$false});$timer.Start();try{[Windows.Threading.Dispatcher]::PushFrame($frame)}finally{$timer.Stop()}}
foreach($scale in @(1,1.25,1.5,2)){
 $view=[Activator]::CreateInstance($viewType)
 $surface=[Windows.Controls.Border]::new();$surface.Padding=[Windows.Thickness]::new(12);$surface.Child=$view;$surface.Width=620;$surface.Height=230;$surface.UseLayoutRounding=$true
 [Windows.Media.TextOptions]::SetTextRenderingMode($surface,[Windows.Media.TextRenderingMode]::Grayscale);[Windows.Media.TextOptions]::SetTextFormattingMode($surface,[Windows.Media.TextFormattingMode]::Ideal);[Windows.Media.TextOptions]::SetTextHintingMode($surface,[Windows.Media.TextHintingMode]::Fixed)
 $fixtureWindow=[Windows.Window]::new();$fixtureWindow.Content=$surface;$fixtureWindow.WindowStyle=[Windows.WindowStyle]::None;$fixtureWindow.ShowInTaskbar=$false;$fixtureWindow.ShowActivated=$false;$fixtureWindow.Left=-32000;$fixtureWindow.Top=-32000;$fixtureWindow.SizeToContent=[Windows.SizeToContent]::WidthAndHeight
 $fixtureWindow.Show()
 try{foreach($theme in @('Dark','Matcha','Dark')){
  $assembly.GetType('MochiDock.Services.ThemeService').GetMethod('ApplyTheme').Invoke($null,@($fixtureWindow,$theme));$surface.Background=$app.Resources['IslandGroundBrush']
  $fixtureWindow.UpdateLayout()
  $retainedStatus=$view.FindName('CopyStatusText');$retainedKey=if($retainedStatus.Text -eq '✓ Copied!'){'IslandPositiveBrush'}else{'IslandAccentBrush'}
  if($retainedStatus.Foreground.Color -ne $app.Resources[$retainedKey].Color){throw 'Retained copy status did not change with the theme'}
  [Windows.Media.VisualTreeHelper]::SetRootDpi($fixtureWindow,[Windows.DpiScale]::new($scale,$scale))
  foreach($client in @('Mailspring','Outlook')){foreach($hasCode in @($false,$true)){
   $mail=[Activator]::CreateInstance($assembly.GetType('MochiDock.Models.EmailNotification'));$mail.FolderName='family@example.com';$mail.Sender='ChatGPT';$mail.Subject='Welcome to ChatGPT Plus';$mail.BodyPreview='Your subscription is ready to review.';$mail.ClientSource=$client
   if($hasCode){$mail.ExtractedVerificationCode='123456'}
   $view.DataContext=[pscustomobject]@{CurrentEmail=$mail;RecentEmailMessages=@($mail,$mail)}
   foreach($copied in @($false,$true)){
    $feedback.Invoke($view,@($copied));[Windows.Threading.Dispatcher]::CurrentDispatcher.Invoke([Windows.Threading.DispatcherPriority]::Loaded,[Action]{})
    foreach($state in @('Normal','Hover','Disabled')){
     $surface.Measure([Windows.Size]::new(620,230));$surface.Arrange([Windows.Rect]::new(0,0,620,230));$surface.UpdateLayout()
     $visuals=[Collections.Generic.List[object]]::new();Find-Visuals $surface $visuals
     $buttons=@($visuals|Where-Object{$_ -is [Windows.Controls.Button] -and $_.Content -is [string]})
     foreach($button in $buttons){$button.IsEnabled=$state -ne 'Disabled';$button.SetValue($hoverKey,$state -eq 'Hover')}
     $surface.UpdateLayout()
     $primary=$buttons|Where-Object{$_.Content -eq $client}|Select-Object -First 1
     if(!$primary -or $primary.Background.Color -ne $app.Resources['IslandAccentFillBrush'].Color -or $primary.Foreground.Color -ne $app.Resources['IslandAccentButtonInkBrush'].Color){throw "Primary mail action palette mismatch $theme/$scale/$client/$state buttons=$($buttons.Content -join ',') primary=$($primary.Content) bg=$($primary.Background) ink=$($primary.Foreground) expected=$($app.Resources['IslandAccentFillBrush'])/$($app.Resources['IslandAccentButtonInkBrush'])"}
     if($state -eq 'Disabled' -and $primary.Opacity -ne .45){throw 'Disabled action opacity changed'}
     $status=$view.FindName('CopyStatusText');$expected=if($copied){'IslandPositiveBrush'}else{'IslandAccentBrush'}
     if($status.Foreground.Color -ne $app.Resources[$expected].Color){throw 'Copy status palette mismatch'}
     $badges=0
     foreach($text in $visuals|Where-Object{$_ -is [Windows.Controls.TextBlock] -and $_.Text}){
      $ancestor=$text;$hidden=$false;while($ancestor -and $ancestor -ne $surface){if($ancestor.Visibility -ne [Windows.Visibility]::Visible){$hidden=$true;break};$ancestor=[Windows.Media.VisualTreeHelper]::GetParent($ancestor)};if($hidden){continue}
      if($text.Text -eq $mail.FolderName){$badges++;if($text.Foreground.Color -ne $app.Resources['IslandEmailBadgeInkBrush'].Color -or $text.Parent.Background.Color -ne $app.Resources['IslandEmailBadgeBackgroundBrush'].Color){throw 'Email badge palette mismatch'}}
      $position=$text.TransformToAncestor($surface).Transform([Windows.Point]::new(0,0))
      if($position.X -lt -.5 -or $position.X+$text.ActualWidth -gt 620.5){throw "Email text exceeds horizontal bounds: $($text.Text)"}
      if($theme -eq 'Matcha' -and $state -ne 'Disabled'){
       $background=$surface.Background;$ancestor=[Windows.Media.VisualTreeHelper]::GetParent($text)
       while($ancestor){if($ancestor -is [Windows.Controls.Border] -and $ancestor.Background -is [Windows.Media.SolidColorBrush] -and $ancestor.Background.Color.A -eq 255){$background=$ancestor.Background;break};$ancestor=[Windows.Media.VisualTreeHelper]::GetParent($ancestor)}
       $a=Lum $text.Foreground.Color;$b=Lum $background.Color
       if(([Math]::Max($a,$b)+.05)/([Math]::Min($a,$b)+.05) -lt 4.5){throw "Matcha email text contrast failed: $($text.Text) $($text.Foreground) / $background"}
      }
     }
     if($badges -lt 2){throw 'Header and generated row badge coverage missing'}
     $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new([int](620*$scale),[int](230*$scale),96*$scale,96*$scale,[Windows.Media.PixelFormats]::Pbgra32);$bitmap.Render($surface)
     $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap));$stream=[IO.File]::Create((Join-Path $output "$theme-$scale-$client-code$hasCode-copied$copied-$state.png"));try{$encoder.Save($stream)}finally{$stream.Dispose()}
    }
   }
  }}
  # A success status must also change brushes while retained during a theme switch.
  $feedback.Invoke($view,@($true))
 }
 # Exercise the real timer/reset without copying, opening mail or touching clipboard.
 $ownedTimer=$viewType.GetField('_copyFeedbackTimer',$flags).GetValue($view);$ownedTimer.Start();Pump 2100
 if($view.FindName('CopyStatusText').Text -ne 'Copy Code' -or $view.FindName('CopyStatusText').Foreground.Color -ne $app.Resources['IslandAccentBrush'].Color){throw 'Copy feedback timeout restored a stale color'}
 $feedback.Invoke($view,@($true));$ownedTimer.Start()
 }finally{$fixtureWindow.Close();Pump 30}
 if($ownedTimer.IsEnabled -or $view.FindName('CopyStatusText').Text -ne 'Copy Code'){throw 'Copy feedback was not normalized at unload'}
}
Write-Output 'Verified retained Email badges/actions/copy feedback in both themes, Mailspring/Outlook, code/no-code, normal/hover/disabled states at four scales. No email or clipboard actions executed.'
$app.Shutdown()