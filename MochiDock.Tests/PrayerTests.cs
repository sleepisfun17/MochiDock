using System.Net;
using System.Text.Json;
using MochiDock.Services;
using MochiDock.Core;
namespace MochiDock.Tests;
public class PrayerTests : IDisposable
{
    private readonly string _directory=Path.Combine(Path.GetTempPath(),"PrayerTests-"+Guid.NewGuid().ToString("N"));
    private DateTimeOffset _now=new(2026,11,6,17,0,0,TimeSpan.Zero);
    private sealed class Handler : HttpMessageHandler
    {
        public bool Offline,Malformed;
        public int Calls;
        public Action? BeforeResponse;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken token)
        {
            Calls++;var callback=BeforeResponse;BeforeResponse=null;callback?.Invoke();if(Offline)throw new HttpRequestException("Offline fixture");
            string path=request.RequestUri!.AbsolutePath;
            if(request.RequestUri.Host.Contains("open-meteo"))return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent("{\"timezone\":\"Asia/Tokyo\"}")});
            string date=path.Split('/').Last();
            string json=JsonSerializer.Serialize(new{data=new{date=new{gregorian=new{date=Malformed?"01-01-2000":date}},meta=new{timezone="Asia/Tokyo"},timings=new{Imsak="04:40",Fajr=date.StartsWith("08")?"04:52":"04:50",Sunrise="06:17",Dhuhr="11:33",Asr="14:27",Maghrib="16:48",Isha="18:10"}}});
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK){Content=new StringContent(json)});
        }
    }
    private PrayerTimesService Create(Handler handler) => new(new HttpClient(handler),()=>_now,Path.Combine(_directory,"cache.json"));
    [Fact] public async Task LocationDateAndTomorrowAreFetchedAndTicksKeepApiTimes()
    {
        var handler=new Handler();var service=Create(handler);service.SetCoordinates(35.68,139.69,"Tokyo");
        var schedule=await service.FetchOrCalculateScheduleAsync(new DateTime(2000,1,1));
        Assert.Equal(new DateTime(2026,11,7),schedule.Date);
        Assert.Equal("Aladhan",schedule.Source);
        Assert.Equal(TimeSpan.FromHours(4)+TimeSpan.FromMinutes(50),schedule.Fajr.Time);
        _now=new DateTimeOffset(2026,11,7,20,0,0,TimeSpan.FromHours(9));schedule.UpdateCountdown();
        Assert.Equal("tomorrow",schedule.NextPrayerDayText);
        Assert.Equal(52,TimeZoneInfo.ConvertTime(schedule.NextPrayerInstant!.Value,TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo")).Minute);
        Assert.Equal(11,schedule.Dhuhr.Time.Hours);
    }
    [Fact] public async Task CacheSurvivesOfflineRestart()
    {
        var handler=new Handler();var first=Create(handler);first.SetCoordinates(35.68,139.69,"Tokyo");
        await first.FetchOrCalculateScheduleAsync(default);
        handler.Offline=true;var second=Create(handler);Assert.True(await second.RestoreCachedLocationAsync("Tokyo"));
        var cached=await second.FetchOrCalculateScheduleAsync(default);
        Assert.Equal("Aladhan (cached)",cached.Source);Assert.Equal("Asia/Tokyo",cached.TimeZoneId);
    }
    [Fact] public async Task MissingTimeZoneDoesNotUsePcZone()
    {
        var handler=new Handler{Offline=true};var service=Create(handler);service.SetCoordinates(0,0,"Unknown");
        await Assert.ThrowsAsync<InvalidOperationException>(()=>service.FetchOrCalculateScheduleAsync(default));
    }
    [Fact] public async Task WrongApiDateIsReplacedWithLabeledOfflineCalculation()
    {
        var service=Create(new Handler{Malformed=true});service.SetCoordinates(35.68,139.69,"Tokyo");
        var schedule=await service.FetchOrCalculateScheduleAsync(default);
        Assert.Equal("Offline calculation",schedule.Source);
        Assert.Equal("Asia/Tokyo",schedule.TimeZoneId);
    }
    [Fact] public void DaylightSavingUsesActualOffsets()
    {
        var zone=TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
        var start=PrayerTime.At(new DateTime(2026,3,8),zone);var end=PrayerTime.At(new DateTime(2026,3,9),zone);
        Assert.Equal(23,(end-start).TotalHours);
        Assert.Throws<ArgumentException>(()=>PrayerTime.At(new DateTime(2026,3,8,2,30,0),zone));
    }
    [Fact] public void RemindersDoNotReplayOrDuplicate()
    {
        var gate=new PrayerReminderGate();var now=DateTimeOffset.UtcNow;
        Assert.False(gate.Due("a",now,now));gate.Reset(now);
        Assert.True(gate.Due("a",now.AddSeconds(1),now.AddSeconds(2)));
        Assert.False(gate.Due("a",now.AddSeconds(1),now.AddSeconds(2)));
        gate.Reset(now.AddHours(1));Assert.False(gate.Due("missed",now.AddMinutes(30),now.AddHours(1)));
    }
    [Theory][InlineData(false,"Tone","Silent")][InlineData(false,"Adzan","Silent")][InlineData(true,"Tone","Tone")][InlineData(true,"Adzan","Silent")]
    public void AudioMigrationPreservesMute(bool enabled,string mode,string expected)=>Assert.Equal(expected,PrayerTime.MigrateSound(enabled,mode));
    [Fact] public async Task ChangedLocationRejectsOldResponse()
    {
        var handler=new Handler();var service=Create(handler);service.SetCoordinates(35.66,139.70,"Shibuya, Tokyo");
        handler.BeforeResponse=()=>service.SetCoordinates(35.68,139.69,"Tokyo");
        await Assert.ThrowsAsync<OperationCanceledException>(()=>service.FetchOrCalculateScheduleAsync(default));
        Assert.Equal("Tokyo",service.Location!.City);
    }
    [Fact] public async Task CacheStaysBoundedAndRolloverUsesNewLocationDate()
    {
        var service=Create(new Handler());service.SetCoordinates(35.68,139.69,"Tokyo");
        for(int day=0;day<18;day++){await service.FetchOrCalculateScheduleAsync(default);_now=_now.AddDays(1);}
        var records=JsonSerializer.Deserialize<PrayerCacheEntry[]>(File.ReadAllText(Path.Combine(_directory,"cache.json")))!;
        Assert.InRange(records.Length,1,32);
        Assert.Equal(TimeZoneInfo.ConvertTime(_now,TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo")).Date,(await service.FetchOrCalculateScheduleAsync(default)).Date);
    }
    [Fact] public async Task ChangedMethodDoesNotReuseOtherMethodsCache()
    {
        var handler=new Handler();var service=Create(handler);service.SetCoordinates(35.68,139.69,"Tokyo");
        await service.FetchOrCalculateScheduleAsync(default,"MWL");handler.Offline=true;
        Assert.Equal("Offline calculation",(await service.FetchOrCalculateScheduleAsync(default,"ISNA")).Source);
    }
    [Theory][InlineData("Adzan","Silent")][InlineData("Tone","Tone")][InlineData("Silent","Silent")]
    public void SimplifiedModePersistsAndRetainsLegacyRecordingField(string mode,string expected)
    {
        string path=Path.Combine(_directory,"preferences.json");
        var data=new Dictionary<string,string>{{"PrayerSoundMode",PrayerTime.NormalizeSound(mode)},{"PrayerAudioWavPath","legacy.wav"}};
        AtomicJsonStore.Save(path,data);
        var loaded=AtomicJsonStore.Load(path,()=>new Dictionary<string,string>()).Value;
        Assert.Equal(expected,loaded["PrayerSoundMode"]);Assert.Equal("legacy.wav",loaded["PrayerAudioWavPath"]);
    }
    [Fact] public void UpdateCountdownDoesNotEmitTransientNullOrEmptyInstantForNextPrayer()
    {
        var zone=TimeZoneInfo.FindSystemTimeZoneById("Asia/Tokyo");
        var now=new DateTimeOffset(2026,11,7,15,0,0,TimeSpan.FromHours(9));
        var schedule=new MochiDock.Models.PrayerSchedule
        {
            Date=new DateTime(2026,11,7),
            TimeZoneId="Asia/Tokyo",
            Clock=()=>now,
            TomorrowFajr=PrayerTime.At(new DateTime(2026,11,8,4,52,0),zone),
        };
        schedule.Fajr.Time=new TimeSpan(4,50,0);
        schedule.Sunrise.Time=new TimeSpan(6,17,0);
        schedule.Dhuhr.Time=new TimeSpan(11,33,0);
        schedule.Asr.Time=new TimeSpan(14,27,0);
        schedule.Maghrib.Time=new TimeSpan(16,48,0);
        schedule.Isha.Time=new TimeSpan(18,10,0);

        var seen=new List<(string? Name,DateTimeOffset? Instant,TimeSpan Remaining)>();
        schedule.PropertyChanged+=(_,e)=>
        {
            if(e.PropertyName==nameof(MochiDock.Models.PrayerSchedule.NextPrayer))
                seen.Add((schedule.NextPrayer?.Name,schedule.NextPrayerInstant,schedule.TimeUntilNextPrayer));
        };

        schedule.UpdateCountdown();
        now=now.AddSeconds(1);schedule.UpdateCountdown();
        now=now.AddSeconds(1);schedule.UpdateCountdown();
        Assert.Single(seen);
        Assert.Equal("Maghrib",seen[0].Name);
        Assert.NotNull(seen[0].Instant);
        Assert.True(seen[0].Remaining>TimeSpan.Zero);
        Assert.Equal("Asr",schedule.CurrentPrayer?.Name);
        Assert.Equal("+33m",schedule.CurrentPrayerElapsedText);

        // After Isha (targeting TomorrowFajr), subsequent 1 s ticks reuse the item and emit zero NextPrayer changes.
        seen.Clear();
        now=new DateTimeOffset(2026,11,7,19,0,0,TimeSpan.FromHours(9));
        schedule.UpdateCountdown();
        now=now.AddSeconds(1);schedule.UpdateCountdown();
        now=now.AddSeconds(1);schedule.UpdateCountdown();
        Assert.Single(seen);
        Assert.Equal("Fajr",seen[0].Name);
        Assert.Equal(schedule.TomorrowFajr,seen[0].Instant);
        Assert.True(seen[0].Remaining>TimeSpan.FromHours(9));
    }

    [Fact]
    public void DhuhaWindowComputesOffsetsActiveStateRemainingAndGuardsShortDays()
    {
        var offset = TimeSpan.FromHours(9);
        var sunrise = new DateTimeOffset(2026, 11, 28, 6, 28, 0, offset);
        var dhuhr = new DateTimeOffset(2026, 11, 28, 11, 40, 0, offset);

        var before = PrayerTime.DhuhaWindow(sunrise, dhuhr, sunrise.AddMinutes(10));
        Assert.NotNull(before);
        Assert.Equal(new DateTimeOffset(2026, 11, 28, 6, 43, 0, offset), before!.Value.Start);
        Assert.Equal(new DateTimeOffset(2026, 11, 28, 11, 30, 0, offset), before.Value.End);
        Assert.False(before.Value.IsActive);
        Assert.Equal(TimeSpan.Zero, before.Value.Remaining);

        var during = PrayerTime.DhuhaWindow(sunrise, dhuhr, new DateTimeOffset(2026, 11, 28, 9, 15, 0, offset));
        Assert.True(during!.Value.IsActive);
        Assert.Equal(TimeSpan.FromMinutes(135), during.Value.Remaining);
        Assert.Equal("2h 15m left", PrayerTime.FormatDhuhaRemaining(during.Value.Remaining));
        Assert.Equal("1m left", PrayerTime.FormatDhuhaRemaining(TimeSpan.FromSeconds(12)));
        Assert.Equal("", PrayerTime.FormatDhuhaRemaining(TimeSpan.Zero));

        var atEnd = PrayerTime.DhuhaWindow(sunrise, dhuhr, new DateTimeOffset(2026, 11, 28, 11, 30, 0, offset));
        Assert.False(atEnd!.Value.IsActive);

        // Extreme latitude where Dhuhr - 10m <= Sunrise + 15m
        Assert.Null(PrayerTime.DhuhaWindow(sunrise, sunrise.AddMinutes(20), sunrise));
    }

    [Fact]
    public void ActivePeriodCoversEveryFardPrayerDhuhaAndProhibitedGaps()
    {
        var offset = TimeSpan.FromHours(9);
        var fajr = new DateTimeOffset(2026, 11, 28, 4, 50, 0, offset);
        var sunrise = new DateTimeOffset(2026, 11, 28, 6, 28, 0, offset);
        var dhuhr = new DateTimeOffset(2026, 11, 28, 11, 40, 0, offset);
        var asr = new DateTimeOffset(2026, 11, 28, 14, 30, 0, offset);
        var maghrib = new DateTimeOffset(2026, 11, 28, 16, 45, 0, offset);
        var isha = new DateTimeOffset(2026, 11, 28, 18, 10, 0, offset);
        var tomorrowFajr = new DateTimeOffset(2026, 11, 29, 4, 51, 0, offset);

        // Pre-Fajr overnight Isha
        var preFajr = PrayerTime.ActivePeriod(fajr, sunrise, dhuhr, asr, maghrib, isha, tomorrowFajr, fajr.AddMinutes(-30));
        Assert.Equal(("Isha", (DateTimeOffset?)null, (DateTimeOffset?)fajr, TimeSpan.FromMinutes(30), false),
            (preFajr!.Value.Name, preFajr.Value.Start, preFajr.Value.End, preFajr.Value.Remaining, preFajr.Value.IsDhuha));

        // Fajr ends at Sunrise (Shuruq)
        var inFajr = PrayerTime.ActivePeriod(fajr, sunrise, dhuhr, asr, maghrib, isha, tomorrowFajr, fajr.AddMinutes(20));
        Assert.Equal(("Fajr", (DateTimeOffset?)fajr, (DateTimeOffset?)sunrise, sunrise - fajr.AddMinutes(20), false),
            (inFajr!.Value.Name, inFajr.Value.Start, inFajr.Value.End, inFajr.Value.Remaining, inFajr.Value.IsDhuha));

        // Prohibited Shuruq gap (Sunrise .. Sunrise + 15m)
        Assert.Null(PrayerTime.ActivePeriod(fajr, sunrise, dhuhr, asr, maghrib, isha, tomorrowFajr, sunrise.AddMinutes(5)));

        // Dhuha (Sunrise + 15m .. Dhuhr - 10m)
        var inDhuha = PrayerTime.ActivePeriod(fajr, sunrise, dhuhr, asr, maghrib, isha, tomorrowFajr, sunrise.AddMinutes(30));
        Assert.Equal(("Dhuha", (DateTimeOffset?)sunrise.AddMinutes(15), (DateTimeOffset?)dhuhr.AddMinutes(-10), true),
            (inDhuha!.Value.Name, inDhuha.Value.Start, inDhuha.Value.End, inDhuha.Value.IsDhuha));

        // Prohibited zenith gap (Dhuhr - 10m .. Dhuhr)
        Assert.Null(PrayerTime.ActivePeriod(fajr, sunrise, dhuhr, asr, maghrib, isha, tomorrowFajr, dhuhr.AddMinutes(-5)));

        // Dhuhr, Asr, Maghrib, Isha
        Assert.Equal("Dhuhr", PrayerTime.ActivePeriod(fajr, sunrise, dhuhr, asr, maghrib, isha, tomorrowFajr, dhuhr.AddMinutes(15))!.Value.Name);
        Assert.Equal("Asr", PrayerTime.ActivePeriod(fajr, sunrise, dhuhr, asr, maghrib, isha, tomorrowFajr, asr.AddMinutes(15))!.Value.Name);
        Assert.Equal("Maghrib", PrayerTime.ActivePeriod(fajr, sunrise, dhuhr, asr, maghrib, isha, tomorrowFajr, maghrib.AddMinutes(15))!.Value.Name);

        var postIsha = PrayerTime.ActivePeriod(fajr, sunrise, dhuhr, asr, maghrib, isha, tomorrowFajr, isha.AddMinutes(30));
        Assert.Equal(("Isha", (DateTimeOffset?)isha, (DateTimeOffset?)tomorrowFajr, tomorrowFajr - isha.AddMinutes(30), false),
            (postIsha!.Value.Name, postIsha.Value.Start, postIsha.Value.End, postIsha.Value.Remaining, postIsha.Value.IsDhuha));
    }

    public void Dispose(){if(Directory.Exists(_directory))Directory.Delete(_directory,true);}
}
