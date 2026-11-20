using System.Net.NetworkInformation;

namespace MochiDock.Services;

public class NetworkService
{
    private readonly Ping _pinger = new();
    private long _lastPingMs = -1;

    public long LastPingMs => _lastPingMs;

    public async Task<long> CheckPingAsync(string host = "1.1.1.1")
    {
        try
        {
            var reply = await _pinger.SendPingAsync(host, 1200);
            if (reply.Status == IPStatus.Success)
            {
                _lastPingMs = reply.RoundtripTime;
                return _lastPingMs;
            }
        }
        catch { }

        _lastPingMs = -1;
        return -1;
    }
}
