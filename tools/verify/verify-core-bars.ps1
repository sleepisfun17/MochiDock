param([string]$Configuration='Release')
# Renders the Stats per-core grid (CoreBars) under MochiDock-style interface zooms for 8, 32 and 64 threads and measures every cell from
# the pixels, row band by row band: as many cells as threads, all the same whole-pixel width and height, at 100-200 %. Saves the
# 32-thread render (mixed loads) as MochiDock/obj/core-bars-32.png for a look. A plain WPF Application, never MochiDock.App; never
# starts MochiDock.
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

function New-Cores([int]$count,[scriptblock]$usage){
 $items=[Activator]::CreateInstance($listType)
 for($i=0;$i -lt $count;$i++){$it=[Activator]::CreateInstance($itemType);$it.CoreIndex=$i+1;$it.Usage=& $usage $i;$it.CoreKind=$(if($i -lt $count/2){'Performance'}else{'Efficiency'});$items.Add($it)}
 ,$items
}

# Renders the control at a layout zoom (like MochiDock's interface size) at 96 dpi: one device pixel per unit after the zoom.
function Render-Cores($items,[double]$zoom){
 $bars=[Activator]::CreateInstance($assembly.GetType('MochiDock.Controls.CoreBars'));$bars.Width=190;$bars.Height=46;$bars.Items=$items
 $zoomed=[Windows.Controls.Border]::new();$zoomed.LayoutTransform=[Windows.Media.ScaleTransform]::new($zoom,$zoom);$zoomed.Child=$bars
 $surface=[Windows.Controls.Border]::new();$surface.Background=[Windows.Media.Brushes]::Black;$surface.Child=$zoomed
 $p=[Windows.Interop.HwndSourceParameters]::new('Bars');$p.WindowStyle=-2147483648;$p.PositionX=-32000;$p.PositionY=-32000;$p.Width=1;$p.Height=1
 $native=[Windows.Interop.HwndSource]::new($p);$native.RootVisual=$surface
 $surface.Measure([Windows.Size]::new(600,300));$surface.Arrange([Windows.Rect]::new(0,0,$surface.DesiredSize.Width,$surface.DesiredSize.Height));$surface.UpdateLayout();$surface.UpdateLayout()
 $w=[int][Math]::Ceiling($surface.ActualWidth);$h=[int][Math]::Ceiling($surface.ActualHeight)
 $bmp=[Windows.Media.Imaging.RenderTargetBitmap]::new($w,$h,96,96,[Windows.Media.PixelFormats]::Pbgra32);$bmp.Render($surface)
 $native.Dispose()
 $bmp
}

function Get-LitRuns([byte[]]$px,[int]$stride,[int]$w,[int]$y){
 $runs=[Collections.Generic.List[int]]::new();$run=0
 for($x=0;$x -lt $w;$x++){$i=$y*$stride+$x*4;if(($px[$i]+$px[$i+1]+$px[$i+2]) -gt 150){$run++}elseif($run -gt 0){$runs.Add($run);$run=0}}
 if($run -gt 0){$runs.Add($run)}
 ,$runs
}

$bad=0
foreach($count in 8,32,64){
 $items=New-Cores $count {param($i) 100}
 foreach($zoom in 1,1.1,1.25,1.5,1.75,2){
  $bmp=Render-Cores $items $zoom
  $w=$bmp.PixelWidth;$h=$bmp.PixelHeight;$stride=$w*4;$px=[byte[]]::new($stride*$h);$bmp.CopyPixels($px,$stride,0)
  # Row bands: consecutive pixel rows with anything lit. Each band is one grid row; its cells are measured on its middle row, where
  # rounded corners do not shorten them.
  $bands=@();$start=-1
  for($y=0;$y -le $h;$y++){
   $any=$false
   if($y -lt $h){for($x=0;$x -lt $w;$x++){$i=$y*$stride+$x*4;if(($px[$i]+$px[$i+1]+$px[$i+2]) -gt 150){$any=$true;break}}}
   if($any -and $start -lt 0){$start=$y}elseif(-not $any -and $start -ge 0){$bands+=,@($start,($y-$start));$start=-1}
  }
  $cells=0;$widths=@();$heights=@()
  foreach($band in $bands){$runs=Get-LitRuns $px $stride $w ($band[0]+[int]($band[1]/2));$cells+=$runs.Count;$widths+=$runs;$heights+=$band[1]}
  $wg=$widths | Group-Object;$hg=$heights | Group-Object
  "$count threads, zoom $zoom`: $($bands.Count) row(s), $cells cells, widths: $((($wg | ForEach-Object { "$($_.Name)px x$($_.Count)" }) -join ', ')), heights: $((($hg | ForEach-Object { "$($_.Name)px x$($_.Count)" }) -join ', '))"
  if($cells -ne $count -or @($wg).Count -ne 1 -or @($hg).Count -ne 1){$bad++}
 }
}

# A 32-thread look at 150 %: mixed loads, so the fills rise from each cell's bottom.
$mixed=New-Cores 32 {param($i) (($i * 37) % 101)}
$bmp=Render-Cores $mixed 1.5
$out=Join-Path $repo 'MochiDock/obj/core-bars-32.png'
$encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bmp))
$stream=[IO.File]::Create($out);try{$encoder.Save($stream)}finally{$stream.Dispose()}
"Saved $out"

if($bad){throw "Core grid is uneven or miscounted in $bad render(s)"}
'Core grid: every thread a cell, all cells equal whole pixels, for 8, 32 and 64 threads at every zoom from 100 to 200 %.'
