param([string]$Configuration='Release')
# Checks ThemeService.RedrawOnThemeChange, which views that copy theme brushes while drawing (AI usage rows, Prayer arc, FX chart,
# Today cells, Battery page) use to repaint after a theme switch: a visible view redraws once, a hidden view redraws when it is
# shown, and re-applying the same theme (every dock expand) redraws nothing.
# Uses a plain WPF Application: never create MochiDock.App here, WPF would run the real app's startup with the user's settings.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework
$repo=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $repo "MochiDock/bin/$Configuration/net8.0-windows10.0.19041.0/MochiDock.dll"))
[AppDomain]::CurrentDomain.add_AssemblyResolve({param($s,$e) if($e.Name.StartsWith('MochiDock')){return $assembly} return $null})
$app=[Windows.Application]::new();$app.ShutdownMode=[Windows.ShutdownMode]::OnExplicitShutdown
[xml]$source=Get-Content (Join-Path $repo 'MochiDock/App.xaml') -Raw
foreach($node in $source.SelectNodes('//*[local-name()="FontFamily"]')){$app.Resources[$node.GetAttribute('Key','http://schemas.microsoft.com/winfx/2006/xaml')]=[Windows.Media.FontFamily]::new([uri]'pack://application:,,,/',$node.InnerText)}
$theme=$assembly.GetType('MochiDock.Services.ThemeService')
function Pump { $f=[Windows.Threading.DispatcherFrame]::new(); [void][Windows.Threading.Dispatcher]::CurrentDispatcher.BeginInvoke('Background',[Action]{ $f.Continue=$false }); [Windows.Threading.Dispatcher]::PushFrame($f) }
function Apply([string]$name) { [void]$theme.GetMethod('ApplyTheme').Invoke($null, @($window, $name)); Pump }

$window=[Windows.Window]::new();$window.Width=300;$window.Height=200;$window.ShowActivated=$false;$window.WindowStyle='None';$window.Left=-4000
$panel=[Windows.Controls.StackPanel]::new();$window.Content=$panel
$shown=[Windows.Controls.Border]::new();$shown.Height=20
$hidden=[Windows.Controls.Border]::new();$hidden.Height=20;$hidden.Visibility='Collapsed'
[void]$panel.Children.Add($shown);[void]$panel.Children.Add($hidden)
$script:shownRedraws=0;$script:hiddenRedraws=0
[void]$theme.GetMethod('RedrawOnThemeChange').Invoke($null,@($shown,[Action]{ $script:shownRedraws++ }))
[void]$theme.GetMethod('RedrawOnThemeChange').Invoke($null,@($hidden,[Action]{ $script:hiddenRedraws++ }))
$window.Show();Pump
try {
  Apply 'Default'
  $script:shownRedraws=0;$script:hiddenRedraws=0
  function Expect([int]$s,[int]$h,[string]$step) { if($script:shownRedraws -ne $s -or $script:hiddenRedraws -ne $h){ throw "$step`: visible redrew $script:shownRedraws (expected $s), hidden $script:hiddenRedraws (expected $h)" } }
  Apply 'Matcha';                          Expect 1 0 'switch to Matcha'
  Apply 'Matcha';                          Expect 1 0 'same theme again'
  $hidden.Visibility='Visible'; Pump;      Expect 1 1 'hidden view shown'
  $hidden.Visibility='Collapsed'; Pump; $hidden.Visibility='Visible'; Pump; Expect 1 1 'shown again without a switch'
  Apply 'Default';                         Expect 2 2 'switch back to Default'
  Apply 'Hojicha';                         Expect 3 3 'switch to Hojicha (dark to dark)'
  Apply 'hojicha';                         Expect 3 3 'Hojicha again, other spelling'
  Apply 'Default';                         Expect 4 4 'Hojicha back to Default'
  Apply 'KuroNight';                        Expect 5 5 'switch to KuroNight (dark to dark)'
  Apply 'Kuro Night';                      Expect 5 5 'KuroNight again, other spelling'
  Apply 'Hojicha';                         Expect 6 6 'KuroNight to Hojicha (dark to dark)'
  Apply 'KuroNight';                        Expect 7 7 'Hojicha to KuroNight'
  Apply 'Default';                         Expect 8 8 'KuroNight back to Default'
  'Theme redraw checks passed.'
} finally { $window.Close() }
