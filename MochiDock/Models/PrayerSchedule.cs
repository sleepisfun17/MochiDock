using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MochiDock.Models;

public class PrayerTimeItem : INotifyPropertyChanged
{
    private string _name = "";
    public string Name
    {
        get => _name;
        set { if (_name != value) { _name = value; OnPropertyChanged(); } }
    }

    private string _arabicName = "";
    public string ArabicName
    {
        get => _arabicName;
        set { if (_arabicName != value) { _arabicName = value; OnPropertyChanged(); } }
    }

    private TimeSpan _time;
    public TimeSpan Time
    {
        get => _time;
        set { if (_time != value) { _time = value; OnPropertyChanged(); OnPropertyChanged(nameof(FormattedTime)); } }
    }

    public string FormattedTime => DateTime.Today.Add(Time).ToString("HH:mm");

    private bool _isNext;
    public bool IsNext
    {
        get => _isNext;
        set { if (_isNext != value) { _isNext = value; OnPropertyChanged(); } }
    }

    private bool _isPassed;
    public bool IsPassed
    {
        get => _isPassed;
        set { if (_isPassed != value) { _isPassed = value; OnPropertyChanged(); } }
    }

    public double ArcPositionX { get; set; }
    public double ArcPositionY { get; set; }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public class PrayerSchedule : INotifyPropertyChanged
{
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string TimeZoneId { get; set; } = "";
    public DateTimeOffset NowUtc { get; set; } = DateTimeOffset.UtcNow;
    public string City { get; set; } = "";
    public string ResolvedLocation { get; set; } = "";
    public string HijriDate { get; set; } = "";
    public string QiblaBearingText { get; set; } = "Qibla unavailable";
    public DateTime Date { get; set; }

    public PrayerTimeItem Imsak { get; set; } = new() { Name = "Imsak", ArabicName = "الإمساك" };
    public PrayerTimeItem Fajr { get; set; } = new() { Name = "Fajr", ArabicName = "الفجر" };
    public PrayerTimeItem Sunrise { get; set; } = new() { Name = "Shuruq", ArabicName = "الشروq" };
    public PrayerTimeItem Dhuhr { get; set; } = new() { Name = "Dhuhr", ArabicName = "الظهر" };
    public PrayerTimeItem Asr { get; set; } = new() { Name = "Asr", ArabicName = "العصر" };
    public PrayerTimeItem Maghrib { get; set; } = new() { Name = "Maghrib", ArabicName = "المغرب" };
    public PrayerTimeItem Isha { get; set; } = new() { Name = "Isha", ArabicName = "العشاء" };

    private PrayerTimeItem? _nextPrayer;
    public PrayerTimeItem? NextPrayer
    {
        get => _nextPrayer;
        set { if (_nextPrayer != value) { _nextPrayer = value; OnPropertyChanged(); } }
    }

    private TimeSpan _timeUntilNextPrayer;
    public TimeSpan TimeUntilNextPrayer
    {
        get => _timeUntilNextPrayer;
        set { if (_timeUntilNextPrayer != value) { _timeUntilNextPrayer = value; OnPropertyChanged(); } }
    }

    private string _countdownText = "";
    public string CountdownText
    {
        get => _countdownText;
        set { if (_countdownText != value) { _countdownText = value; OnPropertyChanged(); } }
    }

    private string _countdownFullText = "00:00:00";
    public string CountdownFullText
    {
        get => _countdownFullText;
        set { if (_countdownFullText != value) { _countdownFullText = value; OnPropertyChanged(); } }
    }

    private double _sunProgress = 0.5;
    public double SunProgress
    {
        get => _sunProgress;
        set { if (!_sunProgress.Equals(value)) { _sunProgress = value; OnPropertyChanged(); } }
    }

    private bool _isDaytime = true;
    public bool IsDaytime
    {
        get => _isDaytime;
        set { if (_isDaytime != value) { _isDaytime = value; OnPropertyChanged(); } }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;
    public DateTimeOffset? TomorrowFajr { get; set; }
    public DateTimeOffset? NextPrayerInstant { get; private set; }
    public string NextPrayerDayText { get; private set; } = "";
    public string Source { get; set; } = "";
    public IEnumerable<(string Name,DateTimeOffset At)> Instants()
    {
        var zone=TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        foreach(var item in new[]{Fajr,Dhuhr,Asr,Maghrib,Isha})yield return(item.Name,MochiDock.Core.PrayerTime.At(Date.Date.Add(item.Time),zone));
        if(TomorrowFajr.HasValue)yield return("Fajr",TomorrowFajr.Value);
    }
    public void UpdateCountdown()
    {
        NowUtc=Clock();
        if(string.IsNullOrWhiteSpace(TimeZoneId)){NextPrayer=null;return;}
        var zone=TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        var local=TimeZoneInfo.ConvertTime(NowUtc,zone);
        PrayerTimeItem[] items=[Fajr,Dhuhr,Asr,Maghrib,Isha];
        NextPrayer=null;NextPrayerInstant=null;
        foreach(var item in items)
        {
            var instant=MochiDock.Core.PrayerTime.At(Date.Date.Add(item.Time),zone);
            item.IsPassed=instant<=NowUtc;item.IsNext=false;
            if(NextPrayer==null&&instant>NowUtc){NextPrayer=item;NextPrayerInstant=instant;item.IsNext=true;}
        }
        if(NextPrayer==null&&TomorrowFajr>NowUtc)
        {NextPrayer=new PrayerTimeItem{Name="Fajr",Time=TimeZoneInfo.ConvertTime(TomorrowFajr.Value,zone).TimeOfDay,IsNext=true};NextPrayerInstant=TomorrowFajr;}
        TimeUntilNextPrayer=NextPrayerInstant.HasValue?NextPrayerInstant.Value-NowUtc:TimeSpan.Zero;
        var remaining=TimeUntilNextPrayer;
        CountdownText=$"{(int)remaining.TotalHours}h {remaining.Minutes}m";
        CountdownFullText=$"{(int)remaining.TotalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}";
        var nextDate=NextPrayerInstant.HasValue?TimeZoneInfo.ConvertTime(NextPrayerInstant.Value,zone).Date:local.Date;
        NextPrayerDayText=nextDate==local.Date?"today":nextDate==local.Date.AddDays(1)?"tomorrow":MochiDock.Core.DateDisplay.Short(nextDate);
        IsDaytime=NowUtc>=MochiDock.Core.PrayerTime.At(Date.Date.Add(Sunrise.Time),zone)&&NowUtc<MochiDock.Core.PrayerTime.At(Date.Date.Add(Maghrib.Time),zone);
        SunProgress=MochiDock.Core.PrayerTimeline.Position(NowUtc,MochiDock.Core.PrayerTime.At(Date.Date,zone),MochiDock.Core.PrayerTime.At(Date.Date.AddDays(1),zone),items.Select(p=>MochiDock.Core.PrayerTime.At(Date.Date.Add(p.Time),zone)).ToArray()) ?? double.NaN;
        OnPropertyChanged(nameof(NowUtc));OnPropertyChanged(nameof(NextPrayerDayText));OnPropertyChanged(nameof(Source));
    }
}