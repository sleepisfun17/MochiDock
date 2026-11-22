using MochiDock.Services;
using Xunit;

namespace MochiDock.Tests;

public class WeatherServiceTests
{
    [Fact]
    public void ParseOpenMeteoForecast_SkipsNullHourlyAndDailyEntries_WithoutFailingForecast()
    {
        var now = new DateTime(2026, 11, 22, 12, 0, 0, DateTimeKind.Local);
        string t1 = now.AddHours(1).ToString("yyyy-MM-ddTHH:00");
        string t2 = now.AddHours(2).ToString("yyyy-MM-ddTHH:00");
        string t3 = now.AddHours(3).ToString("yyyy-MM-ddTHH:00");

        string json = $$"""
        {
          "utc_offset_seconds": 32400,
          "current": {
            "temperature_2m": 18.5,
            "wind_speed_10m": 3.2,
            "weather_code": 2
          },
          "hourly": {
            "time": ["{{t1}}", "{{t2}}", "{{t3}}"],
            "temperature_2m": [19.0, null, 20.5],
            "weather_code": [1, 3, null],
            "precipitation_probability": [10, null, null]
          },
          "minutely_15": {
            "time": ["{{t1}}", "{{t2}}"],
            "precipitation": [null, 0.4],
            "weather_code": [null, 61]
          },
          "daily": {
            "time": ["2026-11-22", "2026-11-23", "2026-11-24"],
            "temperature_2m_max": [21.0, null, 22.0],
            "temperature_2m_min": [12.0, 13.0, 14.0],
            "weather_code": [1, 3, null],
            "precipitation_probability_max": [20, 50, null]
          }
        }
        """;

        var info = OpenMeteoForecastParser.Parse(json, "Tokyo", 35.68, 139.76, now);

        Assert.NotNull(info);
        Assert.Equal(18.5f, info!.Temperature);
        Assert.Equal(3.2f, info.WindSpeed);

        // Hourly entry 2 has null temperature_2m and is skipped; entry 3 has null weather_code/rain_prob and defaults safely.
        Assert.Equal(2, info.HourlyList.Count);
        Assert.Equal(19.0f, info.HourlyList[0].Temp);
        Assert.Equal(10, info.HourlyList[0].RainProbability);
        Assert.Equal(20.5f, info.HourlyList[1].Temp);
        Assert.Equal(0, info.HourlyList[1].RainProbability);

        // Daily entry 2 has null temperature_2m_max and is skipped; entry 3 has null code/rain_prob and defaults safely.
        Assert.Equal(2, info.DailyList.Count);
        Assert.Equal("Today", info.DailyList[0].DayName);
        Assert.Equal(20, info.DailyList[0].RainProbability);
        Assert.Equal("Tue", info.DailyList[1].DayName);
        Assert.Equal(0, info.DailyList[1].RainProbability);

        Assert.Equal(2, info.Next15.Count);
        Assert.Equal(0.4, info.Next15[1].Millimetres);
    }
}
