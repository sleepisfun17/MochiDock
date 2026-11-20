using System.Net;
using System.Text;
using MochiDock.Core;

namespace MochiDock.GowaRelay;

/// <summary>
/// GOWA delivers a webhook to several addresses one after another and retries a dead one for a long time, so one offline dock delays or loses the
/// receipt for the others. This relay gives GOWA ONE loopback address that always answers at once, then forwards each event to every target in
/// parallel (3 s timeout, one retry after 2 s, then dropped). Targets are read from a text file that is re-read when it changes.
/// </summary>
internal static class Program
{
    private static readonly TimeSpan TargetTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);
    private const int MaxBodyBytes = 1_048_576;
    private const long MaxLogBytes = 512 * 1024;

    private static readonly HttpClient Client = new() { Timeout = Timeout.InfiniteTimeSpan };
    private static readonly object LogGate = new();
    private static readonly object TargetGate = new();
    private static string _logPath = "";
    private static string _targetsPath = "";
    private static DateTime _targetsStamp = DateTime.MinValue;
    private static IReadOnlyList<string> _targets = Array.Empty<string>();

    private static async Task<int> Main(string[] args)
    {
        int port = 47830;
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "MochiDockGowaServer");
        _targetsPath = Path.Combine(dir, "relay-targets.txt");
        _logPath = Path.Combine(dir, "relay.log");
        for (int i = 0; i < args.Length - 1; i++)
        {
            switch (args[i])
            {
                case "--port" when int.TryParse(args[i + 1], out var p) && p is > 0 and < 65536: port = p; break;
                case "--targets": _targetsPath = args[i + 1]; break;
                case "--log": _logPath = args[i + 1]; break;
            }
        }
        try { Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_logPath))!); } catch { /* logging is best effort */ }

        using var listener = new HttpListener();
        // Loopback only: GOWA runs on this PC, so nothing outside it can reach (or spoof) the relay.
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        try { listener.Start(); }
        catch (Exception ex) { Log($"Cannot listen on 127.0.0.1:{port}: {ex.Message}"); return 2; }
        Log($"Relay started on 127.0.0.1:{port}; {Targets().Count} target(s): {string.Join(", ", Targets().Select(WebhookRelayRules.Describe))}");

        while (listener.IsListening)
        {
            HttpListenerContext context;
            try { context = await listener.GetContextAsync().ConfigureAwait(false); }
            catch (Exception ex) { Log("Listener stopped: " + ex.Message); break; }
            _ = Task.Run(() => HandleAsync(context));
        }
        return 0;
    }

    private static async Task HandleAsync(HttpListenerContext context)
    {
        byte[] body;
        List<KeyValuePair<string, string>> headers;
        try
        {
            if (context.Request.HttpMethod == "GET") { await ReplyAsync(context, 200, "{\"status\":\"ok\"}").ConfigureAwait(false); return; }
            using var buffer = new MemoryStream();
            var chunk = new byte[16 * 1024];
            int read;
            while ((read = await context.Request.InputStream.ReadAsync(chunk).ConfigureAwait(false)) > 0)
            {
                buffer.Write(chunk, 0, read);
                if (buffer.Length > MaxBodyBytes) { await ReplyAsync(context, 413, "{\"status\":\"too large\"}").ConfigureAwait(false); return; }
            }
            body = buffer.ToArray();
            headers = new List<KeyValuePair<string, string>>();
            foreach (var name in context.Request.Headers.AllKeys)
                if (name is not null && WebhookRelayRules.ShouldForwardHeader(name)) headers.Add(new(name, context.Request.Headers[name] ?? ""));
            // Answer GOWA immediately, before any target is contacted: it must never wait for (or retry because of) an offline dock.
            await ReplyAsync(context, 200, "{\"status\":\"ok\"}").ConfigureAwait(false);
        }
        catch (Exception ex) { Log("Request failed: " + ex.Message); return; }

        var targets = Targets();
        if (targets.Count == 0) { Log("Event received but there are no targets (relay-targets.txt is empty)."); return; }
        await Task.WhenAll(targets.Select(target => ForwardAsync(target, body, headers))).ConfigureAwait(false);
    }

    private static async Task ReplyAsync(HttpListenerContext context, int status, string json)
    {
        context.Response.StatusCode = status;
        context.Response.ContentType = "application/json";
        var bytes = Encoding.UTF8.GetBytes(json);
        await context.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        context.Response.Close();
    }

    private static async Task ForwardAsync(string target, byte[] body, List<KeyValuePair<string, string>> headers)
    {
        string label = WebhookRelayRules.Describe(target);
        for (int attempt = 1; attempt <= 2; attempt++)
        {
            var clock = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                using var cts = new CancellationTokenSource(TargetTimeout);
                using var request = new HttpRequestMessage(HttpMethod.Post, target) { Content = new ByteArrayContent(body) };
                foreach (var (name, value) in headers)
                    if (!request.Headers.TryAddWithoutValidation(name, value)) request.Content.Headers.TryAddWithoutValidation(name, value);
                using var response = await Client.SendAsync(request, cts.Token).ConfigureAwait(false);
                Log($"{label} -> {(int)response.StatusCode} in {clock.ElapsedMilliseconds} ms (attempt {attempt})");
                if (response.IsSuccessStatusCode || (int)response.StatusCode is 401 or 403) return; // a refused token will not change on retry
            }
            catch (Exception ex)
            {
                Log($"{label} -> failed in {clock.ElapsedMilliseconds} ms (attempt {attempt}): {WebhookRelayRules.RedactToken(ex.GetType().Name)}");
            }
            if (attempt == 1) await Task.Delay(RetryDelay).ConfigureAwait(false);
        }
        Log($"{label} -> dropped after 2 attempts");
    }

    /// <summary>The current targets; the file is read again whenever its last-write time changed, so a PC can be added without a restart.</summary>
    private static IReadOnlyList<string> Targets()
    {
        lock (TargetGate)
        {
            try
            {
                var stamp = File.Exists(_targetsPath) ? File.GetLastWriteTimeUtc(_targetsPath) : DateTime.MinValue;
                if (stamp != _targetsStamp)
                {
                    _targetsStamp = stamp;
                    _targets = stamp == DateTime.MinValue ? Array.Empty<string>() : WebhookRelayRules.ParseTargets(File.ReadAllLines(_targetsPath), out var rejected);
                    if (stamp != DateTime.MinValue)
                    {
                        WebhookRelayRules.ParseTargets(File.ReadAllLines(_targetsPath), out var bad);
                        foreach (var line in bad) Log("Ignored an invalid line in the targets file: " + line);
                        Log($"Targets reloaded: {_targets.Count} ({string.Join(", ", _targets.Select(WebhookRelayRules.Describe))})");
                    }
                }
            }
            catch (Exception ex) { Log("Cannot read the targets file: " + ex.Message); }
            return _targets;
        }
    }

    private static void Log(string message)
    {
        lock (LogGate)
        {
            try
            {
                var info = new FileInfo(_logPath);
                if (info.Exists && info.Length > MaxLogBytes) File.Move(_logPath, _logPath + ".1", overwrite: true);
                // Messages never contain a token: targets are described as host:port and redacted wherever a URL could appear.
                File.AppendAllText(_logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {WebhookRelayRules.RedactToken(message)}{Environment.NewLine}");
            }
            catch { /* a full disk must not stop the relay */ }
        }
    }
}
