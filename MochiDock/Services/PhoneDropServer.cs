using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MochiDock.Core;

namespace MochiDock.Services;

public sealed record DropProposal(string Id, IReadOnlyList<DropFile> Files);

// This adapter has no WPF dependency so its actual HTTP/file behavior can be tested in isolation.
public sealed class PhoneDropServer
{
    private sealed class Batch(DropFile[] files, DateTimeOffset now)
    {
        public DropFile[] Files { get; } = files;
        public DateTimeOffset Created { get; } = now;
        public string State = "pending";
        public HashSet<int> Claimed { get; } = new();
        public int Completed;
    }
    private readonly string _folder;
    private readonly Func<DateTimeOffset> _now;
    private readonly TimeSpan _uploadIdleTimeout;
    private readonly TimeSpan _stopDrainTimeout;
    private static readonly TimeSpan ResponseWriteTimeout = TimeSpan.FromSeconds(10);
    internal Func<HttpListenerResponse, byte[], CancellationToken, Task>? WriteResponseOverride { get; set; }
    private readonly object _gate = new();
    private readonly SemaphoreSlim _lifecycle = new(1);
    private readonly ConcurrentDictionary<long, Task> _requests = new();
    private readonly Dictionary<string, Batch> _batches = new();
    private HttpListener? _listener;
    private CancellationTokenSource? _stop;
    private Task? _accept, _expiry;
    private string _secret = "";
    private DateTimeOffset _lastActivity;
    private int _uploads;
    private long _requestId;
    public bool IsRunning { get; private set; }
    public string Url { get; private set; } = "";
    public bool HasPendingApproval { get { lock (_gate) return _batches.Values.Any(b => b.State == "pending"); } }
    public string Status { get; private set; } = "Phone drop stopped";
    public event Action? Changed;
    public event Action<DropProposal>? ApprovalRequested;
    public event Action<string>? FileReceived;
    public PhoneDropServer(string folder, Func<DateTimeOffset>? now = null, TimeSpan? uploadIdleTimeout = null, TimeSpan? stopDrainTimeout = null) { _folder = Path.GetFullPath(folder); _now = now ?? (() => DateTimeOffset.UtcNow); _uploadIdleTimeout = uploadIdleTimeout ?? TimeSpan.FromSeconds(60); _stopDrainTimeout = stopDrainTimeout ?? TimeSpan.FromSeconds(5); }

    public async Task StartAsync(IPAddress address, int port)
    {
        await _lifecycle.WaitAsync();
        try
        {
            if (IsRunning) return;
            Directory.CreateDirectory(_folder);
            var listener = new HttpListener();
            listener.Prefixes.Add($"http://{address}:{port}/");
            try { listener.Start(); }
            catch { listener.Close(); SetStatus("Cannot open phone drop. Check the selected network address and port."); throw; }
            _listener = listener;
            _stop = new CancellationTokenSource();
            lock (_gate)
            {
                _secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
                _lastActivity = _now(); _batches.Clear(); IsRunning = true;
                Url = $"http://{address}:{port}/#" + _secret;
            }
            SetStatus("Ready — each transfer needs approval on this PC");
            _accept = AcceptAsync(listener, _stop.Token);
            _expiry = ExpireAsync(_stop.Token);
        }
        finally { _lifecycle.Release(); }
    }

    public Task StopAsync() => StopCoreAsync(null);
    private async Task StopCoreAsync(CancellationToken? expectedSession)
    {
        await _lifecycle.WaitAsync();
        try
        {
            if (expectedSession.HasValue && (_stop == null || expectedSession.Value != _stop.Token)) return;
            lock (_gate) { IsRunning = false; _secret = ""; Url = ""; _batches.Clear(); }
            _stop?.Cancel(); _listener?.Close();
            if (_accept != null) try { await _accept.ConfigureAwait(false); } catch (Exception logEx) { MochiDock.Services.AppLog.Warn("PhoneDrop", logEx); }
            try { await Task.WhenAll(_requests.Values.ToArray()).WaitAsync(_stopDrainTimeout).ConfigureAwait(false); }
            catch (TimeoutException) { MochiDock.Services.AppLog.Warn("PhoneDrop", "Timed out waiting for in-flight requests during stop."); }
            catch (Exception logEx) { MochiDock.Services.AppLog.Warn("PhoneDrop", logEx); }
            _requests.Clear();
            _stop?.Dispose(); _stop = null; _listener = null;
            SetStatus("Phone drop stopped");
        }
        finally { _lifecycle.Release(); }
    }
    public bool Answer(string id, bool approve)
    {
        lock (_gate)
        {
            if (!IsRunning || !_batches.TryGetValue(id, out var batch) || batch.State != "pending" || _now() - batch.Created >= TimeSpan.FromSeconds(60)) return false;
            batch.State = approve ? "approved" : "rejected";
        }
        SetStatus(approve ? "Transfer approved — waiting for files" : "Transfer rejected"); return true;
    }
    private void SetStatus(string status) { Status = status; Changed?.Invoke(); }
    private async Task ExpireAsync(CancellationToken token)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                bool expire;
                lock (_gate)
                {
                    expire = _now() - _lastActivity >= TimeSpan.FromMinutes(15);
                    foreach (var batch in _batches.Values)
                        if (batch.State == "pending" && _now() - batch.Created >= TimeSpan.FromSeconds(60)) batch.State = "expired";
                }
                Changed?.Invoke();
                if (expire) { await StopCoreAsync(token).ConfigureAwait(false); return; }
            }
        }
        catch (OperationCanceledException) { }
    }
    private async Task AcceptAsync(HttpListener listener, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var context = await listener.GetContextAsync().ConfigureAwait(false);
                if (_requests.Count >= 8) { context.Response.StatusCode = 429; context.Response.Close(); continue; }
                long id = Interlocked.Increment(ref _requestId);
                var task = HandleAsync(context, token);
                _requests[id] = task;
                _ = task.ContinueWith(completed => _requests.TryRemove(id, out var removed), TaskScheduler.Default);
            }
        }
        catch (Exception ex) when (ex is HttpListenerException or ObjectDisposedException or OperationCanceledException) { }
    }
    private bool Authorize(HttpListenerRequest request)
    {
        lock (_gate)
        {
            string actual = request.Headers["Authorization"] ?? "";
            string expected = "Bearer " + _secret;
            if (!IsRunning || _now() - _lastActivity >= TimeSpan.FromMinutes(15) || actual.Length != expected.Length || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(actual), Encoding.UTF8.GetBytes(expected))) return false;
            _lastActivity = _now(); return true;
        }
    }
    private async Task WriteResponseAsync(HttpListenerResponse response, byte[] bytes, CancellationToken token)
    {
        if (WriteResponseOverride != null) { await WriteResponseOverride(response, bytes, token).ConfigureAwait(false); return; }
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(ResponseWriteTimeout);
        using var abortOnTimeout = timeout.Token.Register(() => { try { response.Abort(); } catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("PhoneDrop", logEx); } });
        await response.OutputStream.WriteAsync(bytes, timeout.Token).ConfigureAwait(false);
    }
    private Task JsonAsync(HttpListenerResponse response, int status, object value, CancellationToken token = default)
    {
        response.StatusCode = status; response.ContentType = "application/json";
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        return WriteResponseAsync(response, bytes, token);
    }
    private async Task HandleAsync(HttpListenerContext context, CancellationToken token)
    {
        var request = context.Request; var response = context.Response;
        using var stopped = token.Register(() => { try { response.Abort(); } catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("PhoneDrop", logEx); } });
        try
        {
            response.Headers["Cache-Control"] = "no-store";
            response.Headers["Referrer-Policy"] = "no-referrer";
            response.Headers["X-Content-Type-Options"] = "nosniff";
            if (request.HttpMethod == "GET" && request.Url?.AbsolutePath == "/")
            {
                response.ContentType = "text/html; charset=utf-8";
                await WriteResponseAsync(response, Encoding.UTF8.GetBytes(Page), token); return;
            }
            if (!Authorize(request)) { await JsonAsync(response, 403, new { error = "Session link expired or invalid. Start phone drop and copy a fresh link." }, token); return; }
            string[] route = (request.Url?.AbsolutePath ?? "").Trim('/').Split('/');
            if (request.HttpMethod == "POST" && route.Length == 1 && route[0] == "requests")
            {
                if (request.ContentLength64 < 0) { await JsonAsync(response, 411, new { error = "Content length required." }, token); return; }
                if (request.ContentLength64 > 16384) { await JsonAsync(response, 413, new { error = "Request too large." }, token); return; }
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(60));
                using var metadataTimeout = timeout.Token.Register(() => { try { response.Abort(); } catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("PhoneDrop", logEx); } });
                var files = await JsonSerializer.DeserializeAsync<DropFile[]>(request.InputStream, new JsonSerializerOptions(JsonSerializerDefaults.Web), timeout.Token) ?? throw new ArgumentException("Missing files.");
                var safe = PhoneDropPolicy.Validate(files);
                string id = Guid.NewGuid().ToString("N");
                lock (_gate)
                {
                    if (!IsRunning || token.IsCancellationRequested) throw new OperationCanceledException();
                    if (_batches.Values.Any(b => b.State == "pending") || _batches.Values.Count(b => b.State == "approved") >= 2) throw new InvalidOperationException("Another transfer is awaiting approval or completion.");
                    foreach (string old in _batches.Where(p => p.Value.State is "rejected" or "expired" or "completed" or "failed").Select(p => p.Key).ToArray()) _batches.Remove(old);
                    _batches[id] = new Batch(safe, _now());
                }
                ApprovalRequested?.Invoke(new(id, safe));
                await JsonAsync(response, 202, new { id, state = "pending" }, token); return;
            }
            if (route.Length == 2 && route[0] == "requests" && request.HttpMethod == "GET")
            {
                string state;
                lock (_gate)
                {
                    if (!_batches.TryGetValue(route[1], out var batch)) { response.StatusCode = 404; return; }
                    if (batch.State == "pending" && _now() - batch.Created >= TimeSpan.FromSeconds(60)) batch.State = "expired";
                    state = batch.State;
                }
                await JsonAsync(response, 200, new { state }, token); return;
            }
            if (route.Length == 3 && route[0] == "uploads" && request.HttpMethod == "POST" && int.TryParse(route[2], out int index))
            { await UploadAsync(context, route[1], index, token); return; }
            response.StatusCode = 404;
        }
        catch (ArgumentException ex) { await TryErrorAsync(response, 400, ex.Message, token); }
        catch (JsonException) { await TryErrorAsync(response, 400, "Invalid file proposal.", token); }
        catch (InvalidOperationException ex) { await TryErrorAsync(response, 409, ex.Message, token); }
        catch (OperationCanceledException) { SetStatus("Transfer stopped or timed out; request approval again"); await TryErrorAsync(response, 408, "Transfer stopped or timed out.", token); }
        catch (Exception ex) when (ex is IOException or HttpListenerException or UnauthorizedAccessException or ObjectDisposedException)
        { SetStatus("Transfer failed; no incomplete file was added"); await TryErrorAsync(response, 500, "Transfer could not be saved. Check available space and try again."); }
        finally { try { response.Close(); } catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("PhoneDrop", logEx); } }
    }
    private async Task TryErrorAsync(HttpListenerResponse response, int code, string error, CancellationToken token = default)
    { try { await JsonAsync(response, code, new { error }, token); } catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("PhoneDrop", logEx); } }
    private async Task UploadAsync(HttpListenerContext context, string id, int index, CancellationToken token)
    {
        Batch batch; DropFile file;
        lock (_gate)
        {
            if (!_batches.TryGetValue(id, out batch!) || batch.State != "approved" || index < 0 || index >= batch.Files.Length) throw new InvalidOperationException("Transfer is not approved.");
            file = batch.Files[index];
            if (context.Request.ContentLength64 < 0) { context.Response.StatusCode = 411; return; }
            if (context.Request.ContentLength64 != file.Size) throw new ArgumentException("Upload size does not match approval.");
            if (_uploads >= 2) { context.Response.StatusCode = 429; return; }
            if (!batch.Claimed.Add(index)) throw new InvalidOperationException("File already uploaded or in progress.");
            _uploads++;
        }
        string temporary = Path.Combine(_folder, ".partial-" + Guid.NewGuid().ToString("N"));
        bool committed = false;
        try
        {
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true))
            {
                byte[] buffer = new byte[65536]; long received = 0; long progressTick = 0;
                while (received < file.Size)
                {
                    using var idle = CancellationTokenSource.CreateLinkedTokenSource(token); idle.CancelAfter(_uploadIdleTimeout);
                    // HttpListener can ignore cancellation on a pending native read; abort the response to release it.
                    using var interrupted = idle.Token.Register(() => { try { context.Response.Abort(); } catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("PhoneDrop", logEx); } });
                    int read;
                    try { read = await context.Request.InputStream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, file.Size - received)), idle.Token); }
                    catch (Exception) when (idle.IsCancellationRequested) { throw new OperationCanceledException(idle.Token); }
                    if (read == 0) throw new IOException("Incomplete upload.");
                    await output.WriteAsync(buffer.AsMemory(0, read), token); received += read;
                    lock (_gate) _lastActivity = _now();
                    if (System.Diagnostics.Stopwatch.GetElapsedTime(progressTick) >= TimeSpan.FromMilliseconds(200))
                    {
                        progressTick = System.Diagnostics.Stopwatch.GetTimestamp();
                        SetStatus($"Receiving file {index + 1}/{batch.Files.Length}: {received * 100 / Math.Max(1, file.Size)}%");
                    }
                }
                await output.FlushAsync(token);
            }
            string destination;
            lock (_gate)
            {
                token.ThrowIfCancellationRequested();
                if (!IsRunning) throw new OperationCanceledException();
                destination = CommitFile(temporary, file.Name);
                batch.Completed++;
                if (batch.Completed == batch.Files.Length) batch.State = "completed";
                committed = true;
            }
            FileReceived?.Invoke(destination);
            SetStatus("File received — " + batch.Completed + "/" + batch.Files.Length);
        }
        catch when (!committed) { lock (_gate) batch.State = "failed"; throw; }
        finally { lock (_gate) _uploads--; if (File.Exists(temporary)) File.Delete(temporary); }
        await JsonAsync(context.Response, 200, new { success = true }, token);
    }
    private string CommitFile(string temporary, string name)
    {
        for (int suffix = 0; suffix < 10000; suffix++)
        {
            string candidate = suffix == 0 ? name : Path.GetFileNameWithoutExtension(name) + $" ({suffix})" + Path.GetExtension(name);
            string destination = Path.GetFullPath(Path.Combine(_folder, candidate));
            if (!destination.StartsWith(_folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new IOException("Invalid destination.");
            try { File.Move(temporary, destination); return destination; }
            catch (IOException) when (File.Exists(destination)) { }
        }
        throw new IOException("Too many matching file names.");
    }
    private const string Page = """
<!doctype html><html><head><meta name="viewport" content="width=device-width,initial-scale=1"><title>MochiDock Phone Drop</title><style>body{font:16px system-ui;background:#101016;color:#fff;max-width:36em;margin:auto;padding:24px}button,input{font:inherit;margin:12px 0}#status{white-space:pre-wrap}</style></head><body><h2>Send files to MochiDock</h2><p>Same Wi-Fi. Approve filenames and sizes on your PC before sending. Up to 10 files, 512 MiB each, 1 GiB total.</p><input id="files" type="file" multiple><br><button id="send">Request approval</button><p id="status">Choose files to begin.</p><script>
const token=location.hash.slice(1);history.replaceState(null,'',location.pathname);const status=document.querySelector('#status'),button=document.querySelector('#send');
async function call(path,options={}){options.headers={...options.headers,Authorization:'Bearer '+token};const r=await fetch(path,options);let data;try{data=await r.json()}catch{throw Error('Server stopped or unavailable. Copy a fresh link on your PC.')}if(!r.ok)throw Error(data.error||'Transfer rejected ('+r.status+')');return data}
button.onclick=async()=>{button.disabled=true;try{const files=[...document.querySelector('#files').files];if(!files.length||files.length>10||files.some(f=>f.size>536870912)||files.reduce((a,f)=>a+f.size,0)>1073741824)throw Error('Choose 1–10 files within the size limits.');const request=await call('/requests',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify(files.map(f=>({name:f.name,size:f.size})))});status.textContent='Waiting for approval on your PC…';let state='pending';while(state==='pending'){await new Promise(r=>setTimeout(r,1000));state=(await call('/requests/'+request.id)).state}if(state!=='approved')throw Error('Transfer '+state+'. Request approval again.');for(let i=0;i<files.length;i++){status.textContent='Sending '+(i+1)+'/'+files.length+'…';await call('/uploads/'+request.id+'/'+i,{method:'POST',body:files[i]})}status.textContent='Files received on your PC.'}catch(e){status.textContent=e.message}finally{button.disabled=false}};
</script></body></html>
""";
}
