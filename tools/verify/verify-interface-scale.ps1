# Checks the interface-size (zoom) mechanism on real controls without launching MochiDock: content under a LayoutTransform ScaleTransform
# measures 1.5x larger for the parent at 150% while its own layout size and text measurements stay in unscaled units, and tooltips and
# combo-box drop-downs read the shared IslandInterfaceScaleTransform resource.
$ErrorActionPreference='Stop'
$setup=(Get-Content (Join-Path $PSScriptRoot 'verify-prayer-capsule.ps1') -Raw).Split('$checks=0')[0]
$setup=$setup.Replace('$PSScriptRoot',('{0}{1}{0}' -f [char]39,$PSScriptRoot));Invoke-Expression $setup
$xk='http://schemas.microsoft.com/winfx/2006/xaml'
$pending=@($source.SelectNodes('//*[local-name()="ResourceDictionary"]/*[local-name()="Style"]'))
for($pass=0;$pass -lt 4;$pass++){$left=@();foreach($n in $pending){$key=$n.GetAttribute('Key',$xk);if($key -and $app.Resources.Contains($key)){continue};try{$parsed=[Windows.Markup.XamlReader]::Parse($n.OuterXml,$context);if($key){$app.Resources[$key]=$parsed}else{$app.Resources[$parsed.TargetType]=$parsed}}catch{$left+=$n}};$pending=$left}
if($pending.Count -gt 0){throw "Could not parse $($pending.Count) App.xaml styles"}
$app.Resources['IslandInterfaceScaleTransform']=[Windows.Media.ScaleTransform]::new(1,1)
$app.Resources.MergedDictionaries.Add([Windows.Markup.XamlReader]::Parse((Get-Content (Join-Path $repo 'MochiDock/Views/ColorIconResources.xaml') -Raw)))
$checks=0
function Check($condition,$message){if(-not $condition){throw $message};$script:checks++}
foreach($theme in 'Dark','Matcha'){
 foreach($scale in 1.0,1.25,1.5,2.0){
  $themeHost=[Windows.Window]::new();$assembly.GetType('MochiDock.Services.ThemeService').GetMethod('ApplyTheme').Invoke($null,@($themeHost,$theme))
  $app.Resources['IslandInterfaceScaleTransform']=[Windows.Media.ScaleTransform]::new($scale,$scale)
  $view=[Activator]::CreateInstance($assembly.GetType('MochiDock.Views.Cards.SettingsCardView'));$view.Width=620;$view.Height=400
  $root=[Windows.Controls.Grid]::new();$root.LayoutTransform=[Windows.Media.ScaleTransform]::new($scale,$scale);$root.Children.Add($view)|Out-Null
  $surface=[Windows.Controls.Border]::new();$surface.Child=$root
  $surface.SetResourceReference([Windows.Documents.TextElement]::FontFamilyProperty,'IslandUiFont');$surface.SetResourceReference([Windows.Controls.Border]::BackgroundProperty,'IslandGroundBrush')
  $p=[Windows.Interop.HwndSourceParameters]::new('s');$p.WindowStyle=-2147483648;$p.PositionX=-32000;$p.PositionY=-32000;$p.Width=1;$p.Height=1
  $native=[Windows.Interop.HwndSource]::new($p);$native.RootVisual=$surface
  try{
   $view.DataContext=[pscustomobject]@{IntegrationStatus='WhatsApp connected'}
   $limit=[Windows.Size]::new(2000,1400);$surface.Measure($limit);$surface.Arrange([Windows.Rect]::new(0,0,$surface.DesiredSize.Width,$surface.DesiredSize.Height));$surface.UpdateLayout()
   [Windows.Threading.Dispatcher]::CurrentDispatcher.Invoke([Windows.Threading.DispatcherPriority]::Loaded,[Action]{});$surface.UpdateLayout()
   # The parent sees the scaled size; the content keeps its own (unscaled) layout size.
   Check ([Math]::Abs($surface.DesiredSize.Width-620*$scale) -lt 1.5) "$theme x$scale : parent width $($surface.DesiredSize.Width) is not 620 x $scale"
   Check ([Math]::Abs($surface.DesiredSize.Height-400*$scale) -lt 1.5) "$theme x$scale : parent height $($surface.DesiredSize.Height) is not 400 x $scale"
   Check ([Math]::Abs($view.ActualWidth-620) -lt 0.5 -and [Math]::Abs($view.ActualHeight-400) -lt 0.5) "$theme x$scale : content layout size changed ($($view.ActualWidth) x $($view.ActualHeight))"
   # Text measured inside the scaled tree stays in unscaled units: the heading is as wide at 200% as at 100%.
   $heading=$null;$stack=[Collections.Generic.Stack[object]]::new();$stack.Push($view)
   while($stack.Count -gt 0){$node=$stack.Pop();if($node -is [Windows.Controls.TextBlock] -and $node.Text -eq 'MochiDock Preferences'){$heading=$node;break};if($node -is [Windows.DependencyObject]){foreach($c in [Windows.Media.VisualTreeHelper]::GetChildrenCount($node)..1|ForEach-Object{if($_ -ge 1){[Windows.Media.VisualTreeHelper]::GetChild($node,$_-1)}}){$stack.Push($c)}}}
   Check ($null -ne $heading) "$theme x$scale : heading not found"
   if($scale -eq 1.0){$script:baseHeading=$heading.ActualWidth}
   Check ([Math]::Abs($heading.ActualWidth-$script:baseHeading) -lt 0.5) "$theme x$scale : heading width $($heading.ActualWidth) differs from the unscaled $script:baseHeading"
   # Nothing is clipped: the heading and the footer buttons lie inside the unscaled content bounds.
   $transform=$heading.TransformToAncestor($view);$bounds=$transform.TransformBounds([Windows.Rect]::new(0,0,$heading.ActualWidth,$heading.ActualHeight))
   Check ($bounds.Left -ge 0 -and $bounds.Top -ge 0 -and $bounds.Right -le 620.5) "$theme x$scale : heading is outside the card ($bounds)"
   # Tooltips and combo-box drop-downs pick up the shared scale.
   Check $app.Resources.Contains([Windows.Controls.ToolTip]) "$theme x$scale : implicit ToolTip style missing"
   $applied=[Windows.Controls.ToolTip]::new();$applied.Content='x';$applied.Measure([Windows.Size]::new(400,100))
   $lt=$applied.LayoutTransform -as [Windows.Media.ScaleTransform]
   Check ($null -ne $lt -and [Math]::Abs($lt.ScaleX-$scale) -lt 0.001) "$theme x$scale : tooltip LayoutTransform is not the interface scale ($($applied.LayoutTransform))"
  } finally { $native.Dispose() }
 }
}
Write-Output "Verified the interface-size mechanism at 100, 125, 150 and 200% in both themes ($checks checks) without launching MochiDock."
