using System.Collections.ObjectModel;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using MochiDock.Models;

namespace MochiDock.Services;

public class LocationGeocodeResult
{
    public bool Success { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public string DisplayName { get; set; } = "";
    public string ShortName { get; set; } = "";
}

public class WeatherService
{
    private readonly HttpClient _httpClient;

    public WeatherService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        _httpClient.DefaultRequestHeaders.Add("User-Agent", AppUserAgent.Value);
    }

    public async Task<LocationGeocodeResult> ResolveLocationAsync(string query, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return new LocationGeocodeResult { Success = false };
        }

        string trimmed = query.Trim();

        // 1. Try OpenStreetMap Nominatim API (Best for sub-cities, wards, districts like "Shibuya, Tokyo" or "Serpong, Tangerang Selatan")
        try
        {
            var nominatimUrl = $"https://nominatim.openstreetmap.org/search?q={Uri.EscapeDataString(trimmed)}&format=json&addressdetails=1&limit=1";
            using var request = new HttpRequestMessage(HttpMethod.Get, nominatimUrl);
            request.Headers.Add("User-Agent", AppUserAgent.Value);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(content);
                if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
                {
                    var item = doc.RootElement[0];
                    if (double.TryParse(item.GetProperty("lat").GetString(), CultureInfo.InvariantCulture, out double lat) &&
                        double.TryParse(item.GetProperty("lon").GetString(), CultureInfo.InvariantCulture, out double lon))
                    {
                        string dispName = item.TryGetProperty("display_name", out var dn) ? dn.GetString() ?? trimmed : trimmed;
                        
                        // Extract concise short name if possible
                        string shortName = trimmed;
                        if (item.TryGetProperty("address", out var addr))
                        {
                            string sub = addr.TryGetProperty("suburb", out var s) ? s.GetString() ?? "" :
                                         addr.TryGetProperty("neighbourhood", out var n) ? n.GetString() ?? "" :
                                         addr.TryGetProperty("village", out var v) ? v.GetString() ?? "" : "";
                            string city = addr.TryGetProperty("city", out var c) ? c.GetString() ?? "" :
                                          addr.TryGetProperty("town", out var t) ? t.GetString() ?? "" :
                                          addr.TryGetProperty("county", out var co) ? co.GetString() ?? "" : "";
                            string country = addr.TryGetProperty("country", out var cnt) ? cnt.GetString() ?? "" : "";

                            if (!string.IsNullOrEmpty(sub) && !string.IsNullOrEmpty(city))
                                shortName = $"{sub}, {city}";
                            else if (!string.IsNullOrEmpty(city) && !string.IsNullOrEmpty(country))
                                shortName = $"{city}, {country}";
                        }

                        return new LocationGeocodeResult
                        {
                            Success = true,
                            Latitude = lat,
                            Longitude = lon,
                            DisplayName = dispName,
                            ShortName = shortName
                        };
                    }
                }
            }
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Weather", logEx); }

        // 2. Try Open-Meteo Geocoding API as fallback
        try
        {
            // If comma-separated, also try the first token as fallback
            string[] searchCandidates = trimmed.Contains(',')
                ? new[] { trimmed, trimmed.Split(',')[0].Trim(), trimmed.Split(',')[^1].Trim() }
                : new[] { trimmed };

            foreach (var cand in searchCandidates)
            {
                if (string.IsNullOrWhiteSpace(cand)) continue;
                var geoUrl = $"https://geocoding-api.open-meteo.com/v1/search?name={Uri.EscapeDataString(cand)}&count=1";
                var geoResponse = await _httpClient.GetStringAsync(geoUrl, cancellationToken);
                using var geoDoc = JsonDocument.Parse(geoResponse);
                if (geoDoc.RootElement.TryGetProperty("results", out var results) && results.GetArrayLength() > 0)
                {
                    var loc = results[0];
                    double lat = loc.GetProperty("latitude").GetDouble();
                    double lon = loc.GetProperty("longitude").GetDouble();
                    string name = loc.TryGetProperty("name", out var n) ? n.GetString() ?? cand : cand;
                    string country = loc.TryGetProperty("country", out var c) ? c.GetString() ?? "" : "";
                    string admin1 = loc.TryGetProperty("admin1", out var a) ? a.GetString() ?? "" : "";

                    string disp = !string.IsNullOrEmpty(country) ? $"{name}, {(!string.IsNullOrEmpty(admin1) ? admin1 + ", " : "")}{country}" : name;

                    return new LocationGeocodeResult
                    {
                        Success = true,
                        Latitude = lat,
                        Longitude = lon,
                        DisplayName = disp,
                        ShortName = $"{name}, {country}".TrimEnd(',', ' ')
                    };
                }
            }
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Weather", logEx); }

        return new LocationGeocodeResult { Success = false };
    }

    public async Task<WeatherInfo?> GetWeatherAsync(string city, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(city))
        {
            city = "Tokyo";
        }

        var geo = await ResolveLocationAsync(city, cancellationToken);
        if (geo.Success)
        {
            try
            {
                string latStr = geo.Latitude.ToString("F4", CultureInfo.InvariantCulture);
                string lonStr = geo.Longitude.ToString("F4", CultureInfo.InvariantCulture);

                var weatherUrl = $"https://api.open-meteo.com/v1/forecast?latitude={latStr}&longitude={lonStr}&current=temperature_2m,wind_speed_10m,weather_code,is_day&hourly=temperature_2m,weather_code,precipitation_probability&daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max&minutely_15=precipitation,weather_code&forecast_minutely_15=8&forecast_days=3&wind_speed_unit=ms&timezone=auto";
                var weatherResponse = await _httpClient.GetStringAsync(weatherUrl, cancellationToken);
                string resolvedName = !string.IsNullOrEmpty(geo.ShortName) ? geo.ShortName : city;
                if (OpenMeteoForecastParser.Parse(weatherResponse, resolvedName, geo.Latitude, geo.Longitude) is { } parsed)
                    return parsed;
            }
            catch (OperationCanceledException) { throw; }
            catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Weather", logEx); }
        }

        // 3. Fallback to wttr.in
        try
        {
            var response = await _httpClient.GetStringAsync($"https://wttr.in/{Uri.EscapeDataString(city)}?format=j1", cancellationToken);
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;
            var current = root.GetProperty("current_condition")[0];
            string conditionText = current.GetProperty("weatherDesc")[0].GetProperty("value").GetString() ?? "Clear";
            var (icon, kind) = MapConditionToIconAndKind(conditionText);

            var weatherInfo = new WeatherInfo
            {
                Temperature = float.Parse(current.GetProperty("temp_C").GetString() ?? "0", CultureInfo.InvariantCulture),
                WindSpeed = float.Parse(current.GetProperty("windspeedKmph").GetString() ?? "0", CultureInfo.InvariantCulture) / 3.6f,
                Condition = conditionText,
                WeatherIcon = icon,
                IconKind = kind,
                ResolvedCityName = city
            };

            // Parse wttr.in daily (3 days)
            if (root.TryGetProperty("weather", out var weatherDays))
            {
                int count = Math.Min(3, weatherDays.GetArrayLength());
                for (int i = 0; i < count; i++)
                {
                    var d = weatherDays[i];
                    string dayName = i == 0 ? "Today" : (i == 1 ? "Tomorrow" : (DateTime.TryParse(d.GetProperty("date").GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) ? dt.ToString("ddd", CultureInfo.InvariantCulture) : $"Day {i+1}"));
                    float maxT = float.Parse(d.GetProperty("maxtempC").GetString() ?? "0", CultureInfo.InvariantCulture);
                    float minT = float.Parse(d.GetProperty("mintempC").GetString() ?? "0", CultureInfo.InvariantCulture);
                    var dHourly = d.GetProperty("hourly");
                    string dDesc = dHourly.GetArrayLength() > 4 ? dHourly[4].GetProperty("weatherDesc")[0].GetProperty("value").GetString() ?? "Clear" : "Clear";
                    var (_, dKind) = MapConditionToIconAndKind(dDesc);

                    weatherInfo.DailyList.Add(new DailyForecast
                    {
                        DayName = dayName,
                        MaxTemp = maxT,
                        MinTemp = minT,
                        IconKind = dKind
                    });
                }
            }

            return weatherInfo;
        }
        catch (OperationCanceledException) { throw; }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Weather", logEx); }

        return null;
    }

    private static (string Icon, string Kind) MapConditionToIconAndKind(string desc)
    {
        string lower = desc.ToLowerInvariant();
        if (lower.Contains("thunder")) return ("⛈️", "Thunder");
        if (lower.Contains("snow") || lower.Contains("ice") || lower.Contains("blizzard")) return ("🌨️", "Snow");
        if (lower.Contains("drizzle") || lower.Contains("shower")) return ("🌦️", "Drizzle");
        if (lower.Contains("rain")) return ("🌧️", "Rain");
        if (lower.Contains("overcast") || lower.Contains("cloudy") && !lower.Contains("partly")) return ("☁️", "Cloudy");
        if (lower.Contains("cloud")) return ("⛅", "PartlyCloudy");
        if (lower.Contains("fog") || lower.Contains("mist")) return ("🌫️", "Fog");
        return ("☀️", "Clear");
    }
}
