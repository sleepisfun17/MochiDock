param([string]$Configuration = "Release", [switch]$FxOnly, [switch]$ThemeSwitchOnly)
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName PresentationFramework
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$dll = Join-Path $repo "MochiDock/bin/$Configuration/net8.0-windows10.0.19041.0/MochiDock.dll"
$assembly = [Reflection.Assembly]::LoadFrom($dll)
# Load resources only: never instantiate MochiDock.App or MainWindow.

$application = New-Object System.Windows.Application
$application.ShutdownMode=[Windows.ShutdownMode]::OnExplicitShutdown
[xml]$source = Get-Content (Join-Path $repo "MochiDock/App.xaml") -Raw
foreach($fontNode in $source.SelectNodes('//*[local-name()="FontFamily"]')) {
 $key=$fontNode.GetAttribute("Key","http://schemas.microsoft.com/winfx/2006/xaml")
 $application.Resources[$key]=[Windows.Media.FontFamily]::new([uri]"pack://application:,,,/",$fontNode.InnerText)
}
foreach($weightNode in $source.SelectNodes('//*[local-name()="FontWeight"]')){
 $application.Resources[$weightNode.GetAttribute("Key","http://schemas.microsoft.com/winfx/2006/xaml")]=[Windows.FontWeightConverter]::new().ConvertFromString($weightNode.InnerText)
}
$family=$application.Resources["QuicksandFont"]
$dictionary = [Windows.Markup.XamlReader]::Parse((Get-Content (Join-Path $repo "MochiDock/Views/TypographyResources.xaml") -Raw))
$application.Resources.MergedDictionaries.Add($dictionary)
$application.Resources.MergedDictionaries.Add([Windows.Markup.XamlReader]::Parse((Get-Content (Join-Path $repo "MochiDock/Views/ColorIconResources.xaml") -Raw)))   # flat colour icons used by the cards
foreach ($weight in @([Windows.FontWeights]::Normal,[Windows.FontWeights]::Medium,[Windows.FontWeights]::SemiBold)) {
 $primary = [Windows.Media.FontFamily]::new([uri]"pack://application:,,,/", ($family.Source -replace ", Yu Gothic UI.*$",""))
 $typeface = New-Object Windows.Media.Typeface($primary,[Windows.FontStyles]::Normal,$weight,[Windows.FontStretches]::Normal)
 $glyph = $null
 if (!$typeface.TryGetGlyphTypeface([ref]$glyph)) { throw "Bundled face did not resolve: $weight" }
 if ($glyph.FontUri.ToString() -notmatch 'Quicksand') { throw "Unexpected fallback: $($glyph.FontUri)" }
 Write-Output "Resolved $weight -> $($glyph.FontUri)"
}
foreach ($key in $dictionary.Keys) {
 if ($dictionary[$key] -isnot [Windows.Style]) { continue } # sizes such as IslandInputFontSize are not text styles
 $text = New-Object Windows.Controls.TextBlock
 $text.FontFamily = $family
 $text.Style = $dictionary[$key]
 $text.Text = "a 100% ギザギザ 日本語 😀"
 $text.Measure([Windows.Size]::new(600,100))
 Write-Output "$key : $($text.FontSize) / $($text.FontWeight), height=$($text.DesiredSize.Height)"
}
[xml]$window = Get-Content (Join-Path $repo "MochiDock/MainWindow.xaml") -Raw
if ($window.DocumentElement.GetAttribute("TextOptions.TextRenderingMode") -ne "Grayscale") { throw "Expected grayscale rendering" }
if ($window.DocumentElement.GetAttribute("TextOptions.TextFormattingMode") -ne "Ideal") { throw "Expected Ideal formatting" }
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class TypographyDpi {
 [DllImport("user32.dll")] public static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);
 [DllImport("user32.dll")] public static extern int GetAwarenessFromDpiAwarenessContext(IntPtr context);
 [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr window);
}
'@
$running = Get-Process MochiDock -ErrorAction SilentlyContinue
foreach ($process in $running) {
 if ($process.MainWindowHandle -ne [IntPtr]::Zero) {
  $context = [TypographyDpi]::GetWindowDpiAwarenessContext($process.MainWindowHandle)
  Write-Output "Running window awareness=$([TypographyDpi]::GetAwarenessFromDpiAwarenessContext($context)), DPI=$([TypographyDpi]::GetDpiForWindow($process.MainWindowHandle))"
 }
}
if (!$running) { Write-Output "Live DPI unavailable to this inspector (an elevated process may be hidden); no MochiDock instance was launched." }

# Render the actual control against fixtures; no application services or MainWindow.
foreach ($brush in $source.SelectNodes('//*[local-name()="SolidColorBrush"]')) {
 $key=$brush.GetAttribute("Key","http://schemas.microsoft.com/winfx/2006/xaml")
 if($key){$application.Resources[$key]=[Windows.Media.BrushConverter]::new().ConvertFromString($brush.Color)}
}
foreach ($color in $source.SelectNodes('//*[local-name()="Color"]')) {
 $key=$color.GetAttribute("Key","http://schemas.microsoft.com/winfx/2006/xaml")
 if($key){$application.Resources[$key]=[Windows.Media.ColorConverter]::ConvertFromString($color.InnerText)}
}
$application.Resources["BoolToVisibility"]=[Windows.Controls.BooleanToVisibilityConverter]::new()
$application.Resources["InverseBoolToVisibility"]=[Activator]::CreateInstance($assembly.GetType("MochiDock.Views.InverseBooleanToVisibilityConverter"))
$context=[Windows.Markup.ParserContext]::new()
$context.XmlnsDictionary.Add("","http://schemas.microsoft.com/winfx/2006/xaml/presentation")
$context.XmlnsDictionary.Add("x","http://schemas.microsoft.com/winfx/2006/xaml")
$context.XmlnsDictionary.Add("views","clr-namespace:MochiDock.Views;assembly=MochiDock")
$context.XmlnsDictionary.Add("controls","clr-namespace:MochiDock.Controls;assembly=MochiDock")
foreach($style in $source.SelectNodes('//*[local-name()="ResourceDictionary"]/*[local-name()="Style"]')) {
 $key=$style.GetAttribute("Key","http://schemas.microsoft.com/winfx/2006/xaml")
 $parsedStyle=[Windows.Markup.XamlReader]::Parse($style.OuterXml,$context)
 if($key){$application.Resources[$key]=$parsedStyle}else{$application.Resources[$parsedStyle.TargetType]=$parsedStyle}
}
$stats=[Activator]::CreateInstance($assembly.GetType("MochiDock.Services.HardwareStats"))
$stats.NetworkPingMs=13
$output=Join-Path $repo "MochiDock/obj/typography"
$null=New-Item -ItemType Directory -Force $output
# Actual monitor factory and status XAML, retained while palettes switch.
function Relative-Luminance($color){
 $channels=@($color.R,$color.G,$color.B)|ForEach-Object{$v=$_/255.0;if($v -le 0.04045){$v/12.92}else{[Math]::Pow(($v+0.055)/1.055,2.4)}}
 return 0.2126*$channels[0]+0.7152*$channels[1]+0.0722*$channels[2]
}
function Assert-Contrast($foreground,$background,$label){
 $a=Relative-Luminance $foreground.Color;$b=Relative-Luminance $background.Color
 $ratio=([Math]::Max($a,$b)+0.05)/([Math]::Min($a,$b)+0.05)
 if($ratio -lt 4.5){throw "Insufficient text contrast for $label : $ratio"}
}
[xml]$hardwareSource=Get-Content (Join-Path $repo "MochiDock/Views/Cards/HardwareCardView.xaml") -Raw
$pingNode=$hardwareSource.SelectSingleNode('//*[local-name()="TextBlock" and @Text="{Binding HardwareStats.PingText}"]')
$pingMarkup=$pingNode.ParentNode.ParentNode.ParentNode.OuterXml
$gamesNode=$hardwareSource.SelectSingleNode('//*[local-name()="TextBlock" and @Text="{Binding HardwareStats.GameStatusText}"]')
$hoverKey=[Windows.UIElement].GetField("IsMouseOverPropertyKey",[Reflection.BindingFlags]"NonPublic,Static").GetValue($null)
foreach($scale in @(1,1.25,1.5,2)){
 $panel=[Windows.Controls.StackPanel]::new();$panel.Margin=[Windows.Thickness]::new(12)
 $row=[Windows.Controls.StackPanel]::new();$row.Orientation=[Windows.Controls.Orientation]::Horizontal
 $selected=$assembly.GetType("MochiDock.Controls.DisplayMonitorButton").GetMethod("Create").Invoke($null,@("Display 1",$true))
 $other=$assembly.GetType("MochiDock.Controls.DisplayMonitorButton").GetMethod("Create").Invoke($null,@("Display 2",$false))
 $row.Children.Add($selected)|Out-Null;$row.Children.Add($other)|Out-Null;$panel.Children.Add($row)|Out-Null
 $ping=[Windows.Markup.XamlReader]::Parse($pingMarkup,$context);$panel.Children.Add($ping)|Out-Null
 $games=[Windows.Markup.XamlReader]::Parse($gamesNode.OuterXml,$context);$panel.Children.Add($games)|Out-Null
 $panel.DataContext=[pscustomobject]@{HardwareStats=$stats}
 $surface=[Windows.Controls.Border]::new();$surface.Child=$panel;$surface.Width=320;$surface.Height=118;$surface.SetResourceReference([Windows.Controls.Border]::BackgroundProperty,"IslandGroundBrush")
 [Windows.Media.VisualTreeHelper]::SetRootDpi($surface,[Windows.DpiScale]::new($scale,$scale))
 foreach($theme in @("Dark","Matcha","Dark")){
  $hostWindow=[Windows.Window]::new();$hostWindow.Content=$surface;$assembly.GetType("MochiDock.Services.ThemeService").GetMethod("ApplyTheme").Invoke($null,@($hostWindow,$theme));$hostWindow.Content=$null
  foreach($hover in @($false,$true)){
   $selected.SetValue($hoverKey,$hover);$other.SetValue($hoverKey,$hover)
   $surface.Measure([Windows.Size]::new(320,118));$surface.Arrange([Windows.Rect]::new(0,0,320,118));$surface.UpdateLayout()
   Assert-Contrast $selected.Foreground $selected.Background "selected monitor $theme hover=$hover"
   Assert-Contrast $other.Foreground $other.Background "other monitor $theme hover=$hover"
   Assert-Contrast $games.Foreground $application.Resources["IslandSubCardBackgroundBrush"] "status $theme"
   if($games.Foreground.Color -ne $application.Resources["IslandStatusBrush"].Color){throw "Status failed live palette switch"}
   foreach($button in @($selected,$other)){if($button.ActualHeight -gt 34 -or $button.DesiredSize.Width -gt 145){throw "Monitor button bounds changed"}}
   $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new([int](320*$scale),[int](118*$scale),96*$scale,96*$scale,[Windows.Media.PixelFormats]::Pbgra32);$bitmap.Render($surface)
   $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap));$stream=[IO.File]::Create((Join-Path $output "Contrast-$theme-$hover-$scale.png"));try{$encoder.Save($stream)}finally{$stream.Dispose()}
  }
 }
}
Write-Output "Verified monitor/status contrast, hover states and retained Dark/Matcha/Dark switching at four scales."
$chipStyle=$hardwareSource.SelectSingleNode('//*[local-name()="Style" and @*[local-name()="Key"]="ModuleChipStyle"]')
$application.Resources["ModuleChipStyle"]=[Windows.Markup.XamlReader]::Parse($chipStyle.OuterXml,$context)
$stripNode=$hardwareSource.SelectSingleNode('//*[@*[local-name()="Name"]="HardwareModuleStrip"]')
$stripMarkup=$stripNode.OuterXml -replace '\s+(Click|MouseLeftButtonUp|MouseLeftButtonDown)="[^"]*"',''
$moduleFlags=@("Cpu","Gpu","Memory","Drives","Network","Games","Battery")
foreach($theme in @("Dark","Matcha")){foreach($scale in @(1,1.25,1.5,2)){
 $hostWindow=[Windows.Window]::new();$assembly.GetType("MochiDock.Services.ThemeService").GetMethod("ApplyTheme").Invoke($null,@($hostWindow,$theme))
 foreach($moduleIndex in 0..6){
  $strip=[Windows.Markup.XamlReader]::Parse($stripMarkup,$context)
  $data=@{HardwareStats=$stats};foreach($flag in $moduleFlags){$data["Is"+$flag+"ModuleActive"]=$flag -eq $moduleFlags[$moduleIndex]};$strip.DataContext=[pscustomobject]$data
  $surface=[Windows.Controls.Border]::new();$surface.Child=$strip;$surface.Width=592;$surface.Height=50;$surface.SetResourceReference([Windows.Controls.Border]::BackgroundProperty,"IslandGroundBrush")
  [Windows.Media.VisualTreeHelper]::SetRootDpi($surface,[Windows.DpiScale]::new($scale,$scale));$surface.Measure([Windows.Size]::new(592,50));$surface.Arrange([Windows.Rect]::new(0,0,592,50));$surface.UpdateLayout()
  $chip=$strip.Children|Where-Object{[Windows.Controls.Grid]::GetColumn($_) -eq $moduleIndex*2}|Select-Object -First 1
  if($chip.Background.Color -ne $application.Resources["IslandChipActiveBrush"].Color -or $chip.BorderBrush.Color -ne $application.Resources["IslandSelectionBorderBrush"].Color){throw "Stats selection bypassed theme: $($moduleFlags[$moduleIndex])"}
  $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new([int](592*$scale),[int](50*$scale),96*$scale,96*$scale,[Windows.Media.PixelFormats]::Pbgra32);$bitmap.Render($surface)
  $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap));$stream=[IO.File]::Create((Join-Path $output "Selection-$($moduleFlags[$moduleIndex])-$theme-$scale.png"));try{$encoder.Save($stream)}finally{$stream.Dispose()}
 }
}}
# Parse production templates without service-owning card constructors or event handlers.
[xml]$mediaSource=Get-Content (Join-Path $repo "MochiDock/Views/Cards/MediaCardView.xaml") -Raw
# The five Settings sliders now live in the Advanced Settings window sections (the dock Settings tab has none).
$sliderFiles=@{GamingOffsetSlider="Gaming";ScrollSpeedSlider="Appearance";CpuThresholdSlider="Hardware";GpuThresholdSlider="Hardware";TemperatureThresholdSlider="Hardware"}
$sectionSources=@{};foreach($section in @("Gaming","Appearance","Hardware")){[xml]$sectionSources[$section]=Get-Content (Join-Path $repo "MochiDock/Views/Settings/Sections/${section}SectionView.xaml") -Raw}
$playNode=$mediaSource.SelectSingleNode('//*[local-name()="Button" and @Click="PlayPauseButton_Click"]')
$playMarkup=$playNode.OuterXml -replace '\s+Click="[^"]*"',''
function Collect-ControlVisuals($visual,$items){
 $items.Add($visual)
 for($i=0;$i -lt [Windows.Media.VisualTreeHelper]::GetChildrenCount($visual);$i++){Collect-ControlVisuals ([Windows.Media.VisualTreeHelper]::GetChild($visual,$i)) $items}
}
$pressedKey=[Windows.Controls.Primitives.ButtonBase].GetField("IsPressedPropertyKey",[Reflection.BindingFlags]"NonPublic,Static").GetValue($null)
$focusKey=[Windows.UIElement].GetField("IsKeyboardFocusWithinPropertyKey",[Reflection.BindingFlags]"NonPublic,Static").GetValue($null)
foreach($scale in @(1,1.25,1.5,2)){
 $panel=[Windows.Controls.StackPanel]::new();$panel.Margin=[Windows.Thickness]::new(12)
 $row=[Windows.Controls.StackPanel]::new();$row.Orientation=[Windows.Controls.Orientation]::Horizontal
 $playButtons=@()
 foreach($playing in @($false,$true)){
  $button=[Windows.Markup.XamlReader]::Parse($playMarkup,$context);$button.DataContext=[pscustomobject]@{CurrentMedia=[pscustomobject]@{IsPlaying=$playing}}
  $row.Children.Add($button)|Out-Null;$playButtons+=,$button
 }
 $panel.Children.Add($row)|Out-Null
 $sliders=@()
 foreach($name in @("GamingOffsetSlider","ScrollSpeedSlider","CpuThresholdSlider","GpuThresholdSlider","TemperatureThresholdSlider")){
  $sliderNames=@{GamingOffsetSlider="OffsetSlider";ScrollSpeedSlider="ScrollSpeedSlider";CpuThresholdSlider="CpuSlider";GpuThresholdSlider="GpuSlider";TemperatureThresholdSlider="TempSlider"}
  $node=$sectionSources[$sliderFiles[$name]].SelectSingleNode('//*[@*[local-name()="Name"]="'+$sliderNames[$name]+'"]')
  $slider=[Windows.Markup.XamlReader]::Parse(($node.OuterXml -replace '\s+ValueChanged="[^"]*"',''),$context);$slider.Width=260;$slider.Height=25;$slider.HorizontalAlignment=[Windows.HorizontalAlignment]::Left
  $panel.Children.Add($slider)|Out-Null;$sliders+=,$slider
 }
 $surface=[Windows.Controls.Border]::new();$surface.Child=$panel;$surface.Width=310;$surface.Height=190;$surface.SetResourceReference([Windows.Controls.Border]::BackgroundProperty,"IslandGroundBrush")
 [Windows.Media.VisualTreeHelper]::SetRootDpi($surface,[Windows.DpiScale]::new($scale,$scale))
 foreach($theme in @("Dark","Matcha","Dark")){
  $hostWindow=[Windows.Window]::new();$hostWindow.Content=$surface;$assembly.GetType("MochiDock.Services.ThemeService").GetMethod("ApplyTheme").Invoke($null,@($hostWindow,$theme));$hostWindow.Content=$null
  foreach($state in @("Normal","Hover","Pressed","Focused","Disabled")){foreach($fraction in @(0,0.5,1)){
   foreach($button in $playButtons){$button.IsEnabled=$state -ne "Disabled";$button.SetValue($hoverKey,($state -eq "Hover"));$button.SetValue($pressedKey,($state -eq "Pressed"));$button.SetValue($focusKey,($state -eq "Focused"))}
   foreach($slider in $sliders){$slider.IsEnabled=$state -ne "Disabled";$slider.Value=$slider.Minimum+($slider.Maximum-$slider.Minimum)*$fraction;$slider.SetValue($hoverKey,($state -eq "Hover"));$slider.SetValue($focusKey,($state -eq "Focused"))}
   $surface.Measure([Windows.Size]::new(310,190));$surface.Arrange([Windows.Rect]::new(0,0,310,190));$surface.UpdateLayout()
   foreach($button in $playButtons){
    if($button.Background.Color -ne $application.Resources["IslandAccentFillBrush"].Color){throw "Playback fill bypassed theme"}
    $visuals=[Collections.Generic.List[Windows.DependencyObject]]::new();Collect-ControlVisuals $button $visuals
    $glyph=$visuals|Where-Object{$_ -is [Windows.Shapes.Path] -and $_.Visibility -eq [Windows.Visibility]::Visible}|Select-Object -First 1
    if($null -eq $glyph -or $glyph.Fill.Color -ne $application.Resources["IslandMediaGlyphBrush"].Color){throw "Playback glyph bypassed theme"}
    if($theme -eq "Matcha"){Assert-Contrast $glyph.Fill $button.Background "play/pause glyph"}
   }
   foreach($slider in $sliders){
    $visuals=[Collections.Generic.List[Windows.DependencyObject]]::new();Collect-ControlVisuals $slider $visuals
    $thumb=$visuals|Where-Object{$_ -is [Windows.Shapes.Ellipse]}|Select-Object -First 1
    if($null -eq $thumb -or $thumb.Fill.Color -ne $application.Resources["IslandSliderThumbBrush"].Color -or $thumb.Stroke.Color -ne $application.Resources["IslandAccentBrush"].Color){throw "Slider thumb bypassed theme"}
    $progress=$visuals|Where-Object{$_ -is [Windows.Controls.Border] -and $_.Background -is [Windows.Media.SolidColorBrush] -and $_.Background.Color -eq $application.Resources["IslandAccentBrush"].Color}|Select-Object -First 1
    if($null -eq $progress){throw "Slider progress bypassed theme"}
   }
   $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new([int](310*$scale),[int](190*$scale),96*$scale,96*$scale,[Windows.Media.PixelFormats]::Pbgra32);$bitmap.Render($surface)
   $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap));$stream=[IO.File]::Create((Join-Path $output "Controls-$theme-$state-$fraction-$scale.png"));try{$encoder.Save($stream)}finally{$stream.Dispose()}
  }}
 }
}
Write-Output "Verified actual media glyphs and five Settings slider templates across interaction states, endpoints and live theme changes."
if (!$FxOnly -and !$ThemeSwitchOnly) {
foreach($theme in @("Dark","Matcha")) {
 $application.Resources["IslandGroundBrush"]=if($theme -eq "Dark"){[Windows.Media.Brushes]::Black}else{[Windows.Media.BrushConverter]::new().ConvertFromString("#FFF2DD")}
 $application.Resources["IslandInkBrush"]=if($theme -eq "Dark"){[Windows.Media.Brushes]::White}else{[Windows.Media.Brushes]::Black}
 foreach($scale in @(1,1.25,1.5,2)) {
  $view=[Activator]::CreateInstance($assembly.GetType("MochiDock.Views.Cards.HardwareCardView"))
  $view.DataContext=[pscustomobject]@{HardwareStats=$stats;ActiveSystemModule="Network";IsCpuModuleActive=$false;IsGpuModuleActive=$false;IsMemoryModuleActive=$false;IsDrivesModuleActive=$false;IsNetworkModuleActive=$true;IsGamesModuleActive=$false;IsBatteryModuleActive=$false}
  $fixtureWindow=[Windows.Window]::new()
  $fixtureWindow.Content=$view
  $assembly.GetType("MochiDock.Services.ThemeService").GetMethod("ApplyTheme").Invoke($null,@($fixtureWindow,$theme))
  $view.Width=592;$view.Height=280
  $view.SetResourceReference([Windows.Controls.Control]::FontFamilyProperty,"IslandUiFont")
  [Windows.Media.TextOptions]::SetTextRenderingMode($view,[Windows.Media.TextRenderingMode]::Grayscale)
  [Windows.Media.TextOptions]::SetTextFormattingMode($view,[Windows.Media.TextFormattingMode]::Ideal)
  $view.Measure([Windows.Size]::new(592,280));$view.Arrange([Windows.Rect]::new(0,0,592,280));$view.UpdateLayout()
  $surface=[Windows.Controls.Border]::new();$fixtureWindow.Content=$null;$surface.Child=$view;$surface.Background=$application.Resources["IslandGroundBrush"]; $surface.Width=592;$surface.Height=280;[Windows.Media.VisualTreeHelper]::SetRootDpi($surface,[Windows.DpiScale]::new($scale,$scale));$surface.Measure([Windows.Size]::new(592,280));$surface.Arrange([Windows.Rect]::new(0,0,592,280));$surface.UpdateLayout()
  $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new([int](592*$scale),[int](280*$scale),96*$scale,96*$scale,[Windows.Media.PixelFormats]::Pbgra32)
  $bitmap.Render($surface)
  $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
  $path=Join-Path $output "$theme-$scale.png";$stream=[IO.File]::Create($path);try{$encoder.Save($stream)}finally{$stream.Dispose()}
  Write-Output "Rendered fixture: $path"
 }
}

foreach($name in @("Prayer","Media")) {
 foreach($theme in @("Dark","Matcha")) {
  foreach($scale in @(1,1.25,1.5,2)) {
   $view=[Activator]::CreateInstance($assembly.GetType("MochiDock.Views.Cards."+ $name +"CardView"))
   if($name -eq "Prayer"){
    $schedule=[Activator]::CreateInstance($assembly.GetType("MochiDock.Models.PrayerSchedule"))
    $schedule.Date=[DateTime]::Today;$schedule.TimeZoneId="Asia/Tokyo"
    foreach($pair in @(@("Fajr","04:48"),@("Sunrise","06:14"),@("Dhuhr","11:33"),@("Asr","14:31"),@("Maghrib","16:51"),@("Isha","18:12"))){$schedule.($pair[0]).Time=[TimeSpan]::Parse($pair[1])}
    $schedule.UpdateCountdown()
    $view.DataContext=[pscustomobject]@{CurrentPrayerSchedule=$schedule;PrayerError="";EnablePrayerPreNotification=$true}
   }else{
    $media=[Activator]::CreateInstance($assembly.GetType("MochiDock.Models.MediaInfo"));$media.Title="IRIS OUT";$media.Artist="Kenshi Yonezu";$media.Duration=[TimeSpan]::FromSeconds(151);$media.Position=[TimeSpan]::FromSeconds(74);$media.CanSeek=$true
    $view.DataContext=[pscustomobject]@{CurrentMedia=$media;HasLyrics=$false;LyricsStatusText="Loading lyrics";PlainLyrics=""}
   }
   $view.SetResourceReference([Windows.Controls.Control]::FontFamilyProperty,"IslandUiFont");$view.Width=592;$view.Height=220
   $hostWindow=[Windows.Window]::new();$hostWindow.Content=$view
   $assembly.GetType("MochiDock.Services.ThemeService").GetMethod("ApplyTheme").Invoke($null,@($hostWindow,$theme))
   $hostWindow.Content=$null
   $surface=[Windows.Controls.Border]::new();$surface.Child=$view;$surface.Background=$application.Resources["IslandGroundBrush"];$surface.Width=592;$surface.Height=220
   [Windows.Media.TextOptions]::SetTextRenderingMode($surface,[Windows.Media.TextRenderingMode]::Grayscale)
   [Windows.Media.TextOptions]::SetTextFormattingMode($surface,[Windows.Media.TextFormattingMode]::Ideal)
   [Windows.Media.VisualTreeHelper]::SetRootDpi($surface,[Windows.DpiScale]::new($scale,$scale));$surface.Measure([Windows.Size]::new(592,220));$surface.Arrange([Windows.Rect]::new(0,0,592,220));$surface.UpdateLayout()
   if($name -eq "Prayer"){$view.GetType().GetMethod("RenderSchedule").Invoke($view,@($schedule));$view.GetType().GetMethod("MeasureCountdown",[Reflection.BindingFlags]"NonPublic,Instance").Invoke($view,@());$surface.UpdateLayout()}
   else{$slider=$view.FindName("TrackSlider");$slider.Value=74;$surface.UpdateLayout()}
   if($name -eq "Prayer"){$timeLabel=$view.FindName("NextPrayerScheduledTime");$anchor=$timeLabel.TransformToAncestor($view).Transform([Windows.Point]::new(0,0)).X;foreach($digits in @("00:11:11","08:58:58","09:59:59","10:00:00")){$schedule.CountdownFullText=$digits;$surface.UpdateLayout();$after=$timeLabel.TransformToAncestor($view).Transform([Windows.Point]::new(0,0)).X;if([Math]::Abs($after-$anchor) -gt 0.01){throw "Prayer time moved with countdown"}};Write-Output "Prayer time anchor stable: $anchor"}
   $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new([int](592*$scale),[int](220*$scale),96*$scale,96*$scale,[Windows.Media.PixelFormats]::Pbgra32);$bitmap.Render($surface)
   $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
   $path=Join-Path $output "$name-$theme-$scale.png";$stream=[IO.File]::Create($path);try{$encoder.Save($stream)}finally{$stream.Dispose()}
   Write-Output "Rendered fixture: $path"
  }
 }
}

}


if (!$FxOnly) {
 # Verify a loaded card changes face and remeasures without being recreated.
 $prayer=[Activator]::CreateInstance($assembly.GetType("MochiDock.Views.Cards.PrayerCardView"))
 $prayer.SetResourceReference([Windows.Controls.Control]::FontFamilyProperty,"IslandUiFont")
 $prayer.DataContext=[pscustomobject]@{CurrentPrayerSchedule=$schedule;PrayerError="";EnablePrayerPreNotification=$true}
$marquee=[Activator]::CreateInstance($assembly.GetType("MochiDock.Controls.MarqueeTextBlock"));$marquee.Width=180;$marquee.FontSize=14;$marquee.Text="A long media title that must be measured again when its font changes";$marquee.SetResourceReference([Windows.Controls.Control]::FontFamilyProperty,"IslandUiFont")
 $children=[Windows.Controls.StackPanel]::new();$prayer.Height=220;$children.Children.Add($prayer)|Out-Null;$children.Children.Add($marquee)|Out-Null
 $retainedLabel=[Windows.Controls.TextBlock]::new();$retainedLabel.Text='Network Statistics';$retainedLabel.Style=$application.Resources['HeaderLabel'];$retainedLabel.HorizontalAlignment=[Windows.HorizontalAlignment]::Left
 $assembly.GetType('MochiDock.Controls.TabLabelMotion').GetMethod('SetActive').Invoke($null,@($retainedLabel,$true));$children.Children.Add($retainedLabel)|Out-Null
 $hostWindow=[Windows.Window]::new();$hostWindow.Content=$children;$hostWindow.Width=592;$hostWindow.Height=260;$hostWindow.Left=-32000;$hostWindow.Top=-32000;$hostWindow.WindowStyle=[Windows.WindowStyle]::None;$hostWindow.ShowInTaskbar=$false;$hostWindow.ShowActivated=$false
 $hostWindow.Show()
 $firstWidth=$null
 try{foreach($theme in @("Dark","Matcha","Dark")){
  $assembly.GetType("MochiDock.Services.ThemeService").GetMethod("ApplyTheme").Invoke($null,@($hostWindow,$theme))
  $hostWindow.UpdateLayout()
  [Windows.Threading.Dispatcher]::CurrentDispatcher.Invoke([Windows.Threading.DispatcherPriority]::Loaded,[Action]{})
  $hostWindow.UpdateLayout()
  $countdown=$prayer.FindName("CountdownTextBlock");$column=$prayer.FindName("CountdownColumn")
  $expectedWeight=[Windows.FontWeights]::SemiBold
  if($retainedLabel.FontWeight -ne $expectedWeight){throw 'Retained header weight did not follow theme'}
  $headerProbe=[Windows.Media.FormattedText]::new($retainedLabel.Text,[Globalization.CultureInfo]::CurrentUICulture,$retainedLabel.FlowDirection,[Windows.Media.Typeface]::new($retainedLabel.FontFamily,$retainedLabel.FontStyle,$retainedLabel.FontWeight,$retainedLabel.FontStretch),$retainedLabel.FontSize,[Windows.Media.Brushes]::Black,1)
  if([Math]::Abs($retainedLabel.Width-[Math]::Ceiling($headerProbe.WidthIncludingTrailingWhitespace)) -gt 0.01){throw 'Retained header width did not follow theme font/weight'}
  Write-Output "Switch probe $theme : root=$($prayer.FontFamily.Source); countdown=$($countdown.FontFamily.Source); resource=$($application.Resources["IslandUiFont"].Source); loaded=$($prayer.IsLoaded)"
  if(!$countdown.FontFamily.Source.Contains("Quicksand")){throw "Live theme font did not change"}
  $needed=0
  foreach($digit in '0123456789'.ToCharArray()){
   $probe=[Windows.Controls.TextBlock]::new();$probe.FontFamily=$countdown.FontFamily;$probe.FontSize=30;$probe.FontWeight=[Windows.FontWeights]::Medium;$probe.Typography.NumeralAlignment=[Windows.FontNumeralAlignment]::Tabular;$probe.Text="$digit$digit"+":"+"$digit$digit"+":"+"$digit$digit";$probe.Measure([Windows.Size]::new([double]::PositiveInfinity,[double]::PositiveInfinity));$needed=[Math]::Max($needed,$probe.DesiredSize.Width)
  }
  if($column.Width.Value -lt [Math]::Ceiling($needed)+4){throw "Countdown column was not remeasured for $theme"}
  if($theme -eq "Dark"){if($null -eq $firstWidth){$firstWidth=$column.Width.Value}elseif([Math]::Abs($firstWidth-$column.Width.Value) -gt 0.01){throw "Font round trip changed countdown width"}}
  $primary=$marquee.FindName("PrimaryText");$separator=$marquee.FindName("SeparatorBullet")
  $expected=$primary.DesiredSize.Width+$separator.DesiredSize.Width
  $distance=$marquee.GetType().GetField("_totalLoopDistance",[Reflection.BindingFlags]"NonPublic,Instance").GetValue($marquee)
  if([Math]::Abs($distance-$expected) -gt 0.01 -or $distance -le 180){throw "Marquee width did not follow theme font"}
  Write-Output "Marquee loop distance for $theme : $distance"
  Write-Output "Live font switch $theme : $($countdown.FontFamily.Source); countdown width=$($column.Width.Value)"
 }}finally{$hostWindow.Close()}
}

if ($ThemeSwitchOnly) { $application.Shutdown(); exit 0 }

$marketType=$assembly.GetType("MochiDock.Services.MarketQuote")
$barType=$assembly.GetType("MochiDock.Services.MarketBar")
$dictionaryType=[Collections.Generic.Dictionary``2].MakeGenericType([type[]]@([string],$marketType))
$now=[DateTimeOffset]::UtcNow
function New-MarketBars([decimal]$base){
 $bars=[Array]::CreateInstance($barType,12)
 for($n=0;$n -lt 12;$n++){
  $at=if($n -lt 6){$now.AddHours(-12+$n)}else{$now.AddHours(-4+($n-6))}
  $value=$base+([decimal](($n%4)-1.5)/10)
  $bars.SetValue([Activator]::CreateInstance($barType,@($at,$value)),$n)
 }
 return ,$bars
}
function New-Market([string]$name,[decimal]$value,$change,[string]$provider,[DateTimeOffset]$source,$bars){
 $previous=if($null -eq $change){$null}else{[decimal]($value/(1+([decimal]$change/100)))}
 return [Activator]::CreateInstance($marketType,@($name,$provider,$value,$source,$now,$bars,"1h",$previous,$change))
}
$cases=@(
 [pscustomobject]@{Name="Ready-UsdJpy";Pair="USD/JPY";Status="Ready"},
 [pscustomobject]@{Name="Ready-UsdIdr";Pair="USD/IDR";Status="Ready"},
 [pscustomobject]@{Name="Ready-JpyIdr";Pair="JPY/IDR";Status="Ready"},
 [pscustomobject]@{Name="Unavailable";Pair="JPY/IDR";Status="Some sources unavailable — cached data retained."},
 [pscustomobject]@{Name="Fallback";Pair="JPY/IDR";Status="Ready"},
 [pscustomobject]@{Name="Stale";Pair="JPY/IDR";Status="Ready"},
 [pscustomobject]@{Name="Cooldown";Pair="JPY/IDR";Status="Checked recently — refresh available every minute."},
 [pscustomobject]@{Name="Empty";Pair="JPY/IDR";Status="FX unavailable — cached data retained."}
)
foreach($case in $cases){
 foreach($theme in @("Dark","Matcha")){foreach($scale in @(1,1.25,1.5,2)){
   $markets=[Activator]::CreateInstance($dictionaryType)
   if($case.Name -ne "Empty"){
    $values=@{"USD/JPY"=[decimal]158.34;"USD/IDR"=[decimal]18103;"JPY/IDR"=[decimal]114.36}
    $changes=@{"USD/JPY"=[decimal]0.4;"USD/IDR"=[decimal]0.3;"JPY/IDR"=[decimal]-0.1}
    foreach($name in @("USD/JPY","USD/IDR","JPY/IDR")){
     $change=if($case.Name -eq "Unavailable"){$null}else{$changes[$name]}
     $provider=if($case.Name -eq "Fallback" -and $name -eq $case.Pair){"ExchangeRate-API"}else{"Yahoo Finance"}
     $quoteSource=if($case.Name -eq "Stale" -and $name -eq $case.Pair){$now.AddHours(-3)}else{$now.AddMinutes(-5)}
     $bars=if($provider -eq "Yahoo Finance"){New-MarketBars $values[$name]}else{[Array]::CreateInstance($barType,0)}
     $latest=New-Market $name $values[$name] $change $provider $quoteSource $bars
     $markets[$name]=$latest
     $history=if($provider -eq "Yahoo Finance"){$latest}else{New-Market $name $values[$name] $changes[$name] "Yahoo Finance" $now.AddMinutes(-5) (New-MarketBars $values[$name])}
     $markets[$name+":1h"]=$history
    }
   }
  $view=[Activator]::CreateInstance($assembly.GetType("MochiDock.Views.Cards.FxRatesCardView"));$view.Width=592;$view.Height=300
  $hostWindow=[Windows.Window]::new();$hostWindow.Content=$view;$assembly.GetType("MochiDock.Services.ThemeService").GetMethod("ApplyTheme").Invoke($null,@($hostWindow,$theme));$hostWindow.Content=$null
  $surface=[Windows.Controls.Border]::new();$surface.Child=$view;$surface.Width=592;$surface.Height=300;$surface.Background=$application.Resources["IslandGroundBrush"]
  [Windows.Media.TextOptions]::SetTextRenderingMode($surface,[Windows.Media.TextRenderingMode]::Grayscale)
  [Windows.Media.VisualTreeHelper]::SetRootDpi($surface,[Windows.DpiScale]::new($scale,$scale));$surface.Measure([Windows.Size]::new(592,300));$surface.Arrange([Windows.Rect]::new(0,0,592,300));$surface.UpdateLayout()
   $view.GetType().GetMethod("RenderMarkets").Invoke($view,@($markets,$case.Pair,$case.Status,[Nullable[DateTimeOffset]]$now));$surface.UpdateLayout()
   if($case.Name -ne "Empty" -and $view.FindName("Chart").Children.Count -ne 3){throw "FX must contain one continuous fill, line and endpoint even with irregular timestamps"}
   foreach($controlName in @("UsdJpy","UsdIdr","JpyIdr","Chart","SourceText","RefreshButton")){
    $control=$view.FindName($controlName);$point=$control.TranslatePoint([Windows.Point]::new(0,0),$view)
    $tolerance=1/$scale
    if($control.ActualWidth -le 0 -or $control.ActualHeight -le 0 -or $point.X -lt -$tolerance -or $point.Y -lt -$tolerance -or $point.X+$control.ActualWidth -gt 592+$tolerance -or $point.Y+$control.ActualHeight -gt 300+$tolerance){throw "FX bounds failed: $($case.Name) $controlName $theme $scale"}
   }
  $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new([int](592*$scale),[int](300*$scale),96*$scale,96*$scale,[Windows.Media.PixelFormats]::Pbgra32);$bitmap.Render($surface)
   $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap));$path=Join-Path $output "FX-$($case.Name)-$theme-$scale.png";$stream=[IO.File]::Create($path);try{$encoder.Save($stream)}finally{$stream.Dispose()}
  }}
}
Write-Output "Rendered FX dashboard selections, irregular samples, fallback, stale, cooldown and unavailable states at four scales."
$weatherMarkup=(Get-Content (Join-Path $repo "MochiDock/Views/WeatherIconResources.xaml") -Raw) -replace 'clr-namespace:MochiDock.Models','clr-namespace:MochiDock.Models;assembly=MochiDock'
$weatherResources=[Windows.Markup.XamlReader]::Parse($weatherMarkup,$context);$application.Resources.MergedDictionaries.Add($weatherResources)
if($null -eq $weatherResources["WeatherVectorIconTemplate"]){throw "Weather fixture template missing"}
foreach($theme in @("Dark","Matcha")){foreach($scale in @(1,1.25,1.5,2)){
 $themeWindow=[Windows.Window]::new();$assembly.GetType("MochiDock.Services.ThemeService").GetMethod("ApplyTheme").Invoke($null,@($themeWindow,$theme))
 $lastWidth=$null
 foreach($weatherCase in @("Off","Clear","Cloudy","Negative")){
 $lastWidth=$null
 foreach($fpsValue in @(0,9,144,999,1000)){
  $readout=[Activator]::CreateInstance($assembly.GetType("MochiDock.Controls.GamingReadout"))
  $readout.SetResourceReference([Windows.Documents.TextElement]::FontFamilyProperty,"IslandUiFont")
  $gamingStats=[Activator]::CreateInstance($assembly.GetType("MochiDock.Services.HardwareStats"));$gamingStats.CurrentFps=$fpsValue;$gamingStats.GpuUsage=100;$gamingStats.CpuUsage=100;$gamingStats.GpuTemp=71
  $readout.Update($gamingStats,$null);$readout.Measure([Windows.Size]::new(600,32))
  if($fpsValue -lt 1000){if($null -ne $lastWidth -and [Math]::Abs($readout.DesiredSize.Width-$lastWidth) -gt 0.1){throw "Gaming width changed inside three-digit capacity"};$lastWidth=$readout.DesiredSize.Width}
  $weatherInfo=$null
  if($weatherCase -ne "Off"){$weatherInfo=[Activator]::CreateInstance($assembly.GetType("MochiDock.Models.WeatherInfo"));$weatherInfo.IconKind=if($weatherCase -eq "Clear"){"Clear"}else{"Cloudy"};$weatherInfo.Temperature=if($weatherCase -eq "Negative"){-12}else{18}}
  if($weatherCase -eq "Negative"){$gamingStats.GpuTemp=0}
  $readout.Update($gamingStats,$weatherInfo)
  $complete=[Windows.Controls.StackPanel]::new();$complete.Orientation=[Windows.Controls.Orientation]::Horizontal;$complete.VerticalAlignment=[Windows.VerticalAlignment]::Center
  $mascot=[Activator]::CreateInstance($assembly.GetType("MochiDock.Controls.MascotControl"));$mascot.Width=22;$mascot.Height=19;$mascot.Margin=[Windows.Thickness]::new(0,0,9,0);$mascot.VerticalAlignment=[Windows.VerticalAlignment]::Center;$complete.Children.Add($mascot)|Out-Null;$complete.Children.Add($readout)|Out-Null
  $mascot.ApplyTheme($theme -eq "Matcha")
  $complete.Measure([Windows.Size]::new(600,32))
  $surface=[Windows.Controls.Border]::new();$surface.Child=$complete;$surface.Padding=[Windows.Thickness]::new(12,0,12,0);$surface.CornerRadius=[Windows.CornerRadius]::new(16);$surface.Height=32;$surface.Width=$complete.DesiredSize.Width+24;$surface.Background=$application.Resources["IslandGroundBrush"]
  [Windows.Media.VisualTreeHelper]::SetRootDpi($surface,[Windows.DpiScale]::new($scale,$scale));$surface.Measure([Windows.Size]::new(600,32));$surface.Arrange([Windows.Rect]::new(0,0,$surface.Width,32));$surface.UpdateLayout()
  $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new([int][Math]::Ceiling($surface.Width*$scale),[int](32*$scale),96*$scale,96*$scale,[Windows.Media.PixelFormats]::Pbgra32);$bitmap.Render($surface)
  if($complete.DesiredSize.Height -gt 32){throw "Gaming weather overflow"}
  $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap));$stream=[IO.File]::Create((Join-Path $output "Gaming-$fpsValue-$weatherCase-$theme-$scale.png"));try{$encoder.Save($stream)}finally{$stream.Dispose()}
 }
 }
}}
# Retain the same weather artwork and mascot while switching both palettes.
foreach($scale in @(1,1.25,1.5,2)){
 $row=[Windows.Controls.StackPanel]::new();$row.Orientation=[Windows.Controls.Orientation]::Horizontal;$row.Margin=[Windows.Thickness]::new(12)
 $mascot=[Activator]::CreateInstance($assembly.GetType("MochiDock.Controls.MascotControl"));$mascot.Width=30;$mascot.Height=28;$mascot.Margin=[Windows.Thickness]::new(0,0,12,0);$row.Children.Add($mascot)|Out-Null
 foreach($kind in @("Clear","PartlyCloudy","Cloudy","Fog","Drizzle","Rain","Snow","Thunder")){
  $info=[Activator]::CreateInstance($assembly.GetType("MochiDock.Models.WeatherInfo"));$info.IconKind=$kind
  $icon=[Windows.Controls.ContentControl]::new();$icon.Content=$info;$icon.ContentTemplate=$weatherResources["WeatherVectorIconTemplate"];$icon.Width=24;$icon.Height=24;$icon.Margin=[Windows.Thickness]::new(5,0,5,0);$row.Children.Add($icon)|Out-Null
 }
 $surface=[Windows.Controls.Border]::new();$surface.Child=$row;$surface.Width=350;$surface.Height=54;$surface.SetResourceReference([Windows.Controls.Border]::BackgroundProperty,"IslandGroundBrush")
 [Windows.Media.VisualTreeHelper]::SetRootDpi($surface,[Windows.DpiScale]::new($scale,$scale))
 foreach($theme in @("Dark","Matcha","Dark")){
  $hostWindow=[Windows.Window]::new();$hostWindow.Content=$surface;$surface.Measure([Windows.Size]::new(350,54));$surface.Arrange([Windows.Rect]::new(0,0,350,54));$surface.UpdateLayout()
  $assembly.GetType("MochiDock.Services.ThemeService").GetMethod("ApplyTheme").Invoke($null,@($hostWindow,$theme));$surface.UpdateLayout();$hostWindow.Content=$null
  $mascot.ApplyTheme($theme -eq "Matcha")
  if($theme -eq "Matcha"){
   foreach($foreground in @("IslandInkBrush","IslandSecondaryInkBrush","IslandMutedInkBrush","IslandAccentBrush","IslandStatusBrush")){
    foreach($background in @("IslandGroundBrush","IslandSubCardBackgroundBrush","IslandChipActiveBrush","IslandActionButtonHoverBrush")){Assert-Contrast $application.Resources[$foreground] $application.Resources[$background] "$foreground on $background"}
   }
   Assert-Contrast $application.Resources["IslandAccentButtonInkBrush"] $application.Resources["IslandAccentFillBrush"] "filled action"
   foreach($semantic in @("IslandPositiveBrush","IslandNegativeBrush")){Assert-Contrast $application.Resources[$semantic] $application.Resources["IslandChipBackgroundBrush"] $semantic}
  }
  $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new([int](350*$scale),[int](54*$scale),96*$scale,96*$scale,[Windows.Media.PixelFormats]::Pbgra32);$bitmap.Render($surface)
  $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap));$stream=[IO.File]::Create((Join-Path $output "Palette-weather-$theme-$scale.png"));try{$encoder.Save($stream)}finally{$stream.Dispose()}
 }
}
if ($FxOnly) { exit 0 }

# Header uses the actual XAML and compiled navigation vectors, without a MainWindow instance.
$iconSource=[xml](Get-Content (Join-Path $repo "MochiDock/Views/NavigationIconResources.xaml") -Raw)
$application.Resources["BoolToActiveTag"]=[Activator]::CreateInstance($assembly.GetType("MochiDock.Views.BoolToActiveTagConverter"))
foreach($styleName in @("TabLabelBaseStyle","ActivityTabButton")){
 $node=$window.SelectSingleNode('//*[local-name()="Style" and @*[local-name()="Key"]="'+$styleName+'"]')
 $application.Resources[$styleName]=[Windows.Markup.XamlReader]::Parse($node.OuterXml,$context)
}
$headerNode=$window.SelectSingleNode('//*[local-name()="StackPanel" and @*[local-name()="Name"]="HeaderTabsPanel"]')
$headerMarkup=($headerNode.ParentNode.OuterXml -replace 'clr-namespace:MochiDock.Controls"','clr-namespace:MochiDock.Controls;assembly=MochiDock"') -replace '\s+(Click|PreviewMouseWheel|SizeChanged)="[^"]*"',''
foreach($active in @("Media","WhatsApp","Email","Discord","Hardware","Weather","Pomodoro","Prayer","FileShelf","Clipboard","Today","FxRates")){foreach($notifications in @($false,$true)){foreach($theme in @("Dark","Matcha")){foreach($scale in @(1,1.25,1.5,2)){
 $native=$null
 if(!$notifications -and $active -in @("WhatsApp","Email","Discord")){continue}
 $hostWindow=[Windows.Window]::new();$assembly.GetType("MochiDock.Services.ThemeService").GetMethod("ApplyTheme").Invoke($null,@($hostWindow,$theme))
 foreach($icon in $iconSource.DocumentElement.ChildNodes){if($icon.LocalName -ne "Viewbox"){continue};$copy=$icon.CloneNode($true);$key=$copy.GetAttribute("Key","http://schemas.microsoft.com/winfx/2006/xaml");$copy.RemoveAttribute("Key","http://schemas.microsoft.com/winfx/2006/xaml");$copy.RemoveAttribute("Shared","http://schemas.microsoft.com/winfx/2006/xaml");$application.Resources[$key]=[Windows.Markup.XamlReader]::Parse($copy.OuterXml,$context)}
 try{$layer=[Windows.Markup.XamlReader]::Parse($headerMarkup,$context);$panel=$layer.FindName("HeaderTabsPanel");$overlay=$layer.FindName("HeaderPillOverlay");$pill=$layer.FindName("HeaderSelectionPill")}catch{$problem=$_.Exception;while($problem.InnerException){$problem=$problem.InnerException};throw $problem.Message}
 $data=@{IsExpanded=$true}
 foreach($name in @("Media","WhatsApp","Email","Discord","Hardware","Weather","Pomodoro","Prayer","FileShelf","Clipboard","Today","FxRates")){$data["Is"+$name+"Active"]=$name -eq $active}
 foreach($name in @("ShowStatsTab","ShowWeatherTab","ShowPomodoroTab","EnablePrayerTimes","ShowFileShelfTab","ShowClipboardTab","ShowTodayTab","ShowFxRatesTab")){$data[$name]=$true}
 foreach($name in @("HasActiveWhatsApp","HasActiveEmail","HasActiveDiscord")){$data[$name]=$notifications}
 $layer.DataContext=[pscustomobject]$data
 $viewport=[Windows.Controls.ScrollViewer]::new();$viewport.Content=$layer;$viewport.HorizontalScrollBarVisibility=[Windows.Controls.ScrollBarVisibility]::Hidden;$viewport.VerticalScrollBarVisibility=[Windows.Controls.ScrollBarVisibility]::Disabled
 $surface=[Windows.Controls.Border]::new();$surface.Child=$viewport;$surface.Background=$application.Resources["IslandGroundBrush"];$surface.Width=340;$surface.Height=40
 [Windows.Media.VisualTreeHelper]::SetRootDpi($surface,[Windows.DpiScale]::new($scale,$scale))
 [Windows.Media.TextOptions]::SetTextRenderingMode($surface,[Windows.Media.TextRenderingMode]::Grayscale)
 [Windows.Media.TextOptions]::SetTextFormattingMode($surface,[Windows.Media.TextFormattingMode]::Ideal)
 $surface.Measure([Windows.Size]::new(340,40));$surface.Arrange([Windows.Rect]::new(0,0,340,40));$surface.UpdateLayout()
 $selected=$panel.Children|Where-Object{$_.Tag -eq "Active"}|Select-Object -First 1
 $selected.BringIntoView();$surface.UpdateLayout();[Windows.Threading.Dispatcher]::CurrentDispatcher.Invoke([Windows.Threading.DispatcherPriority]::Loaded,[Action]{})
 $label=$selected.Content.Children|Where-Object{$_ -is [Windows.Controls.TextBlock]}|Select-Object -First 1
 if(!$label.FontFamily.Source.Contains("Quicksand")){throw "Incorrect selected header font for $theme"}
 $position=$selected.TranslatePoint([Windows.Point]::new(0,0),$viewport)
 if($position.X -lt -1 -or $position.X+$selected.ActualWidth -gt 341){throw "Selected header tab clipped: $active / $notifications"}
 if($active -eq "Hardware" -and $notifications){
  $parameters=[Windows.Interop.HwndSourceParameters]::new("Isolated combined header");$parameters.WindowStyle=-2147483648;$parameters.PositionX=-32000;$parameters.PositionY=-32000;$parameters.Width=340;$parameters.Height=40
  $native=[Windows.Interop.HwndSource]::new($parameters);$native.RootVisual=$surface
  [Windows.Media.VisualTreeHelper]::SetRootDpi($surface,[Windows.DpiScale]::new($scale,$scale))
  try {
   [Windows.Threading.Dispatcher]::CurrentDispatcher.Invoke([Windows.Threading.DispatcherPriority]::Loaded,[Action]{})
   foreach($target in @("Media","Hardware","Prayer","FxRates","Hardware")){
    foreach($name in @("Media","WhatsApp","Email","Discord","Hardware","Weather","Pomodoro","Prayer","FileShelf","Clipboard","Today","FxRates")){$data["Is"+$name+"Active"]=$name -eq $target}
    $layer.DataContext=[pscustomobject]$data;$surface.UpdateLayout();$overlay.Refresh($true)
    $frame=[Windows.Threading.DispatcherFrame]::new();$timer=[Windows.Threading.DispatcherTimer]::new();$timer.Interval=[TimeSpan]::FromMilliseconds(40);$timer.Tag=$frame;$timer.Add_Tick({param($sender,$eventArgs)$sender.Stop();$sender.Tag.Continue=$false});$timer.Start();[Windows.Threading.Dispatcher]::PushFrame($frame)
    if([double]::IsNaN($pill.Width)){throw "Invalid width during rapid header selection"}
   }
   $frame=[Windows.Threading.DispatcherFrame]::new();$timer=[Windows.Threading.DispatcherTimer]::new();$timer.Interval=[TimeSpan]::FromMilliseconds(240);$timer.Tag=$frame;$timer.Add_Tick({param($sender,$eventArgs)$sender.Stop();$sender.Tag.Continue=$false});$timer.Start();[Windows.Threading.Dispatcher]::PushFrame($frame)
   $surface.UpdateLayout();$selected=$panel.Children|Where-Object{$_.Tag -eq "Active"}|Select-Object -First 1
   $point=$selected.TranslatePoint([Windows.Point]::new(0,0),$overlay)
   if([Math]::Abs([Windows.Controls.Canvas]::GetLeft($pill)-$point.X)*$scale -gt 1 -or [Math]::Abs($pill.ActualWidth-$selected.ActualWidth)*$scale -gt 1){throw "Animated pill failed to settle on Stats"}
   $overlay.IsSelectionEnabled=$false;$surface.UpdateLayout();if($pill.Opacity -ne 0){throw "Collapse did not hide the pill"};$overlay.IsSelectionEnabled=$true
   $viewport.ScrollToHorizontalOffset(70);$surface.UpdateLayout();$overlay.Refresh($false)
  } catch {$native.Dispose();throw}
 }
 $surface.Measure([Windows.Size]::new(340,40));$surface.Arrange([Windows.Rect]::new(0,0,340,40));$surface.UpdateLayout();$overlay.Refresh($false);$surface.UpdateLayout()
 $buttonPoint=$selected.TranslatePoint([Windows.Point]::new(0,0),$overlay)
 if($pill.ActualWidth -lt 28 -or $pill.ActualHeight -lt 27){throw "Degenerate header fixture bounds"}
 if($pill.Opacity -ne 1){throw "Selection pill is not visible"}
 if([Math]::Abs([Windows.Controls.Canvas]::GetLeft($pill)-$buttonPoint.X)*$scale -gt 1 -or [Math]::Abs($pill.ActualWidth-$selected.ActualWidth)*$scale -gt 1 -or [Math]::Abs([Windows.Controls.Canvas]::GetTop($pill)-$buttonPoint.Y)*$scale -gt 1 -or [Math]::Abs($pill.ActualHeight-$selected.ActualHeight)*$scale -gt 1){throw "Header pill bounds mismatch: $active / $theme / $scale"}
 $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new([int](340*$scale),[int](40*$scale),96*$scale,96*$scale,[Windows.Media.PixelFormats]::Pbgra32);$bitmap.Render($surface)
 $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap));$path=Join-Path $output "Header-$active-$notifications-$theme-$scale.png";$stream=[IO.File]::Create($path);try{$encoder.Save($stream)}finally{$stream.Dispose();if($native){$native.Dispose()}}
}}}}
Write-Output "Rendered header orders/selection/notifications at native visual DPI."
$application.Shutdown()
