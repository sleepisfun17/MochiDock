using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using MochiDock.Core;

namespace MochiDock.Services;

/// <summary>
/// Reads the official League of Legends Live Client Data API while a match runs. The game serves it on this computer only
/// (https://127.0.0.1:2999, a self-signed certificate that is accepted for loopback addresses and nothing else). Nothing is
/// injected into the game and no game memory is read. It is started when League is the game being played and stopped when the
/// game ends; the last good answer is kept because the API disappears when the game closes.
/// </summary>
public sealed class LolLiveClientService : IDisposable
{
    public const string DefaultBaseUrl = "https://127.0.0.1:2999";
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly TimeSpan _interval;
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private GameMatchSummary? _latest;
    private DateTime _latestAtUtc;

    public LolLiveClientService(string? baseUrl = null, TimeSpan? interval = null)
    {
        _baseUrl = (baseUrl ?? DefaultBaseUrl).TrimEnd('/');
        _interval = interval ?? TimeSpan.FromSeconds(5);
        var handler = new HttpClientHandler
        {
            // The game's certificate is self-signed. Trust it only for a loopback address, never for a remote host.
            ServerCertificateCustomValidationCallback = (message, certificate, chain, errors) => message.RequestUri?.IsLoopback == true,
        };
        _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) };
    }

    /// <summary>The last match summary read from the game, or null.</summary>
    public GameMatchSummary? Latest { get { lock (_gate) return _latest; } }
    public DateTime LatestAtUtc { get { lock (_gate) return _latestAtUtc; } }
    public bool IsRunning { get { lock (_gate) return _cts != null; } }

    /// <summary>Starts polling in the background; a new match forgets the last one. Does nothing when already running.</summary>
    public void Start()
    {
        lock (_gate)
        {
            if (_cts != null) return;
            _latest = null;
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _ = Task.Run(() => LoopAsync(token));
        }
    }

    /// <summary>Stops polling. The last summary stays available.</summary>
    public void Stop()
    {
        lock (_gate)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _cts = null;
        }
    }

    private async Task LoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await PollOnceAsync(token).ConfigureAwait(false);
                await Task.Delay(_interval, token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>One request. Returns true when a match of a player was read. A closed port or an error page is the normal "no match running" answer.</summary>
    public async Task<bool> PollOnceAsync(CancellationToken token = default)
    {
        try
        {
            string json = await _http.GetStringAsync(_baseUrl + "/liveclientdata/allgamedata", token).ConfigureAwait(false);
            var match = LolMatchParser.TryParse(json);
            if (match == null) return false;
            lock (_gate) { _latest = match; _latestAtUtc = DateTime.UtcNow; }
            return true;
        }
        catch (HttpRequestException) { return false; }
        catch (TaskCanceledException) { return false; }        // the 2 second timeout, or a stop
    }

    public void Dispose()
    {
        Stop();
        _http.Dispose();
    }
}
