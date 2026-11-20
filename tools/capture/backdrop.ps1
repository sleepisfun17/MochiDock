param([int]$Monitor = 0, [int]$MinutesToLive = 30)
# A plain, borderless full-screen gradient window used as a clean background while capturing the dock. It is NOT topmost, so the
# dock (which is topmost) stays above it, and it never takes focus. It closes itself after $MinutesToLive minutes or when Esc is pressed.
# No Windows settings, wallpaper or other applications are changed.
Add-Type -AssemblyName PresentationFramework, System.Windows.Forms
$screen = [Windows.Forms.Screen]::AllScreens[$Monitor]
$b = $screen.Bounds
$w = [Windows.Window]::new()
$w.WindowStyle = 'None'; $w.ResizeMode = 'NoResize'; $w.ShowInTaskbar = $false; $w.ShowActivated = $false; $w.Topmost = $true   # topmost so no normal window can cover it; the capture scripts re-raise the dock above it
$w.AllowsTransparency = $false; $w.Title = 'MochiDock capture backdrop'
$w.WindowStartupLocation = 'Manual'
$w.Left = $b.X; $w.Top = $b.Y; $w.Width = $b.Width; $w.Height = $b.Height
$brush = [Windows.Media.LinearGradientBrush]::new()
$brush.StartPoint = [Windows.Point]::new(0, 0); $brush.EndPoint = [Windows.Point]::new(1, 1)
foreach ($s in @(@('#8FA6C4', 0.0), @('#B9B2D3', 0.55), @('#D6C3CF', 1.0))) {
  $brush.GradientStops.Add([Windows.Media.GradientStop]::new([Windows.Media.ColorConverter]::ConvertFromString($s[0]), $s[1]))
}
$w.Background = $brush
$w.Add_KeyDown({ if ($_.Key -eq 'Escape') { $w.Close() } })
$timer = [Windows.Threading.DispatcherTimer]::new(); $timer.Interval = [TimeSpan]::FromMinutes($MinutesToLive)
$timer.Add_Tick({ $w.Close() }); $timer.Start()
[void]$w.ShowDialog()
