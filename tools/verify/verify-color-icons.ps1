param([string]$Configuration='Release')
# Loads the flat colour icon dictionary, checks every icon is present and draws something inside its 16 x 16 box, and writes a strip of
# all icons (at 14 and 22 DIPs, 100% and 200%, on the black and the cream capsule) to MochiDock/obj/icons for a human look.
# Never starts MochiDock.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework
$repo=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$app=[Windows.Application]::new();$app.ShutdownMode=[Windows.ShutdownMode]::OnExplicitShutdown
$dictionary=[Windows.Markup.XamlReader]::Parse((Get-Content (Join-Path $repo 'MochiDock/Views/ColorIconResources.xaml') -Raw))
$names=@('IconBook','IconPin','IconClipboard','IconDownload','IconGroup','IconBell','IconBellOff','IconLocation','IconNote','IconFolder')
foreach($name in $names){
 if(!$dictionary.Contains($name)){throw "Icon $name is missing"}
 $image=$dictionary[$name]
 $bounds=$image.Drawing.Bounds
 if($bounds.Width -lt 8 -or $bounds.Height -lt 8){throw "Icon $name draws almost nothing: $bounds"}
 if($bounds.X -lt -0.5 -or $bounds.Y -lt -0.5 -or $bounds.Right -gt 16.5 -or $bounds.Bottom -gt 16.5){throw "Icon $name draws outside its 16 x 16 box: $bounds"}
}
# Any extra icon in the file must be listed above, so a new one cannot go unchecked.
foreach($key in $dictionary.Keys){if($names -notcontains $key){throw "Icon $key is not covered by this check; add it to the list"}}
$output=Join-Path $repo 'MochiDock/obj/icons';$null=New-Item -ItemType Directory -Force $output
foreach($style in @(@('black',[Windows.Media.Brushes]::Black),@('cream',[Windows.Media.SolidColorBrush]::new([Windows.Media.Color]::FromRgb(0xF3,0xEF,0xE0))))){
 foreach($dpi in 1,2){
  $panel=[Windows.Controls.StackPanel]::new();$panel.Orientation=[Windows.Controls.Orientation]::Vertical;$panel.Background=$style[1]
  foreach($size in 14,22){
   $row=[Windows.Controls.StackPanel]::new();$row.Orientation=[Windows.Controls.Orientation]::Horizontal;$row.Margin=[Windows.Thickness]::new(8,6,8,6)
   foreach($name in $names){$img=[Windows.Controls.Image]::new();$img.Source=$dictionary[$name];$img.Width=$size;$img.Height=$size;$img.Stretch=[Windows.Media.Stretch]::Uniform;$img.Margin=[Windows.Thickness]::new(0,0,12,0);$row.Children.Add($img)|Out-Null}
   $panel.Children.Add($row)|Out-Null
  }
  $panel.Measure([Windows.Size]::new(2000,2000));$panel.Arrange([Windows.Rect]::new(0,0,$panel.DesiredSize.Width,$panel.DesiredSize.Height));$panel.UpdateLayout()
  $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new([int]($panel.ActualWidth*$dpi),[int]($panel.ActualHeight*$dpi),96*$dpi,96*$dpi,[Windows.Media.PixelFormats]::Pbgra32);$bitmap.Render($panel)
  $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
  $stream=[IO.File]::Create((Join-Path $output ("icons-"+$style[0]+"-"+$dpi+"x.png")));$encoder.Save($stream);$stream.Dispose()
 }
}
Write-Output "Verified $($names.Count) colour icons (present, drawn inside their 16 x 16 box). Strips: $output"
