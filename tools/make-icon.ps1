param([string]$Configuration='Release',[string]$Mood='Idle',[switch]$Matcha)
# Renders the real MascotControl on a transparent square at every icon size and packs the frames into a multi-size icon.ico
# (PNG-compressed entries), plus a 512 px PNG for the README. Never starts MochiDock. Run after a Release build, never concurrently with one.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework
$repo=Split-Path $PSScriptRoot -Parent
$directory=Join-Path $repo "MochiDock/bin/$Configuration/net8.0-windows10.0.19041.0"
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $directory 'MochiDock.dll'))
$core=[Reflection.Assembly]::LoadFrom((Join-Path $directory 'MochiDock.Core.dll'))
[AppDomain]::CurrentDomain.add_AssemblyResolve({param($s,$e) if($e.Name.StartsWith('MochiDock')){return $assembly} if($e.Name.StartsWith('MochiDock.Core')){return $core} return $null})
$app=[Windows.Application]::new();$app.ShutdownMode=[Windows.ShutdownMode]::OnExplicitShutdown
$moodType=$core.GetType('MochiDock.Core.MascotMood')
$mascotType=$assembly.GetType('MochiDock.Controls.MascotControl')

function Render-Frame([int]$pixels){
 # The 30 x 26 drawing fills 86% of the square; the headset/extras of other moods need none, Idle has none.
 $mascot=[Activator]::CreateInstance($mascotType)
 if($Matcha){$mascot.ApplyTheme($true)}
 $mascot.Mood=[Enum]::Parse($moodType,$Mood)
 $body=$mascot.FindName('BodyBounceTransform')
 $body.BeginAnimation([Windows.Media.TranslateTransform]::XProperty,$null);$body.BeginAnimation([Windows.Media.TranslateTransform]::YProperty,$null);$body.X=0;$body.Y=0
 $width=$pixels*0.86;$height=$width*26/30
 $mascot.Width=$width;$mascot.Height=$height
 $host_=[Windows.Controls.Grid]::new();$host_.Width=$pixels;$host_.Height=$pixels;$host_.Background=[Windows.Media.Brushes]::Transparent
 $mascot.HorizontalAlignment='Center';$mascot.VerticalAlignment='Center'
 $host_.Children.Add($mascot)|Out-Null
 $parameters=[Windows.Interop.HwndSourceParameters]::new('Icon');$parameters.WindowStyle=-2147483648;$parameters.PositionX=-32000;$parameters.PositionY=-32000;$parameters.Width=1;$parameters.Height=1
 $native=[Windows.Interop.HwndSource]::new($parameters);$native.RootVisual=$host_
 try{
  $host_.Measure([Windows.Size]::new($pixels,$pixels));$host_.Arrange([Windows.Rect]::new(0,0,$pixels,$pixels));$host_.UpdateLayout()
  $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new($pixels,$pixels,96,96,[Windows.Media.PixelFormats]::Pbgra32);$bitmap.Render($host_)
  $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
  $stream=[IO.MemoryStream]::new();$encoder.Save($stream);return ,$stream.ToArray()
 } finally { $native.Dispose() }
}

$sizes=16,24,32,48,64,128,256
$frames=foreach($s in $sizes){,(Render-Frame $s)}
$icon=[IO.MemoryStream]::new();$w=[IO.BinaryWriter]::new($icon)
$w.Write([uint16]0);$w.Write([uint16]1);$w.Write([uint16]$sizes.Count)
$offset=6+16*$sizes.Count
for($i=0;$i -lt $sizes.Count;$i++){
 $s=$sizes[$i];$dim=if($s -ge 256){0}else{$s}
 $w.Write([byte]$dim);$w.Write([byte]$dim);$w.Write([byte]0);$w.Write([byte]0);$w.Write([uint16]1);$w.Write([uint16]32)
 $w.Write([uint32]$frames[$i].Length);$w.Write([uint32]$offset);$offset+=$frames[$i].Length
}
foreach($f in $frames){$w.Write($f)}
$w.Flush()
$out=Join-Path $repo 'MochiDock/icon.ico'
[IO.File]::WriteAllBytes($out,$icon.ToArray())
$images=Join-Path $repo 'docs/images';$null=New-Item -ItemType Directory -Force $images
[IO.File]::WriteAllBytes((Join-Path $images 'mochi-icon.png'),(Render-Frame 512))
Write-Output "Wrote $out ($($icon.Length) bytes, sizes $($sizes -join ', ')) and docs/images/mochi-icon.png"
