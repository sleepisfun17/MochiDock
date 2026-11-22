using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MochiDock.Core;
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Session;

namespace MochiDock.Services;

public class FpsMonitorService : IDisposable
{
    private static readonly Guid DxgiProviderGuid = Guid.Parse("CA11C036-0102-4A2D-A6AD-F03CFED5D3C9");
    private static readonly Guid D3D9ProviderGuid = Guid.Parse("783FD2C6-FAC1-4E10-A4B4-73EA7E3A0E0A");
    private static readonly Guid DxgKrnlProviderGuid = Guid.Parse("802EC45A-1E99-4B83-9920-87C98277BA9D");

    private TraceEventSession? _session;
    private Thread? _sessionThread;
    private bool _isDisposed;
    private int _targetPid = -1;
    private string _activeGameName = "";
    private string _activeGameTitle = "";
    private int _currentFps;
    private double _averageFrameTimeMs = 0.0;
    private double _slowestFrameTimeMs = 0.0;
    private long _lastDxgiPresentTimestamp;
    private readonly object _lock = new();

    // Whole-session statistics: every frame time goes into a fixed histogram (see GameSessionStats).
    private readonly GameSessionStats _sessionStats = new();
    private readonly Dictionary<string, long> _framesByGame = new(StringComparer.OrdinalIgnoreCase);
    private readonly DateTime _startedAt = DateTime.Now;
    private long _lastFrameTicks;
    private readonly GameSessionTimeline _timeline = new();
    private readonly PingStats _pingStats = new();

    // The slowest frame of each 250 ms for the last 60 s, newest last (the live frame-time chart).
    private const int RingSize = 240;
    private const double RingBucketMs = 250;
    private readonly Queue<double> _worstFrameRing = new(RingSize + 1);
    private double _ringElapsedMs, _ringWorstMs;

    // Circular timestamp buffer (Stopwatch ticks) for the last second
    private readonly Queue<long> _frameTimestamps = new(512);

    public int CurrentFps
    {
        get
        {
            lock (_lock) return _currentFps;
        }
    }

    public double AverageFrameTimeMs
    {
        get
        {
            lock (_lock) return _averageFrameTimeMs;
        }
    }

    public double SlowestFrameTimeMs
    {
        get
        {
            lock (_lock) return _slowestFrameTimeMs;
        }
    }

    public string ActiveGameName
    {
        get
        {
            lock (_lock) return _activeGameName;
        }
    }

    public string ActiveGameTitle
    {
        get
        {
            lock (_lock) return _activeGameTitle;
        }
    }

    public int TargetPid => Volatile.Read(ref _targetPid);

    // Caller holds the lock.
    private void AddToRing(double frameMs)
    {
        _ringElapsedMs += frameMs;
        if (frameMs > _ringWorstMs) _ringWorstMs = frameMs;
        if (_ringElapsedMs < RingBucketMs) return;
        _worstFrameRing.Enqueue(_ringWorstMs);
        while (_worstFrameRing.Count > RingSize) _worstFrameRing.Dequeue();
        _ringElapsedMs = 0;
        _ringWorstMs = 0;
    }

    /// <summary>The slowest frame (ms) of each recent 250 ms, oldest first, for the live frame-time chart.</summary>
    public double[] RecentWorstFrames()
    {
        lock (_lock) return _worstFrameRing.ToArray();
    }

    /// <summary>Records one ping result (ms, or null when no answer came back) for the session.</summary>
    public void AddPing(double? pingMs)
    {
        lock (_lock) { _pingStats.Add(pingMs); _timeline.AddPing(pingMs); }
    }

    /// <summary>
    /// The finished session's record: the game that produced the most frames, or null when the session was too short or no
    /// frames were captured (for example when the game blocks ETW), so no numbers are shown instead of wrong ones.
    /// </summary>
    public GameSessionRecord? BuildSessionRecord()
    {
        lock (_lock)
        {
            var result = _sessionStats.Result();
            if (result == null) return null;
            string process = "";
            long most = 0;
            foreach (var pair in _framesByGame) if (pair.Value > most) { most = pair.Value; process = pair.Key; }
            string game = string.Equals(process, _activeGameName, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(_activeGameTitle)
                ? _activeGameTitle : process;
            if (string.IsNullOrWhiteSpace(game)) game = "Game";
            if (game.Length > 60) game = game[..59].TrimEnd() + "…";
            var record = GameSessionRecord.From(game, _startedAt, result);
            record.ApplyPing(_pingStats.Result());
            record.ApplyTimeline(_timeline);
            return record;
        }
    }

    public bool IsMonitoring { get; private set; }
    public bool IsEtwActive { get; private set; }

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    public void Start()
    {
        lock (_lock)
        {
            if (IsMonitoring || _isDisposed) return;
            IsMonitoring = true;
        }

        Task.Run(FpsCalculationLoop);

        _sessionThread = new Thread(RunEtwSession)
        {
            IsBackground = true,
            Name = "MochiDock_EtwFpsSession",
            Priority = ThreadPriority.BelowNormal
        };
        _sessionThread.Start();
    }

    private void RunEtwSession()
    {
        const string sessionName = "MochiDock_Dxgi_Fps_Session";

        lock (_lock)
        {
            if (_isDisposed) return;
        }

        try
        {
            using (var existing = new TraceEventSession(sessionName))
            {
                existing.Stop(true);
            }
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Fps", logEx); }

        TraceEventSession? session = null;
        try
        {
            session = new TraceEventSession(sessionName)
            {
                StopOnDispose = true
            };

            session.EnableProvider(DxgiProviderGuid, TraceEventLevel.Informational);
            session.EnableProvider(D3D9ProviderGuid, TraceEventLevel.Informational);
            session.EnableProvider(DxgKrnlProviderGuid, TraceEventLevel.Informational, 0x1);

            session.Source.Dynamic.All += OnTraceEvent;

            lock (_lock)
            {
                if (_isDisposed)
                {
                    session.Dispose();
                    return;
                }
                _session = session;
                IsEtwActive = true;
            }

            session.Source.Process();
        }
        catch
        {
            IsEtwActive = false;
        }
        finally
        {
            IsEtwActive = false;
            lock (_lock)
            {
                if (ReferenceEquals(_session, session)) _session = null;
            }
            try { session?.Dispose(); } catch { }
        }
    }

    private void OnTraceEvent(TraceEvent data)
    {
        int pid = data.ProcessID;
        if (pid <= 4 || pid != Volatile.Read(ref _targetPid)) return;

        bool isFrameStart = false;
        long nowTicks = Stopwatch.GetTimestamp();

        if (data.ProviderGuid == DxgiProviderGuid)
        {
            if ((int)data.ID == 42 ||
                (data.Opcode == TraceEventOpcode.Start &&
                 data.EventName != null &&
                 data.EventName.StartsWith("Present", StringComparison.OrdinalIgnoreCase) &&
                 !data.EventName.Contains("Multiplane", StringComparison.OrdinalIgnoreCase)))
            {
                isFrameStart = true;
                Volatile.Write(ref _lastDxgiPresentTimestamp, nowTicks);
            }
        }
        else if (data.ProviderGuid == D3D9ProviderGuid)
        {
            if ((int)data.ID == 1 ||
                (data.Opcode == TraceEventOpcode.Start &&
                 data.EventName != null &&
                 data.EventName.StartsWith("Present", StringComparison.OrdinalIgnoreCase)))
            {
                isFrameStart = true;
                Volatile.Write(ref _lastDxgiPresentTimestamp, nowTicks);
            }
        }
        else if (data.ProviderGuid == DxgKrnlProviderGuid)
        {
            long lastDxgi = Volatile.Read(ref _lastDxgiPresentTimestamp);
            if (nowTicks - lastDxgi > Stopwatch.Frequency / 2)
            {
                if ((int)data.ID == 184 ||
                    (data.EventName != null &&
                     data.EventName.StartsWith("Present/", StringComparison.OrdinalIgnoreCase) &&
                     (data.Opcode == TraceEventOpcode.Info || data.Opcode == TraceEventOpcode.Start)))
                {
                    isFrameStart = true;
                }
            }
        }

        if (isFrameStart)
        {
            lock (_lock)
            {
                if (pid == _targetPid)
                {
                    _frameTimestamps.Enqueue(nowTicks);
                    if (_lastFrameTicks != 0)
                    {
                        double frameMs = (nowTicks - _lastFrameTicks) * 1000.0 / Stopwatch.Frequency;
                        if (_sessionStats.Add(frameMs)) { _timeline.AddFrame(frameMs); AddToRing(frameMs); }
                    }
                    _lastFrameTicks = nowTicks;
                    if (_activeGameName.Length > 0) _framesByGame[_activeGameName] = _framesByGame.GetValueOrDefault(_activeGameName) + 1;
                }
            }
        }
    }

    private async Task FpsCalculationLoop()
    {
        while (!_isDisposed && IsMonitoring)
        {
            try
            {
                UpdateTargetProcess();

                long now = Stopwatch.GetTimestamp();
                long oneSecondAgo = now - Stopwatch.Frequency;

                lock (_lock)
                {
                    while (_frameTimestamps.Count > 0 && _frameTimestamps.Peek() < oneSecondAgo)
                    {
                        _frameTimestamps.Dequeue();
                    }

                    _currentFps = _frameTimestamps.Count;

                    // Compute frame times (average and slowest) from timestamps
                    if (_frameTimestamps.Count > 1)
                    {
                        long prev = 0;
                        long maxDelta = 0;
                        int count = 0;
                        foreach (var t in _frameTimestamps)
                        {
                            if (prev > 0)
                            {
                                long delta = t - prev;
                                if (delta > maxDelta) maxDelta = delta;
                                count++;
                            }
                            prev = t;
                        }

                        _averageFrameTimeMs = _currentFps > 0 ? (1000.0 / _currentFps) : 0.0;
                        _slowestFrameTimeMs = maxDelta > 0 ? ((double)maxDelta / Stopwatch.Frequency * 1000.0) : _averageFrameTimeMs;
                    }
                    else if (_currentFps == 1)
                    {
                        _averageFrameTimeMs = 1000.0;
                        _slowestFrameTimeMs = 1000.0;
                    }
                    else
                    {
                        _averageFrameTimeMs = 0.0;
                        _slowestFrameTimeMs = 0.0;
                    }
                }
            }
            catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Fps", logEx); }

            await Task.Delay(250);
        }
    }

    private void UpdateTargetProcess()
    {
        try
        {
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd != IntPtr.Zero)
            {
                GetWindowThreadProcessId(hwnd, out uint pid);
                if (pid > 4 && pid != Environment.ProcessId)
                {
                    if (Volatile.Read(ref _targetPid) != (int)pid)
                    {
                        string procName = "";
                        string winTitle = "";
                        try
                        {
                            using var proc = Process.GetProcessById((int)pid);
                            procName = proc.ProcessName;
                        }
                        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Fps", logEx); }

                        try
                        {
                            var sb = new StringBuilder(256);
                            if (GetWindowText(hwnd, sb, 256) > 0)
                            {
                                winTitle = sb.ToString().Trim();
                            }
                        }
                        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Fps", logEx); }

                        lock (_lock)
                        {
                            _targetPid = (int)pid;
                            _activeGameName = procName;
                            _activeGameTitle = !string.IsNullOrWhiteSpace(winTitle) ? winTitle : procName;
                            _frameTimestamps.Clear();
                            _currentFps = 0;
                            _averageFrameTimeMs = 0.0;
                            _slowestFrameTimeMs = 0.0;
                            _lastDxgiPresentTimestamp = 0;
                            _lastFrameTicks = 0;   // the time across a switch of the foreground process is not a frame
                        }
                    }
                    else
                    {
                        // Periodically refresh window title if it changed
                        try
                        {
                            var sb = new StringBuilder(256);
                            if (GetWindowText(hwnd, sb, 256) > 0)
                            {
                                string title = sb.ToString().Trim();
                                if (!string.IsNullOrWhiteSpace(title))
                                {
                                    lock (_lock)
                                    {
                                        _activeGameTitle = title;
                                    }
                                }
                            }
                        }
                        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Fps", logEx); }
                    }
                }
            }
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Fps", logEx); }
    }

    public void Dispose()
    {
        TraceEventSession? session;
        lock (_lock)
        {
            if (_isDisposed) return;
            _isDisposed = true;
            IsMonitoring = false;
            IsEtwActive = false;
            session = _session;
            _session = null;
            Volatile.Write(ref _targetPid, -1);
            _activeGameName = "";
            _activeGameTitle = "";
            _currentFps = 0;
            _averageFrameTimeMs = 0.0;
            _slowestFrameTimeMs = 0.0;
            _frameTimestamps.Clear();
        }
        try
        {
            session?.Dispose();
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Fps", logEx); }
    }
}
