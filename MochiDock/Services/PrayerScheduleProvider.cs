using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using MochiDock.Models;

namespace MochiDock.Services;
public sealed record PrayerLocation(double Latitude,double Longitude,string City,string Name,string Zone);
public sealed record PrayerCacheEntry(string Key, DateTime Date, PrayerLocation Location,string Method,string Source,string Hijri,long[] Ticks);
public sealed class PrayerTimesService
{
    private readonly HttpClient _http;
    private readonly Func<DateTimeOffset> _clock;
    private readonly string _path;
    private readonly SemaphoreSlim _gate = new(1);
    private List<PrayerCacheEntry>? _cache;
    private PrayerLocation? _location;
    private long _locationVersion;
    public PrayerLocation? Location => _location;
    public PrayerTimesService(HttpClient? http=null,Func<DateTimeOffset>? clock=null,string? cachePath=null)
    {
        _http=http??new HttpClient{Timeout=TimeSpan.FromSeconds(10)};
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent",AppUserAgent.Value);
        _clock=clock??(()=>DateTimeOffset.UtcNow);
        _path=cachePath??Path.Combine(AppDataPaths.Local,"prayer-cache.json");
    }
    public async Task<bool> RestoreCachedLocationAsync(string city)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _cache??=await Task.Run(()=>AtomicJsonStore.Load(_path,()=>new List<PrayerCacheEntry>()).Value).ConfigureAwait(false);
            var remembered=_cache.LastOrDefault(e=>string.Equals(e.Location.City,city,StringComparison.OrdinalIgnoreCase));
            if(remembered==null)return false;
            var location=remembered.Location;
            if(!double.IsFinite(location.Latitude)||!double.IsFinite(location.Longitude)||Math.Abs(location.Latitude)>90||Math.Abs(location.Longitude)>180)return false;
            _=TimeZoneInfo.FindSystemTimeZoneById(location.Zone);
            Interlocked.Increment(ref _locationVersion);_location=location;return true;
        }
        finally{_gate.Release();}
    }
    public void SetCoordinates(double lat,double lon,string city,string resolvedName="")
    {
        if(!double.IsFinite(lat)||!double.IsFinite(lon)||Math.Abs(lat)>90||Math.Abs(lon)>180)throw new ArgumentException("Invalid location.");
        if(_location?.Latitude==lat&&_location.Longitude==lon&&_location.City==city)return;
        Interlocked.Increment(ref _locationVersion);
        _location=new(lat,lon,city,string.IsNullOrEmpty(resolvedName)?city:resolvedName,"");
    }
    public async Task UpdateLocationByCityAsync(string city,WeatherService? weatherService=null)
    {
        if(weatherService==null)throw new InvalidOperationException("Location resolver unavailable.");
        var result=await weatherService.ResolveLocationAsync(city);
        if(!result.Success)throw new InvalidOperationException("Cannot resolve prayer location.");
        SetCoordinates(result.Latitude,result.Longitude,city,result.ShortName);
    }
    public async Task<PrayerSchedule> FetchOrCalculateScheduleAsync(DateTime ignored,string method="MWL",CancellationToken token=default)
    {
        await _gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            var location=_location??throw new InvalidOperationException("Choose a valid prayer location.");
            long version=Interlocked.Read(ref _locationVersion);
            _cache??=(await Task.Run(()=>AtomicJsonStore.Load(_path,()=>new List<PrayerCacheEntry>()).Value,token)).TakeLast(32).ToList();
            if(string.IsNullOrEmpty(location.Zone))
            {
                try
                {
                    string url=FormattableString.Invariant($"https://api.open-meteo.com/v1/forecast?latitude={location.Latitude:F4}&longitude={location.Longitude:F4}&current=temperature_2m&timezone=auto");
                    using var document=JsonDocument.Parse(await _http.GetStringAsync(url,token).ConfigureAwait(false));
                    string zone=document.RootElement.GetProperty("timezone").GetString()??"";
                    _=TimeZoneInfo.FindSystemTimeZoneById(zone);
                    location=location with{Zone=zone};
                }
                catch(Exception ex) when(ex is not OperationCanceledException)
                {
                    var remembered=_cache.LastOrDefault(e=>e.Location.Latitude==location.Latitude&&e.Location.Longitude==location.Longitude&&e.Location.City==location.City);
                    if(remembered==null)throw new InvalidOperationException("Prayer time zone unavailable. Connect once to resolve this location.",ex);
                    location=location with{Zone=remembered.Location.Zone};
                }
            }
            if(version!=Interlocked.Read(ref _locationVersion))throw new OperationCanceledException("Location changed.");
            _location=location;
            var zoneInfo=TimeZoneInfo.FindSystemTimeZoneById(location.Zone);
            DateTime date=TimeZoneInfo.ConvertTime(_clock(),zoneInfo).Date;
            var today=await DailyAsync(location,date,method,token);
            var tomorrow=await DailyAsync(location,date.AddDays(1),method,token);
            if(version!=Interlocked.Read(ref _locationVersion))throw new OperationCanceledException("Location changed.");
            var schedule=Build(today);
            schedule.TomorrowFajr=MochiDock.Core.PrayerTime.At(date.AddDays(1).AddTicks(tomorrow.Ticks[1]),zoneInfo);
            schedule.Clock=_clock;
            schedule.UpdateCountdown();
            return schedule;
        }
        finally{_gate.Release();}
    }
    private async Task<PrayerCacheEntry> DailyAsync(PrayerLocation location,DateTime date,string method,CancellationToken token)
    {
        string key=JsonSerializer.Serialize(new{location.Latitude,location.Longitude,location.Zone,method,date});
        var existing=_cache!.LastOrDefault(e=>e.Key==key&&e.Ticks.Length==7);
        if(existing!=null&&existing.Source=="Aladhan"){try{Validate(existing.Ticks);return existing with{Source="Aladhan (cached)"};}catch(JsonException){existing=null;}}
        PrayerCacheEntry entry;
        try
        {
            int number=method switch{"MWL"=>3,"ISNA"=>2,"Egypt"=>5,"UmmAlQura"=>4,"Karachi"=>1,"Kemenag"=>20,_=>throw new ArgumentException("Unknown calculation method.")};
            string url=FormattableString.Invariant($"https://api.aladhan.com/v1/timings/{date:dd-MM-yyyy}?latitude={location.Latitude:F4}&longitude={location.Longitude:F4}&method={number}&timezonestring={Uri.EscapeDataString(location.Zone)}");
            using var doc=JsonDocument.Parse(await _http.GetStringAsync(url,token).ConfigureAwait(false));
            var data=doc.RootElement.GetProperty("data");
            if(data.GetProperty("date").GetProperty("gregorian").GetProperty("date").GetString()!=date.ToString("dd-MM-yyyy",CultureInfo.InvariantCulture))throw new JsonException("Wrong prayer date.");
            if(data.GetProperty("meta").GetProperty("timezone").GetString()!=location.Zone)throw new JsonException("Wrong prayer time zone.");
            var timings=data.GetProperty("timings");
            string[] names=["Imsak","Fajr","Sunrise","Dhuhr","Asr","Maghrib","Isha"];
            long[] times=names.Select(n=>TimeSpan.ParseExact((timings.GetProperty(n).GetString()??"").Split(' ')[0],"hh\\:mm",CultureInfo.InvariantCulture).Ticks).ToArray();
            Validate(times);
            string hijri="";
            if(data.GetProperty("date").TryGetProperty("hijri",out var h))hijri=$"{h.GetProperty("day").GetString()} {h.GetProperty("month").GetProperty("en").GetString()} {h.GetProperty("year").GetString()}";
            entry=new(key,date,location,method,"Aladhan",hijri,times);
        }
        catch(Exception ex) when(ex is not OperationCanceledException && ex is not ArgumentException)
        {
            var calculator=new LegacyPrayerCalculator{Zone=TimeZoneInfo.FindSystemTimeZoneById(location.Zone)};
            calculator.SetCoordinates(location.Latitude,location.Longitude,location.City,location.Name);
            var calculated=calculator.CalculateSchedule(date,method);
            long[] times=[calculated.Imsak.Time.Ticks,calculated.Fajr.Time.Ticks,calculated.Sunrise.Time.Ticks,calculated.Dhuhr.Time.Ticks,calculated.Asr.Time.Ticks,calculated.Maghrib.Time.Ticks,calculated.Isha.Time.Ticks];
            Validate(times);
            entry=new(key,date,location,method,"Offline calculation","",times);
        }
        _cache!.RemoveAll(e=>e.Key==key); _cache.Add(entry);_cache=_cache.TakeLast(32).ToList();
        try{await Task.Run(()=>AtomicJsonStore.Save(_path,_cache),token);}catch(IOException){ }
        return entry;
    }
    private static void Validate(long[] times)
    {
        if(times.Length!=7||times.Any(t=>t<0||t>=TimeSpan.TicksPerDay)||!times.SequenceEqual(times.Order()))throw new JsonException("Invalid prayer timings.");
    }
    private PrayerSchedule Build(PrayerCacheEntry entry)
    {
        var result=new PrayerSchedule{Date=entry.Date,Latitude=entry.Location.Latitude,Longitude=entry.Location.Longitude,City=entry.Location.Name,TimeZoneId=entry.Location.Zone,Source=entry.Source,HijriDate=entry.Hijri,Clock=_clock};
        PrayerTimeItem[] items=[result.Imsak,result.Fajr,result.Sunrise,result.Dhuhr,result.Asr,result.Maghrib,result.Isha];
        for(int i=0;i<items.Length;i++)items[i].Time=TimeSpan.FromTicks(entry.Ticks[i]);
        result.QiblaBearingText=$"Qibla {MochiDock.Core.PrayerGeometry.QiblaBearing(result.Latitude,result.Longitude):0}°";
        return result;
    }
}
