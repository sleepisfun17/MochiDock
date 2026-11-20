using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using MochiDock.Models;
using MochiDock.ViewModels;
using UserControl = System.Windows.Controls.UserControl;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;

namespace MochiDock.Views.Cards;

public partial class PrayerCardView : UserControl
{
    private static readonly double[] Slots = [0.1, 0.3, 0.5, 0.7, 0.9];
    private static readonly SolidColorBrush PastDotBrush = new(Color.FromArgb((byte)(0.30 * 255), 255, 255, 255));
    private static readonly SolidColorBrush LaterDotBrush = new(Color.FromArgb((byte)(0.62 * 255), 255, 255, 255));
    private static readonly SolidColorBrush GoneArcBrush = new(Color.FromArgb((byte)(0.42 * 255), 255, 255, 255));
    private static readonly SolidColorBrush ToComeArcBrush = new(Color.FromArgb((byte)(0.16 * 255), 255, 255, 255));
    private static readonly SolidColorBrush PastTextBrush = new(Color.FromArgb((byte)(0.40 * 255), 255, 255, 255));
    private static readonly SolidColorBrush SunHaloBrush = new(Color.FromArgb((byte)(0.22 * 255), 255, 159, 10));
    private static readonly SolidColorBrush MoonHaloBrush = new(Color.FromArgb((byte)(0.22 * 255), 185, 174, 247));

    public PrayerCardView()
    {
        InitializeComponent();
        Loaded += PrayerCardView_Loaded;
        IsVisibleChanged += (s, e) => { if (IsVisible) RedrawArc(); };
        Unloaded += (s,e) => { if (_subscribedSchedule != null) _subscribedSchedule.PropertyChanged -= Schedule_PropertyChanged; if (ViewModel != null) ViewModel.PropertyChanged -= ViewModelChanged; };
        DataContextChanged += PrayerCardView_DataContextChanged;
        MochiDock.Services.ThemeService.RedrawOnThemeChange(this, RedrawArc);   // the arc copies theme brushes when drawn
    }

    private bool _fontMeasureQueued;
    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property != FontFamilyProperty || CountdownTextBlock == null || _fontMeasureQueued) return;
        _fontMeasureQueued = true;
        Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
        {
            _fontMeasureQueued = false;
            if (!IsLoaded) return;
            MeasureCountdown();
            RedrawArc();
        });
    }

    private void MeasureCountdown(){double widest=0;foreach(char digit in "0123456789"){var probe=new TextBlock{Text=$"{digit}{digit}:{digit}{digit}:{digit}{digit}",FontFamily=CountdownTextBlock.FontFamily,FontSize=30,FontWeight=FontWeights.Medium};System.Windows.Documents.TextElement.SetFontFamily(probe,CountdownTextBlock.FontFamily);probe.Typography.NumeralAlignment=FontNumeralAlignment.Tabular;probe.Measure(new System.Windows.Size(double.PositiveInfinity,double.PositiveInfinity));widest=Math.Max(widest,probe.DesiredSize.Width);}CountdownColumn.Width=new GridLength(Math.Ceiling(widest)+4);}
    private void PrayerCardView_Loaded(object sender, RoutedEventArgs e)
    {
        MeasureCountdown();
        UpdateSoundButtonVisuals();
        if (ViewModel != null) { ViewModel.PropertyChanged -= ViewModelChanged; ViewModel.PropertyChanged += ViewModelChanged; }
        HookScheduleEvents();
        RedrawArc();
    }

    private void PrayerCardView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        HookScheduleEvents();
        RedrawArc();
    }

    private void ViewModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if(e.PropertyName == nameof(MainViewModel.CurrentPrayerSchedule)) { HookScheduleEvents(); RedrawArc(); }
        // The sound can also be changed in Advanced Settings (Location & Prayer): keep the pill in step.
        if(e.PropertyName == nameof(MainViewModel.PrayerSoundMode)) Dispatcher.BeginInvoke(UpdateSoundButtonVisuals);
    }
    private PrayerSchedule? _subscribedSchedule;
    private void HookScheduleEvents()
    {
        if (_subscribedSchedule != null)
        {
            _subscribedSchedule.PropertyChanged -= Schedule_PropertyChanged;
            _subscribedSchedule = null;
        }

        if (ViewModel?.CurrentPrayerSchedule != null)
        {
            _subscribedSchedule = ViewModel.CurrentPrayerSchedule;
            _subscribedSchedule.PropertyChanged += Schedule_PropertyChanged;
        }
    }

    private void Schedule_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PrayerSchedule.SunProgress) ||
            e.PropertyName == nameof(PrayerSchedule.IsDaytime) ||
            e.PropertyName == nameof(PrayerSchedule.NextPrayer))
        {
            Dispatcher.Invoke(RedrawArc);
        }
    }

    private void ArcCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        RedrawArc();
    }

    private Point ArcPoint(double position,double width) => new(position*width,MochiDock.Core.PrayerTimeline.Height(position));
    public void RedrawArc()=>RenderSchedule(ViewModel?.CurrentPrayerSchedule);
    public void RenderSchedule(PrayerSchedule? schedule)
    {
        if(ArcCanvas==null||ArcCanvas.ActualWidth<=10)return;
        ArcCanvas.Children.Clear();

        void Unavailable(){var note=new TextBlock{Text="Prayer timeline updating / unavailable",FontSize=11};note.SetResourceReference(TextBlock.ForegroundProperty,"IslandMutedInkBrush");ArcCanvas.Children.Add(note);}
        if(schedule==null||string.IsNullOrEmpty(schedule.TimeZoneId)){Unavailable();return;}
        TimeZoneInfo zone;
        DateTimeOffset start,end;DateTimeOffset[] instants;
        PrayerTimeItem[] prayers=[schedule.Fajr,schedule.Dhuhr,schedule.Asr,schedule.Maghrib,schedule.Isha];
        try
        {
            zone=TimeZoneInfo.FindSystemTimeZoneById(schedule.TimeZoneId);
            start=MochiDock.Core.PrayerTime.At(schedule.Date.Date,zone);
            end=MochiDock.Core.PrayerTime.At(schedule.Date.Date.AddDays(1),zone);
            instants=prayers.Select(p=>MochiDock.Core.PrayerTime.At(schedule.Date.Date.Add(p.Time),zone)).ToArray();
        }
        catch(ArgumentException){Unavailable();return;}
        catch(TimeZoneNotFoundException){Unavailable();return;}
        var progress=MochiDock.Core.PrayerTimeline.Position(schedule.NowUtc,start,end,instants);
        if(!progress.HasValue){Unavailable();return;}
        double here=progress.Value,width=ArcCanvas.ActualWidth;
        Point marker=ArcPoint(here,width);
        var ink=(System.Windows.Media.Brush)FindResource("IslandInkBrush");
        var muted=(System.Windows.Media.Brush)FindResource("IslandMutedInkBrush");
        var accent=(System.Windows.Media.Brush)FindResource("IslandAccentBrush");
        var halo=(System.Windows.Media.Brush)(TryFindResource("IslandAccentSubtleBrush")??SunHaloBrush);
        void Circle(Point point,double diameter,System.Windows.Media.Brush fill,System.Windows.Media.Brush? stroke=null)
        {
            var dot = new System.Windows.Shapes.Path
            {
                Data = new EllipseGeometry(point, diameter / 2.0, diameter / 2.0),
                Fill = fill,
                Stroke = stroke,
                StrokeThickness = stroke != null ? 1.5 : 0
            };
            ArcCanvas.Children.Add(dot);
        }
        // Schematic prayer timeline: its peak is Dhuhr, not a measured solar altitude.
        ArcCanvas.Children.Add(new Line{X1=0,X2=width,Y1=38,Y2=38,Stroke=muted,Opacity=.25,StrokeDashArray=new DoubleCollection{3,3}});
        int count=Math.Max(2,(int)(width/6));
        for(int i=0;i<=count;i++)
        {
            double p=(double)i/count;Point point=ArcPoint(p,width);
            if((point-marker).Length<12||MochiDock.Core.PrayerTimeline.Slots.Any(slot=>Math.Abs(slot-p)*width<10))continue;
            Circle(point,2.4,muted);ArcCanvas.Children[ArcCanvas.Children.Count-1].Opacity=p<=here?.8:.25;
        }
        int ongoingSlot = MochiDock.Core.PrayerTimeline.GetCurrentSlotIndex(schedule.NowUtc, instants);
        for(int i=0;i<5;i++)
        {
            double slot=MochiDock.Core.PrayerTimeline.Slots[i];Point point=ArcPoint(slot,width);
            bool next=MochiDock.Core.PrayerTimeline.IsNextSlot(i,schedule.NextPrayerInstant,schedule.NowUtc,instants);
            bool ongoing = (i == ongoingSlot);
            int size=MochiDock.Core.PrayerTimeline.TargetSize((point-marker).Length);

            if (ongoing)
            {
                // Active ongoing prayer dot: 100% concentric 6 DIP solid dot inside 12 DIP outer ring
                Circle(point, 12, Brushes.Transparent, accent);
                Circle(point, 6, accent);
            }
            else if (next)
            {
                // Upcoming prayer: target ring (size 19) or approaching accent dot
                if (size > 0)
                {
                    Circle(point, size, size == 19 ? Brushes.Transparent : accent, size == 19 ? accent : null);
                }
                else
                {
                    Circle(point, 5, accent);
                }
            }
            else
            {
                // Standard prayer dot: cleanly rendered 5 DIP dot that never vanishes
                Circle(point, 5, muted);
            }

            string displayTime=(i==0&&schedule.NextPrayer!=null&&string.Equals(schedule.NextPrayer.Name,"Fajr",StringComparison.OrdinalIgnoreCase)&&schedule.NowUtc>=instants[4])
                ? schedule.NextPrayer.FormattedTime
                : prayers[i].FormattedTime;
            var text=new TextBlock{Width=width/5,TextAlignment=TextAlignment.Center,Foreground=next?accent:(ongoing?ink:muted),FontWeight=(next||ongoing)?FontWeights.Bold:FontWeights.SemiBold};
            text.Inlines.Add(new System.Windows.Documents.Run(prayers[i].Name){FontSize=11});
            text.Inlines.Add(new System.Windows.Documents.LineBreak());
            text.Inlines.Add(new System.Windows.Documents.Run(displayTime){FontSize=12});
            Canvas.SetLeft(text,point.X-width/10);Canvas.SetTop(text,54);ArcCanvas.Children.Add(text);
        }
        // Sun vs Moon distinction: above horizon line (Y <= 38) vs below horizon line (Y > 38)
        bool isAboveHorizon = marker.Y <= 38.0;
        var sunGoldBrush = (System.Windows.Media.Brush)FindResource("IslandWarningBrush"); // solar gold
        var sunHaloBrush = new SolidColorBrush(Color.FromArgb((byte)(0.25 * 255), 245, 158, 11));
        var moonBrush = (System.Windows.Media.Brush)(TryFindResource("IslandHighlightBrush") ?? FindResource("IslandPurpleBrush"));
        var moonHaloBrush = (System.Windows.Media.Brush)(TryFindResource("IslandHighlightSubtleBrush") ?? MoonHaloBrush);

        bool showHalo = instants.Select((_,i)=>(ArcPoint(MochiDock.Core.PrayerTimeline.Slots[i],width)-marker).Length).Min()>=20.5;
        if(isAboveHorizon)
        {
            if(showHalo) Circle(marker, 18, sunHaloBrush);
            Circle(marker, 10, sunGoldBrush);
            // Subtle sun center core
            Circle(marker, 4, (System.Windows.Media.Brush)FindResource("IslandInkBrush"));
        }
        else
        {
            // Below horizon: render a recognizable crescent moon
            if(showHalo) Circle(marker, 18, moonHaloBrush);
            Circle(marker, 12, (System.Windows.Media.Brush)(TryFindResource("IslandGroundBrush") ?? Brushes.Black), moonBrush);
            var moonPath = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M8.88 6.25A3.6 3.6 0 1 1 4.54 1.9A3.15 3.15 0 0 0 8.88 6.25Z"),
                Fill = moonBrush,
                Width = 10,
                Height = 10,
                Stretch = Stretch.Uniform
            };
            Canvas.SetLeft(moonPath, marker.X - 5);
            Canvas.SetTop(moonPath, marker.Y - 5);
            ArcCanvas.Children.Add(moonPath);
        }
        ArcCanvas.ToolTip=$"Prayer timeline — {TimeZoneInfo.ConvertTime(schedule.NowUtc,zone):HH:mm:ss} {schedule.TimeZoneId}\nNext: {schedule.NextPrayer?.Name} {schedule.NextPrayer?.FormattedTime} {schedule.NextPrayerDayText}";
    }    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void ChangePlace_Click(object sender, RoutedEventArgs e)
    {
        App.OpenSettings(MochiDock.Core.SettingsSections.Location);
    }

    private void ReadQuran_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://quran.com",
                UseShellExecute = true
            });
        }
        catch { }
    }

    private void SoundTone_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.PrayerSoundMode = "Tone";
            UpdateSoundButtonVisuals();
            ViewModel.PreviewPrayerSound(); // choosing Tone plays the chime once, so the choice is audible right away
        }
    }

    private void SoundSilent_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.PrayerSoundMode = "Silent";
            UpdateSoundButtonVisuals();
            MochiDock.Services.PrayerAudio.Stop();
        }
    }

    private void UpdateSoundButtonVisuals()
    {
        string mode = ViewModel?.PrayerSoundMode ?? "Tone";
        HighlightSoundButton(mode switch
        {
            "Silent" => SoundSilentBtn,
            _ => SoundToneBtn
        });
    }

    private void HighlightSoundButton(System.Windows.Controls.Button activeBtn)
    {
        // The chosen one is filled with the accent (the same fill as primary buttons); the other is flat and muted. The earlier chip-active
        // fill was almost the colour of the pill itself, so the choice could not be seen.
        foreach (var button in new[] { SoundToneBtn, SoundSilentBtn })
        {
            bool active = ReferenceEquals(button, activeBtn);
            button.SetResourceReference(System.Windows.Controls.Control.BackgroundProperty, active ? "IslandAccentFillBrush" : "IslandControlBackgroundBrush");
            button.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, active ? "IslandAccentButtonInkBrush" : "IslandMutedInkBrush");
            button.SetResourceReference(System.Windows.Controls.Control.FontWeightProperty, active ? "IslandSmallLabelWeight" : "IslandBodyWeight");
            System.Windows.Automation.AutomationProperties.SetHelpText(button, active ? "Selected" : "");
        }
    }
}
