namespace MochiDock.Services;
// Only supplies the unused geocoding adapter signature to isolated provider tests.
public class WeatherService
{
    public Task<GeocodeResult> ResolveLocationAsync(string city) => throw new InvalidOperationException("Live geocoding is forbidden in tests.");
}
public record GeocodeResult(bool Success,double Latitude,double Longitude,string ShortName);
