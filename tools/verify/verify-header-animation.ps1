param([string]$Configuration = 'Release')
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName WindowsBase
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$dll = Join-Path $repo "MochiDock/bin/$Configuration/net8.0-windows10.0.19041.0/MochiDock.dll"
$assembly = [Reflection.Assembly]::LoadFrom($dll)
$animate = $assembly.GetType('MochiDock.Controls.ElementSizeAnimation').GetMethod('AnimateWidth')

# Verify that the former To-only animation fails for WPF's default Auto width.
$old = [Windows.Media.Animation.DoubleAnimation]::new(100, [Windows.Duration]::new([TimeSpan]::FromMilliseconds(156)))
$clock = $old.CreateClock()
$clock.Controller.Begin()
$clock.Controller.SeekAlignedToLastTick([TimeSpan]::FromMilliseconds(50), [Windows.Media.Animation.TimeSeekOrigin]::BeginTime)
$reproduced = $false
try { $null = $old.GetCurrentValue([double]::NaN, [double]::NaN, $clock) }
catch { if ($_.Exception.ToString() -notmatch 'NaN') { throw }; $reproduced = $true }
$clock.Controller.Remove()
if (!$reproduced) { throw 'The old Auto-width failure was not reproduced.' }
Write-Output 'Reproduced the previous NaN animation failure without running MochiDock.'

function Pump-Frames([int]$Milliseconds) {
    $frame = [Windows.Threading.DispatcherFrame]::new()
    $timer = [Windows.Threading.DispatcherTimer]::new()
    $timer.Interval = [TimeSpan]::FromMilliseconds($Milliseconds)
    $timer.Tag = $frame
    $timer.Add_Tick({ param($sender, $eventArgs) $sender.Stop(); $sender.Tag.Continue = $false })
    $timer.Start()
    try { [Windows.Threading.Dispatcher]::PushFrame($frame) } finally { $timer.Stop() }
}
function Animate-Width($Element, [double]$Width, [int]$Milliseconds) {
    $animate.Invoke($null, @($Element, $Width, [TimeSpan]::FromMilliseconds($Milliseconds), [Windows.Media.Animation.CubicEase]::new()))
}
function Assert-FiniteWidth($Element) {
    if ([double]::IsNaN($Element.Width) -or [double]::IsInfinity($Element.Width) -or $Element.Width -lt 0) { throw "Invalid animated width: current=$($Element.Width), actual=$($Element.ActualWidth), base=$($Element.GetAnimationBaseValue([Windows.FrameworkElement]::WidthProperty))." }
}

# Parse the actual pill declaration; no App/MainWindow, settings, services or accounts.
[xml]$source = Get-Content (Join-Path $repo 'MochiDock/MainWindow.xaml') -Raw
$pillNode = $source.SelectSingleNode('//*[local-name()="Border" and @*[local-name()="Name"]="HeaderSelectionPill"]')
foreach ($scale in @(1, 1.25, 1.5, 2)) {
    $pill = [Windows.Markup.XamlReader]::Parse($pillNode.OuterXml)
    if (![double]::IsNaN($pill.Width)) { throw 'Fixture must start with the production Auto width.' }
    # The earliest selection can arrive before layout has assigned ActualWidth.
    Animate-Width $pill 88 156
    Assert-FiniteWidth $pill
    $root = [Windows.Controls.Grid]::new(); $root.Width = 340; $root.Height = 40
    $root.Children.Add($pill) | Out-Null
    $parameters = [Windows.Interop.HwndSourceParameters]::new('Isolated header animation regression')
    $parameters.WindowStyle = -2147483648; $parameters.PositionX = -32000; $parameters.PositionY = -32000; $parameters.Width = 1; $parameters.Height = 1
    $native = [Windows.Interop.HwndSource]::new($parameters)
    try {
        $native.RootVisual = $root
        [Windows.Media.VisualTreeHelper]::SetRootDpi($root, [Windows.DpiScale]::new($scale, $scale))
        $root.Measure([Windows.Size]::new(340,40)); $root.Arrange([Windows.Rect]::new(0,0,340,40)); $root.UpdateLayout()
        Pump-Frames 50; Assert-FiniteWidth $pill
        foreach ($target in @(120, 36, 96)) { Animate-Width $pill $target 156; Pump-Frames 25; Assert-FiniteWidth $pill }
        Pump-Frames 220; Assert-FiniteWidth $pill
        if ([Math]::Abs($pill.Width - 96) -gt 0.01) { throw 'Latest width did not settle.' }
        # Repeat from Auto after layout, when ActualWidth supplies the origin.
        $pill.Width = [double]::NaN; $root.UpdateLayout()
        Animate-Width $pill 70 156; Pump-Frames 220; Assert-FiniteWidth $pill
        Animate-Width $pill 42 0
        if ($pill.Width -ne 42 -or $pill.HasAnimatedProperties) { throw 'Reduced-motion width did not settle immediately.' }
        $pill.Visibility = 'Collapsed'; Animate-Width $pill 110 156; Pump-Frames 220; Assert-FiniteWidth $pill
        Write-Output "Header Auto-width, retarget, settle, reduced-motion and hidden checks passed at scale $scale."
    } finally { $native.Dispose() }
}
