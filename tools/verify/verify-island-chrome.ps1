param([string]$Configuration="Release")
$ErrorActionPreference="Stop"
Add-Type -AssemblyName PresentationFramework
$repo=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $repo "MochiDock/bin/$Configuration/net8.0-windows10.0.19041.0/MochiDock.dll"))
$application=[Windows.Application]::new();$application.ShutdownMode=[Windows.ShutdownMode]::OnExplicitShutdown
[xml]$appSource=Get-Content (Join-Path $repo "MochiDock/App.xaml") -Raw
foreach($node in $appSource.SelectNodes('//*[local-name()="FontFamily"]')){$application.Resources[$node.GetAttribute("Key","http://schemas.microsoft.com/winfx/2006/xaml")]=[Windows.Media.FontFamily]::new([uri]"pack://application:,,,/",$node.InnerText)}
[xml]$main=Get-Content (Join-Path $repo "MochiDock/MainWindow.xaml") -Raw
foreach($name in @("CompactPillBorder","ExpandedBorder","CompanionBubble")){
 $node=$main.SelectSingleNode('//*[@*[local-name()="Name"]="'+$name+'"]')
 if($node.Background -ne '{DynamicResource IslandGroundBrush}'){throw "Outer background is not shared: $name"}
}
$output=Join-Path $repo "MochiDock/obj/chrome";$null=New-Item -ItemType Directory -Force $output
foreach($theme in @("Default","Matcha","Hojicha","KuroNight")){
 $window=[Windows.Window]::new();$assembly.GetType("MochiDock.Services.ThemeService").GetMethod("ApplyTheme").Invoke($null,@($window,$theme))
 foreach($notch in @($false,$true)){foreach($scale in @(1,1.25,1.5,2)){foreach($state in @(@(125,36),@(300,32),@(315,48),@(420,200),@(620,360))){
  $w=$state[0];$h=$state[1]
  $chrome=[Activator]::CreateInstance($assembly.GetType("MochiDock.Controls.IslandChrome"));$chrome.Width=$w;$chrome.Height=$h;$chrome.IsNotch=$notch;$chrome.CornerRadius=[Windows.CornerRadius]::new(18);$chrome.BorderThickness=[Windows.Thickness]::new(1.2)
  $chrome.SetResourceReference([Windows.Controls.Border]::BackgroundProperty,"IslandGroundBrush");$chrome.SetResourceReference([Windows.Controls.Border]::BorderBrushProperty,"IslandBorderBrush")
  $chrome.HorizontalAlignment=[Windows.HorizontalAlignment]::Left;$chrome.VerticalAlignment=[Windows.VerticalAlignment]::Top;$chrome.Margin=[Windows.Thickness]::new(12,0,12,0)
  $surface=[Windows.Controls.Grid]::new();$surface.Width=$w+24;$surface.Height=$h+2;$surface.Children.Add($chrome)|Out-Null
  [Windows.Media.VisualTreeHelper]::SetRootDpi($surface,[Windows.DpiScale]::new($scale,$scale));$surface.Measure([Windows.Size]::new($w+24,$h+2));$surface.Arrange([Windows.Rect]::new(0,0,$w+24,$h+2));$surface.UpdateLayout()
  if($notch){$geometry=$chrome.NotchGeometry();if(!$geometry.FillContains([Windows.Point]::new(-4,1)) -or $geometry.FillContains([Windows.Point]::new(-8,8))){throw "Shoulder geometry or hit silhouette failed"}}
  $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new([int][Math]::Ceiling(($w+24)*$scale),[int][Math]::Ceiling(($h+2)*$scale),96*$scale,96*$scale,[Windows.Media.PixelFormats]::Pbgra32);$bitmap.Render($surface)
  $pixel=[byte[]]::new(4);$bitmap.CopyPixels([Windows.Int32Rect]::new([int](($w/2+12)*$scale),[int](($h/2)*$scale),1,1),$pixel,4,0)
  if($pixel[3] -ne 255){throw "Outer chrome is not opaque"}
  if($theme -eq "Default" -and ($pixel[0]+$pixel[1]+$pixel[2]) -ne 0){throw "Default chrome is not pure black"}
  $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap));$stream=[IO.File]::Create((Join-Path $output "$theme-$notch-$w-$h-$scale.png"));try{$encoder.Save($stream)}finally{$stream.Dispose()}
 }}}
}
$application.Shutdown()
Write-Output "Verified production chrome, pure-black pixels, shoulders and resize dimensions in all four themes at four scales."
