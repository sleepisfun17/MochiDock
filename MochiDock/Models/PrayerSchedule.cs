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

    private string _lastMoonSummary = "";
    private int _lastMoonPercent = -1;
    private bool _lastSouthern;

    public MochiDock.Core.MoonState Moon => MochiDock.Core.MoonPhase.At(NowUtc);
    public double MoonCycleFraction => Moon.CycleFraction;
    public bool IsSouthernHemisphere => Latitude < 0;
    public string MoonPhaseSummary => Moon.Summary;
    public string MoonPhaseToolTip => Moon.DetailTooltip;

    public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;
    public DateTimeOffset? TomorrowFajr { get; set; }
    public DateTimeOffset? NextPrayerInstant { get; private set; }
    public string NextPrayerDayText { get; private set; } = "";
    public PrayerTimeItem? CurrentPrayer { get; private set; }
    public DateTimeOffset? CurrentPrayerInstant { get; private set; }
    public TimeSpan? TimeSinceCurrentPrayer { get; private set; }

    private string _currentPrayerElapsedText = "";
    public string CurrentPrayerElapsedText
    {
        get => _currentPrayerElapsedText;
        private set { if (_currentPrayerElapsedText != value) { _currentPrayerElapsedText = value; OnPropertyChanged(); } }
    }

    public DateTimeOffset? DhuhaStart { get; private set; }
    public DateTimeOffset? DhuhaEnd { get; private set; }
    public bool HasDhuha => DhuhaStart.HasValue && DhuhaEnd.HasValue;

    private bool _isDhuhaActive;
    public bool IsDhuhaActive
    {
        get => _isDhuhaActive;
        private set { if (_isDhuhaActive != value) { _isDhuhaActive = value; OnPropertyChanged(); } }
    }

    private string _dhuhaRangeText = "";
    public string DhuhaRangeText
    {
        get => _dhuhaRangeText;
        private set { if (_dhuhaRangeText != value) { _dhuhaRangeText = value; OnPropertyChanged(); } }
    }

    private string _dhuhaStatusText = "";
    public string DhuhaStatusText
    {
        get => _dhuhaStatusText;
        private set { if (_dhuhaStatusText != value) { _dhuhaStatusText = value; OnPropertyChanged(); } }
    }

    public DateTimeOffset? ActivePeriodStart { get; private set; }
    public DateTimeOffset? ActivePeriodEnd { get; private set; }

    private string _activePeriodName = "";
    public string ActivePeriodName
    {
        get => _activePeriodName;
        private set { if (_activePeriodName != value) { _activePeriodName = value; OnPropertyChanged(); } }
    }

    private bool _isAnyPeriodActive;
    public bool IsAnyPeriodActive
    {
        get => _isAnyPeriodActive;
        private set { if (_isAnyPeriodActive != value) { _isAnyPeriodActive = value; OnPropertyChanged(); } }
    }

    private bool _isActivePeriodDhuha;
    public bool IsActivePeriodDhuha
    {
        get => _isActivePeriodDhuha;
        private set { if (_isActivePeriodDhuha != value) { _isActivePeriodDhuha = value; OnPropertyChanged(); } }
    }

    private string _activePeriodStatusText = "";
    public string ActivePeriodStatusText
    {
        get => _activePeriodStatusText;
        private set { if (_activePeriodStatusText != value) { _activePeriodStatusText = value; OnPropertyChanged(); } }
    }

    public bool HasPeriodBanner => !string.IsNullOrEmpty(ActivePeriodStatusText);

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
        if(string.IsNullOrWhiteSpace(TimeZoneId)){CurrentPrayer=null;CurrentPrayerInstant=null;TimeSinceCurrentPrayer=null;CurrentPrayerElapsedText="";NextPrayerInstant=null;TimeUntilNextPrayer=TimeSpan.Zero;NextPrayer=null;DhuhaStart=null;DhuhaEnd=null;IsDhuhaActive=false;DhuhaRangeText="";DhuhaStatusText="";OnPropertyChanged(nameof(HasDhuha));ActivePeriodStart=null;ActivePeriodEnd=null;ActivePeriodName="";IsAnyPeriodActive=false;IsActivePeriodDhuha=false;ActivePeriodStatusText="";OnPropertyChanged(nameof(HasPeriodBanner));return;}
        var zone=TimeZoneInfo.FindSystemTimeZoneById(TimeZoneId);
        var local=TimeZoneInfo.ConvertTime(NowUtc,zone);
        PrayerTimeItem[] items=[Fajr,Dhuhr,Asr,Maghrib,Isha];
        PrayerTimeItem? nextItem=null,currentItem=null;
        DateTimeOffset? nextInstant=null,currentInstant=null;
        foreach(var item in items)
        {
            var instant=MochiDock.Core.PrayerTime.At(Date.Date.Add(item.Time),zone);
            item.IsPassed=instant<=NowUtc;
            if(item.IsPassed){currentItem=item;currentInstant=instant;}
            bool isNext=nextItem==null&&instant>NowUtc;
            item.IsNext=isNext;
            if(isNext){nextItem=item;nextInstant=instant;}
        }
        if(nextItem==null&&TomorrowFajr>NowUtc)
        {
            var tomorrowTime=TimeZoneInfo.ConvertTime(TomorrowFajr.Value,zone).TimeOfDay;
            bool reuseSynthetic=_nextPrayer!=null&&!Array.Exists(items,p=>ReferenceEquals(p,_nextPrayer))&&string.Equals(_nextPrayer.Name,"Fajr",StringComparison.Ordinal)&&_nextPrayer.Time==tomorrowTime;
            nextItem=reuseSynthetic?_nextPrayer!:new PrayerTimeItem{Name="Fajr",Time=tomorrowTime,IsNext=true};
            nextInstant=TomorrowFajr;
        }
        TimeSpan? sinceCurrent=currentInstant.HasValue?NowUtc-currentInstant.Value:null;
        CurrentPrayer=currentItem;
        CurrentPrayerInstant=currentInstant;
        TimeSinceCurrentPrayer=sinceCurrent;
        NextPrayerInstant=nextInstant;
        TimeUntilNextPrayer=nextInstant.HasValue?nextInstant.Value-NowUtc:TimeSpan.Zero;
        var remaining=TimeUntilNextPrayer;
        var nextDate=nextInstant.HasValue?TimeZoneInfo.ConvertTime(nextInstant.Value,zone).Date:local.Date;
        NextPrayerDayText=nextDate==local.Date?"today":nextDate==local.Date.AddDays(1)?"tomorrow":MochiDock.Core.DateDisplay.Short(nextDate);
        IsDaytime=NowUtc>=MochiDock.Core.PrayerTime.At(Date.Date.Add(Sunrise.Time),zone)&&NowUtc<MochiDock.Core.PrayerTime.At(Date.Date.Add(Maghrib.Time),zone);
        SunProgress=MochiDock.Core.PrayerTimeline.Position(NowUtc,MochiDock.Core.PrayerTime.At(Date.Date,zone),MochiDock.Core.PrayerTime.At(Date.Date.AddDays(1),zone),items.Select(p=>MochiDock.Core.PrayerTime.At(Date.Date.Add(p.Time),zone)).ToArray()) ?? double.NaN;
        NextPrayer=nextItem;
        int totalMinutes=remaining>TimeSpan.Zero?(int)Math.Ceiling(remaining.TotalMinutes):0;
        CountdownText=$"{totalMinutes/60}h {totalMinutes%60}m";
        CountdownFullText=$"{(int)remaining.TotalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}";
        CurrentPrayerElapsedText=sinceCurrent is { } el?MochiDock.Core.TaskbarReadouts.FormatPrayerElapsed(el):"";
        var sunriseInstant = MochiDock.Core.PrayerTime.At(Date.Date.Add(Sunrise.Time), zone);
        var dhuhrInstant = MochiDock.Core.PrayerTime.At(Date.Date.Add(Dhuhr.Time), zone);
        if (MochiDock.Core.PrayerTime.DhuhaWindow(sunriseInstant, dhuhrInstant, NowUtc) is { } dhuha)
        {
            DhuhaStart = dhuha.Start;
            DhuhaEnd = dhuha.End;
            var startLocal = TimeZoneInfo.ConvertTime(dhuha.Start, zone);
            var endLocal = TimeZoneInfo.ConvertTime(dhuha.End, zone);
            string range = $"{startLocal:HH:mm}–{endLocal:HH:mm}";
            DhuhaRangeText = range;
            IsDhuhaActive = dhuha.IsActive;
            DhuhaStatusText = dhuha.IsActive
                ? $"Dhuha now · {range} ({MochiDock.Core.PrayerTime.FormatDhuhaRemaining(dhuha.Remaining)})"
                : $"Dhuha {range}";
        }
        else
        {
            DhuhaStart = null;
            DhuhaEnd = null;
            IsDhuhaActive = false;
            DhuhaRangeText = "";
            DhuhaStatusText = "";
        }
        OnPropertyChanged(nameof(HasDhuha));

        var fajrInstant = MochiDock.Core.PrayerTime.At(Date.Date.Add(Fajr.Time), zone);
        var asrInstant = MochiDock.Core.PrayerTime.At(Date.Date.Add(Asr.Time), zone);
        var maghribInstant = MochiDock.Core.PrayerTime.At(Date.Date.Add(Maghrib.Time), zone);
        var ishaInstant = MochiDock.Core.PrayerTime.At(Date.Date.Add(Isha.Time), zone);
        if (MochiDock.Core.PrayerTime.ActivePeriod(fajrInstant, sunriseInstant, dhuhrInstant, asrInstant, maghribInstant, ishaInstant, TomorrowFajr, NowUtc) is { } ap)
        {
            ActivePeriodName = ap.Name;
            ActivePeriodStart = ap.Start;
            ActivePeriodEnd = ap.End;
            IsAnyPeriodActive = true;
            IsActivePeriodDhuha = ap.IsDhuha;
            string rangeText = (ap.Start, ap.End) switch
            {
                ({ } s, { } e) => $"{TimeZoneInfo.ConvertTime(s, zone):HH:mm}–{TimeZoneInfo.ConvertTime(e, zone):HH:mm}",
                (null, { } e) => $"until {TimeZoneInfo.ConvertTime(e, zone):HH:mm}",
                ({ } s, null) => $"from {TimeZoneInfo.ConvertTime(s, zone):HH:mm}",
                _ => ""
            };
            string remText = MochiDock.Core.PrayerTime.FormatDhuhaRemaining(ap.Remaining);
            ActivePeriodStatusText = remText.Length > 0
                ? $"{ap.Name} now · {rangeText} ({remText})"
                : $"{ap.Name} now · {rangeText}";
        }
        else
        {
            ActivePeriodName = "";
            ActivePeriodStart = null;
            ActivePeriodEnd = null;
            IsAnyPeriodActive = false;
            IsActivePeriodDhuha = false;
            ActivePeriodStatusText = HasDhuha ? $"Dhuha {DhuhaRangeText}" : "";
        }
        OnPropertyChanged(nameof(HasPeriodBanner));
        OnPropertyChanged(nameof(NowUtc));OnPropertyChanged(nameof(NextPrayerDayText));OnPropertyChanged(nameof(Source));
        var moon = Moon;
        if (!string.Equals(moon.Summary, _lastMoonSummary, StringComparison.Ordinal) || moon.IlluminationPercent != _lastMoonPercent)
        {
            _lastMoonSummary = moon.Summary;
            _lastMoonPercent = moon.IlluminationPercent;
            OnPropertyChanged(nameof(Moon));
            OnPropertyChanged(nameof(MoonCycleFraction));
            OnPropertyChanged(nameof(MoonPhaseSummary));
            OnPropertyChanged(nameof(MoonPhaseToolTip));
        }
        bool southern = IsSouthernHemisphere;
        if (southern != _lastSouthern)
        {
            _lastSouthern = southern;
            OnPropertyChanged(nameof(IsSouthernHemisphere));
        }
    }
}