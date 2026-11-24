using System.Globalization;
using System.Text.Json;
using MochiDock.Models;

namespace MochiDock.Services;

public static class OpenMeteoForecastParser
{
    public static WeatherInfo? Parse(string json, string resolvedCityName, double latitude, double longitude, DateTime? nowLocal = null)
    {
        using var weatherDoc = JsonDocument.Parse(json);
        var root = weatherDoc.RootElement;
        if (!root.TryGetProperty("current", out var current) || current.ValueKind != JsonValueKind.Object) return null;
        if (!TryGetNumber(current, "temperature_2m", out double currentTemp)) return null;
        double currentWind = TryGetNumber(current, "wind_speed_10m", out double wind) ? wind : 0;

        // timezone=auto: times are the city's local time (so "Today" is the city's day); this offset turns them into instants.
        int utcOffset = root.TryGetProperty("utc_offset_seconds", out var offsetEl) && offsetEl.ValueKind == JsonValueKind.Number
            ? offsetEl.GetInt32()
            : 0;

        int weatherCode = current.TryGetProperty("weather_code", out var codeEl) && codeEl.ValueKind == JsonValueKind.Number
            ? codeEl.GetInt32()
            : 0;
        bool? isDay = current.TryGetProperty("is_day", out var dayEl) && dayEl.ValueKind == JsonValueKind.Number
            ? dayEl.GetInt32() != 0
            : null;
        var (condition, icon, kind) = DecodeWmoWeatherCode(weatherCode);

        var weatherInfo = new WeatherInfo
        {
            Temperature = (float)currentTemp,
            WindSpeed = (float)currentWind,
            Condition = condition,
            WeatherIcon = icon,
            IconKind = kind,
            IsDay = isDay,
            ResolvedCityName = resolvedCityName,
            Latitude = latitude,
            Longitude = longitude
        };

        // Parse Hourly (Next 12 hours), skipping slots with null/missing values instead of failing the forecast.
        if (root.TryGetProperty("hourly", out var hourly) && hourly.ValueKind == JsonValueKind.Object &&
            hourly.TryGetProperty("time", out var times) && times.ValueKind == JsonValueKind.Array &&
            hourly.TryGetProperty("temperature_2m", out var temps) && temps.ValueKind == JsonValueKind.Array &&
            hourly.TryGetProperty("weather_code", out var codes) && codes.ValueKind == JsonValueKind.Array)
        {
            var rainProbs = hourly.TryGetProperty("precipitation_probability", out var rp) ? rp : default;
            var now = nowLocal ?? DateTime.Now;
            int count = Math.Min(times.GetArrayLength(), Math.Min(temps.GetArrayLength(), codes.GetArrayLength()));
            int added = 0;

            for (int i = 0; i < count && added < 12; i++)
            {
                if (times[i].ValueKind != JsonValueKind.String || temps[i].ValueKind != JsonValueKind.Number) continue;
                if (MochiDock.Core.RainSoonPolicy.ParseTime(times[i].GetString(), utcOffset) is not { } at) continue;
                var dt = at.LocalDateTime;
                if (dt < now.AddHours(-0.5)) continue;

                int hCode = codes[i].ValueKind == JsonValueKind.Number ? codes[i].GetInt32() : 0;
                var (_, _, hKind) = DecodeWmoWeatherCode(hCode);
                int rainP = rainProbs.ValueKind == JsonValueKind.Array && i < rainProbs.GetArrayLength() && rainProbs[i].ValueKind == JsonValueKind.Number
                    ? rainProbs[i].GetInt32()
                    : 0;

                weatherInfo.HourlyList.Add(new HourlyForecast
                {
                    Time = dt,
                    Temp = (float)temps[i].GetDouble(),
                    IconKind = hKind,
                    RainProbability = rainP
                });
                added++;
            }
        }

        // The next two hours in 15-minute steps, for the "rain soon" notice.
        if (root.TryGetProperty("minutely_15", out var quarter) && quarter.ValueKind == JsonValueKind.Object &&
            quarter.TryGetProperty("time", out var qTimes) && qTimes.ValueKind == JsonValueKind.Array)
        {
            var qRain = quarter.TryGetProperty("precipitation", out var qp) ? qp : default;
            var qCodes = quarter.TryGetProperty("weather_code", out var qc) ? qc : default;
            for (int i = 0; i < qTimes.GetArrayLength(); i++)
            {
                if (qTimes[i].ValueKind != JsonValueKind.String) continue;
                if (MochiDock.Core.RainSoonPolicy.ParseTime(qTimes[i].GetString(), utcOffset) is not { } start) continue;
                double mm = qRain.ValueKind == JsonValueKind.Array && i < qRain.GetArrayLength() && qRain[i].ValueKind == JsonValueKind.Number
                    ? qRain[i].GetDouble()
                    : 0;
                int qCode = qCodes.ValueKind == JsonValueKind.Array && i < qCodes.GetArrayLength() && qCodes[i].ValueKind == JsonValueKind.Number
                    ? qCodes[i].GetInt32()
                    : 0;
                weatherInfo.Next15.Add(new MochiDock.Core.PrecipitationSlot(start, mm, qCode));
            }
        }

        // Parse Daily (3 Days)
        if (root.TryGetProperty("daily", out var daily) && daily.ValueKind == JsonValueKind.Object &&
            daily.TryGetProperty("time", out var dates) && dates.ValueKind == JsonValueKind.Array &&
            daily.TryGetProperty("temperature_2m_max", out var maxTemps) && maxTemps.ValueKind == JsonValueKind.Array &&
            daily.TryGetProperty("temperature_2m_min", out var minTemps) && minTemps.ValueKind == JsonValueKind.Array &&
            daily.TryGetProperty("weather_code", out var dCodes) && dCodes.ValueKind == JsonValueKind.Array)
        {
            var dRain = daily.TryGetProperty("precipitation_probability_max", out var dr) ? dr : default;
            int dCount = Math.Min(3, Math.Min(dates.GetArrayLength(), Math.Min(maxTemps.GetArrayLength(), Math.Min(minTemps.GetArrayLength(), dCodes.GetArrayLength()))));
            for (int i = 0; i < dCount; i++)
            {
                if (maxTemps[i].ValueKind != JsonValueKind.Number || minTemps[i].ValueKind != JsonValueKind.Number) continue;
                string? dateStr = dates[i].ValueKind == JsonValueKind.String ? dates[i].GetString() : null;
                string dayName = i == 0 ? "Today" : (i == 1 ? "Tomorrow" : (DateTime.TryParse(dateStr, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) ? dt.ToString("ddd", CultureInfo.InvariantCulture) : $"Day {i + 1}"));
                int dCode = dCodes[i].ValueKind == JsonValueKind.Number ? dCodes[i].GetInt32() : 0;
                var (_, _, dKind) = DecodeWmoWeatherCode(dCode);
                int rainP = dRain.ValueKind == JsonValueKind.Array && i < dRain.GetArrayLength() && dRain[i].ValueKind == JsonValueKind.Number
                    ? dRain[i].GetInt32()
                    : 0;

                weatherInfo.DailyList.Add(new DailyForecast
                {
                    DayName = dayName,
                    MaxTemp = (float)maxTemps[i].GetDouble(),
                    MinTemp = (float)minTemps[i].GetDouble(),
                    IconKind = dKind,
                    RainProbability = rainP
                });
            }
        }

        return weatherInfo;
    }

    public static (string Condition, string Icon, string Kind) DecodeWmoWeatherCode(int code) => code switch
    {
        0 => ("Clear Sky", "☀️", "Clear"),
        1 => ("Mainly Clear", "🌤️", "PartlyCloudy"),
        2 => ("Partly Cloudy", "⛅", "PartlyCloudy"),
        3 => ("Overcast", "☁️", "Cloudy"),
        45 or 48 => ("Foggy", "🌫️", "Fog"),
        51 or 53 or 55 => ("Drizzle", "🌦️", "Drizzle"),
        56 or 57 => ("Freezing Drizzle", "🌧️", "Rain"),
        61 or 63 or 65 => ("Rain", "🌧️", "Rain"),
        66 or 67 => ("Freezing Rain", "🌧️", "Rain"),
        71 or 73 or 75 => ("Snowfall", "🌨️", "Snow"),
        77 => ("Snow Grains", "🌨️", "Snow"),
        80 or 81 or 82 => ("Rain Showers", "🌦️", "Drizzle"),
        85 or 86 => ("Snow Showers", "🌨️", "Snow"),
        95 => ("Thunderstorm", "⛈️", "Thunder"),
        96 or 99 => ("Thunderstorm w/ Hail", "⛈️", "Thunder"),
        _ => ("Clear", "☀️", "Clear")
    };

    private static bool TryGetNumber(JsonElement obj, string name, out double value)
    {
        if (obj.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.Number)
        {
            value = el.GetDouble();
            return true;
        }
        value = 0;
        return false;
    }
}
