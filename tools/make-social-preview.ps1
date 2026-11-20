param([string]$Configuration='Release')
# Renders docs/images/social-preview.png (1280 x 640), the card GitHub shows when the repository link is shared. Upload it by hand in
# the repository's Settings > General > Social preview (GitHub has no API for it). Uses the real Mochi from the Release build and the
# README screenshots. A plain WPF Application: never MochiDock.App (that would start the real app). Never starts MochiDock.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework
$repo=Split-Path $PSScriptRoot -Parent
$directory=Join-Path $repo "MochiDock/bin/$Configuration/net8.0-windows10.0.19041.0"
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $directory 'MochiDock.dll'))
$core=[Reflection.Assembly]::LoadFrom((Join-Path $directory 'MochiDock.Core.dll'))
[AppDomain]::CurrentDomain.add_AssemblyResolve({param($s,$e) if($e.Name.StartsWith('MochiDock.Core')){return $core} if($e.Name.StartsWith('MochiDock')){return $assembly} return $null})
$app=[Windows.Application]::new();$app.ShutdownMode=[Windows.ShutdownMode]::OnExplicitShutdown
function Brush($hex){[Windows.Media.BrushConverter]::new().ConvertFromString($hex)}
function Shot($file,$width){
 $image=[Windows.Controls.Image]::new();$image.Source=[Windows.Media.Imaging.BitmapImage]::new([Uri](Join-Path $repo $file));$image.Width=$width
 $image.Clip=[Windows.Media.RectangleGeometry]::new([Windows.Rect]::new(0,0,$width,$width*$image.Source.PixelHeight/$image.Source.PixelWidth),14,14)
 $frame=[Windows.Controls.Border]::new();$frame.Child=$image;$frame.Margin=[Windows.Thickness]::new(0,0,0,22)
 $frame.Effect=[Windows.Media.Effects.DropShadowEffect]@{BlurRadius=40;ShadowDepth=8;Direction=270;Opacity=0.55;Color=[Windows.Media.Colors]::Black}
 return $frame
}

$root=[Windows.Controls.Grid]::new();$root.Width=1280;$root.Height=640
$gradient=[Windows.Media.LinearGradientBrush]::new();$gradient.StartPoint=[Windows.Point]::new(0,0);$gradient.EndPoint=[Windows.Point]::new(1,1)
$gradient.GradientStops.Add([Windows.Media.GradientStop]::new([Windows.Media.ColorConverter]::ConvertFromString('#15151C'),0))
$gradient.GradientStops.Add([Windows.Media.GradientStop]::new([Windows.Media.ColorConverter]::ConvertFromString('#24222E'),0.6))
$gradient.GradientStops.Add([Windows.Media.GradientStop]::new([Windows.Media.ColorConverter]::ConvertFromString('#3A3241'),1))
$root.Background=$gradient
foreach($w in 560,720){$c=[Windows.Controls.ColumnDefinition]::new();$c.Width=[Windows.GridLength]::new($w);$root.ColumnDefinitions.Add($c)}

# Left: Mochi, the name and the tagline.
$left=[Windows.Controls.StackPanel]::new();$left.VerticalAlignment='Center';$left.Margin=[Windows.Thickness]::new(80,0,20,0)
$mascot=[Activator]::CreateInstance($assembly.GetType('MochiDock.Controls.MascotControl'));$mascot.Width=190;$mascot.Height=165;$mascot.HorizontalAlignment='Left'
$mascot.Mood=[Enum]::Parse($core.GetType('MochiDock.Core.MascotMood'),'Music');$mascot.Margin=[Windows.Thickness]::new(0,0,0,26)
$title=[Windows.Controls.TextBlock]::new();$title.Text='MochiDock';$title.FontFamily=[Windows.Media.FontFamily]::new('Segoe UI');$title.FontWeight='Bold';$title.FontSize=78;$title.Foreground=Brush '#F5F5F7'
$tagline=[Windows.Controls.TextBlock]::new();$tagline.Text='A dynamic-island companion for Windows';$tagline.FontFamily=[Windows.Media.FontFamily]::new('Segoe UI');$tagline.FontSize=27;$tagline.Foreground=Brush '#D4BC8E';$tagline.TextWrapping='Wrap';$tagline.Margin=[Windows.Thickness]::new(2,4,0,0)
$detail=[Windows.Controls.TextBlock]::new();$detail.Text='Media and lyrics, notifications, stats and gaming, weather, your day, and Mochi.';$detail.FontFamily=[Windows.Media.FontFamily]::new('Segoe UI');$detail.FontSize=19;$detail.Foreground=Brush '#9E9EA8';$detail.TextWrapping='Wrap';$detail.Margin=[Windows.Thickness]::new(2,16,0,0)
foreach($e in $mascot,$title,$tagline,$detail){[void]$left.Children.Add($e)}
[Windows.Controls.Grid]::SetColumn($left,0);[void]$root.Children.Add($left)

# Right: two dock screenshots.
$right=[Windows.Controls.StackPanel]::new();$right.VerticalAlignment='Center';$right.HorizontalAlignment='Center';$right.Margin=[Windows.Thickness]::new(0,0,40,0)
[void]$right.Children.Add((Shot 'docs/images/live/weather-default.png' 520))   # no album art on the shared card
[void]$right.Children.Add((Shot 'docs/images/live/stats-default.png' 520))
[Windows.Controls.Grid]::SetColumn($right,1);[void]$root.Children.Add($right)

$parameters=[Windows.Interop.HwndSourceParameters]::new('SocialPreview');$parameters.WindowStyle=-2147483648;$parameters.PositionX=-32000;$parameters.PositionY=-32000;$parameters.Width=1;$parameters.Height=1
$native=[Windows.Interop.HwndSource]::new($parameters);$native.RootVisual=$root
try{
 $root.Measure([Windows.Size]::new(1280,640));$root.Arrange([Windows.Rect]::new(0,0,1280,640));$root.UpdateLayout()
 $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new(1280,640,96,96,[Windows.Media.PixelFormats]::Pbgra32);$bitmap.Render($root)
 $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
 $path=Join-Path $repo 'docs/images/social-preview.png';$stream=[IO.File]::Create($path);$encoder.Save($stream);$stream.Dispose()
 "Wrote $path (1280 x 640). Upload it in GitHub: Settings > General > Social preview."
} finally { $native.Dispose() }
