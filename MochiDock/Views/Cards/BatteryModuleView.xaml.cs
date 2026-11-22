using System.Windows.Controls;
using System.Windows.Media;
using Brush = System.Windows.Media.Brush;
using FontFamily = System.Windows.Media.FontFamily;
using Orientation = System.Windows.Controls.Orientation;
using System.Windows;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using Microsoft.Win32;
namespace MochiDock.Views.Cards;
public partial class BatteryModuleView : System.Windows.Controls.UserControl {
 MochiDock.Services.PowerNotifications? notifications;
 readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromSeconds(30)};
 // Total power and its history come from the view model, which samples it with the hardware readings whether or not this page is
 // open; the page only draws them (the chart is already filled when the page opens and survives tab switches).
 MochiDock.Core.PowerReading? power;
 bool powerLaptop;
 TextBlock? wattsText,averageText,sessionText,legendText;
 PowerChartView? chart;
 void PowerSampled()
 {
  if(vm==null||!IsVisible)return;
  power=vm.LatestPower;
  ApplyPower();
  ApplySession();
 }
 /// <summary>"This session 85 Wh" from the power log, with this and recent sessions in the tooltip; hidden while logging is off.</summary>
 void ApplySession()
 {
  if(sessionText==null)return;
  var log=vm?.PowerLog;
  if(log==null||vm==null||!vm.EnablePowerLogging){sessionText.Visibility=Visibility.Collapsed;return;}
  sessionText.Visibility=Visibility.Visible;
  var cur=log.Current;
  var prefix=cur?.Source=="Measured"?"":"~";
  sessionText.Text=cur==null?"This session: collecting…":"This session "+prefix+MochiDock.Core.PowerSessionFormat.Energy(cur.EnergyWh);
  var history=log.History;
  var lines=history.Take(5).Select(r=>MochiDock.Core.PowerSessionFormat.Summary(r,DateTime.Now)+(r.EndedUnexpectedly?" (ended unexpectedly)":""));
  var current=cur==null?"":$"This session: avg {prefix}{MochiDock.Core.PowerSessionFormat.Watts(cur.AverageW)}, peak {MochiDock.Core.PowerSessionFormat.Watts(cur.PeakW)}, sampled {MochiDock.Core.GameSessionFormat.Duration(cur.SampledSeconds)} since {cur.StartUtc.ToLocalTime():HH:mm}\n";
  sessionText.ToolTip=current+(history.Count>0?"Recent sessions:\n"+string.Join("\n",lines):"Power is logged about every 10 seconds; the total is an estimate unless it was read from the laptop battery.");
 }
 // Updates the panel's numbers, legend and chart in place (no rebuild on every sample).
 void ApplyPower()
 {
  if(wattsText==null)return;
  var estimated=power?.Source==MochiDock.Core.PowerSource.Estimated;
  if(power==null){wattsText.Text="-";wattsText.ToolTip=powerLaptop?"Windows reports no battery rate right now.":"No CPU or GPU power reading is available. Windows' Energy Meter is tried first; the optional PawnIO driver adds more sensors (Settings > Hardware & power > CPU sensors), and GPU power is read on NVIDIA cards.";}
  else{wattsText.Text=(estimated?"~":"")+power.Watts.ToString(power.Watts<100?"0.0":"0")+" W";wattsText.ToolTip=(estimated?"Estimate: ":"Measured: ")+power.Detail;}
  var points=vm?.PowerPoints??Array.Empty<MochiDock.Core.PowerSample>();
  chart?.Show(points,vm?.PowerSampleCapacity??60);
  if(averageText!=null)
  {
   var g=chart?.Geometry;
   averageText.Text=g==null?"":$"avg {(estimated?"~":"")}{g.Average:0} · peak {g.Peak:0} W";
   averageText.ToolTip=g==null?null:"Over the last "+points.Count+" readings (about 1-2 minutes). The dashed line is the average, the dot the latest peak.";
  }
  if(legendText!=null)
  {
   // Where the latest total goes: the measured CPU and GPU, and the rest (memory when exposed plus the assumed allowance).
   legendText.Inlines.Clear();
   if(points.Count>0&&(points[^1].CpuW!=null||points[^1].GpuW!=null))
   {
    var last=points[^1];
    double cpuW=Math.Clamp(last.CpuW??0,0,last.Total),gpuW=Math.Clamp(last.GpuW??0,0,last.Total-cpuW);
    Legend("IslandGreenBrush","CPU",cpuW);Legend("IslandBlueBrush","GPU",gpuW);Legend("IslandMutedInkBrush","Other",last.Total-cpuW-gpuW);
   }
   else legendText.Inlines.Add(new System.Windows.Documents.Run(power?.Source==MochiDock.Core.PowerSource.Measured?"Measured battery discharge rate":powerLaptop?"Battery rate not reported right now":"Waiting for CPU and GPU power readings"));
  }
 }
 void Legend(string brush,string name,double watts)
 {
  var dot=new System.Windows.Documents.Run("● ");dot.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty,brush);
  legendText!.Inlines.Add(dot);legendText.Inlines.Add(new System.Windows.Documents.Run($"{name} {watts:0} W   "));
 }
 /// <summary>
 /// One full-width "This PC" panel in place of the old half-width power and session cards (the same height, so the device cards
 /// below do not move): state on the left, the Power chart in the middle, the numbers on the right.
 /// </summary>
 Border BuildPowerPanel(string badge,string badgeBrush,UIElement gauge,string percent,string caption)
 {
  var panel=new Border{BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(10),Padding=new Thickness(10,6,10,6),Margin=new Thickness(0,0,8,8),Height=_cardHeight>0?_cardHeight:double.NaN};
  panel.SetResourceReference(Border.BackgroundProperty,"IslandSubCardBackgroundBrush");panel.SetResourceReference(Border.BorderBrushProperty,"IslandSubCardBorderBrush");
  var grid=new Grid();
  grid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto,MinWidth=150});grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(12)});
  grid.ColumnDefinitions.Add(new ColumnDefinition());grid.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(12)});
  grid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto,MinWidth=96});

  // Left: name and state, gauge (and the percentage on a laptop), caption.
  var left=new StackPanel{MaxWidth=170};
  var top=new StackPanel{Orientation=Orientation.Horizontal};
  var glyph=Text("",14,"IslandAccentBrush");glyph.FontFamily=new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");glyph.Margin=new Thickness(0,0,6,0);top.Children.Add(glyph);
  top.Children.Add(Text("This PC",13,"IslandInkBrush",true));
  var chip=new Border{CornerRadius=new CornerRadius(4),Padding=new Thickness(5,1,5,1),Margin=new Thickness(8,0,0,0),VerticalAlignment=VerticalAlignment.Center};
  chip.SetResourceReference(Border.BackgroundProperty,"IslandChipActiveBrush");chip.Child=Text(badge,11,badgeBrush,true);top.Children.Add(chip);
  left.Children.Add(top);
  var mid=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,6,0,0)};
  mid.Children.Add(gauge);
  if(percent.Length>0){var pct=Text(percent,22,"IslandInkBrush",true);pct.Margin=new Thickness(10,0,0,0);mid.Children.Add(pct);}
  left.Children.Add(mid);
  var cap=Text(caption,11,"IslandMutedInkBrush");cap.Margin=new Thickness(0,4,0,0);cap.ToolTip=caption;left.Children.Add(cap);
  grid.Children.Add(left);

  // Middle: the chart, with where the power goes underneath.
  var middle=new Grid();middle.RowDefinitions.Add(new RowDefinition());middle.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
  chart=new PowerChartView{MinHeight=30};
  var frame=new Border{CornerRadius=new CornerRadius(6),BorderThickness=new Thickness(1),Child=chart,ClipToBounds=true};
  frame.SetResourceReference(Border.BorderBrushProperty,"IslandSubCardBorderBrush");
  middle.Children.Add(frame);
  legendText=Text("",10,"IslandSecondaryInkBrush");legendText.Margin=new Thickness(0,3,0,0);Grid.SetRow(legendText,1);middle.Children.Add(legendText);
  Grid.SetColumn(middle,2);grid.Children.Add(middle);

  // Right: the latest total, the recent average and peak, and this session's energy when logging is on.
  var right=new StackPanel{VerticalAlignment=VerticalAlignment.Center};
  wattsText=Text("-",18,"IslandInkBrush",true,TextAlignment.Right);
  averageText=Text("",11,"IslandSecondaryInkBrush",false,TextAlignment.Right);averageText.Margin=new Thickness(0,2,0,0);
  sessionText=Text("",11,"IslandMutedInkBrush",false,TextAlignment.Right);sessionText.Margin=new Thickness(0,2,0,0);
  foreach(var t in new[]{wattsText,averageText,sessionText}){System.Windows.Documents.Typography.SetNumeralAlignment(t,FontNumeralAlignment.Tabular);right.Children.Add(t);}
  Grid.SetColumn(right,4);grid.Children.Add(right);

  panel.Child=grid;
  ApplyPower();
  ApplySession();
  return panel;
 }
 [StructLayout(LayoutKind.Sequential)] struct Power {public byte AC,Flag,Percent,Saver;public uint Life,Full;}
 [DllImport("kernel32.dll")] static extern bool GetSystemPowerStatus(out Power value);
 public BatteryModuleView(){InitializeComponent();DataContextChanged+=(_,_)=>{Attach();if(IsVisible)Rebuild();};Loaded+=Start;Unloaded+=Stop;IsVisibleChanged+=(_,_)=>{if(IsVisible)Start(this,new RoutedEventArgs());else Stop(this,new RoutedEventArgs());};timer.Tick+=(_,_)=>Refresh();MochiDock.Services.ThemeService.RedrawOnThemeChange(this,Rebuild);}
 void Start(object s,RoutedEventArgs e){Stop(s,e);if(!IsVisible)return;SystemEvents.PowerModeChanged+=Changed;var window=Window.GetWindow(this);if(window!=null){notifications=new(new System.Windows.Interop.WindowInteropHelper(window).Handle);notifications.PowerChanged+=Notify;}if(IsVisible){Refresh();timer.Start();if(vm!=null){vm.PowerSampled-=PowerSampled;vm.PowerSampled+=PowerSampled;}PowerSampled();}}
 void Stop(object s,RoutedEventArgs e){timer.Stop();if(vm!=null)vm.PowerSampled-=PowerSampled;SystemEvents.PowerModeChanged-=Changed;if(notifications!=null){notifications.PowerChanged-=Notify;notifications.Dispose();notifications=null;}}
 void Changed(object s,PowerModeChangedEventArgs e){Dispatcher.BeginInvoke(()=>{if(IsVisible)Refresh();});}
 void Notify(){if(IsVisible)Refresh();}
 async void RefreshButton_Click(object s,RoutedEventArgs e)
 {
  // Re-read this PC's power state and the total power now, and rescan the wireless devices; the shared helper shows the spinner and "Updated HH:mm".
  await MochiDock.Controls.RefreshFeedback.RunAsync(RefreshButton,async()=>{Refresh();vm?.SamplePowerTick(true);if(vm!=null)await vm.RefreshDeviceBatteries();});
 }

 MochiDock.ViewModels.MainViewModel? vm;
 System.Collections.Specialized.INotifyCollectionChanged? watched;
 Power last;
 bool haveStatus;
 void Refresh(){haveStatus=GetSystemPowerStatus(out last);Rebuild();}
 void Attach(){Detach();vm=DataContext as MochiDock.ViewModels.MainViewModel;if(vm!=null){vm.DeviceBatterySettingsChanged+=SettingsChanged;if(IsVisible)vm.PowerSampled+=PowerSampled;}if(vm?.DeviceBatteries is {} devices){watched=devices;devices.CollectionChanged+=DevicesChanged;}}
 void SettingsChanged(){Dispatcher.BeginInvoke(()=>{if(IsVisible)Rebuild();});}
 void Detach(){if(vm!=null){vm.DeviceBatterySettingsChanged-=SettingsChanged;vm.PowerSampled-=PowerSampled;}if(watched!=null)watched.CollectionChanged-=DevicesChanged;watched=null;}
 void DevicesChanged(object? s,System.Collections.Specialized.NotifyCollectionChangedEventArgs e){Dispatcher.BeginInvoke(()=>{if(IsVisible)Rebuild();});}

 Brush Res(string key)=>(Brush)FindResource(key);
 TextBlock Text(string text,double size,string brush,bool strong=false,TextAlignment align=TextAlignment.Left){var t=new TextBlock{Text=text,FontSize=size,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center,TextAlignment=align};t.SetResourceReference(TextBlock.ForegroundProperty,brush);if(strong)t.SetResourceReference(TextBlock.FontWeightProperty,"IslandSmallLabelWeight");return t;}
 // Card height: every card is half of the page's height, so up to four cards (two rows) fill the dock exactly, one or two cards use
 // only the top half, and from five cards on the page scrolls. The 8-DIP bottom margin is part of each row.
 const double MinCardHeight=84,RowGap=8;
 double _cardHeight;
 void CardsScroll_SizeChanged(object s,SizeChangedEventArgs e)
 {
  var height=Math.Max(MinCardHeight,Math.Floor(CardsScroll.ActualHeight/2)-RowGap);
  if(Math.Abs(height-_cardHeight)<0.5)return;
  _cardHeight=height;
  foreach(var child in CardsPanel.Children)if(child is Border card)card.Height=height;
  if(PowerPanelHost.Child is Border panel)panel.Height=height;
 }
 Border Card(string title,string? glyph,string badge,string badgeBrush,UIElement gauge,string percent,string caption,string? deviceKey=null,UIElement? extra=null)
 {
  var card=new Border{BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(10),Padding=new Thickness(10,6,10,6),Margin=new Thickness(0,0,8,8),Height=_cardHeight>0?_cardHeight:double.NaN};
  card.SetResourceReference(Border.BackgroundProperty,"IslandSubCardBackgroundBrush");card.SetResourceReference(Border.BorderBrushProperty,"IslandSubCardBorderBrush");
  var stack=new StackPanel();
  var top=new Grid();top.ColumnDefinitions.Add(new ColumnDefinition());top.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});top.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});
  // A Grid (icon: Auto, name: star) instead of a horizontal StackPanel: a StackPanel measures its children without a width limit, so a long
  // device name ("G903 LIGHTSPEED Wireless Gaming Mouse") would run under the badge instead of being trimmed with an ellipsis.
  var name=new Grid{Margin=new Thickness(0,0,6,0)};name.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});name.ColumnDefinitions.Add(new ColumnDefinition());
  if(glyph!=null){var g=Text(glyph,14,"IslandAccentBrush");g.FontFamily=new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");g.Margin=new Thickness(0,0,6,0);name.Children.Add(g);}
  var titleBlock=Text(title,13,"IslandInkBrush",true);titleBlock.ToolTip=title;Grid.SetColumn(titleBlock,1);name.Children.Add(titleBlock);
  top.Children.Add(name);
  var chip=new Border{CornerRadius=new CornerRadius(4),Padding=new Thickness(5,1,5,1),VerticalAlignment=VerticalAlignment.Center};
  chip.SetResourceReference(Border.BackgroundProperty,"IslandChipActiveBrush");chip.Child=Text(badge,11,badgeBrush,true);
  Grid.SetColumn(chip,1);top.Children.Add(chip);
  if(deviceKey!=null&&vm!=null)
  {
   // Per-device hide: removes it from the taskbar readout and the low-battery notice; it stays on this page.
   var hidden=vm.IsDeviceBatteryHidden(deviceKey);
   var eye=Text(hidden?"":"",13,hidden?"IslandMutedInkBrush":"IslandSecondaryInkBrush");
   eye.FontFamily=new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");
   var hit=new Border{Background=System.Windows.Media.Brushes.Transparent,Padding=new Thickness(6,0,0,0),Cursor=System.Windows.Input.Cursors.Hand,Child=eye,ToolTip=hidden?"Hidden from the taskbar. Click to show it again.":"Shown on the taskbar. Click to hide it there."};
   hit.MouseLeftButtonUp+=(_,e)=>{vm.SetDeviceBatteryHidden(deviceKey,!hidden);e.Handled=true;};
   Grid.SetColumn(hit,2);top.Children.Add(hit);
   if(hidden)card.Opacity=0.65;
  }
  stack.Children.Add(top);
  var mid=new Grid{Margin=new Thickness(0,6,0,0)};mid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});mid.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});mid.ColumnDefinitions.Add(new ColumnDefinition());
  mid.Children.Add(gauge);
  var pct=Text(percent,22,"IslandInkBrush",true);pct.Margin=new Thickness(10,0,0,0);Grid.SetColumn(pct,1);mid.Children.Add(pct);
  if(extra!=null){Grid.SetColumn(extra,2);mid.Children.Add(extra);}
  stack.Children.Add(mid);
  var cap=Text(caption,11,"IslandMutedInkBrush");cap.Margin=new Thickness(0,4,0,0);cap.ToolTip=caption;stack.Children.Add(cap);
  card.Child=stack;return card;
 }
 void Rebuild()
 {
  CardsPanel.Children.Clear();
  var laptop=haveStatus&&(last.Flag&128)==0&&last.Flag!=255&&last.Percent<=100;
  powerLaptop=laptop;
  if(laptop)
  {
   var charging=(last.Flag&8)!=0;var onAc=last.AC==1;
   var state=charging?"Charging":onAc?"On AC power":"On battery";
   string caption=onAc||charging?"Plugged in":(last.Life!=0xFFFFFFFF?$"{(int)TimeSpan.FromSeconds(last.Life).TotalHours}h {TimeSpan.FromSeconds(last.Life).Minutes:00}m left":"Running on battery");
   if(last.Saver==1)caption+=" · Battery saver on";
   PowerPanelHost.Child=BuildPowerPanel(state,charging?"IslandPositiveBrush":"IslandMutedInkBrush",new BatteryGauge{Percent=last.Percent,Charging=charging,HorizontalAlignment=System.Windows.HorizontalAlignment.Left},last.Percent+"%",caption);
  }
  else
   PowerPanelHost.Child=BuildPowerPanel("Wall power","IslandPositiveBrush",new BatteryGauge{IsPsu=true,HorizontalAlignment=System.Windows.HorizontalAlignment.Left},"","Estimated from CPU + GPU");
  if(vm?.DeviceBatteries==null)return;
  foreach(var r in vm.DeviceBatteries)
  {
   var badge=r.Stale?"Last seen":r.Charging?"Charging":r.Percent is <=15?"Low":"Connected";
   var badgeBrush=r.Stale?"IslandMutedInkBrush":r.Charging?"IslandPositiveBrush":r.Percent is <=15?"IslandWarningBrush":"IslandPositiveBrush";
   var caption=r.Stale?$"{r.Via} · last seen {r.ReadAtUtc.ToLocalTime():HH:mm}":r.Via;
   CardsPanel.Children.Add(Card(r.Name,DeviceBatteryRowConverter.Glyph(r.Kind),badge,badgeBrush,new BatteryGauge{Percent=r.Percent,Charging=r.Charging,HorizontalAlignment=System.Windows.HorizontalAlignment.Left},r.Percent is {} p?p+"%":"?",vm.IsDeviceBatteryHidden(r.Key)?caption+" · hidden from taskbar":caption,r.Key));
  }
 }
}
/// <summary>Formats a <see cref="MochiDock.Core.DeviceBatteryReading"/> for the Devices list: glyph, state line or percentage.</summary>
public sealed class DeviceBatteryRowConverter : System.Windows.Data.IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is not MochiDock.Core.DeviceBatteryReading r) return "";
        return (parameter as string) switch
        {
            "glyph" => Glyph(r.Kind),
            "percent" => r.Percent is { } p ? p + "%" : "?",
            "brush" => Brush(r.Percent, r.Charging),
            _ => State(r),
        };
    }
    public static string Glyph(MochiDock.Core.BatteryDeviceKind kind) => kind switch
    {
        MochiDock.Core.BatteryDeviceKind.Keyboard => "",
        MochiDock.Core.BatteryDeviceKind.Mouse => "",
        MochiDock.Core.BatteryDeviceKind.Headset => "",
        MochiDock.Core.BatteryDeviceKind.Gamepad => "",
        MochiDock.Core.BatteryDeviceKind.Phone => "",
        _ => "",
    };
    /// <summary>Level colour from theme brushes: charging green, low red, otherwise the accent.</summary>
    public static System.Windows.Media.Brush Brush(int? percent, bool charging)
    {
        var key = charging ? "IslandGreenBrush" : percent is <= 15 ? "IslandRedBrush" : "IslandAccentBrush";
        return System.Windows.Application.Current?.TryFindResource(key) as System.Windows.Media.Brush ?? System.Windows.Media.Brushes.Gray;
    }
    public static string State(MochiDock.Core.DeviceBatteryReading r) =>
        r.Stale ? $"{r.Via} · last seen {r.ReadAtUtc.ToLocalTime():HH:mm}" : r.Charging ? $"{r.Via} · charging" : r.Via;
    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture) => throw new NotSupportedException();
}
