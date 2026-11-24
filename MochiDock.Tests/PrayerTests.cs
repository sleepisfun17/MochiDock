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
    public void Dispose(){if(Directory.Exists(_directory))Directory.Delete(_directory,true);}
}
