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
 // open; the page only draws them (the sparkline is already filled when the page opens and survives tab switches).
 MochiDock.Core.PowerReading? power;
 bool powerLaptop;
 const double SparkWidth=48,SparkHeight=16;
 TextBlock? wattsText;
 System.Windows.Shapes.Polyline? spark;
 void PowerSampled()
 {
  if(vm==null||!IsVisible)return;
  power=vm.LatestPower;
  ApplyPower();
  ApplySessionCard();
 }
 TextBlock? sessionEnergy,sessionDetail;
 /// <summary>"Power use" card: energy, average and peak of this session (the power log) and a summary of the previous session.</summary>
 Border? BuildSessionCard()
 {
  var log=vm?.PowerLog;
  if(log==null||vm==null||!vm.EnablePowerLogging)return null;
  var history=log.History;
  var icon=Text("",24,"IslandAccentBrush");icon.FontFamily=new System.Windows.Media.FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");icon.HorizontalAlignment=System.Windows.HorizontalAlignment.Left;
  sessionEnergy=Text("-",20,"IslandInkBrush",true);sessionEnergy.Margin=new Thickness(0,0,10,0);
  sessionDetail=Text("",11,"IslandSecondaryInkBrush");
  var extra=new StackPanel{Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center};extra.Children.Add(sessionEnergy);extra.Children.Add(sessionDetail);
  var caption=history.Count>0?"Last: "+MochiDock.Core.PowerSessionFormat.Summary(history[0],DateTime.Now,false):"No earlier session recorded yet";
  var card=Card("Power use",null,"This session","IslandMutedInkBrush",icon,"",caption,null,extra);
  var lines=history.Take(5).Select(r=>MochiDock.Core.PowerSessionFormat.Summary(r,DateTime.Now)+(r.EndedUnexpectedly?" (ended unexpectedly)":""));
  card.ToolTip=history.Count>0?"Recent sessions:\n"+string.Join("\n",lines):"Power is logged about every 10 seconds; the total is an estimate unless it was read from the laptop battery.";
  ApplySessionCard();
  return card;
 }
 void ApplySessionCard()
 {
  if(sessionEnergy==null||sessionDetail==null)return;
  var cur=vm?.PowerLog?.Current;
  if(cur==null){sessionEnergy.Text="-";sessionDetail.Text="collecting…";sessionEnergy.ToolTip=null;return;}
  var prefix=cur.Source=="Measured"?"":"~";
  sessionEnergy.Text=prefix+MochiDock.Core.PowerSessionFormat.Energy(cur.EnergyWh);
  sessionDetail.Text="avg "+prefix+MochiDock.Core.PowerSessionFormat.Watts(cur.AverageW)+"\npeak "+MochiDock.Core.PowerSessionFormat.Watts(cur.PeakW);
  sessionEnergy.ToolTip=cur.Source+" · sampled "+MochiDock.Core.GameSessionFormat.Duration(cur.SampledSeconds)+" since "+cur.StartUtc.ToLocalTime().ToString("HH:mm");
 }
 // Updates the watts line and sparkline of the "This PC" card in place (no card rebuild on every sample).
 void ApplyPower()
 {
  if(wattsText==null)return;
  if(power==null){wattsText.Text="-";wattsText.ToolTip=powerLaptop?"Windows reports no battery rate right now.":"No CPU or GPU power reading is available. Windows' Energy Meter is tried first; the optional PawnIO driver adds more sensors (Settings > Hardware & power > CPU sensors), and GPU power is read on NVIDIA cards.";}
  else{wattsText.Text=(power.Source==MochiDock.Core.PowerSource.Estimated?"~":"")+power.Watts.ToString(power.Watts<100?"0.0":"0")+" W";wattsText.ToolTip=(power.Source==MochiDock.Core.PowerSource.Estimated?"Estimate: ":"Measured: ")+power.Detail;}
  if(spark==null)return;
  var samples=vm?.PowerSamples??Array.Empty<double>();
  spark.Points.Clear();
  if(samples.Count<2)return;
  double max=Math.Max(1,samples.Max()),min=samples.Min(),range=Math.Max(1,max-min);
  for(var i=0;i<samples.Count;i++)spark.Points.Add(new System.Windows.Point(SparkWidth*i/(samples.Count-1),SparkHeight-1-(SparkHeight-2)*(samples[i]-min)/range));
 }
 /// <summary>Watts line plus sparkline shown on the right of the "This PC" card.</summary>
 UIElement BuildPowerBlock()
 {
  // Side by side (sparkline, then watts) so the block is no taller than the percentage and the caption below stays visible.
  wattsText=Text("-",16,"IslandInkBrush",true,TextAlignment.Right);
  spark=new System.Windows.Shapes.Polyline{Width=SparkWidth,Height=SparkHeight,StrokeThickness=1.5,StrokeLineJoin=PenLineJoin.Round,Margin=new Thickness(0,0,8,0),VerticalAlignment=VerticalAlignment.Center};
  spark.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty,"IslandAccentBrush");
  var block=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=System.Windows.HorizontalAlignment.Right,VerticalAlignment=VerticalAlignment.Center};
  block.Children.Add(spark);block.Children.Add(wattsText);
  ApplyPower();
  return block;
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
   CardsPanel.Children.Add(Card("This PC","\uE7F8",state,charging?"IslandPositiveBrush":"IslandMutedInkBrush",new BatteryGauge{Percent=last.Percent,Charging=charging,HorizontalAlignment=System.Windows.HorizontalAlignment.Left},last.Percent+"%",caption,null,BuildPowerBlock()));
  }
  else
   CardsPanel.Children.Add(Card("This PC","\uE7F8","Wall power","IslandPositiveBrush",new BatteryGauge{IsPsu=true,HorizontalAlignment=System.Windows.HorizontalAlignment.Left},"PSU","Desktop · estimated from CPU + GPU",null,BuildPowerBlock()));
  sessionEnergy=null;sessionDetail=null;
  if(BuildSessionCard() is {} sessionCard)CardsPanel.Children.Add(sessionCard);
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
