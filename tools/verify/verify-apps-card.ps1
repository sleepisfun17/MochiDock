$ErrorActionPreference='Stop'
# Renders the real Apps card (notifications from watched apps) in both themes at four scales, with and without an app logo,
# and checks bounds, the Open button label and Matcha contrast. Never creates MochiDock.App; no notification access is used.
$setup=(Get-Content (Join-Path $PSScriptRoot 'verify-prayer-capsule.ps1') -Raw).Split('$checks=0')[0]
$setup=$setup.Replace('$PSScriptRoot',('{0}{1}{0}' -f [char]39,$PSScriptRoot));Invoke-Expression $setup
$app.Resources.MergedDictionaries.Add([Windows.Markup.XamlReader]::Parse(((Get-Content (Join-Path $repo 'MochiDock/Views/NavigationIconResources.xaml') -Raw) -replace ' x:Shared="False"','')))
foreach($style in $source.SelectNodes('//*[local-name()="ResourceDictionary"]/*[local-name()="Style"]')){
 $key=$style.GetAttribute('Key','http://schemas.microsoft.com/winfx/2006/xaml');$parsed=[Windows.Markup.XamlReader]::Parse($style.OuterXml,$context)
 if($key){$app.Resources[$key]=$parsed}else{$app.Resources[$parsed.TargetType]=$parsed}
}
$output=Join-Path $repo 'MochiDock/obj/apps-card';$null=New-Item -ItemType Directory -Force $output
$viewType=$assembly.GetType('MochiDock.Views.Cards.AppsCardView');$modelType=$assembly.GetType('MochiDock.Models.AppNotification')
function Lum($color){$v=@($color.R,$color.G,$color.B)|ForEach-Object{$c=$_/255.0;if($c -le .04045){$c/12.92}else{[Math]::Pow(($c+.055)/1.055,2.4)}};return .2126*$v[0]+.7152*$v[1]+.0722*$v[2]}
function Find-All($root,$list){$list.Add($root);for($i=0;$i -lt [Windows.Media.VisualTreeHelper]::GetChildrenCount($root);$i++){Find-All ([Windows.Media.VisualTreeHelper]::GetChild($root,$i)) $list}}
# A small generated logo stands in for the app icon Windows would supply.
$logo=[Windows.Media.DrawingImage]::new([Windows.Media.GeometryDrawing]::new([Windows.Media.Brushes]::SeaGreen,$null,[Windows.Media.RectangleGeometry]::new([Windows.Rect]::new(0,0,16,16),4,4)));$logo.Freeze()
$checks=0
foreach($scale in @(1,1.25,1.5,2)){
 $view=[Activator]::CreateInstance($viewType)
 $surface=[Windows.Controls.Border]::new();$surface.Padding=[Windows.Thickness]::new(12);$surface.Child=$view;$surface.Width=620;$surface.Height=230;$surface.UseLayoutRounding=$true
 $fixtureWindow=[Windows.Window]::new();$fixtureWindow.Content=$surface;$fixtureWindow.WindowStyle=[Windows.WindowStyle]::None;$fixtureWindow.ShowInTaskbar=$false;$fixtureWindow.ShowActivated=$false;$fixtureWindow.Left=-32000;$fixtureWindow.Top=-32000;$fixtureWindow.SizeToContent=[Windows.SizeToContent]::WidthAndHeight
 $fixtureWindow.Show()
 try{foreach($theme in @('Dark','Matcha')){
  $assembly.GetType('MochiDock.Services.ThemeService').GetMethod('ApplyTheme').Invoke($null,@($fixtureWindow,$theme));$surface.Background=$app.Resources['IslandGroundBrush']
  [Windows.Media.VisualTreeHelper]::SetRootDpi($fixtureWindow,[Windows.DpiScale]::new($scale,$scale))
  foreach($withIcon in @($true,$false)){
   $items=foreach($i in 0..2){$n=[Activator]::CreateInstance($modelType);$n.AppName='LINE';$n.Aumid='NAVER.LINEWin8_8ptj331gd3tyt!LINE';$n.Title=@('Aki Tanaka','Family group','A very long sender name that has to be trimmed before it reaches the counter')[$i];$n.Message='See you at the station at seven, I will bring the tickets 🎫';if($withIcon){$n.Icon=$logo};$n}
   $view.DataContext=[pscustomobject]@{CurrentApp=$items[2];RecentAppMessages=$items;OpenAppButtonText='Open LINE'}
   $surface.Measure([Windows.Size]::new(620,230));$surface.Arrange([Windows.Rect]::new(0,0,620,230));$surface.UpdateLayout()
   $visuals=[Collections.Generic.List[object]]::new();Find-All $surface $visuals
   $open=@($visuals|Where-Object{$_ -is [Windows.Controls.Button] -and $_.Content -eq 'Open LINE'})
   if($open.Count -ne 1){throw "Open button label missing ($theme/$scale)"}
   $images=@($visuals|Where-Object{$_ -is [Windows.Controls.Image] -and $_.IsVisible -and [object]::ReferenceEquals($_.Source,$logo)})   # emoji in messages are images too
   if($withIcon -and $images.Count -lt 4){throw "App logos not shown ($theme/$scale): $($images.Count)"}
   if(!$withIcon -and $images.Count -ne 0){throw "Empty logo shown ($theme/$scale)"}
   foreach($text in $visuals|Where-Object{$_ -is [Windows.Controls.TextBlock] -and $_.Text -and $_.IsVisible}){
    $position=$text.TransformToAncestor($surface).Transform([Windows.Point]::new(0,0))
    if($position.X -lt -.5 -or $position.X+$text.ActualWidth -gt 620.5){throw "Apps text exceeds the card: $($text.Text)"}
    if($theme -eq 'Matcha'){
     $background=$surface.Background;$ancestor=[Windows.Media.VisualTreeHelper]::GetParent($text)
     while($ancestor){if($ancestor -is [Windows.Controls.Border] -and $ancestor.Background -is [Windows.Media.SolidColorBrush] -and $ancestor.Background.Color.A -eq 255){$background=$ancestor.Background;break};$ancestor=[Windows.Media.VisualTreeHelper]::GetParent($ancestor)}
     $a=Lum $text.Foreground.Color;$b=Lum $background.Color
     if(([Math]::Max($a,$b)+.05)/([Math]::Min($a,$b)+.05) -lt 4.5){throw "Matcha Apps text contrast failed: $($text.Text)"}
    }
    $checks++
   }
   $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new([int](620*$scale),[int](230*$scale),96*$scale,96*$scale,[Windows.Media.PixelFormats]::Pbgra32);$bitmap.Render($surface)
   $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap));$stream=[IO.File]::Create((Join-Path $output "$theme-$scale-icon$withIcon.png"));try{$encoder.Save($stream)}finally{$stream.Dispose()}
  }
 }}finally{$fixtureWindow.Close()}
}
Write-Output "Verified the Apps card in both themes at four scales, with and without app logos ($checks text checks). Renders in $output."
$app.Shutdown()
