param([string]$Configuration = "Release")
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName PresentationFramework
$repo = Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
$dll = Join-Path $repo "MochiDock/bin/$Configuration/net8.0-windows10.0.19041.0/MochiDock.dll"
$assembly = [Reflection.Assembly]::LoadFrom($dll)
# EmojiImages reads Assets\emoji.pak beside the app (AppContext.BaseDirectory), which inside pwsh is the pwsh folder. Open the pak once
# with the base directory pointed at the build output, then restore it; EmojiImages keeps the opened pak for the rest of the run.
$pak = Join-Path (Split-Path $dll) "Assets/emoji.pak"
if (!(Test-Path $pak)) { throw "Build output has no Assets/emoji.pak: $pak" }
$baseKey = "APP_CONTEXT_BASE_DIRECTORY"; $baseOld = [AppContext]::GetData($baseKey)
[AppContext]::SetData($baseKey, (Split-Path $dll) + [IO.Path]::DirectorySeparatorChar)
try { if ($null -eq [MochiDock.Services.EmojiImages]::Get("1f600")) { throw "emoji.pak did not load from $pak" } }
finally { [AppContext]::SetData($baseKey, $baseOld) }
# Load resources only: never instantiate MochiDock.App or MainWindow.

$application = New-Object System.Windows.Application
[xml]$source = Get-Content (Join-Path $repo "MochiDock/App.xaml") -Raw
foreach($fontNode in $source.SelectNodes('//*[local-name()="FontFamily"]')) {
 $key=$fontNode.GetAttribute("Key","http://schemas.microsoft.com/winfx/2006/xaml")
 $application.Resources[$key]=[Windows.Media.FontFamily]::new([uri]"pack://application:,,,/",$fontNode.InnerText)
}
foreach($weightNode in $source.SelectNodes('//*[local-name()="FontWeight"]')){
 $application.Resources[$weightNode.GetAttribute("Key","http://schemas.microsoft.com/winfx/2006/xaml")]=[Windows.FontWeightConverter]::new().ConvertFromString($weightNode.InnerText)
}
$family=$application.Resources["QuicksandFont"]
$dictionary = [Windows.Markup.XamlReader]::Parse((Get-Content (Join-Path $repo "MochiDock/Views/TypographyResources.xaml") -Raw))
$application.Resources.MergedDictionaries.Add($dictionary)
foreach ($weight in @([Windows.FontWeights]::Normal,[Windows.FontWeights]::Medium,[Windows.FontWeights]::SemiBold)) {
 $primary = [Windows.Media.FontFamily]::new([uri]"pack://application:,,,/", ($family.Source -replace ", Yu Gothic UI.*$",""))
 $typeface = New-Object Windows.Media.Typeface($primary,[Windows.FontStyles]::Normal,$weight,[Windows.FontStretches]::Normal)
 $glyph = $null
 if (!$typeface.TryGetGlyphTypeface([ref]$glyph)) { throw "Bundled face did not resolve: $weight" }
 if ($glyph.FontUri.ToString() -notmatch 'Quicksand') { throw "Unexpected fallback: $($glyph.FontUri)" }
 Write-Output "Resolved $weight -> $($glyph.FontUri)"
}
foreach ($key in $dictionary.Keys) {
 if ($dictionary[$key] -isnot [Windows.Style]) { continue } # sizes such as IslandInputFontSize are not text styles
 $text = New-Object Windows.Controls.TextBlock
 $text.FontFamily = $family
 $text.Style = $dictionary[$key]
 $text.Text = "a 100% ギザギザ 日本語 😀"
 $text.Measure([Windows.Size]::new(600,100))
 Write-Output "$key : $($text.FontSize) / $($text.FontWeight), height=$($text.DesiredSize.Height)"
}
[xml]$window = Get-Content (Join-Path $repo "MochiDock/MainWindow.xaml") -Raw
if ($window.DocumentElement.GetAttribute("TextOptions.TextRenderingMode") -ne "Grayscale") { throw "Expected grayscale rendering" }
if ($window.DocumentElement.GetAttribute("TextOptions.TextFormattingMode") -ne "Ideal") { throw "Expected Ideal formatting" }
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class TypographyDpi {
 [DllImport("user32.dll")] public static extern IntPtr GetWindowDpiAwarenessContext(IntPtr window);
 [DllImport("user32.dll")] public static extern int GetAwarenessFromDpiAwarenessContext(IntPtr context);
 [DllImport("user32.dll")] public static extern uint GetDpiForWindow(IntPtr window);
 [DllImport("kernel32.dll", CharSet=CharSet.Unicode, SetLastError=true)] public static extern IntPtr LoadLibraryEx(string file, IntPtr reserved, uint flags);
 [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] public static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);
 [DllImport("kernel32.dll")] public static extern uint SizeofResource(IntPtr module, IntPtr resource);
 [DllImport("kernel32.dll")] public static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
 [DllImport("kernel32.dll")] public static extern IntPtr LockResource(IntPtr data);
 [DllImport("kernel32.dll")] public static extern bool FreeLibrary(IntPtr module);
}
'@
$running = Get-Process MochiDock -ErrorAction SilentlyContinue
foreach ($process in $running) {
 if ($process.MainWindowHandle -ne [IntPtr]::Zero) {
  $context = [TypographyDpi]::GetWindowDpiAwarenessContext($process.MainWindowHandle)
  Write-Output "Running window awareness=$([TypographyDpi]::GetAwarenessFromDpiAwarenessContext($context)), DPI=$([TypographyDpi]::GetDpiForWindow($process.MainWindowHandle))"
 }
}
if (!$running) { Write-Output "Live DPI unavailable to this inspector (an elevated process may be hidden); no MochiDock instance was launched." }

# Render the actual control against fixtures; no application services or MainWindow.
foreach ($brush in $source.SelectNodes('//*[local-name()="SolidColorBrush"]')) {
 $key=$brush.GetAttribute("Key","http://schemas.microsoft.com/winfx/2006/xaml")
 if($key){$application.Resources[$key]=[Windows.Media.BrushConverter]::new().ConvertFromString($brush.Color)}
}
$application.Resources["BoolToVisibility"]=[Windows.Controls.BooleanToVisibilityConverter]::new()
$application.Resources["InverseBoolToVisibility"]=[Activator]::CreateInstance($assembly.GetType("MochiDock.Views.InverseBooleanToVisibilityConverter"))
$context=[Windows.Markup.ParserContext]::new()
$context.XmlnsDictionary.Add("","http://schemas.microsoft.com/winfx/2006/xaml/presentation")
$context.XmlnsDictionary.Add("x","http://schemas.microsoft.com/winfx/2006/xaml")
$context.XmlnsDictionary.Add("views","clr-namespace:MochiDock.Views;assembly=MochiDock")
# The text box template reads an attached property of MochiDock.Controls, so its prefix must be mapped before styles are parsed.
$context.XmlnsDictionary.Add("controls","clr-namespace:MochiDock.Controls;assembly=MochiDock")
foreach($style in $source.SelectNodes('//*[local-name()="ResourceDictionary"]/*[local-name()="Style"]')) {
 $key=$style.GetAttribute("Key","http://schemas.microsoft.com/winfx/2006/xaml")
 $parsedStyle=[Windows.Markup.XamlReader]::Parse($style.OuterXml,$context)
 if($key){$application.Resources[$key]=$parsedStyle}else{$application.Resources[$parsedStyle.TargetType]=$parsedStyle}
}

# Load source XAML into base UserControls: no card constructors, settings, network or accounts.
$weatherMarkup=(Get-Content (Join-Path $repo "MochiDock/Views/WeatherIconResources.xaml") -Raw) -replace 'clr-namespace:MochiDock.Models','clr-namespace:MochiDock.Models;assembly=MochiDock'
$weather=[Windows.Markup.XamlReader]::Parse($weatherMarkup,$context)
$application.Resources.MergedDictionaries.Add($weather)
# The flat colour icons that replaced emoji in the cards.
$application.Resources.MergedDictionaries.Add([Windows.Markup.XamlReader]::Parse((Get-Content (Join-Path $repo "MochiDock/Views/ColorIconResources.xaml") -Raw),$context))
$output=Join-Path $repo "MochiDock/obj/card-design"
$null=New-Item -ItemType Directory -Force $output

# Map the apphost as resource data only: no executable code, startup, mutex or elevation.
$module=[TypographyDpi]::LoadLibraryEx((Join-Path (Split-Path $dll) "MochiDock.exe"),[IntPtr]::Zero,0x22)
if($module -eq [IntPtr]::Zero){throw "Cannot inspect embedded apphost manifest"}
try{
 $resource=[TypographyDpi]::FindResource($module,[IntPtr]::new(1),[IntPtr]::new(24))
 if($resource -eq [IntPtr]::Zero){throw "Missing embedded apphost manifest"}
 $bytes=[byte[]]::new([TypographyDpi]::SizeofResource($module,$resource))
 $address=[TypographyDpi]::LockResource([TypographyDpi]::LoadResource($module,$resource))
 [Runtime.InteropServices.Marshal]::Copy($address,$bytes,0,$bytes.Length)
 $manifestText=[Text.Encoding]::UTF8.GetString($bytes).TrimEnd([char]0)
 [xml]$manifest=$manifestText
 if($manifest.SelectSingleNode('//*[local-name()="dpiAwareness"]').InnerText -ne "PerMonitorV2"){throw "Apphost DPI manifest is not PerMonitorV2"}
 $manifestText | Set-Content (Join-Path $output "embedded-manifest.xml")
 Write-Output "Verified PerMonitorV2 in the built executable's embedded manifest without running it."
}finally{$null=[TypographyDpi]::FreeLibrary($module)}
$stats=[Activator]::CreateInstance($assembly.GetType("MochiDock.Services.HardwareStats"))
$stats.CpuUsage=37;$stats.CpuTemp=56;$stats.GpuUsage=4;$stats.RamUsagePercent=63;$stats.LogicalCores=8
$stats.SystemCpuPercent=17;$stats.UserCpuPercent=20;$stats.IdleCpuPercent=63;$stats.CpuSpeedGhz=2.36;$stats.CpuBaseGhz=3.6
$stats.FormattedDiskSpeed="941.9 MB/s";$stats.DiskReadBytesPerSec=987654321;$stats.NetDownloadBytesPerSec=987654321
foreach($property in @("CpuChipSparkPoints","GpuChipSparkPoints","DiskChipSparkPoints","NetChipSparkPoints","FpsChipSparkPoints")){$stats.$property=[Windows.Media.PointCollection]::Parse("0.75,3.25 20,1.25 40,2.25 60,0.75 79.25,2.25")}
foreach($name in @("MochiDock","Desktop Window Manager","Task Manager","Windows Explorer","HWiNFO 64 (x64)","System","Host Process for Windows Tasks","Codex","Claude","TRCC")){
 $item=[Activator]::CreateInstance($assembly.GetType("MochiDock.Services.ProcessMetricItem"));$item.Name=$name;$item.DisplayName=$name;$item.ValueText="2.3%";$stats.TopProcesses.Add($item)
}

# An i9-like CPU: 16 P-core threads and 16 E-cores, so the per-core grid and its caption are drawn as on a hybrid CPU.
$stats.IsHybridCpu=$true;$stats.CoreSummaryText="8 P-cores (16 threads) · 16 E-cores";$stats.CoreHeaderText="PER CORE · 8P + 16E"
for($i=0;$i -lt 32;$i++){$core=[Activator]::CreateInstance($assembly.GetType("MochiDock.Services.CoreUsageItem"));$core.CoreIndex=$i+1;$core.Usage=($i*37)%101;$core.CoreKind=$(if($i -lt 16){"Performance"}else{"Efficiency"});$stats.CoreUsages.Add($core)}
$message=[pscustomobject]@{Sender="Alex · 日本語";SenderName="Alex";Message="A readable message with a, numbers 100%, 日本語 and emoji 😀 👍🏽 🇯🇵 👨‍👩‍👧 ❤️.";FormattedTime="13:44";Subject="Design review and a long subject";BodyPreview="Please review the updated card typography. 日本語テキスト";FolderName="Inbox";ClientSource="Outlook"}
$task=[pscustomobject]@{Title="Review the calendar layout";IsCompleted=$false;CountdownText="in 30 min";HasDueTime=$true}
$media=[Activator]::CreateInstance($assembly.GetType("MochiDock.Models.MediaInfo"));$media.Title="A song title";$media.Artist="Artist 日本語";$media.Duration=[TimeSpan]::FromSeconds(151);$media.CanSeek=$true;$media.Position=[TimeSpan]::FromSeconds(74)
$data=[pscustomobject]@{
 CurrentDiscord=$message;CurrentEmail=$message;CurrentWhatsApp=$message;RecentDiscordMessages=@($message,$message);RecentEmailMessages=@($message,$message);RecentWhatsAppMessages=@($message,$message)
 SelectedWhatsAppConversation=[pscustomobject]@{CounterBadgeText="2 messages"};ReplyText="";HasSummaryWhatsApp=$true;HasSummaryEmail=$true;SummaryWhatsAppCount=2;SummaryEmailCount=1;SummaryTitle="Game Session Complete";SummarySubtitle="VALORANT-Win64-Shipping · 34m 57s";SummaryDetails="Two messages and one email are ready to review."
 HasSummaryLines=$true;ShowSummaryDetails=$false;HasSummaryGraph=$true;SummaryStutterGeometry="M 40,0 L 40,76 M 120,0 L 120,76";SummaryFpsPoints=[Windows.Media.PointCollection]::Parse("0,50 40,20 80,45 120,10 160,40 200,25 260,35");SummaryPingPoints=[Windows.Media.PointCollection]::Parse("0,70 130,66 260,68")
 SummaryGraphFpsLabel="FPS (top = 579)";SummaryGraphPingLabel="Ping (top = 50 ms)";SummaryGraphDuration="34m 57s";HasSummaryGraphPing=$true;HasSummaryGraphStutters=$true
 SummaryLines=@([pscustomobject]@{Label="Speed";Text="";HasStats=$true;IsPlain=$false;Stats=@([pscustomobject]@{Value="579";Caption="avg FPS";Warn=$false},[pscustomobject]@{Value="6";Caption="1% low";Warn=$false},[pscustomobject]@{Value="1";Caption="0.1% low";Warn=$false})},[pscustomobject]@{Label="Frames";Text="";HasStats=$true;IsPlain=$false;Stats=@([pscustomobject]@{Value="1";Caption="min FPS";Warn=$false},[pscustomobject]@{Value="12";Caption="stutters";Warn=$false},[pscustomobject]@{Value="34m 57s";Caption="played";Warn=$false})},[pscustomobject]@{Label="GPU";Text="";HasStats=$true;IsPlain=$false;Stats=@([pscustomobject]@{Value="31%";Caption="avg load";Warn=$false},[pscustomobject]@{Value="92%";Caption="max load";Warn=$false},[pscustomobject]@{Value="86°";Caption="max temp";Warn=$true})},[pscustomobject]@{Label="CPU";Text="";HasStats=$true;IsPlain=$false;Stats=@([pscustomobject]@{Value="41%";Caption="avg load";Warn=$false},[pscustomobject]@{Value="82%";Caption="max load";Warn=$false},[pscustomobject]@{Value="71°";Caption="max temp";Warn=$false})},[pscustomobject]@{Label="Network";Text="";HasStats=$true;IsPlain=$false;Stats=@([pscustomobject]@{Value="15 ms";Caption="avg ping";Warn=$false},[pscustomobject]@{Value="17 ms";Caption="worst";Warn=$false},[pscustomobject]@{Value="25";Caption="lost";Warn=$true})},[pscustomobject]@{Label="Trend";Text="vs your usual (6 sessions): avg +117 FPS, 1% low +1, and ping 4 ms better than the average";HasStats=$false;IsPlain=$true;Stats=@()},[pscustomobject]@{Label="Missed";Text="1 WhatsApp message · 2 Discord messages";HasStats=$false;IsPlain=$true;Stats=@()})
 CurrentDayOfWeekUpper="FRIDAY";CurrentDateMonthText="6 November";CalendarDays=@(1..35|ForEach-Object{[pscustomobject]@{DayNumber=($_-1)%30+1;IsToday=$_ -eq 6;IsCurrentMonth=$true}});Tasks=@($task,$task)
 ShelfFiles=@([pscustomobject]@{FileName="Layout notes 日本語.txt";ExtensionBadge="TXT";FormattedSize="12 KB"});ClipboardHistory=@([pscustomobject]@{PreviewText="Copied text · 日本語 😀";FormattedTime="13:44";IsPinned=$false})
 PhoneDropAddresses=@("192.168.1.20");PhoneDropAddress="192.168.1.20";PhoneDropStatus="Stopped";PhoneDropState="Stopped";CanStartPhoneDrop=$true;CanStopPhoneDrop=$false;HasPendingDropApproval=$false;IsPhoneDropEnabled=$false
 CurrentMedia=$media;HasLyrics=$false;LyricsStatusText="Loading lyrics";PlainLyrics="";PomodoroTimeDisplay="25:00";PomodoroStateText="Ready";PomodoroModeLabel="Focus";PomodoroPlayPauseIcon="▶";PomodoroPlayPauseLabel="Start";HardwareStats=$stats
 UpdateStatusText="Version 0.1.3-beta is available (you have 0.1.2-beta).";HasUpdate=$true;CanInstallUpdate=$true;IsUpdateDownloading=$false;UpdateInstallHint="";HasUpdateInstallHint=$false;CheckForUpdates=$true;UpdateChipText="↑ Update to 0.1.3-beta";IsUpdateChipHighlighted=$true;UpdateChipToolTip="Version 0.1.3-beta is available (you have 0.1.2-beta). Click to update."
 WeatherCityDisplay="Tokyo";WeatherDisplayText="18°C";WeatherConditionText="Partly cloudy";WeatherInfo=[pscustomobject]@{HourlyList=@();DailyList=@()};ActiveSystemModule="CPU";IsStatsChart24h=$true;StatsChartSpanLabel="24 H";StatsChartSpanTooltip="Showing the last 24 hours";HasStatsHistory=$true;CpuPeak24Text="Peak 86 °C at 21:40";CpuLoad24Geometry=[Windows.Media.Geometry]::Parse("M0,80 L40,60 80,85 110,30 M150,70 L220,50");CpuTemp24Geometry=[Windows.Media.Geometry]::Parse("M0,50 L40,45 80,55 110,20 M150,48 L220,44");IsCpuModuleActive=$true;IsGpuModuleActive=$false;IsMemoryModuleActive=$false;IsDrivesModuleActive=$false;IsNetworkModuleActive=$false;IsGamesModuleActive=$false;IsBatteryModuleActive=$false
}
$heights=@{Media=170;WhatsApp=340;Discord=230;Apps=230;Email=230;Settings=400;Hardware=345;Summary=350;Weather=275;Pomodoro=170;Prayer=270;FileShelf=410;Clipboard=250;Today=360;FxRates=360;BatteryModule=345;AiUsage=360;Script=300;Downloads=260}
$glyphEvidence=[Collections.Generic.List[object]]::new()
function Add-Visuals($visual,$list){
 if($null -eq $visual){return}
 $list.Add($visual)
 for($i=0;$i -lt [Windows.Media.VisualTreeHelper]::GetChildrenCount($visual);$i++){Add-Visuals ([Windows.Media.VisualTreeHelper]::GetChild($visual,$i)) $list}
}
function Read-Glyphs($drawing,$card){
 if($drawing -is [Windows.Media.GlyphRunDrawing]){
  $run=$drawing.GlyphRun;$uri=$run.GlyphTypeface.FontUri.ToString();$characters=-join $run.Characters
  $latin=$characters -cmatch '[A-Za-z]'
  if($latin -and $uri -notmatch 'quicksand'){throw "Quicksand Latin fallback in $card : $uri"}
  $glyphEvidence.Add([pscustomobject]@{Card=$card;Theme=$script:fixtureTheme;Scale=$script:fixtureScale;Font=$uri;Family=($run.GlyphTypeface.FamilyNames.Values -join ", ");Latin=$latin})
 }elseif($drawing -is [Windows.Media.DrawingGroup]){foreach($child in $drawing.Children){Read-Glyphs $child $card}}
}
$events='PreviewMouseDown|PreviewMouseUp|LostKeyboardFocus|LostFocus|Click|Checked|Unchecked|SelectionChanged|TextChanged|ValueChanged|PreviewMouseWheel|MouseLeftButtonDown|MouseRightButtonUp|MouseRightButtonDown|MouseLeftButtonUp|MouseDown|MouseUp|MouseMove|MouseEnter|MouseLeave|PreviewMouseLeftButtonDown|PreviewMouseLeftButtonUp|PreviewMouseMove|PreviewKeyDown|KeyDown|LostMouseCapture|DragStarted|DragCompleted|DragDelta|Loaded|Unloaded|SizeChanged|DataContextChanged|IsVisibleChanged|Drop|DragOver|CycleCompleted'
# The navigation icons (the Apps card uses the bell); x:Shared only works in compiled dictionaries.
$application.Resources.MergedDictionaries.Add([Windows.Markup.XamlReader]::Parse(((Get-Content (Join-Path $repo 'MochiDock/Views/NavigationIconResources.xaml') -Raw) -replace ' x:Shared="False"','')))
foreach($file in Get-ChildItem (Join-Path $repo "MochiDock/Views/Cards") -Filter '*View.xaml'){
 $name=$file.BaseName -replace 'CardView$','' -replace 'View$',''
 [xml]$xml=Get-Content $file.FullName -Raw
 $xml.DocumentElement.RemoveAttribute("Class","http://schemas.microsoft.com/winfx/2006/xaml")
 foreach($attribute in @($xml.SelectNodes('//@*'))){if($attribute.LocalName -match ('^(?:[A-Za-z]+\.)?(?:'+$events+')$')){$null=$attribute.OwnerElement.RemoveAttributeNode($attribute)}}
 $markup=$xml.OuterXml -replace 'clr-namespace:(MochiDock[.A-Za-z]+)(?=")','clr-namespace:$1;assembly=MochiDock'
 foreach($theme in @("Dark","Matcha","Hojicha")){foreach($scale in @(1,1.25,1.5,2)){
 $script:fixtureTheme=$theme;$script:fixtureScale=$scale
  $view=[Windows.Markup.XamlReader]::Parse($markup,$context);$view.DataContext=$data;$view.SetResourceReference([Windows.Controls.Control]::FontFamilyProperty,"IslandUiFont")
  if($name -eq "Today"){
   # Today builds its 7x7 calendar in code-behind, so render the real control (with a fixed date) instead of bare XAML.
   $view=[Activator]::CreateInstance($assembly.GetType("MochiDock.Views.Cards.TodayCardView"));$view.SetResourceReference([Windows.Controls.Control]::FontFamilyProperty,"IslandUiFont")
   $flags=[Reflection.BindingFlags]"NonPublic,Instance,DeclaredOnly"
   $type=$view.GetType();$fixedDate=[datetime]::new(2026,11,11)
   $type.GetField("_month",$flags).SetValue($view,$fixedDate);$type.GetField("_selected",$flags).SetValue($view,$fixedDate)
   # A primed calendar feed (no network): events on the fixed day, dots on other days, and a meeting link.
   $feedType=$assembly.GetType("MochiDock.Services.CalendarFeedService");$eventType=$feedType.GetProperty("Events").PropertyType.GetGenericArguments()[0]
   # The fixture only needs a primed object (no timers or I/O), so skip the constructor.
   $feed=[Runtime.CompilerServices.RuntimeHelpers]::GetUninitializedObject($feedType)
   $list=[Activator]::CreateInstance([Collections.Generic.List[object]].GetGenericTypeDefinition().MakeGenericType($eventType))
   $day=[datetime]::new(2026,11,11)
   foreach($e in @(@("hol","Company holiday",$day,$day.AddDays(1),$true,"",$null),@("a","Standup",$day.AddHours(9.5),$day.AddHours(10),$false,"","https://meet.google.com/abc-defg-hij"),@("b","Design review with a long title that must trim",$day.AddHours(14),$day.AddHours(15.5),$false,"Room 4F","https://acme.zoom.us/j/123"),@("c","Dentist",$day.AddDays(2).AddHours(11),$day.AddDays(2).AddHours(12),$false,"",$null),@("d","Trip",$day.AddDays(7),$day.AddDays(9),$true,"",$null))){$list.Add([Activator]::CreateInstance($eventType,@($e[0],$e[1],[datetime]$e[2],[datetime]$e[3],[bool]$e[4],$e[5],$e[6])))}
   $feedType.GetProperty("Events").SetValue($feed,$list)
   $feedType.GetProperty("State").SetValue($feed,[Enum]::Parse($assembly.GetType("MochiDock.Services.CalendarFeedState"),"Updated"))
   $feedType.GetProperty("UpdatedAt").SetValue($feed,[Nullable[datetime]]$day.AddHours(10.2))
   $feedType.GetField("_url",$flags).SetValue($feed,"https://calendar.google.com/calendar/ical/x/private-y/basic.ics")
   $feedType.GetField("_gate",$flags).SetValue($feed,[object]::new())
   # Attach after the card has loaded: its own Loaded/Stop handlers reset the feed when no view model owns it.
   $renderToday={$type.GetField("_feed",$flags).SetValue($view,$feed);$type.GetMethod("Render",$flags).Invoke($view,@())}
  }
  if($name -eq "AiUsage"){
   # AiUsage draws its provider cards in code-behind: render the real control with fixture snapshots covering every state.
   $view=[Activator]::CreateInstance($assembly.GetType("MochiDock.Views.Cards.AiUsageCardView"));$view.SetResourceReference([Windows.Controls.Control]::FontFamilyProperty,"IslandUiFont")
   $winType=$assembly.GetType("MochiDock.Services.AiUsageWindow");$snapType=$assembly.GetType("MochiDock.Services.AiProviderSnapshot");$stateType=$assembly.GetType("MochiDock.Services.AiProviderState")
   $newWindows={param($items)$list=[Activator]::CreateInstance([Collections.Generic.List[object]].GetGenericTypeDefinition().MakeGenericType($winType));foreach($i in $items){$list.Add([Activator]::CreateInstance($winType,@([string]$i[0],[double]$i[1],[string]$i[2])))};,$list}
   $newSnap={param($id,$title,$plan,$account,$items,$status,$isError,$state,$hint)[Activator]::CreateInstance($snapType,@($id,$title,$plan,$account,(& $newWindows $items),$status,[bool]$isError,[Enum]::Parse($stateType,$state),$hint))}
   $snapshots=[Collections.Generic.List[object]]::new()
   $snapshots.Add((& $newSnap "claude" "Claude Code" "Max" "ab***@mail.com" @(@("5-hour",0.62,"Resets in 3h"),@("Weekly",0.18,"Resets in 4d")) "Active" $false "Ok" ""))
   $snapshots.Add((& $newSnap "cursor" "Cursor" "Pro" "" @() "Sign-in expired" $true "Expired" "Open Cursor to renew it."))
   $snapshots.Add((& $newSnap "gemini" "Gemini CLI" "" "" @() "Not available" $true "Disabled" "Google no longer serves plan usage for personal Gemini accounts; Workspace and enterprise accounts still do."))
   $snapshots.Add((& $newSnap "copilot" "GitHub Copilot" "Individual pro" "" @(@("Premium requests",0.88,"Resets in 20d")) "Offline · last reading" $true "Offline" ""))
   $renderAi={$method=$view.GetType().GetMethod("RenderSnapshots",[Reflection.BindingFlags]"NonPublic,Instance");$typed=[Activator]::CreateInstance([Collections.Generic.List[object]].GetGenericTypeDefinition().MakeGenericType($snapType));foreach($x in $snapshots){$typed.Add($x)};$method.Invoke($view,@(,$typed))}
  }
  $height=$heights[$name]-64;if(!$height){throw "Missing card height: $name"}
  $view.Width=592;$view.Height=$height
  $hostWindow=[Windows.Window]::new();$hostWindow.Content=$view;$assembly.GetType("MochiDock.Services.ThemeService").GetMethod("ApplyTheme").Invoke($null,@($hostWindow,$theme));$hostWindow.Content=$null
  $surface=[Windows.Controls.Border]::new();$surface.Child=$view;$surface.Width=592;$surface.Height=$height;$surface.Background=$application.Resources["IslandGroundBrush"];$surface.UseLayoutRounding=$true
  [Windows.Media.TextOptions]::SetTextRenderingMode($surface,[Windows.Media.TextRenderingMode]::Grayscale)
  [Windows.Media.TextOptions]::SetTextFormattingMode($surface,[Windows.Media.TextFormattingMode]::Ideal)
  [Windows.Media.TextOptions]::SetTextHintingMode($surface,[Windows.Media.TextHintingMode]::Fixed)
  [Windows.Media.VisualTreeHelper]::SetRootDpi($surface,[Windows.DpiScale]::new($scale,$scale))
  $nativeParameters=[Windows.Interop.HwndSourceParameters]::new("MochiDock isolated design fixture");$nativeParameters.WindowStyle=-2147483648;$nativeParameters.PositionX=-32000;$nativeParameters.PositionY=-32000;$nativeParameters.Width=1;$nativeParameters.Height=1
  $nativeSource=[Windows.Interop.HwndSource]::new($nativeParameters);$nativeSource.RootVisual=$surface
  [Windows.Threading.Dispatcher]::CurrentDispatcher.Invoke([Windows.Threading.DispatcherPriority]::Loaded,[Action]{})
  [Windows.Media.VisualTreeHelper]::SetRootDpi($surface,[Windows.DpiScale]::new($scale,$scale))
  if($name -eq "Today"){& $renderToday}
  if($name -eq "AiUsage"){& $renderAi}
  if($name -eq "Summary"){
   # Every row, the graph and the buttons must sit inside the card: nothing may be cut off, and the rows must stay left of the graph.
   $surface.Measure([Windows.Size]::new($surface.Width,$surface.Height));$surface.Arrange([Windows.Rect]::new(0,0,$surface.Width,$surface.Height));$surface.UpdateLayout()
   $stack=[Collections.Generic.Stack[Windows.DependencyObject]]::new();$stack.Push($surface);$graphLeft=[double]::MaxValue;$boxes=@()
   while($stack.Count -gt 0){$node=$stack.Pop()
    for($i=0;$i -lt [Windows.Media.VisualTreeHelper]::GetChildrenCount($node);$i++){$stack.Push([Windows.Media.VisualTreeHelper]::GetChild($node,$i))}
    if($node -is [Windows.FrameworkElement] -and $node.IsVisible -and $node.ActualWidth -gt 0){
     $box=$node.TransformToAncestor($surface).TransformBounds([Windows.Rect]::new(0,0,$node.ActualWidth,$node.ActualHeight))
     if($node -is [Windows.Controls.Canvas] -and $node.Name -eq "SessionGraph"){$graphLeft=$box.Left}
     if($node -is [Windows.Controls.TextBlock] -or $node -is [Windows.Controls.Button] -or ($node -is [Windows.Controls.Canvas] -and $node.Name -eq "SessionGraph")){$boxes+=[pscustomobject]@{Node=$node;Box=$box}}
    }}
   foreach($item in $boxes){
    $b=$item.Box
    if($b.Bottom -gt $surface.Height+0.5 -or $b.Top -lt -0.5 -or $b.Right -gt $surface.Width+0.5){throw "Summary content is cut off: $($item.Node.GetType().Name) $($item.Node.Name) $b, $theme, $scale"}
    if($item.Node -is [Windows.Controls.TextBlock] -and $item.Node.Name -eq "" -and $b.Top -gt 60 -and $b.Bottom -lt $surface.Height-50 -and $b.Left -lt $graphLeft-20 -and $b.Right -gt $graphLeft+0.5){throw "Summary text runs under the graph: $($item.Node.Text), $theme, $scale"}
   }
   $rows=@($boxes|Where-Object{$_.Node -is [Windows.Controls.TextBlock]-and $_.Box.Top -gt 56 -and $_.Box.Bottom -lt $surface.Height-48})
   if($rows.Count -lt 6){throw "Summary rows are missing ($($rows.Count) text blocks found), $theme, $scale"}
  }
  if($name -eq "WhatsApp" -or $name -eq "Discord"){
   # Message text must show its emoji as coloured pictures (WPF text draws them black and white): the Emoji.Text inlines hold Twemoji images (decoded at 32 px by EmojiImages).
   $surface.Measure([Windows.Size]::new($surface.Width,$surface.Height));$surface.Arrange([Windows.Rect]::new(0,0,$surface.Width,$surface.Height));$surface.UpdateLayout()
   $stack=[Collections.Generic.Stack[Windows.DependencyObject]]::new();$stack.Push($surface);$pictures=0
   while($stack.Count -gt 0){$node=$stack.Pop()
    for($k=0;$k -lt [Windows.Media.VisualTreeHelper]::GetChildrenCount($node);$k++){$stack.Push([Windows.Media.VisualTreeHelper]::GetChild($node,$k))}
    if($node -is [Windows.Controls.TextBlock]){foreach($kid in $node.Inlines){if($kid -is [Windows.Documents.InlineUIContainer]){$stack.Push($kid.Child)}}}
    if($node -is [Windows.Controls.Image] -and $node.Source -is [Windows.Media.Imaging.BitmapSource] -and $node.Source.PixelWidth -eq 32 -and $node.Margin.Left -eq 0.5){$pictures++}
   }
   if($pictures -lt 5){throw "Colour emoji are not drawn as pictures in the $name card ($pictures found), $theme, $scale"}
  }
  $surface.Measure([Windows.Size]::new(592,$height));$surface.Arrange([Windows.Rect]::new(0,0,592,$height));$surface.UpdateLayout()
  $visuals=[Collections.Generic.List[Windows.Media.Visual]]::new();Add-Visuals $view $visuals
  foreach($visual in $visuals){
   if($visual.GetType().Name -eq "MarqueeTextBlock"){$visual.UpdateMarquee($false)}
  }
  $surface.UpdateLayout()
  if([Math]::Abs([Windows.Media.VisualTreeHelper]::GetDpi($view).DpiScaleX-$scale) -gt 0.001){throw "Unexpected effective visual DPI for $name"}
  foreach($visual in $visuals){
   if($visual -is [Windows.Controls.TextBlock] -and $visual.Text){
    if([Windows.Media.TextOptions]::GetTextRenderingMode($visual) -ne [Windows.Media.TextRenderingMode]::Grayscale){throw "Unexpected rendering in $name"}
    $inMarquee=$false;for($p=[Windows.Media.VisualTreeHelper]::GetParent($visual);$null -ne $p;$p=[Windows.Media.VisualTreeHelper]::GetParent($p)){if($p.GetType().Name -eq "MarqueeTextBlock"){$inMarquee=$true;break}}
    $expectedMode=if($inMarquee){[Windows.Media.TextFormattingMode]::Ideal}else{[Windows.Media.TextFormattingMode]::Ideal}
    if([Windows.Media.TextOptions]::GetTextFormattingMode($visual) -ne $expectedMode){throw "Unexpected text formatting in $name"}
   }
   Read-Glyphs ([Windows.Media.VisualTreeHelper]::GetDrawing($visual)) $name
  }
  if($name -eq "Hardware"){
   # The CPU page's three columns end at the page's bottom edge (FillLastPanel), and all ten processes fit above it.
   $page=@($view.FindName("HardwareModuleStrip").Parent.Children | Where-Object {$_ -isnot [Windows.Controls.Border] -and $_.GetType().Name -eq "TabTransitionHost"})[0]
   $cpu=@($page.Children | Where-Object {$_.Tag -eq "CPU"})[0]
   $columns=@($cpu.Children | Where-Object {$_.GetType().Name -eq "FillLastPanel"})
   if($columns.Count -ne 3){throw "The CPU page should have three filling columns, found $($columns.Count)"}
   foreach($column in $columns){
    $lastChild=@($column.Children | Where-Object {$_.Visibility -ne [Windows.Visibility]::Collapsed})[-1]
    $bottom=$lastChild.TransformToAncestor($cpu).Transform([Windows.Point]::new(0,$lastChild.ActualHeight)).Y
    if([Math]::Abs($bottom-$cpu.ActualHeight) -gt 1){throw "A CPU column ends at $bottom, not at the page bottom $($cpu.ActualHeight), $theme, $scale"}
   }
   $rows=@($visuals | Where-Object {$_ -is [Windows.Controls.ContentPresenter] -and $_.Content -and $_.Content.GetType().Name -eq "ProcessMetricItem" -and $_.IsVisible})
   foreach($row in $rows){$rowBottom=$row.TransformToAncestor($cpu).Transform([Windows.Point]::new(0,$row.ActualHeight)).Y;if($rowBottom -gt $cpu.ActualHeight+0.5){throw "Process row $($row.Content.Name) is cut off at $rowBottom, $theme, $scale"}}
   if($rows.Count -lt 10){throw "Expected ten process rows on the CPU page, found $($rows.Count)"}
   # 24 h view: the lines stay inside the chart, the captions are not clipped and the span switch is drawn.
   $paths=@($visuals | Where-Object {$_ -is [Windows.Shapes.Path] -and $_.IsVisible -and $_.Data -and $_.Data.Bounds.Width -gt 100})
   if($paths.Count -lt 2){throw "The 24 h lines are missing ($($paths.Count)), $theme, $scale"}
   foreach($line in $paths){$chart=[Windows.Media.VisualTreeHelper]::GetParent($line);$b=$line.Data.Bounds;if($b.Right -gt $chart.ActualWidth+30 -or $b.Bottom -gt $chart.ActualHeight+0.5 -or $b.Top -lt -0.5){throw "A 24 h line leaves its chart: $b in $($chart.ActualWidth)x$($chart.ActualHeight)"}}
   foreach($caption in @($visuals | Where-Object {$_ -is [Windows.Controls.TextBlock] -and $_.IsVisible -and $_.Text -in @("24 h ago","now","Peak 86 °C at 21:40","24 H")})){if($caption.DesiredSize.Width -gt $caption.ActualWidth+0.5 -or $caption.DesiredSize.Height -gt $caption.ActualHeight+0.5){throw "Caption clipped: $($caption.Text), $theme, $scale"}}
   if(@($visuals | Where-Object {$_ -is [Windows.Controls.TextBlock] -and $_.IsVisible -and $_.Text -eq "24 H"}).Count -lt 1){throw "The 2 min / 24 h switch is missing"}
   $strip=$view.FindName("HardwareModuleStrip")
   $chips=@($strip.Children | Where-Object {$_ -is [Windows.Controls.Border] -and [Windows.Controls.Grid]::GetColumn($_) -lt 12}) # column 12 is the optional Battery chip
   if($chips.Count -ne 6){throw "Expected six hardware chips"}
   foreach($chip in $chips){
    $grid=$chip.Child
    $texts=@($grid.Children | Where-Object {$_ -is [Windows.Controls.TextBlock]})
    $graph=@($grid.Children | Where-Object {[Windows.Controls.Grid]::GetRow($_) -eq 2})[0]
    $graphTop=$graph.TransformToAncestor($chip).Transform([Windows.Point]::new(0,0)).Y
    foreach($text in $texts){
     $top=$text.TransformToAncestor($chip).Transform([Windows.Point]::new(0,0)).Y
     if($top -lt -0.5 -or $top+$text.ActualHeight -gt $chip.ActualHeight+0.5 -or $text.DesiredSize.Height -gt $text.ActualHeight+0.5 -or $top+$text.ActualHeight -gt $graphTop+0.5){throw "Chip text clips or overlaps graph: $($text.Text), $theme, $scale"}
     if($null -eq $text.ToolTip){throw "Missing full chip-text tooltip"}
    }
   }
  }
  $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new([int](592*$scale),[int]($height*$scale),96*$scale,96*$scale,[Windows.Media.PixelFormats]::Pbgra32);$bitmap.Render($surface)
  $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap));$stream=[IO.File]::Create((Join-Path $output "$name-$theme-$scale.png"));try{$encoder.Save($stream)}finally{$stream.Dispose();$nativeSource.Dispose()}
 }}
 Write-Output "Rendered actual $name layout in both themes at four native visual DPIs."
}

# Menu templates are rendered as inert menu rows; never open a live tray menu or invoke actions.
foreach($key in @("ModernTrayMenuItemStyle","ModernTraySeparatorStyle")){
 $node=$window.SelectSingleNode('//*[local-name()="Style" and @*[local-name()="Key"]="'+$key+'"]')
 $application.Resources[$key]=[Windows.Markup.XamlReader]::Parse($node.OuterXml,$context)
}
# Retain a real menu parent/child and tooltip across palette changes. Render the
# submenu child as an inert row so glyph resolution is checked without a popup.
foreach($scale in @(1,1.25,1.5,2)){
 $parent=[Windows.Controls.MenuItem]::new();$parent.Header='Music Controls';$parent.Style=$application.Resources['ModernTrayMenuItemStyle']
 $child=[Windows.Controls.MenuItem]::new();$child.Header='Play / Pause';$child.Style=$application.Resources['ModernTrayMenuItemStyle'];$parent.Items.Add($child)|Out-Null
 $tip=[Windows.Controls.ToolTip]::new();$tip.Content='Show Dock';$tip.Style=$application.Resources[[Windows.Controls.ToolTip]]
 $rows=[Windows.Controls.StackPanel]::new();$rows.Children.Add($parent)|Out-Null
 $surface=[Windows.Controls.Border]::new();$surface.Child=$rows;$surface.Width=300;$surface.Height=130
 $surface.SetResourceReference([Windows.Controls.Border]::BackgroundProperty,'IslandSurfaceBrush')
 [Windows.Media.VisualTreeHelper]::SetRootDpi($surface,[Windows.DpiScale]::new($scale,$scale))
 foreach($theme in @('Dark','Matcha','Dark')){
  $script:fixtureTheme=$theme;$script:fixtureScale=$scale
  $hostWindow=[Windows.Window]::new();$assembly.GetType('MochiDock.Services.ThemeService').GetMethod('ApplyTheme').Invoke($null,@($hostWindow,$theme))
  [Windows.Threading.Dispatcher]::CurrentDispatcher.Invoke([Windows.Threading.DispatcherPriority]::Render,[Action]{})
  foreach($control in @($parent,$child,$tip)){if(!$control.FontFamily.Source.Contains('Quicksand')){throw 'Retained tray/submenu/tooltip font mismatch'}}
  $surface.Background=$application.Resources['IslandSurfaceBrush'];$surface.InvalidateVisual()
  $tip.Background=$application.Resources['IslandSurfaceBrush'];$tip.InvalidateVisual()
  $tip.Width=280;$tip.Height=40;$tip.ApplyTemplate()|Out-Null
  [Windows.Media.VisualTreeHelper]::SetRootDpi($tip,[Windows.DpiScale]::new($scale,$scale));$tip.Measure([Windows.Size]::new(280,40));$tip.Arrange([Windows.Rect]::new(0,0,280,40));$tip.UpdateLayout()
  $tipVisuals=[Collections.Generic.List[Windows.Media.Visual]]::new();Add-Visuals $tip $tipVisuals
  foreach($visual in $tipVisuals){Read-Glyphs ([Windows.Media.VisualTreeHelper]::GetDrawing($visual)) 'Retained tooltip'}
  $tipBitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new([int](280*$scale),[int](40*$scale),96*$scale,96*$scale,[Windows.Media.PixelFormats]::Pbgra32);$tipBitmap.Render($tip)
  $tipEncoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$tipEncoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($tipBitmap));$tipStream=[IO.File]::Create((Join-Path $output "Tooltip-$theme-$scale.png"));try{$tipEncoder.Save($tipStream)}finally{$tipStream.Dispose()}
  $parent.Items.Remove($child);$rows.Children.Insert(1,$child)
  $surface.Measure([Windows.Size]::new(300,130));$surface.Arrange([Windows.Rect]::new(0,0,300,130));$surface.UpdateLayout()
  $visuals=[Collections.Generic.List[Windows.Media.Visual]]::new();Add-Visuals $surface $visuals
  foreach($visual in $visuals){Read-Glyphs ([Windows.Media.VisualTreeHelper]::GetDrawing($visual)) 'Retained tray/submenu/tooltip'}
  $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new([int](300*$scale),[int](130*$scale),96*$scale,96*$scale,[Windows.Media.PixelFormats]::Pbgra32);$bitmap.Render($surface)
  $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap));$stream=[IO.File]::Create((Join-Path $output "Menu-retained-$theme-$scale.png"));try{$encoder.Save($stream)}finally{$stream.Dispose()}
  $rows.Children.Remove($child);$parent.Items.Add($child)|Out-Null
 }

}
foreach($theme in @("Dark","Matcha")){foreach($scale in @(1,1.25,1.5,2)){
 $script:fixtureTheme=$theme;$script:fixtureScale=$scale
 $hostWindow=[Windows.Window]::new();$assembly.GetType("MochiDock.Services.ThemeService").GetMethod("ApplyTheme").Invoke($null,@($hostWindow,$theme))
 $rows=[Windows.Controls.StackPanel]::new()
 foreach($label in @("Show Dock","Music Controls","Select Monitor","System Performance","Start with Windows","Exit")){
  $item=[Windows.Controls.MenuItem]::new();$item.Header=$label;$item.Style=$application.Resources["ModernTrayMenuItemStyle"];$rows.Children.Add($item)|Out-Null
 }
 $surface=[Windows.Controls.Border]::new();$surface.Child=$rows;$surface.Width=300;$surface.Height=208;$surface.Padding=[Windows.Thickness]::new(4);$surface.CornerRadius=[Windows.CornerRadius]::new(12);$surface.UseLayoutRounding=$true;$surface.Background=$application.Resources["IslandSurfaceBrush"]
 [Windows.Media.VisualTreeHelper]::SetRootDpi($surface,[Windows.DpiScale]::new($scale,$scale));$surface.Measure([Windows.Size]::new(300,208));$surface.Arrange([Windows.Rect]::new(0,0,300,208));$surface.UpdateLayout()
 $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new([int](300*$scale),[int](208*$scale),96*$scale,96*$scale,[Windows.Media.PixelFormats]::Pbgra32);$bitmap.Render($surface)
 $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap));$stream=[IO.File]::Create((Join-Path $output "Menu-$theme-$scale.png"));try{$encoder.Save($stream)}finally{$stream.Dispose()}
 $visuals=[Collections.Generic.List[Windows.Media.Visual]]::new();Add-Visuals $surface $visuals
 foreach($visual in $visuals){Read-Glyphs ([Windows.Media.VisualTreeHelper]::GetDrawing($visual)) "Tray menu"}
}}
Write-Output "Rendered shared menu rows in both themes at four DPIs."

# Inspect the pixels of text alone, independently of icon/shape antialiasing.
foreach($scale in @(1,1.25,1.5,2)){
 $text=[Windows.Controls.TextBlock]::new();$text.Text="a 100% CPU";$text.FontFamily=$family;$text.FontSize=12;$text.Foreground=[Windows.Media.Brushes]::White
 $surface=[Windows.Controls.Border]::new();$surface.Child=$text;$surface.Width=200;$surface.Height=24;$surface.Background=[Windows.Media.Brushes]::Black
 [Windows.Media.TextOptions]::SetTextRenderingMode($surface,[Windows.Media.TextRenderingMode]::Grayscale);[Windows.Media.TextOptions]::SetTextFormattingMode($surface,[Windows.Media.TextFormattingMode]::Ideal)
 [Windows.Media.VisualTreeHelper]::SetRootDpi($surface,[Windows.DpiScale]::new($scale,$scale));$surface.Measure([Windows.Size]::new(200,24));$surface.Arrange([Windows.Rect]::new(0,0,200,24));$surface.UpdateLayout()
 $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new([int](200*$scale),[int](24*$scale),96*$scale,96*$scale,[Windows.Media.PixelFormats]::Pbgra32);$bitmap.Render($surface)
 $stride=$bitmap.PixelWidth*4;$pixels=[byte[]]::new($stride*$bitmap.PixelHeight);$bitmap.CopyPixels($pixels,$stride,0);$edges=0
 for($pixel=0;$pixel -lt $pixels.Length;$pixel+=4){if($pixels[$pixel] -gt 0 -and $pixels[$pixel] -lt 255 -and $pixels[$pixel] -eq $pixels[$pixel+1] -and $pixels[$pixel] -eq $pixels[$pixel+2]){$edges++}}
 if($edges -lt 30){throw "Text antialiasing not demonstrated at scale $scale"}
 $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap));$stream=[IO.File]::Create((Join-Path $output "Text-edge-$scale.png"));try{$encoder.Save($stream)}finally{$stream.Dispose()}
 Write-Output "Text-only grayscale edge pixels at scale $scale : $edges"
}
$glyphEvidence | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $output "glyph-faces.json")
Write-Output "Actual glyph faces:"
$glyphEvidence.Font | Sort-Object -Unique | Write-Output
# Exercise the production host with retained controls and rapid retargeting.
foreach($theme in @("Dark","Matcha")){foreach($scale in @(1,1.25,1.5,2)){
 $themeWindow=[Windows.Window]::new();$assembly.GetType("MochiDock.Services.ThemeService").GetMethod("ApplyTheme").Invoke($null,@($themeWindow,$theme))
 $transition=[Activator]::CreateInstance($assembly.GetType("MochiDock.Controls.TabTransitionHost"));$transition.Width=592;$transition.Height=260
 foreach($key in @("CPU","GPU","Memory","Drives","Network","Games","Battery")){
  $page=[Windows.Controls.TextBlock]::new();$page.Tag=$key;$page.Text="$key · retained transition fixture";$page.FontSize=18;$page.SetResourceReference([Windows.Controls.TextBlock]::FontFamilyProperty,"IslandUiFont");$page.SetResourceReference([Windows.Controls.TextBlock]::ForegroundProperty,"IslandInkBrush");$transition.Children.Add($page)|Out-Null
 }
 $transition.SelectedKey="CPU"
 $nativeParameters=[Windows.Interop.HwndSourceParameters]::new("Isolated tab transitions");$nativeParameters.WindowStyle=-2147483648;$nativeParameters.PositionX=-32000;$nativeParameters.PositionY=-32000;$nativeParameters.Width=1;$nativeParameters.Height=1
 $native=[Windows.Interop.HwndSource]::new($nativeParameters);$native.RootVisual=$transition
 [Windows.Media.VisualTreeHelper]::SetRootDpi($transition,[Windows.DpiScale]::new($scale,$scale))
 [Windows.Threading.Dispatcher]::CurrentDispatcher.Invoke([Windows.Threading.DispatcherPriority]::Loaded,[Action]{})
 $transition.Measure([Windows.Size]::new(592,260));$transition.Arrange([Windows.Rect]::new(0,0,592,260));$transition.UpdateLayout()
 foreach($key in @("GPU","Memory","Drives","Network","Games","Battery","CPU")){
  $transition.SelectedKey=$key
  if(@($transition.Children|Where-Object{$_.Visibility -eq "Visible"}).Count -gt 2){throw "Unbounded transition pages"}
  if(@($transition.Children|Where-Object{$_.IsHitTestVisible}).Count -ne 1){throw "Outgoing page accepts input"}
 }
 $frame=[Windows.Threading.DispatcherFrame]::new();$timer=[Windows.Threading.DispatcherTimer]::new();$timer.Interval=[TimeSpan]::FromMilliseconds(70);$timer.Tag=$frame;$timer.Add_Tick({param($sender,$eventArgs)$sender.Stop();$sender.Tag.Continue=$false});$timer.Start();[Windows.Threading.Dispatcher]::PushFrame($frame)
 $bitmap=[Windows.Media.Imaging.RenderTargetBitmap]::new([int](592*$scale),[int](260*$scale),96*$scale,96*$scale,[Windows.Media.PixelFormats]::Pbgra32);$bitmap.Render($transition)
 $encoder=[Windows.Media.Imaging.PngBitmapEncoder]::new();$encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap));$stream=[IO.File]::Create((Join-Path $output "Transition-middle-$theme-$scale.png"));try{$encoder.Save($stream)}finally{$stream.Dispose()}
 $transition.Settle();$transition.UpdateLayout()
 if(@($transition.Children|Where-Object{$_.Visibility -eq "Visible"}).Count -ne 1){throw "Transition cleanup failed"}
 if($transition.Children[0].Opacity -ne 1){throw "Settled page opacity incorrect"}
 $native.Dispose()
}}
Write-Output "Verified retained tab transitions, interruption, input ownership and cleanup at four DPIs."
& (Join-Path $PSScriptRoot 'verify-header-animation.ps1') -Configuration $Configuration
$application.Shutdown()
