using System.Collections.ObjectModel;

namespace MochiDock.Models;

public class HourlyForecast
{
    public DateTime Time { get; set; }
    public float Temp { get; set; }
    public string IconKind { get; set; } = "Clear";
    public int RainProbability { get; set; }

    public string DisplayTime => Time.ToString("h tt", System.Globalization.CultureInfo.InvariantCulture);
    public string DisplayTemp => $"{Temp:0}°";
}

public class DailyForecast
{
    public string DayName { get; set; } = "Today";
    public float MaxTemp { get; set; }
    public float MinTemp { get; set; }
    public string IconKind { get; set; } = "Clear";
    public int RainProbability { get; set; }

    public string TempRange => $"{MaxTemp:0}° / {MinTemp:0}°";
}

public class WeatherInfo
{
    public float Temperature { get; set; }
    public float WindSpeed { get; set; }
    public string Condition { get; set; } = "Clear";
    public string IconKind { get; set; } = "Clear";
    public string WeatherIcon { get; set; } = "☀️";
    public bool? IsDay { get; set; }
    public string? IconUrl { get; set; }

    public string ResolvedCityName { get; set; } = "";
    public double Latitude { get; set; }
    public double Longitude { get; set; }

    public string DisplayText => $"{Temperature:0}°C · {WindSpeed:0.0} m/s";

    public ObservableCollection<HourlyForecast> HourlyList { get; set; } = new();
    public ObservableCollection<DailyForecast> DailyList { get; set; } = new();
    /// <summary>The next two hours of precipitation in 15-minute steps (Open-Meteo only; empty from the fallback provider).</summary>
    public List<MochiDock.Core.PrecipitationSlot> Next15 { get; set; } = new();
}
