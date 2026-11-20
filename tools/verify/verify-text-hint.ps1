param([string]$Configuration='Release')
# Checks that the grey hint of the shared text box template starts where the first typed character (and the caret) would,
# in both themes at four scales. Loads the production assembly and App.xaml styles; never starts MochiDock.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName PresentationFramework
$repo=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$assembly=[Reflection.Assembly]::LoadFrom((Join-Path $repo "MochiDock/bin/$Configuration/net8.0-windows10.0.19041.0/MochiDock.dll"))
[AppDomain]::CurrentDomain.add_AssemblyResolve({param($s,$e) if($e.Name.StartsWith('MochiDock')){return $assembly} return $null})
$app=[Windows.Application]::new();$app.ShutdownMode=[Windows.ShutdownMode]::OnExplicitShutdown
[xml]$source=Get-Content (Join-Path $repo 'MochiDock/App.xaml') -Raw
$context=[Windows.Markup.ParserContext]::new()
$context.XmlnsDictionary.Add('','http://schemas.microsoft.com/winfx/2006/xaml/presentation')
$context.XmlnsDictionary.Add('x','http://schemas.microsoft.com/winfx/2006/xaml')
$context.XmlnsDictionary.Add('controls','clr-namespace:MochiDock.Controls;assembly=MochiDock')
foreach($node in $source.SelectNodes('//*[local-name()="FontFamily"]')){$app.Resources[$node.GetAttribute('Key','http://schemas.microsoft.com/winfx/2006/xaml')]=[Windows.Media.FontFamily]::new([uri]'pack://application:,,,/',$node.InnerText)}
foreach($node in $source.SelectNodes('//*[local-name()="FontWeight"]')){$app.Resources[$node.GetAttribute('Key','http://schemas.microsoft.com/winfx/2006/xaml')]=[Windows.FontWeightConverter]::new().ConvertFromString($node.InnerText)}
$app.Resources.MergedDictionaries.Add([Windows.Markup.XamlReader]::Parse((Get-Content (Join-Path $repo 'MochiDock/Views/TypographyResources.xaml') -Raw)))
$style=$null
foreach($node in $source.SelectNodes('//*[local-name()="Style" and @*[local-name()="Key"]="ModernDarkTextBox"]')){$style=[Windows.Markup.XamlReader]::Parse($node.OuterXml,$context)}
if($null -eq $style){throw 'ModernDarkTextBox style not found'}
$checked=0
foreach($theme in 'Dark','Matcha'){
 foreach($scale in 1,1.25,1.5,2){
  $themeWindow=[Windows.Window]::new();$assembly.GetType('MochiDock.Services.ThemeService').GetMethod('ApplyTheme').Invoke($null,@($themeWindow,$theme))
  foreach($padding in '8,4','4,2'){
   $hintBox=[Windows.Controls.TextBox]::new();$hintBox.Style=$style;$hintBox.Width=240;$hintBox.Padding=[Windows.Thickness]::new(($padding -split ',')[0],($padding -split ',')[1],($padding -split ',')[0],($padding -split ',')[1])
   $assembly.GetType('MochiDock.Controls.TextBoxHint').GetMethod('SetText').Invoke($null,@($hintBox,'New task, or Review in 30m...'))|Out-Null
   $textBox=[Windows.Controls.TextBox]::new();$textBox.Style=$style;$textBox.Width=240;$textBox.Padding=$hintBox.Padding;$textBox.Text='N'
   $panel=[Windows.Controls.StackPanel]::new();$panel.Children.Add($hintBox)|Out-Null;$panel.Children.Add($textBox)|Out-Null
   $parameters=[Windows.Interop.HwndSourceParameters]::new('Hint alignment');$parameters.WindowStyle=-2147483648;$parameters.PositionX=-32000;$parameters.PositionY=-32000;$parameters.Width=1;$parameters.Height=1
   $native=[Windows.Interop.HwndSource]::new($parameters);$native.RootVisual=$panel
   try{
    [Windows.Media.VisualTreeHelper]::SetRootDpi($panel,[Windows.DpiScale]::new($scale,$scale))
    $panel.Measure([Windows.Size]::new(240,200));$panel.Arrange([Windows.Rect]::new(0,0,240,200));$panel.UpdateLayout()
    $hint=$hintBox.Template.FindName('Hint',$hintBox)
    if($hint.Visibility -ne [Windows.Visibility]::Visible){throw "Hint not visible for an empty box ($theme/$scale)"}
    $hintBounds=$hint.TransformToAncestor($hintBox).TransformBounds([Windows.Rect]::new(0,0,$hint.ActualWidth,$hint.ActualHeight))
    $glyph=$textBox.GetRectFromCharacterIndex(0)
    $hintTextX=$hintBounds.X+$hint.Padding.Left
    $dx=$hintTextX-$glyph.X
    $dy=($hintBounds.Y+$hintBounds.Height/2)-($glyph.Y+$glyph.Height/2)
    Write-Output ("{0}/{1} padding {2}: hint text x={3:N2} glyph x={4:N2} dx={5:N2}; hint centre y={6:N2} glyph centre y={7:N2} dy={8:N2}" -f $theme,$scale,$padding,$hintTextX,$glyph.X,$dx,($hintBounds.Y+$hintBounds.Height/2),($glyph.Y+$glyph.Height/2),$dy)
    if([Math]::Abs($dx) -gt .6 -or [Math]::Abs($dy) -gt .75){throw "Hint is not in line with the text: $theme/$scale padding $padding dx=$dx dy=$dy"}
    $textBox.Text=''; $hintBox.Text='x'; $panel.UpdateLayout()
    if($hint.Visibility -ne [Windows.Visibility]::Collapsed){throw "Hint stays visible when the box has text ($theme/$scale)"}
    $checked++
   } finally { $native.Dispose() }
  }
 }
}
Write-Output "Verified text box hint alignment in $checked combinations (both themes, four scales, two paddings) without launching MochiDock."
