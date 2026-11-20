param([string]$Configuration='Release')
# Renders the Stats per-core bars (CoreBars) under MochiDock-style interface zooms and measures every bar from the pixels: all 32 must
# be the same whole-pixel width at 100-200 %. A plain WPF Application, never MochiDock.App; never starts MochiDock.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework
$repo=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$directory=Join-Path $repo "MochiDock/bin/$Configuration/net8.0-windows10.0.19041.0"
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $directory 'MochiDock.dll'))
$core=[Reflection.Assembly]::LoadFrom((Join-Path $directory 'MochiDock.Core.dll'))
[AppDomain]::CurrentDomain.add_AssemblyResolve({param($s,$e) if($e.Name.StartsWith('MochiDock.Core')){return $core} if($e.Name.StartsWith('MochiDock')){return $assembly} return $null})
$app=[Windows.Application]::new();$app.ShutdownMode=[Windows.ShutdownMode]::OnExplicitShutdown
$app.Resources['IslandTrackBrush']=[Windows.Media.SolidColorBrush]::new([Windows.Media.Color]::FromRgb(44,44,64))
$itemType=$assembly.GetType('MochiDock.Services.CoreUsageItem')
$listType=[Collections.ObjectModel.ObservableCollection[object]].GetGenericTypeDefinition().MakeGenericType($itemType)
$items=[Activator]::CreateInstance($listType)
for($i=0;$i -lt 32;$i++){$it=[Activator]::CreateInstance($itemType);$it.CoreIndex=$i+1;$it.Usage=100;$it.CoreKind=$(if($i -lt 16){'Performance'}else{'Efficiency'});$items.Add($it)}
$bad=0
foreach($zoom in 1,1.1,1.25,1.5,1.75,2){
 $bars=[Activator]::CreateInstance($assembly.GetType('MochiDock.Controls.CoreBars'));$bars.Width=190;$bars.Height=46;$bars.Items=$items
 # Like MochiDock's interface size: a layout zoom above the control, rendered at 96 dpi (one device pixel per unit after the zoom).
 $zoomed=[Windows.Controls.Border]::new();$zoomed.LayoutTransform=[Windows.Media.ScaleTransform]::new($zoom,$zoom);$zoomed.Child=$bars
 $surface=[Windows.Controls.Border]::new();$surface.Background=[Windows.Media.Brushes]::Black;$surface.Child=$zoomed
 $p=[Windows.Interop.HwndSourceParameters]::new('Bars');$p.WindowStyle=-2147483648;$p.PositionX=-32000;$p.PositionY=-32000;$p.Width=1;$p.Height=1
 $native=[Windows.Interop.HwndSource]::new($p);$native.RootVisual=$surface
 $surface.Measure([Windows.Size]::new(600,300));$surface.Arrange([Windows.Rect]::new(0,0,$surface.DesiredSize.Width,$surface.DesiredSize.Height));$surface.UpdateLayout();$surface.UpdateLayout()
 $w=[int][Math]::Ceiling($surface.ActualWidth);$h=[int][Math]::Ceiling($surface.ActualHeight)
 $bmp=[Windows.Media.Imaging.RenderTargetBitmap]::new($w,$h,96,96,[Windows.Media.PixelFormats]::Pbgra32);$bmp.Render($surface)
 $stride=$w*4;$px=[byte[]]::new($stride*$h);$bmp.CopyPixels($px,$stride,0)
 $y=[int]($h/2);$runs=@();$run=0
 for($x=0;$x -lt $w;$x++){$i=$y*$stride+$x*4;$lit=($px[$i]+$px[$i+1]+$px[$i+2]) -gt 150;if($lit){$run++}elseif($run -gt 0){$runs+=$run;$run=0}}
 if($run -gt 0){$runs+=$run}
 $groups=$runs | Group-Object
 "zoom $zoom`: $($runs.Count) bars, widths: $((($groups | ForEach-Object { "$($_.Name)px x$($_.Count)" }) -join ', '))"
 if($runs.Count -ne 32 -or @($groups).Count -ne 1){$bad++}
 $native.Dispose()
}
if($bad){throw "Core bars are uneven at $bad zoom level(s)"}
'Core bars: 32 equal whole-pixel bars at every zoom from 100 to 200 %.'
