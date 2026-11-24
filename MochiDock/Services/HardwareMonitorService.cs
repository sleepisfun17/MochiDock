using System.Windows.Media;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace MochiDock.Services;

public class HardwareMonitorService : IDisposable
{
    private readonly MochiDock.Core.NetworkHistory _networkHistory = new();
    private readonly MochiDock.Core.ProcessCpuSampler _processCpu = new();
    private readonly CpuTelemetryReader _cpuTelemetry = new();
    private readonly SensorService _sensors = new();
    private readonly EnergyMeterReader _energyMeter = new();
    public void StopCpuTelemetry() { _cpuTelemetry.Dispose(); _sensors.Dispose(); _energyMeter.Dispose(); }

    [DllImport("kernel32.dll")] private static extern bool GetSystemTimes(out ulong idle, out ulong kernel, out ulong user);
    private (ulong Idle, ulong Total)? _lightTimes;

    /// <summary>
    /// A light reading for the 24-hour Stats history while the dock is closed: total CPU load (from its own GetSystemTimes deltas, so the
    /// Stats page's counters are untouched), RAM, GPU load and temperature (NVML, or the sensor library) and the measured CPU temperature.
    /// No process list, no per-core or chart work. Temperatures are null unless measured. Call on the UI thread, like GetCurrentStats.
    /// </summary>
    public MochiDock.Core.HistorySample GetLightSample(DateTimeOffset now)
    {
        double? cpuLoad = null;
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            ulong total = kernel + user;   // kernel time includes idle time
            if (_lightTimes is { } previous && total > previous.Total)
                cpuLoad = Math.Clamp(100.0 * (1 - (double)(idle - previous.Idle) / (total - previous.Total)), 0, 100);
            _lightTimes = (idle, total);
        }

        double? ram = ReadPhysicalMemory()?.Percent;   // live, not the GC's last-collection figure

        // The sensor reader refreshes in the background when asked, so a 15-second sampler gets the previous refresh: allow 30 s.
        var sensors = _sensors.Latest(_detectedGpuName);
        bool fresh = sensors.CapturedAt != DateTimeOffset.MinValue && DateTimeOffset.UtcNow - sensors.CapturedAt < TimeSpan.FromSeconds(30);
        double? gpuLoad = null, gpuTemp = null;
        if (_nvmlLoaded && _nvmlDevice != IntPtr.Zero)
        {
            try
            {
                var util = new NvmlUtilization();
                if (nvmlDeviceGetUtilizationRates(_nvmlDevice, ref util) == 0) gpuLoad = util.gpu;
                if (nvmlDeviceGetTemperature(_nvmlDevice, 0, out uint temp) == 0) gpuTemp = temp;
            }
            catch (Exception ex) { AppLog.Warn("Hardware", ex); }
        }
        if (gpuTemp is null && fresh) gpuTemp = sensors.GpuTempC;
        double? cpuTemp = fresh ? sensors.CpuTempC : null;
        return new MochiDock.Core.HistorySample(now, cpuLoad, cpuTemp, gpuLoad, gpuTemp, ram);
    }

    // ---- Taskbar readout sampler --------------------------------------------------------------------------------------------------------
    // SampleTaskbar runs once a second while a system, memory/disk or network block is on the taskbar. It has its OWN state: its own
    // GetSystemTimes deltas (_taskbarCpu) and byte-rate deltas (_taskbarNet). It must never use _lightTimes, which belongs to the 15 s history
    // sampler (GetLightSample): a reading from here would shorten that sampler's interval and skew its next load. Each part is read only when
    // its block is on, and nothing here lists processes or reads per-core data.
    private readonly MochiDock.Core.CpuLoadDelta _taskbarCpu = new();
    private readonly MochiDock.Core.ByteRateDelta _taskbarNet = new();
    private PerformanceCounter? _taskbarDiskIdle;
    private bool _taskbarDiskPrimed, _taskbarDiskFailed;
    private long _taskbarTempsTicks;
    private double? _taskbarCpuTemp, _taskbarGpuTemp;
    private bool _taskbarCpuTempMeasured;
    private System.Net.NetworkInformation.NetworkInterface[] _taskbarNics = Array.Empty<System.Net.NetworkInformation.NetworkInterface>();
    private long _taskbarNicsTicks;
    private static readonly long TwoSeconds = 2 * Stopwatch.Frequency, TenSeconds = 10 * Stopwatch.Frequency;

    /// <summary>
    /// One taskbar reading: CPU load (own GetSystemTimes delta), RAM, GPU load (NVML), temperatures (sensor reader and NVML, refreshed at most every
    /// 2 s; the CPU one is the estimate marked as such when no sensor reading exists), disk activity (100 minus the PhysicalDisk % Idle Time counter,
    /// created on first use) and network bytes per second (the active adapters' counters, the adapter list refreshed every 10 s).
    /// Call on the UI thread, like <see cref="GetLightSample"/>.
    /// </summary>
    public MochiDock.Core.TaskbarSample SampleTaskbar(MochiDock.Core.TaskbarNeeds needs)
    {
        double? cpu = null, gpu = null, ram = null, disk = null, down = null, up = null;
        double? cpuTemp = null, gpuTemp = null;
        bool cpuTempMeasured = false;
        long now = Stopwatch.GetTimestamp();

        if (needs.HasFlag(MochiDock.Core.TaskbarNeeds.System))
        {
            if (GetSystemTimes(out var idle, out var kernel, out var user)) cpu = _taskbarCpu.Update(idle, kernel + user);
            if (_nvmlLoaded && _nvmlDevice != IntPtr.Zero)
            {
                try
                {
                    var util = new NvmlUtilization();
                    if (nvmlDeviceGetUtilizationRates(_nvmlDevice, ref util) == 0) gpu = util.gpu;
                }
                catch (Exception ex) { AppLog.Warn("Hardware", ex); }
            }
            if (_taskbarTempsTicks == 0 || now - _taskbarTempsTicks >= TwoSeconds)
            {
                _taskbarTempsTicks = now;
                var sensors = _sensors.Latest(_detectedGpuName);
                bool fresh = sensors.CapturedAt != DateTimeOffset.MinValue && DateTimeOffset.UtcNow - sensors.CapturedAt < TimeSpan.FromSeconds(30);
                _taskbarCpuTemp = fresh ? sensors.CpuTempC : null;
                _taskbarCpuTempMeasured = _taskbarCpuTemp is not null;
                double? gpuT = null;
                if (_nvmlLoaded && _nvmlDevice != IntPtr.Zero)
                {
                    try { if (nvmlDeviceGetTemperature(_nvmlDevice, 0, out uint temp) == 0) gpuT = temp; }
                    catch (Exception ex) { AppLog.Warn("Hardware", ex); }
                }
                _taskbarGpuTemp = gpuT ?? (fresh ? sensors.GpuTempC : null);
            }
            cpuTemp = _taskbarCpuTemp;
            cpuTempMeasured = _taskbarCpuTempMeasured;
            if (cpuTemp is null && cpu is { } load) cpuTemp = MochiDock.Core.TaskbarSampling.EstimateCpuTemperature(load);
            gpuTemp = gpu is null ? null : _taskbarGpuTemp;
        }

        if (needs.HasFlag(MochiDock.Core.TaskbarNeeds.MemoryDisk))
        {
            ram = ReadPhysicalMemory()?.Percent;   // live, not the GC's last-collection figure
            disk = ReadTaskbarDiskActivity();
        }

        if (needs.HasFlag(MochiDock.Core.TaskbarNeeds.Network))
        {
            long received = 0, sent = 0;
            try
            {
                RefreshTaskbarNics(now);
                foreach (var nic in _taskbarNics)
                {
                    try { var stats = nic.GetIPv4Statistics(); received += stats.BytesReceived; sent += stats.BytesSent; }
                    catch (Exception ex) when (ex is System.Net.NetworkInformation.NetworkInformationException or InvalidOperationException) { }
                }
                if (_taskbarNet.Update(now / (double)Stopwatch.Frequency, received, sent) is { } rate) { down = rate.Down; up = rate.Up; }
            }
            catch (Exception ex) { AppLog.Warn("Hardware", ex); }
        }

        return new MochiDock.Core.TaskbarSample
        {
            CpuPercent = cpu, CpuTempC = cpuTemp, CpuTempMeasured = cpuTempMeasured, GpuPercent = gpu, GpuTempC = gpuTemp,
            RamPercent = ram, DiskPercent = disk, NetDownBytesPerSecond = down, NetUpBytesPerSecond = up,
        };
    }

    /// <summary>Forget the taskbar sampler's last readings (after the PC was locked or the display off), so the first rates are not averaged over the pause.</summary>
    public void ResetTaskbarSampling() { _taskbarCpu.Reset(); _taskbarNet.Reset(); _taskbarTempsTicks = 0; }

    private double? ReadTaskbarDiskActivity()
    {
        if (_taskbarDiskFailed) return null;
        try
        {
            _taskbarDiskIdle ??= new PerformanceCounter("PhysicalDisk", "% Idle Time", "_Total", true);
            float idle = _taskbarDiskIdle.NextValue();
            if (!_taskbarDiskPrimed) { _taskbarDiskPrimed = true; return null; }   // the first reading of a counter is 0
            return MochiDock.Core.TaskbarSampling.DiskActivityFromIdle(idle);
        }
        catch (Exception ex)
        {
            _taskbarDiskFailed = true;   // no counter on this PC: no disk figure, and no retry every second
            AppLog.Warn("Hardware", ex);
            try { _taskbarDiskIdle?.Dispose(); } catch { }
            _taskbarDiskIdle = null;
            return null;
        }
    }

    private void RefreshTaskbarNics(long now)
    {
        if (_taskbarNicsTicks != 0 && now - _taskbarNicsTicks < TenSeconds) return;
        _taskbarNicsTicks = now;
        var active = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces()
            .Where(nic => nic.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up &&
                          nic.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback &&
                          nic.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Tunnel)
            .ToArray();
        if (!active.Select(n => n.Id).OrderBy(i => i, StringComparer.Ordinal).SequenceEqual(_taskbarNics.Select(n => n.Id).OrderBy(i => i, StringComparer.Ordinal), StringComparer.Ordinal))
            _taskbarNet.Reset();   // an adapter came or went: its whole byte count must not become one huge rate
        _taskbarNics = active;
    }

    /// <summary>
    /// Light power sample for the Battery page: CPU package and memory power from the sensor reader (or Windows' Energy Meter counters when that has none) plus GPU board power (NVML first, sensors as fallback).
    /// Does not touch the hardware-card sampling state. Values are null when no measured reading exists.
    /// </summary>
    public (double? CpuW, double? MemoryW, double? GpuW) SamplePower()
    {
        var sensors = _sensors.Latest(_detectedGpuName);
        bool fresh = sensors.CapturedAt != DateTimeOffset.MinValue && DateTimeOffset.UtcNow - sensors.CapturedAt < TimeSpan.FromSeconds(8);
        double? gpu = null;
        if (Volatile.Read(ref _nvmlLoaded) && _nvmlDevice != IntPtr.Zero)
        {
            try
            {
                if (nvmlDeviceGetPowerUsage(_nvmlDevice, out uint milliwatts) == 0 && milliwatts > 0) gpu = milliwatts / 1000.0;
            }
            catch (Exception ex) { AppLog.Warn("Hardware", ex); }
        }
        if (gpu is null && fresh) gpu = sensors.GpuPowerW;
        double? cpu = fresh ? sensors.CpuPackagePowerW : null;
        double? memory = fresh ? sensors.CpuMemoryPowerW : null;
        // Without the PawnIO driver the sensor reader has no CPU power: use Windows' own Energy Meter counters instead.
        if (cpu is null || memory is null)
        {
            var (packageW, dramW) = _energyMeter.Sample();
            cpu ??= packageW;
            memory ??= dramW;
        }
        return (cpu, memory, gpu);
    }
    private const uint SHGFI_ICON = 0x000000100;
    private const uint SHGFI_SMALLICON = 0x000000001;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHFILEINFOW
    {
        public IntPtr hIcon;
        public int iIcon;
        public uint dwAttributes;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szDisplayName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 80)] public string szTypeName;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SHGetFileInfoW(string pszPath, uint dwFileAttributes, ref SHFILEINFOW psfi, uint cbFileInfo, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    // Shell User Notification State to detect full-screen games
    public enum QUERY_USER_NOTIFICATION_STATE
    {
        QUNS_NOT_PRESENT = 1,
        QUNS_BUSY = 2,
        QUNS_RUNNING_D3D_FULL_SCREEN = 3,
        QUNS_PRESENTATION_MODE = 4,
        QUNS_ACCEPTS_NOTIFICATIONS = 5,
        QUNS_QUIET_TIME = 6,
        QUNS_APP = 7
    }

    [DllImport("shell32.dll")]
    private static extern int SHQueryUserNotificationState(out QUERY_USER_NOTIFICATION_STATE pquns);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint dwFlags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const int GWL_STYLE = -16;
    private const int WS_CAPTION = 0x00C00000;

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    private static readonly System.Collections.Generic.HashSet<string> NonGameProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        // System & Windows Shell
        "explorer", "dwm", "SearchHost", "SearchApp", "ShellExperienceHost",
        "StartMenuExperienceHost", "ApplicationFrameHost", "SystemSettings",
        "Taskmgr", "LockApp", "TextInputHost", "Widgets", "SecurityHealthSystray",
        "cmd", "powershell", "pwsh", "WindowsTerminal", "conhost", "regedit", "mmc",
        "mstsc", " snippingtool", "ScreenClippingHost",
        // Browsers
        "chrome", "msedge", "firefox", "brave", "opera", "vivaldi", "arc", "waterfox", "tor",
        // Communication, Social & Media
        "Discord", "Spotify", "WhatsApp", "Telegram", "Slack", "Teams", "ms-teams",
        "Zoom", "vlc", "mpv", "mpc-hc64", "PotPlayerMini64", "obs64", "ShareX",
        // IDEs, Productivity & Office
        "Code", "devenv", "rider64", "idea64", "pycharm64", "studio64", "Cursor",
        "Windsurf", "Antigravity", "WINWORD", "EXCEL", "POWERPNT", "OUTLOOK", "olk",
        "Mailspring", "Thunderbird", "Notion", "Obsidian", "Acrobat", "FoxitPDFReader",
        "notepad", "notepad++",
        // Developer Tools, Git Clients & File Managers (GPU accelerated UI tools)
        "Fork", "OneCommander", "OneCommanderConnector", "GitExtensions", "SourceTree",
        "SmartGit", "GitKraken", "TortoiseGitProc", "git", "git-credential-manager",
        "TotalCmd", "TotalCmd64", "DirectoryOpus", "dopus", "Files", "AltapSalamander",
        "alacritty", "wezterm-gui", "ghostty", "Tabby", "WindowsTerminal", "msbuild", "dotnet",
        // Game Launchers (not games themselves)
        "steam", "steamwebhelper", "EpicGamesLauncher", "GalaxyClient", "Battle.net",
        "RiotClientServices", "EADesktop", "UbisoftConnect", "Playnite.DesktopApp",
        // Self
        "MochiDock", "AuraIsland" // "AuraIsland" is the pre-rename executable name; keep so older installs are still skipped
    };

    private static readonly string[] KnownGamePathTokens =
    {
        @"\steamapps\common\",
        @"\Epic Games\",
        @"\XboxGames\",
        @"\Riot Games\",
        @"\GOG Galaxy\Games\",
        @"\Ubisoft Game Launcher\games\",
        @"\EA Games\",
        @"\Origin Games\",
        @"\Battle.net\",
        @"\HoYoPlay\",
        @"\Genshin Impact",
        @"\Star Rail",
        @"\Wuthering Waves"
    };

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern IntPtr LoadLibrary(string lpFileName);

    // NVML P/Invoke
    [DllImport("nvml.dll", EntryPoint = "nvmlInit_v2")]
    private static extern int nvmlInit();

    [DllImport("nvml.dll", EntryPoint = "nvmlShutdown")]
    private static extern int nvmlShutdown();

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetHandleByIndex_v2")]
    private static extern int nvmlDeviceGetHandleByIndex(uint index, out IntPtr device);

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetName")]
    private static extern int nvmlDeviceGetName(IntPtr device, StringBuilder name, uint length);

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlUtilization
    {
        public uint gpu;
        public uint memory;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NvmlMemory
    {
        public ulong total;
        public ulong free;
        public ulong used;
    }

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetUtilizationRates")]
    private static extern int nvmlDeviceGetUtilizationRates(IntPtr device, ref NvmlUtilization utilization);

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetTemperature")]
    private static extern int nvmlDeviceGetTemperature(IntPtr device, int sensorType, out uint temp);

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetMemoryInfo")]
    private static extern int nvmlDeviceGetMemoryInfo(IntPtr device, ref NvmlMemory memory);

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetPowerUsage")]
    private static extern int nvmlDeviceGetPowerUsage(IntPtr device, out uint power);

    [DllImport("nvml.dll", EntryPoint = "nvmlDeviceGetClockInfo")]
    private static extern int nvmlDeviceGetClockInfo(IntPtr device, int clockType, out uint clockMhz);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessIoCounters(IntPtr ProcessHandle, out IO_COUNTERS IoCounters);

    [DllImport("pdh.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int PdhOpenQuery(IntPtr szDataSource, IntPtr dwUserData, out IntPtr phQuery);

    [DllImport("pdh.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int PdhAddEnglishCounter(IntPtr hQuery, string szFullCounterPath, IntPtr dwUserData, out IntPtr phCounter);

    [DllImport("pdh.dll", SetLastError = true)]
    private static extern int PdhCollectQueryData(IntPtr hQuery);

    [DllImport("pdh.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern int PdhGetFormattedCounterArray(IntPtr hCounter, uint dwFormat, ref uint lpdwBufferSize, ref uint lpdwItemCount, IntPtr lpItemBuffer);

    [DllImport("pdh.dll", SetLastError = true)]
    private static extern int PdhCloseQuery(IntPtr hQuery);

    private const uint PDH_FMT_DOUBLE = 0x00000200;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PDH_FMT_COUNTERVALUE_ITEM
    {
        public IntPtr szName;
        public uint CStatus;
        public double doubleValue;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemCpuSetInformation(IntPtr Information, uint BufferLength, out uint ReturnedLength, IntPtr Process, uint Flags);

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION
    {
        public long IdleTime;
        public long KernelTime;
        public long UserTime;
        public long DpcTime;
        public long InterruptTime;
        public uint InterruptCount;
    }

    [DllImport("ntdll.dll")]
    private static extern int NtQuerySystemInformation(int SystemInformationClass, IntPtr SystemInformation, uint SystemInformationLength, out uint ReturnLength);

    private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string DisplayName, ImageSource? Icon)> _processMetaCache = new(StringComparer.OrdinalIgnoreCase);

    private PerformanceCounter? _cpuCounter;
    private PerformanceCounter? _gpuCounter;
    private PerformanceCounter? _vramCounter;
    private IntPtr _pdhGpuQuery = IntPtr.Zero;
    private IntPtr _pdh3DCounter = IntPtr.Zero;
    private IntPtr _pdhCopyCounter = IntPtr.Zero;
    private IntPtr _pdhDecodeCounter = IntPtr.Zero;
    private IntPtr _pdhEncodeCounter = IntPtr.Zero;
    private PerformanceCounter? _diskReadCounter;
    private PerformanceCounter? _diskWriteCounter;
    private readonly NetworkService _networkService = new();
    private FpsMonitorService _fpsService = new();
    private bool _capturing;
    /// <summary>The host pinged for the network readout and the game session ping numbers.</summary>
    public string PingHost { get; set; } = "1.1.1.1";
    private MochiDock.Core.GameSessionHardware _sessionHardware = new();
    /// <summary>Raised when a capture ends with usable statistics; the view model stores and shows the session.</summary>
    public event Action<MochiDock.Core.GameSessionRecord>? GameSessionEnded;
    public void SetGameCapture(bool active)
    {
        if (active == _capturing) return;
        _capturing = active;
        if (active) { _fpsService = new FpsMonitorService(); _fpsService.Start(); _sessionHardware = new(); return; }
        var record = _fpsService.BuildSessionRecord();
        _fpsService.Dispose();
        if (record != null) { _sessionHardware.Apply(record); GameSessionEnded?.Invoke(record); }
    }
    public void ResetSamplingBaseline(){_cpuTelemetry.Reset();_networkHistory.ResetBaseline();_lastNetworkSampleTime=DateTime.MinValue;}
    private float _totalRamGb = 16.0f;
    private float _totalVramGb = 0f;
    private double _observedCpuMaxGhz = 4.5;
    private bool _initialized;
    private bool _nvmlLoaded;
    private IntPtr _nvmlDevice = IntPtr.Zero;
    private string _detectedGpuName = "GPU";
    private string _detectedGpuDriver = "–";
    private bool _isGamingMode = true;
    private long _cachedPing = -1;
    private DateTime _lastPingTime = DateTime.MinValue;

    private long _lastBytesReceived = 0;
    private long _lastBytesSent = 0;
    private DateTime _lastNetworkSampleTime = DateTime.MinValue;
    private double _cachedDownloadSpeedMbps = 0.0;
    private double _cachedUploadSpeedMbps = 0.0;
    private double _peakDownloadMbps = 0.0;
    private double _peakUploadMbps = 0.0;
    private string _detectedNicName = "Ethernet / Wi-Fi";
    private string _detectedNicIp = "–";
    private string _detectedNicMac = "–";
    private string _detectedNicSpeed = "1.0 Gbps";
    private bool _isNicOnline = true;
    private bool _lastGamingState;
    private int _nonGamingConsecutiveChecks;
    [StructLayout(LayoutKind.Sequential)]
    private struct PERFORMANCE_INFORMATION
    {
        public uint cb;
        public nuint CommitTotal, CommitLimit, CommitPeak, PhysicalTotal, PhysicalAvailable, SystemCache, KernelTotal, KernelPaged, KernelNonpaged, PageSize;
        public uint HandleCount, ProcessCount, ThreadCount;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool K32GetPerformanceInfo(ref PERFORMANCE_INFORMATION info, uint size);

    /// <summary>Rows in each top-process list (CPU, memory, disk): ten 21-DIP rows fill a Stats page column.</summary>
    public const int TopProcessCount = 10;

    private Dictionary<int, string> _coreKindMap = new();
    // "8 P-cores (16 threads) · 16 E-cores", from the CPU Sets' physical core index and efficiency class (CoreTopology.Summary).
    private string _coreSummary = "", _coreHeader = "PER CORE / THREADS";
    private bool _isHybridCpuDetected;
    private SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION[]? _prevCoreTimes;
    
    private readonly Queue<double> _cpuHistoryQueue = new();
    private readonly Queue<double> _gpuHistoryQueue = new();
    private readonly Queue<double> _ramHistoryQueue = new();
    private readonly Queue<double> _diskReadHistoryQueue = new();
    private readonly Queue<double> _diskWriteHistoryQueue = new();
    private readonly Queue<double> _netDownHistoryQueue = new();
    private readonly Queue<double> _netUpHistoryQueue = new();
    private readonly Queue<double> _fpsHistoryQueue = new();
    private List<ProcessMetricItem> _cachedTopMemoryProcesses = new();
    private List<ProcessMetricItem> _cachedTopProcesses = new();
    private List<ProcessMetricItem> _cachedTopDiskProcesses = new();
    private DateTime _lastProcSample = DateTime.MinValue;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, ulong> _lastProcIoTotal = new();
    private DateTime _lastIoSampleTime = DateTime.MinValue;
    private double _cachedDiskReadBytesPerSec = 0.0;
    private double _cachedDiskWriteBytesPerSec = 0.0;
    private double _cachedNetDownloadBytesPerSec = 0.0;
    private double _cachedNetUploadBytesPerSec = 0.0;
private float _lastGpuUsage;
    private bool _disposed;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, (long StartTimeTicks, string Name, bool IsExcluded, bool IsKnownGameDir)> _pidClassificationCache = new();

    public bool IsGamingMode
    {
        get => _isGamingMode;
        set => _isGamingMode = value;
    }

    public HardwareMonitorService()
    {
        Task.Run(InitCounters);

    }

    private static (string DisplayName, ImageSource? Icon) GetProcessMeta(Process p)
    {
        string name = p.ProcessName;
        if (_processMetaCache.TryGetValue(name, out var cached))
        {
            return cached;
        }

        string displayName = name;
        ImageSource? icon = null;

        try
        {
            string? exePath = null;
            IntPtr hProc = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, p.Id);
            if (hProc != IntPtr.Zero)
            {
                try
                {
                    var sb = new StringBuilder(1024);
                    int size = sb.Capacity;
                    if (QueryFullProcessImageName(hProc, 0, sb, ref size))
                    {
                        exePath = sb.ToString();
                    }
                }
                finally
                {
                    CloseHandle(hProc);
                }
            }

            if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
            {
                try
                {
                    var vi = FileVersionInfo.GetVersionInfo(exePath);
                    if (!string.IsNullOrWhiteSpace(vi.FileDescription))
                    {
                        displayName = vi.FileDescription.Trim();
                    }
                }
                catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Hardware", logEx); }

                try
                {
                    var shfi = new SHFILEINFOW();
                    if (SHGetFileInfoW(exePath, 0, ref shfi, (uint)Marshal.SizeOf<SHFILEINFOW>(), SHGFI_ICON | SHGFI_SMALLICON) != IntPtr.Zero &&
                        shfi.hIcon != IntPtr.Zero)
                    {
                        try
                        {
                            var bmp = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                                shfi.hIcon,
                                System.Windows.Int32Rect.Empty,
                                System.Windows.Media.Imaging.BitmapSizeOptions.FromWidthAndHeight(16, 16));
                            bmp.Freeze();
                            icon = bmp;
                        }
                        finally { DestroyIcon(shfi.hIcon); }
                    }
                }
                catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Hardware", logEx); }
            }
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Hardware", logEx); }

        // Clean fallbacks for well-known system apps
        if (displayName == name)
        {
            if (name.Equals("dwm", StringComparison.OrdinalIgnoreCase)) displayName = "Desktop Window Manager";
            else if (name.Equals("explorer", StringComparison.OrdinalIgnoreCase)) displayName = "Windows Explorer";
            else if (name.Equals("Taskmgr", StringComparison.OrdinalIgnoreCase)) displayName = "Task Manager";
            else if (name.Equals("System", StringComparison.OrdinalIgnoreCase)) displayName = "System";
        }

        var res = (displayName, icon);
        _processMetaCache[name] = res;
        return res;
    }

    private static string? FindNvmlPath()
    {
        try
        {
            // 1. Standard System32
            string systemPath = Path.Combine(Environment.SystemDirectory, "nvml.dll");
            if (File.Exists(systemPath)) return systemPath;

            // 2. NVIDIA Corporation NVSMI
            string nvsmiPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "NVIDIA Corporation", "NVSMI", "nvml.dll");
            if (File.Exists(nvsmiPath)) return nvsmiPath;
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Hardware", logEx); }

        return null;
    }

    private static float GetTotalVramFromRegistry()
    {
        try
        {
            // Scan Windows Video Controller registry for 64-bit qwMemorySize
            const string classKeyPath = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}";
            using var baseKey = Registry.LocalMachine.OpenSubKey(classKeyPath);
            if (baseKey != null)
            {
                foreach (var subKeyName in baseKey.GetSubKeyNames())
                {
                    if (subKeyName.StartsWith("000"))
                    {
                        using var subKey = baseKey.OpenSubKey(subKeyName);
                        if (subKey != null)
                        {
                            var memVal = subKey.GetValue("HardwareInformation.qwMemorySize");
                            if (memVal is long qwBytes && qwBytes > 0)
                            {
                                return (float)Math.Round((double)qwBytes / (1024.0 * 1024 * 1024), 1);
                            }
                            if (memVal is byte[] bytes && bytes.Length >= 8)
                            {
                                long qw = BitConverter.ToInt64(bytes, 0);
                                if (qw > 0)
                                    return (float)Math.Round((double)qw / (1024.0 * 1024 * 1024), 1);
                            }
                        }
                    }
                }
            }
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Hardware", logEx); }

        // Fallback to WMI if registry was not accessible
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher("SELECT AdapterRAM FROM Win32_VideoController");
            foreach (var mo in searcher.Get())
            {
                if (mo["AdapterRAM"] != null && ulong.TryParse(mo["AdapterRAM"].ToString(), out ulong ramBytes) && ramBytes > 0)
                {
                    return (float)Math.Round((double)ramBytes / (1024.0 * 1024 * 1024), 1);
                }
            }
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Hardware", logEx); }

        return 0f;
    }

    private void InitCounters()
    {
        if (_disposed) return;
        try
        {
            var cpuCounter = new PerformanceCounter("Processor", "% Processor Time", "_Total", true);
            cpuCounter.NextValue();
            _cpuCounter = cpuCounter;

            var gcMemoryInfo = GC.GetGCMemoryInfo();
            if (gcMemoryInfo.TotalAvailableMemoryBytes > 0)
            {
                _totalRamGb = (float)Math.Round(gcMemoryInfo.TotalAvailableMemoryBytes / (1024.0 * 1024 * 1024), 1);
            }
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Hardware", logEx); }

        // Query Total VRAM from registry early (universal across all GPUs)
        _totalVramGb = GetTotalVramFromRegistry();

        // 1. Initialize NVML (NVIDIA GeForce/RTX)
        try
        {
            string? nvmlPath = FindNvmlPath();
            if (!string.IsNullOrEmpty(nvmlPath))
            {
                LoadLibrary(nvmlPath);
            }

            int res = nvmlInit();
            if (res == 0)
            {
                res = nvmlDeviceGetHandleByIndex(0, out IntPtr nvmlDevice);
                if (res == 0 && nvmlDevice != IntPtr.Zero)
                {
                    var sb = new StringBuilder(64);
                    if (nvmlDeviceGetName(nvmlDevice, sb, 64) == 0)
                    {
                        _detectedGpuName = sb.ToString();
                    }
                    _nvmlDevice = nvmlDevice;
                    Volatile.Write(ref _nvmlLoaded, true);
                }
            }
        }
        catch
        {
            Volatile.Write(ref _nvmlLoaded, false);
        }

        // 2. Single Win32_VideoController query for GPU name (when NVML didn't supply it), driver version, and VRAM fallback.
        bool needName = !Volatile.Read(ref _nvmlLoaded) || string.IsNullOrEmpty(_detectedGpuName) || _detectedGpuName == "GPU";
        bool needDriver = _detectedGpuDriver == "–";
        bool needVram = _totalVramGb <= 0f;
        if (needName || needDriver || needVram)
        {
            try
            {
                using var searcher = new System.Management.ManagementObjectSearcher("SELECT Name, DriverVersion, AdapterRAM FROM Win32_VideoController");
                using var results = searcher.Get();
                string? fallbackDriver = null;
                foreach (var obj in results)
                {
                    using var mo = obj;
                    string name = mo["Name"]?.ToString() ?? "";
                    if (name.Contains("Remote", StringComparison.OrdinalIgnoreCase)) continue;

                    if (needVram && mo["AdapterRAM"] != null && ulong.TryParse(mo["AdapterRAM"].ToString(), out ulong ramBytes) && ramBytes > 0)
                    {
                        _totalVramGb = (float)Math.Round((double)ramBytes / (1024.0 * 1024 * 1024), 1);
                        needVram = false;
                    }

                    if (needName && !string.IsNullOrEmpty(name))
                    {
                        _detectedGpuName = name;
                        _detectedGpuDriver = mo["DriverVersion"]?.ToString() ?? "–";
                        needName = false;
                        needDriver = false;
                    }
                    else if (needDriver)
                    {
                        string? driver = mo["DriverVersion"]?.ToString();
                        if (!string.IsNullOrEmpty(driver))
                        {
                            fallbackDriver ??= driver;
                            if (name.Length > 0 && (_detectedGpuName.Contains(name, StringComparison.OrdinalIgnoreCase) || name.Contains(_detectedGpuName, StringComparison.OrdinalIgnoreCase)))
                            {
                                _detectedGpuDriver = driver;
                                needDriver = false;
                            }
                        }
                    }
                }
                if (needDriver && fallbackDriver != null) _detectedGpuDriver = fallbackDriver;
            }
            catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Hardware", logEx); }
        }

        // Initialize Windows PDH Query for GPU Engine Wildcards (captures all active processes on 3D/Copy/Decode/Encode)
        try
        {
            if (PdhOpenQuery(IntPtr.Zero, IntPtr.Zero, out IntPtr pdhQuery) == 0)
            {
                PdhAddEnglishCounter(pdhQuery, @"\GPU Engine(*engtype_3D*)\Utilization Percentage", IntPtr.Zero, out _pdh3DCounter);
                PdhAddEnglishCounter(pdhQuery, @"\GPU Engine(*engtype_Copy*)\Utilization Percentage", IntPtr.Zero, out _pdhCopyCounter);
                PdhAddEnglishCounter(pdhQuery, @"\GPU Engine(*engtype_VideoDecode*)\Utilization Percentage", IntPtr.Zero, out _pdhDecodeCounter);
                PdhAddEnglishCounter(pdhQuery, @"\GPU Engine(*engtype_VideoEncode*)\Utilization Percentage", IntPtr.Zero, out _pdhEncodeCounter);
                PdhCollectQueryData(pdhQuery);
                _pdhGpuQuery = pdhQuery;
            }
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Hardware", logEx); }

        // CPU Sets tell Performance and Efficiency cores apart on hybrid CPUs (EfficiencyClass).
        try
        {
            uint need = 0;
            GetSystemCpuSetInformation(IntPtr.Zero, 0, out need, IntPtr.Zero, 0);
            if (need > 0)
            {
                IntPtr buf = Marshal.AllocHGlobal((int)need);
                try
                {
                    if (GetSystemCpuSetInformation(buf, need, out need, IntPtr.Zero, 0))
                    {
                        var classes = new Dictionary<int, byte>();
                        var topology = new List<(int CoreIndex, int EfficiencyClass)>();
                        int off = 0;
                        while (off + 20 <= need)
                        {
                            uint size = (uint)Marshal.ReadInt32(buf, off);
                            int type = Marshal.ReadInt32(buf, off + 4);
                            if (size < 8 || off + size > need) break;

                            if (type == 0) // CpuSetInformation
                            {
                                byte logicalIndex = Marshal.ReadByte(buf, off + 14);
                                byte coreIndex = Marshal.ReadByte(buf, off + 15);
                                byte efficiencyClass = Marshal.ReadByte(buf, off + 18);
                                classes[logicalIndex] = efficiencyClass;
                                topology.Add((Marshal.ReadInt16(buf, off + 12) * 256 + coreIndex, efficiencyClass));   // group and core: unique per physical core
                            }
                            off += (int)size;
                        }

                        _coreSummary = MochiDock.Core.CoreTopology.Summary(topology);
                        _coreHeader = MochiDock.Core.CoreTopology.Header(topology);
                        if (classes.Count > 0)
                        {
                            byte minClass = classes.Values.Min();
                            byte maxClass = classes.Values.Max();
                            if (minClass != maxClass)
                            {
                                _isHybridCpuDetected = true;
                                var kindMap = new Dictionary<int, string>(classes.Count);
                                foreach (var kvp in classes)
                                {
                                    kindMap[kvp.Key] = (kvp.Value == maxClass) ? "Performance" : "Efficiency";
                                }
                                _coreKindMap = kindMap;
                            }
                        }
                    }
                }
                finally
                {
                    Marshal.FreeHGlobal(buf);
                }
            }
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Hardware", logEx); }

        // Initialize Windows Performance Counter for GPU 3D Load as fallback
        try
        {
            var category = new PerformanceCounterCategory("GPU Engine");
            var instanceNames = category.GetInstanceNames();
            foreach (var name in instanceNames)
            {
                if (name.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase))
                {
                    _gpuCounter = new PerformanceCounter("GPU Engine", "Utilization Percentage", name, true);
                    _gpuCounter.NextValue();
                    break;
                }
            }
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Hardware", logEx); }

        // Initialize Universal Windows Performance Counter for Dedicated VRAM Usage
        try
        {
            var vramCategory = new PerformanceCounterCategory("GPU Adapter Memory");
            var vramInstances = vramCategory.GetInstanceNames();
            if (vramInstances.Length > 0)
            {
                // Find primary adapter instance
                string bestInstance = vramInstances[0];
                float maxSample = 0;
                foreach (var inst in vramInstances)
                {
                    try
                    {
                        using var testCounter = new PerformanceCounter("GPU Adapter Memory", "Dedicated Usage", inst, true);
                        float val = testCounter.NextValue();
                        if (val > maxSample)
                        {
                            maxSample = val;
                            bestInstance = inst;
                        }
                    }
                    catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Hardware", logEx); }
                }

                _vramCounter = new PerformanceCounter("GPU Adapter Memory", "Dedicated Usage", bestInstance, true);
                _vramCounter.NextValue();
            }
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Hardware", logEx); }

        // Initialize PhysicalDisk Read/Write counters for live Drives tab activity
        try
        {
            _diskReadCounter = new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total", true);
            _diskReadCounter.NextValue();
            _diskWriteCounter = new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total", true);
            _diskWriteCounter.NextValue();
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Hardware", logEx); }

        Volatile.Write(ref _initialized, true);
    }

    private (string Name, bool IsExcluded, bool IsKnownGameDir) ClassifyProcess(int pid)
    {
        string procName = "";
        bool isExcluded = false;
        bool isKnownGameDir = false;

        try
        {
            using var proc = Process.GetProcessById(pid);
            long startTicks = 0;
            try { startTicks = proc.StartTime.ToUniversalTime().Ticks; } catch { }
            if (startTicks != 0 && _pidClassificationCache.TryGetValue(pid, out var cached) && cached.StartTimeTicks == startTicks)
            {
                return (cached.Name, cached.IsExcluded, cached.IsKnownGameDir);
            }

            procName = proc.ProcessName;

            if (string.IsNullOrWhiteSpace(procName) || NonGameProcesses.Contains(procName))
            {
                isExcluded = true;
            }
            else
            {
                try
                {
                    string? exePath = proc.MainModule?.FileName;
                    if (!string.IsNullOrEmpty(exePath))
                    {
                        string winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
                        if (!string.IsNullOrEmpty(winDir) && exePath.StartsWith(winDir, StringComparison.OrdinalIgnoreCase))
                        {
                            isExcluded = true;
                        }
                        else
                        {
                            foreach (var token in KnownGamePathTokens)
                            {
                                if (exePath.Contains(token, StringComparison.OrdinalIgnoreCase))
                                {
                                    isKnownGameDir = true;
                                    break;
                                }
                            }
                        }
                    }
                }
                catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Hardware", logEx); }
            }

            if (startTicks != 0 && !string.IsNullOrEmpty(procName))
            {
                _pidClassificationCache[pid] = (startTicks, procName, isExcluded, isKnownGameDir);
            }
            return (procName, isExcluded, isKnownGameDir);
        }
        catch
        {
            _pidClassificationCache.TryRemove(pid, out _);
            return ("", true, false);
        }
    }

    private static bool IsFullscreenOrBorderlessWindow(IntPtr hwnd)
    {
        try
        {
            var classSb = new StringBuilder(64);
            if (GetClassName(hwnd, classSb, 64) > 0)
            {
                string cls = classSb.ToString();
                if (cls.Equals("Progman", StringComparison.OrdinalIgnoreCase) ||
                    cls.Equals("WorkerW", StringComparison.OrdinalIgnoreCase) ||
                    cls.Equals("Shell_TrayWnd", StringComparison.OrdinalIgnoreCase) ||
                    cls.Equals("Shell_SecondaryTrayWnd", StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            if (!GetWindowRect(hwnd, out var winRect)) return false;

            IntPtr hMonitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
            if (hMonitor == IntPtr.Zero) return false;

            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfo(hMonitor, ref mi)) return false;

            int winW = winRect.Right - winRect.Left;
            int winH = winRect.Bottom - winRect.Top;
            int monW = mi.rcMonitor.Right - mi.rcMonitor.Left;
            int monH = mi.rcMonitor.Bottom - mi.rcMonitor.Top;

            if (monW <= 0 || monH <= 0) return false;

            // Check if window covers the entire physical monitor (including taskbar area)
            bool coversFullMonitor =
                Math.Abs(winRect.Left - mi.rcMonitor.Left) <= 2 &&
                Math.Abs(winRect.Top - mi.rcMonitor.Top) <= 2 &&
                winW >= monW - 4 &&
                winH >= monH - 4;

            if (!coversFullMonitor) return false;

            // Distinguish borderless fullscreen games from standard maximized desktop windows with a titlebar
            int style = GetWindowLong(hwnd, GWL_STYLE);
            bool hasStandardCaption = (style & WS_CAPTION) == WS_CAPTION;
            return !hasStandardCaption;
        }
        catch
        {
            return false;
        }
    }

    private string _detectedGameName = "";

    /// <summary>The game being played: the title the frame capture found, else its process name, else the last foreground process detected as a game.</summary>
    public string CurrentGameName =>
        !string.IsNullOrWhiteSpace(_fpsService.ActiveGameTitle) ? _fpsService.ActiveGameTitle
        : !string.IsNullOrWhiteSpace(_fpsService.ActiveGameName) ? _fpsService.ActiveGameName
        : _detectedGameName;

    /// <summary>Every name the game being played is known by (frame-capture title, its process name, the detected process).</summary>
    public IReadOnlyList<string> CurrentGameNames() =>
        new[] { _fpsService.ActiveGameTitle, _fpsService.ActiveGameName, _detectedGameName }
            .Where(n => !string.IsNullOrWhiteSpace(n)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

    public bool IsGameRunning()
    {
        bool detectedNow = DetectGameImmediate();

        if (detectedNow)
        {
            _nonGamingConsecutiveChecks = 0;
            _lastGamingState = true;
            return true;
        }

        if (_lastGamingState)
        {
            _nonGamingConsecutiveChecks++;
            // Brief grace period (2 polls) so quick Alt-Tab or hovering doesn't immediately drop gaming mode
            if (_nonGamingConsecutiveChecks < 2)
            {
                return true;
            }
            _lastGamingState = false;
        }

        return false;
    }

    private bool DetectGameImmediate()
    {
        try
        {
            // Inspect Foreground Window & Process first
            IntPtr hwnd = GetForegroundWindow();
            if (hwnd == IntPtr.Zero) return false;

            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid <= 4) return false;

            // If user is interacting with MochiDock itself, preserve current gaming state
            if (pid == Environment.ProcessId)
            {
                return _lastGamingState;
            }

            var (candidateName, isExcluded, isKnownGameDir) = ClassifyProcess((int)pid);
            if (isExcluded) return false;

            // Fast path: Exclusive D3D Fullscreen on a non-excluded app
            if (SHQueryUserNotificationState(out var state) == 0 &&
                state == QUERY_USER_NOTIFICATION_STATE.QUNS_RUNNING_D3D_FULL_SCREEN)
            {
                _detectedGameName = candidateName;
                return true;
            }

            // Known game installation path (Steam, Epic, XboxGames, Riot, GOG, HoYoPlay, etc.)
            if (isKnownGameDir)
            {
                _detectedGameName = candidateName;
                return true;
            }

            // Active ETW 3D presentation (DXGI / D3D9 / DxgKrnl) on a non-excluded foreground app
            int currentFps = _fpsService.CurrentFps;
            if (currentFps >= 15 && _fpsService.TargetPid == (int)pid)
            {
                _detectedGameName = candidateName;
                return true;
            }

            // Borderless Fullscreen window on a non-excluded app with active GPU usage or QUNS_BUSY
            if (IsFullscreenOrBorderlessWindow(hwnd))
            {
                float gpuLoad = SampleQuickGpuUsage();
                if (gpuLoad >= 15f || currentFps > 0 || state == QUERY_USER_NOTIFICATION_STATE.QUNS_BUSY)
                {
                    _detectedGameName = candidateName;
                    return true;
                }
            }
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Hardware", logEx); }

        return false;
    }

    private float SampleQuickGpuUsage()
    {
        if (_nvmlLoaded && _nvmlDevice != IntPtr.Zero)
        {
            try
            {
                var util = new NvmlUtilization();
                if (nvmlDeviceGetUtilizationRates(_nvmlDevice, ref util) == 0)
                {
                    _lastGpuUsage = util.gpu;
                    return _lastGpuUsage;
                }
            }
            catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Hardware", logEx); }
        }
        return _lastGpuUsage;
    }

    private readonly object _statsGate = new();

    public HardwareStats GetCurrentStats(bool includeHeavy = true, bool? isGamingActiveOverride = null)
    {
        lock (_statsGate)
        {
            var stats = new HardwareStats
            {
                TotalRamGb = _totalRamGb > 0 ? _totalRamGb : 16.0f,
                TotalVramGb = _totalVramGb,
                IsNvmlAvailable = _nvmlLoaded,
                IsGamingActive = isGamingActiveOverride ?? IsGameRunning(),
                IsGamingMode = _isGamingMode,
                GpuName = _detectedGpuName
            };

            var sensors = _sensors.Latest(_detectedGpuName);
            var sensorAge = DateTimeOffset.UtcNow - sensors.CapturedAt;
            bool sensorsFresh = sensors.CapturedAt != DateTimeOffset.MinValue && sensorAge < TimeSpan.FromSeconds(5);

            SampleCpu(stats, sensors, sensorsFresh);
            SampleMemory(stats);
            SampleGpu(stats, sensors, sensorsFresh);
            SampleDrivesAndNetwork(stats);
            SampleGamingFps(stats);
            // Every reading, light or full, goes into the 2-minute chart history, so the Stats charts are already filled
            // when the card opens; only building the chart shapes waits for the card.
            PushHistory(stats);

            if (!includeHeavy) return stats;

            SampleFixedDrives(stats);
            SampleChartsAndHistory(stats);
            SampleCoreUsages(stats);
            SampleTopProcesses(stats);
            return stats;
        }
    }

    private void SampleCpu(HardwareStats stats, MochiDock.Core.SensorSnapshot sensors, bool sensorsFresh)
    {
        if (_initialized && _cpuCounter != null)
        {
            try
            {
                stats.CpuUsage = (float)Math.Round(_cpuCounter.NextValue(), 0);
                stats.CpuUsageReading = new(stats.CpuUsage, "Windows CPU counter", DateTimeOffset.UtcNow);
            }
            catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Hardware", logEx); }
        }

        var cpu = _cpuTelemetry.Read();
        stats.CpuSpeedGhz = cpu.Ghz;
        stats.CpuBaseGhz = cpu.BaseGhz;
        if (cpu.BaseGhz.HasValue && cpu.BaseGhz.Value > 0)
            _observedCpuMaxGhz = MochiDock.Core.CpuClock.CeilingGhz(cpu.BaseGhz.Value, cpu.Ghz, _observedCpuMaxGhz);
        else if (cpu.Ghz.HasValue && cpu.Ghz.Value > _observedCpuMaxGhz - 0.1)
            _observedCpuMaxGhz = MochiDock.Core.CpuClock.CeilingGhz(2.5, cpu.Ghz, _observedCpuMaxGhz);

        stats.CpuMaxGhz = _observedCpuMaxGhz;
        stats.CpuClockStatus = cpu.Ghz.HasValue && cpu.BaseGhz.HasValue
            ? $"{cpu.Ghz.Value:0.00} GHz (Base: {cpu.BaseGhz.Value:0.00} GHz, Boost Ceiling: {_observedCpuMaxGhz:0.00} GHz)\n{cpu.ClockStatus}"
            : cpu.ClockStatus;
        if (cpu.Times is { } time)
        {
            stats.SystemCpuPercent = time.SystemPercent;
            stats.UserCpuPercent = time.UserPercent;
            stats.IdleCpuPercent = time.IdlePercent;
            stats.CpuUsage = (float)Math.Round(time.UsagePercent, 0);
            stats.CpuUsageReading = new(stats.CpuUsage, "Windows CPU time counters", cpu.CapturedAt);
        }

        if (sensorsFresh && sensors.CpuTempC is { } cpuTempC)
        {
            stats.CpuTemp = (float)Math.Round(cpuTempC, 0);
            stats.CpuTemperatureReading = new(cpuTempC, "LibreHardwareMonitor CPU", sensors.CapturedAt);
        }
        else
        {
            // Estimate only: shown with "~" and never used for alerts.
            stats.CpuTemp = (float)Math.Round(42.0 + (stats.CpuUsage * 0.38), 0);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length, MemoryLoad;
        public ulong TotalPhys, AvailPhys, TotalPageFile, AvailPageFile, TotalVirtual, AvailVirtual, AvailExtendedVirtual;
    }
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx status);

    /// <summary>
    /// The machine's physical memory now: total and used bytes and the used percentage. Read live from Windows (GlobalMemoryStatusEx);
    /// GC.GetGCMemoryInfo only reports the memory load as of the last garbage collection, which in a quiet app can be minutes old.
    /// Falls back to that GC figure if the call fails, and gives null when neither works (never an invented value).
    /// </summary>
    private static (double Percent, double TotalBytes, double UsedBytes)? ReadPhysicalMemory()
    {
        try
        {
            var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (GlobalMemoryStatusEx(ref status) && status.TotalPhys > 0)
            {
                double used = status.TotalPhys - Math.Min(status.AvailPhys, status.TotalPhys);
                return (Math.Clamp(100.0 * used / status.TotalPhys, 0, 100), status.TotalPhys, used);
            }
            var gc = GC.GetGCMemoryInfo();
            if (gc.TotalAvailableMemoryBytes > 0)
                return (Math.Clamp(100.0 * gc.MemoryLoadBytes / gc.TotalAvailableMemoryBytes, 0, 100), gc.TotalAvailableMemoryBytes, gc.MemoryLoadBytes);
        }
        catch (Exception ex) { AppLog.Warn("Hardware", ex); }
        return null;
    }

    private void SampleMemory(HardwareStats stats)
    {
        // Unavailable leaves the previous values in place rather than showing made-up numbers.
        if (ReadPhysicalMemory() is { } memory)
        {
            stats.UsedRamGb = (float)Math.Round(memory.UsedBytes / (1024.0 * 1024 * 1024), 1);
            stats.TotalRamGb = (float)Math.Round(memory.TotalBytes / (1024.0 * 1024 * 1024), 1);
            stats.RamUsagePercent = (float)Math.Round(memory.Percent, 0);
        }

        try
        {
            var perf = new PERFORMANCE_INFORMATION { cb = (uint)Marshal.SizeOf<PERFORMANCE_INFORMATION>() };
            if (K32GetPerformanceInfo(ref perf, perf.cb))
            {
                double page = (double)perf.PageSize, gb = 1024.0 * 1024 * 1024;
                stats.CommittedRamGb = (float)Math.Round(perf.CommitTotal * page / gb, 1);
                stats.CommitLimitGb = (float)Math.Round(perf.CommitLimit * page / gb, 1);
                stats.CachedRamGb = (float)Math.Round(perf.SystemCache * page / gb, 1);
            }
        }
        catch (Exception ex) { AppLog.Warn("Hardware", ex); }
    }

    private void SampleGpu(HardwareStats stats, MochiDock.Core.SensorSnapshot sensors, bool sensorsFresh)
    {
        bool vramPopulated = false;

        if (_nvmlLoaded && _nvmlDevice != IntPtr.Zero)
        {
            try
            {
                var util = new NvmlUtilization();
                if (nvmlDeviceGetUtilizationRates(_nvmlDevice, ref util) == 0)
                {
                    stats.GpuUsage = util.gpu;
                    stats.GpuUsageReading = new(stats.GpuUsage, "NVIDIA NVML", DateTimeOffset.UtcNow);
                }

                if (nvmlDeviceGetTemperature(_nvmlDevice, 0, out uint temp) == 0)
                {
                    stats.GpuTemp = temp;
                    stats.GpuTemperatureReading = new(temp, "NVIDIA NVML", DateTimeOffset.UtcNow);
                }

                var mem = new NvmlMemory();
                if (nvmlDeviceGetMemoryInfo(_nvmlDevice, ref mem) == 0)
                {
                    stats.UsedVramGb = (float)Math.Round(mem.used / (1024.0 * 1024 * 1024), 1);
                    stats.TotalVramGb = (float)Math.Round(mem.total / (1024.0 * 1024 * 1024), 1);
                    vramPopulated = true;
                }
            }
            catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Hardware", logEx); }
        }

        if (_pdhGpuQuery != IntPtr.Zero)
        {
            try
            {
                bool pdhCollected = PdhCollectQueryData(_pdhGpuQuery) == 0;
                bool pdhValid = false;

                double ReadPdhArray(IntPtr hCounter)
                {
                    if (hCounter == IntPtr.Zero) return 0;
                    uint bufSize = 0, itemCount = 0;
                    PdhGetFormattedCounterArray(hCounter, PDH_FMT_DOUBLE, ref bufSize, ref itemCount, IntPtr.Zero);
                    if (bufSize == 0) return 0;

                    IntPtr buffer = Marshal.AllocHGlobal((int)bufSize);
                    try
                    {
                        if (PdhGetFormattedCounterArray(hCounter, PDH_FMT_DOUBLE, ref bufSize, ref itemCount, buffer) == 0)
                        {
                            double total = 0;
                            int itemSize = Marshal.SizeOf(typeof(PDH_FMT_COUNTERVALUE_ITEM));
                            for (int i = 0; i < itemCount; i++)
                            {
                                var item = Marshal.PtrToStructure<PDH_FMT_COUNTERVALUE_ITEM>(IntPtr.Add(buffer, i * itemSize));
                                if ((item.CStatus == 0 || item.CStatus == 1) && double.IsFinite(item.doubleValue) && item.doubleValue >= 0)
                                {
                                    pdhValid = true;
                                    total += item.doubleValue;
                                }
                            }
                            return Math.Clamp(total, 0.0, 100.0);
                        }
                    }
                    finally { Marshal.FreeHGlobal(buffer); }
                    return 0;
                }

                stats.GpuEngine3DPercent = (float)Math.Round(ReadPdhArray(_pdh3DCounter), 0);
                stats.GpuEngineCopyPercent = (float)Math.Round(ReadPdhArray(_pdhCopyCounter), 0);
                stats.GpuEngineDecodePercent = (float)Math.Round(ReadPdhArray(_pdhDecodeCounter), 0);
                stats.GpuEngineEncodePercent = (float)Math.Round(ReadPdhArray(_pdhEncodeCounter), 0);

                float pdhMax = Math.Max(stats.GpuEngine3DPercent, Math.Max(stats.GpuEngineCopyPercent, Math.Max(stats.GpuEngineDecodePercent, stats.GpuEngineEncodePercent)));
                if (pdhMax > 0 || stats.GpuUsage <= 0)
                {
                    stats.GpuUsage = pdhMax;
                    if (pdhCollected && pdhValid) stats.GpuUsageReading = new(pdhMax, "Windows PDH GPU engine", DateTimeOffset.UtcNow);
                }
            }
            catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Hardware", logEx); }
        }

        if (stats.GpuUsage <= 0 && _gpuCounter != null)
        {
            try
            {
                stats.GpuUsage = (float)Math.Round(_gpuCounter.NextValue(), 0);
                stats.GpuUsageReading = new(stats.GpuUsage, "Windows GPU counter", DateTimeOffset.UtcNow);
            }
            catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Hardware", logEx); }
        }

        if (stats.GpuTemp <= 0 && sensorsFresh && sensors.GpuTempC is { } gpuTempC)
        {
            stats.GpuTemp = (float)Math.Round(gpuTempC, 0);
            stats.GpuTemperatureReading = new(gpuTempC, "LibreHardwareMonitor GPU", sensors.CapturedAt);
        }
        if (stats.GpuTemp <= 0)
            stats.GpuTemp = (float)Math.Round(45.0 + (stats.GpuUsage * 0.28), 0);

        if (stats.GpuPowerWatts <= 0)
        {
            if (Volatile.Read(ref _nvmlLoaded) && _nvmlDevice != IntPtr.Zero)
            {
                try
                {
                    if (nvmlDeviceGetPowerUsage(_nvmlDevice, out uint milliwatts) == 0 && milliwatts > 0)
                        stats.GpuPowerWatts = (float)Math.Round(milliwatts / 1000.0, 0);
                }
                catch (Exception ex) { AppLog.Warn("Hardware", ex); }
            }
            if (stats.GpuPowerWatts <= 0 && sensorsFresh && sensors.GpuPowerW is { } gpuPowerW)
                stats.GpuPowerWatts = (float)Math.Round(gpuPowerW, 0);
        }

        if (stats.GpuClockMhz <= 0)
        {
            if (Volatile.Read(ref _nvmlLoaded) && _nvmlDevice != IntPtr.Zero)
            {
                try
                {
                    if (nvmlDeviceGetClockInfo(_nvmlDevice, 0, out uint clockMhz) == 0 && clockMhz > 0)
                        stats.GpuClockMhz = clockMhz;
                }
                catch (Exception ex) { AppLog.Warn("Hardware", ex); }
            }
            if (stats.GpuClockMhz <= 0 && sensorsFresh && sensors.GpuClockMhz is { } gpuClockMhz)
                stats.GpuClockMhz = (float)Math.Round(gpuClockMhz, 0);
        }

        if (!vramPopulated && _vramCounter != null)
        {
            try
            {
                float vramBytes = _vramCounter.NextValue();
                stats.UsedVramGb = (float)Math.Round(vramBytes / (1024.0 * 1024 * 1024), 1);
                if (stats.TotalVramGb <= 0) stats.TotalVramGb = _totalVramGb > 0 ? _totalVramGb : 8.0f;
            }
            catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Hardware", logEx); }
        }
    }

    private void SampleDrivesAndNetwork(HardwareStats stats)
    {
        UpdateDiskBandwidth();
        stats.DiskReadBytesPerSec = _cachedDiskReadBytesPerSec;
        stats.DiskWriteBytesPerSec = _cachedDiskWriteBytesPerSec;
        stats.DiskReadSpeedMbps = _cachedDiskReadBytesPerSec / (1024.0 * 1024.0);
        stats.DiskWriteSpeedMbps = _cachedDiskWriteBytesPerSec / (1024.0 * 1024.0);
        stats.FormattedDiskSpeed = HardwareStats.FormatRate(_cachedDiskReadBytesPerSec + _cachedDiskWriteBytesPerSec);

        // Every sample (cheap: one adapter list and its byte counters), so the Network chart keeps recording while Stats is closed.
        UpdateNetworkBandwidth();
        stats.NetDownloadBytesPerSec = _cachedNetDownloadBytesPerSec;
        stats.NetUploadBytesPerSec = _cachedNetUploadBytesPerSec;
        stats.DownloadSpeedMbps = _cachedNetDownloadBytesPerSec / (1024.0 * 1024.0);
        stats.UploadSpeedMbps = _cachedNetUploadBytesPerSec / (1024.0 * 1024.0);
        stats.NetInterfaceName = _detectedNicName;
        stats.NetStatusText = _isNicOnline ? "Connected" : "Offline";
        stats.NetIpAddress = _detectedNicIp;
        stats.NetMacAddress = _detectedNicMac;
        stats.NetLinkSpeedText = _detectedNicSpeed;
        stats.NetSessionReceivedText = _lastBytesReceived >= 1024L * 1024 * 1024 ? $"{(_lastBytesReceived / (1024.0 * 1024 * 1024)):0.0} GB" : $"{(_lastBytesReceived / (1024.0 * 1024)):0.#} MB";
        stats.NetSessionSentText = _lastBytesSent >= 1024L * 1024 * 1024 ? $"{(_lastBytesSent / (1024.0 * 1024 * 1024)):0.0} GB" : $"{(_lastBytesSent / (1024.0 * 1024)):0.#} MB";
        stats.NetPeakDownloadText = HardwareStats.FormatRate(_peakDownloadMbps * 1024.0 * 1024.0);
        stats.NetPeakUploadText = HardwareStats.FormatRate(_peakUploadMbps * 1024.0 * 1024.0);

        if ((!stats.IsGamingActive || _capturing) && (DateTime.UtcNow - _lastPingTime).TotalSeconds >= 3.0)
        {
            _lastPingTime = DateTime.UtcNow;
            var capture = _capturing ? _fpsService : null;
            string host = PingHost;
            Task.Run(async () =>
            {
                _cachedPing = await _networkService.CheckPingAsync(host);
                capture?.AddPing(_cachedPing >= 0 ? _cachedPing : null);
            });
        }
        stats.NetworkPingMs = _cachedPing;
    }

    private void SampleGamingFps(HardwareStats stats)
    {
        _lastGpuUsage = stats.GpuUsage;
        if (_capturing)
        {
            _sessionHardware.Add(stats.GpuUsage, stats.GpuTemp, stats.IsGpuTempMeasured, stats.CpuUsage, stats.CpuTemp, stats.IsCpuTempMeasured);
            stats.CurrentFps = _fpsService.CurrentFps;
            stats.ActiveGameName = _fpsService.ActiveGameName;
            stats.ActiveGameTitle = _fpsService.ActiveGameTitle;
            stats.AverageFrameTimeMs = _fpsService.AverageFrameTimeMs;
            stats.SlowestFrameTimeMs = _fpsService.SlowestFrameTimeMs;
        }
        stats.GpuName = !string.IsNullOrEmpty(_detectedGpuName) ? _detectedGpuName : "GPU";
        stats.GpuDriverVersion = !string.IsNullOrEmpty(_detectedGpuDriver) ? _detectedGpuDriver : "–";
    }

    private static void SampleFixedDrives(HardwareStats stats)
    {
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.IsReady && drive.DriveType == DriveType.Fixed)
                {
                    stats.DrivesList.Add(new DriveMetricItem
                    {
                        Name = drive.Name.TrimEnd('\\'),
                        VolumeLabel = drive.VolumeLabel,
                        FreeGb = Math.Round(drive.AvailableFreeSpace / (1024.0 * 1024 * 1024), 1),
                        TotalGb = Math.Round(drive.TotalSize / (1024.0 * 1024 * 1024), 1)
                    });
                }
            }
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Hardware", logEx); }
    }

    private static PointCollection ToPointCollection(IReadOnlyList<MochiDock.Core.ChartPoint> points)
    {
        var pc = new PointCollection(points.Count);
        foreach (var pt in points) pc.Add(new System.Windows.Point(pt.X, pt.Y));
        pc.Freeze();
        return pc;
    }

    /// <summary>Adds one reading to the 2-minute chart history (cheap; runs on every sample, card open or not). Call under <c>_statsGate</c>.</summary>
    private void PushHistory(HardwareStats stats)
    {
        MochiDock.Core.HardwareChartMath.PushBounded(_cpuHistoryQueue, stats.CpuUsage);
        MochiDock.Core.HardwareChartMath.PushBounded(_gpuHistoryQueue, stats.GpuUsage);
        MochiDock.Core.HardwareChartMath.PushBounded(_ramHistoryQueue, stats.RamUsagePercent);
        MochiDock.Core.HardwareChartMath.PushBounded(_diskReadHistoryQueue, _cachedDiskReadBytesPerSec);
        MochiDock.Core.HardwareChartMath.PushBounded(_diskWriteHistoryQueue, _cachedDiskWriteBytesPerSec);
        MochiDock.Core.HardwareChartMath.PushBounded(_netDownHistoryQueue, _cachedNetDownloadBytesPerSec);
        MochiDock.Core.HardwareChartMath.PushBounded(_netUpHistoryQueue, _cachedNetUploadBytesPerSec);
        MochiDock.Core.HardwareChartMath.PushBounded(_fpsHistoryQueue, stats.CurrentFps);
    }

    /// <summary>Builds the chart shapes from the history (only while the Stats card is open).</summary>
    private void SampleChartsAndHistory(HardwareStats stats)
    {
        var (cpuLine, cpuFill) = MochiDock.Core.HardwareChartMath.BuildAreaSeries(_cpuHistoryQueue, 220.0, 100.0, 100.0);
        stats.CpuHistoryPoints = ToPointCollection(cpuLine);
        stats.CpuHistoryFillPoints = ToPointCollection(cpuFill);

        var (gpuLine, gpuFill) = MochiDock.Core.HardwareChartMath.BuildAreaSeries(_gpuHistoryQueue, 220.0, 100.0, 100.0);
        stats.GpuHistoryPoints = ToPointCollection(gpuLine);
        stats.GpuHistoryFillPoints = ToPointCollection(gpuFill);

        var (ramLine, ramFill) = MochiDock.Core.HardwareChartMath.BuildAreaSeries(_ramHistoryQueue, 220.0, 100.0, 100.0);
        stats.RamHistoryPoints = ToPointCollection(ramLine);
        stats.RamHistoryFillPoints = ToPointCollection(ramFill);

        var disk = MochiDock.Core.HardwareChartMath.BuildMirroredSeries(_diskReadHistoryQueue, _diskWriteHistoryQueue, 220.0, 152.0);
        stats.DiskHistoryPoints = ToPointCollection(disk.UpLine);
        stats.DiskHistoryFillPoints = ToPointCollection(disk.UpFill);
        stats.DiskWriteHistoryPoints = ToPointCollection(disk.DownLine);
        stats.DiskWriteHistoryFillPoints = ToPointCollection(disk.DownFill);
        stats.DiskChartScaleText = HardwareStats.FormatRate(disk.NiceCeiling);

        var net = MochiDock.Core.HardwareChartMath.BuildMirroredSeries(_netDownHistoryQueue, _netUpHistoryQueue, 220.0, 152.0);
        stats.NetHistoryPoints = ToPointCollection(net.UpLine);
        stats.NetHistoryFillPoints = ToPointCollection(net.UpFill);
        stats.NetUploadHistoryPoints = ToPointCollection(net.DownLine);
        stats.NetUploadHistoryFillPoints = ToPointCollection(net.DownFill);
        stats.NetChartScaleText = HardwareStats.FormatRate(net.NiceCeiling);

        double peakFps = _fpsHistoryQueue.Count > 0 ? _fpsHistoryQueue.Max() : 60.0;
        float pickedCeiling = MochiDock.Core.HardwareChartMath.PickFpsCeiling(peakFps);
        stats.FpsCeiling = pickedCeiling;

        var (fpsLine, fpsFill) = MochiDock.Core.HardwareChartMath.BuildAreaSeries(_fpsHistoryQueue, 220.0, 55.0, pickedCeiling);
        stats.FpsHistoryPoints = ToPointCollection(fpsLine);
        stats.FpsHistoryFillPoints = ToPointCollection(fpsFill);

        if (_capturing)
        {
            var worst = _fpsService.RecentWorstFrames();
            if (worst.Length >= 2)
            {
                var ft = MochiDock.Core.HardwareChartMath.BuildFrameTimeSeries(worst, HardwareStats.FrameTimeChartWidth, HardwareStats.FrameTimeChartHeight);
                stats.FrameTimePoints = ToPointCollection(ft.Points);
                stats.FrameTimeGuideY = ft.GuideY;
                stats.FrameTimeCeilingText = $"{ft.Ceiling:0} ms";
            }
        }

        stats.CpuChipSparkPoints = ToPointCollection(MochiDock.Core.HardwareChartMath.BuildChipSparkline(_cpuHistoryQueue, 100.0));
        stats.GpuChipSparkPoints = ToPointCollection(MochiDock.Core.HardwareChartMath.BuildChipSparkline(_gpuHistoryQueue, 100.0));
        stats.DiskChipSparkPoints = ToPointCollection(MochiDock.Core.HardwareChartMath.BuildChipSparkline(_diskReadHistoryQueue, Math.Max(1024.0 * 1024.0, _cachedDiskReadBytesPerSec + _cachedDiskWriteBytesPerSec)));
        stats.NetChipSparkPoints = ToPointCollection(MochiDock.Core.HardwareChartMath.BuildChipSparkline(_netDownHistoryQueue, Math.Max(1024.0 * 1024.0, _cachedNetDownloadBytesPerSec)));
        stats.FpsChipSparkPoints = ToPointCollection(MochiDock.Core.HardwareChartMath.BuildChipSparkline(_fpsHistoryQueue, pickedCeiling));
    }

    private void SampleCoreUsages(HardwareStats stats)
    {
        int coreCount = Environment.ProcessorCount;
        stats.IsHybridCpu = _isHybridCpuDetected;
        stats.CoreSummaryText = _coreSummary.Length > 0 ? _coreSummary : $"{Environment.ProcessorCount} threads";
        stats.CoreHeaderText = _coreHeader;
        int size = Marshal.SizeOf(typeof(SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION)) * coreCount;
        IntPtr pBuf = Marshal.AllocHGlobal(size);
        try
        {
            if (NtQuerySystemInformation(8, pBuf, (uint)size, out uint returnLength) == 0)
            {
                int itemSize = Marshal.SizeOf(typeof(SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION));
                int returnedCount = Math.Min(coreCount, (int)(returnLength / (uint)itemSize));
                var currentTimes = new SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION[returnedCount];
                for (int i = 0; i < returnedCount; i++)
                    currentTimes[i] = Marshal.PtrToStructure<SYSTEM_PROCESSOR_PERFORMANCE_INFORMATION>(IntPtr.Add(pBuf, i * itemSize));

                var kindMap = _coreKindMap;
                if (_prevCoreTimes != null && _prevCoreTimes.Length == returnedCount)
                {
                    double perfSum = 0, effSum = 0;
                    int perfCount = 0, effCount = 0;

                    for (int i = 0; i < returnedCount; i++)
                    {
                        long deltaKernel = currentTimes[i].KernelTime - _prevCoreTimes[i].KernelTime;
                        long deltaUser = currentTimes[i].UserTime - _prevCoreTimes[i].UserTime;
                        long deltaIdle = currentTimes[i].IdleTime - _prevCoreTimes[i].IdleTime;
                        long total = deltaKernel + deltaUser;
                        double usage = (total > 0) ? Math.Clamp(((total - deltaIdle) / (double)total) * 100.0, 0.0, 100.0) : 0.0;

                        string kind = kindMap.TryGetValue(i, out var k) ? k : "Any";
                        if (kind == "Performance") { perfSum += usage; perfCount++; }
                        else if (kind == "Efficiency") { effSum += usage; effCount++; }

                        stats.CoreUsages.Add(new CoreUsageItem { CoreIndex = i + 1, Usage = usage, CoreKind = kind });
                    }

                    if (perfCount > 0) stats.PerformanceCoresPercent = (float)Math.Round(perfSum / perfCount, 0);
                    if (effCount > 0) stats.EfficiencyCoresPercent = (float)Math.Round(effSum / effCount, 0);
                }
                else
                {
                    for (int i = 0; i < returnedCount; i++)
                    {
                        string kind = kindMap.TryGetValue(i, out var k) ? k : "Any";
                        stats.CoreUsages.Add(new CoreUsageItem { CoreIndex = i + 1, Usage = stats.CpuUsage, CoreKind = kind });
                    }
                }

                _prevCoreTimes = currentTimes;
            }
        }
        catch
        {
            for (int i = 0; i < coreCount; i++)
                stats.CoreUsages.Add(new CoreUsageItem { CoreIndex = i + 1, Usage = stats.CpuUsage, CoreKind = "Any" });
        }
        finally { Marshal.FreeHGlobal(pBuf); }
    }

    private void SampleTopProcesses(HardwareStats stats)
    {
        if ((DateTime.UtcNow - _lastProcSample).TotalSeconds >= 2.5)
        {
            _lastProcSample = DateTime.UtcNow;
            try
            {
                var liveCpu = new List<(int Pid, long Created)>();
                var livePids = new HashSet<int>();
                var sampleSeconds = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
                var allProcs = Process.GetProcesses();
                var procMetaList = new List<(string Name, (string DisplayName, ImageSource? Icon) Meta, ulong IoRate, double? Cpu, long Memory)>(allProcs.Length);
                try
                {
                    foreach (var p in allProcs)
                    {
                        using (p)
                        {
                            livePids.Add(p.Id);
                            string name = SafeProcessName(p);
                            if (name == "Idle") continue;
                            double? procCpu = null;
                            long memory = 0;
                            try { var created = p.StartTime.ToUniversalTime().Ticks; liveCpu.Add((p.Id, created)); procCpu = _processCpu.Sample(p.Id, created, p.TotalProcessorTime.Ticks, sampleSeconds, Environment.ProcessorCount); memory = p.WorkingSet64; }
                            catch (System.ComponentModel.Win32Exception) { }
                            catch (System.InvalidOperationException) { }
                            catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Hardware", logEx); }
                            var meta = GetProcessMeta(p);
                            ulong ioRate = 0;
                            try
                            {
                                IntPtr hProc = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, p.Id);
                                if (hProc != IntPtr.Zero)
                                {
                                    try
                                    {
                                        if (GetProcessIoCounters(hProc, out var io))
                                        {
                                            ulong totalIo = io.ReadTransferCount + io.WriteTransferCount;
                                            if (_lastProcIoTotal.TryGetValue(p.Id, out ulong prev))
                                                ioRate = totalIo >= prev ? totalIo - prev : 0;
                                            _lastProcIoTotal[p.Id] = totalIo;
                                        }
                                    }
                                    finally { CloseHandle(hProc); }
                                }
                            }
                            catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Hardware", logEx); }
                            procMetaList.Add((name, meta, ioRate, procCpu, memory));
                        }
                    }
                }
                finally { foreach (var p in allProcs) { try { p.Dispose(); } catch { } } }
                foreach (var stalePid in _pidClassificationCache.Keys)
                    if (!livePids.Contains(stalePid)) _pidClassificationCache.TryRemove(stalePid, out _);
                _processCpu.Retain(liveCpu);

                _cachedTopProcesses = procMetaList
                    .OrderByDescending(p => p.Cpu)
                    .Take(TopProcessCount)
                    .Select(p => new ProcessMetricItem
                    {
                        Name = p.Name,
                        DisplayName = p.Meta.DisplayName,
                        Icon = p.Meta.Icon,
                        CpuPercent = p.Cpu,
                        ValueText = p.Cpu.HasValue ? $"{p.Cpu.Value:0.0}%" : "—",
                        MemoryUsageText = $"{Math.Round(p.Memory / (1024.0 * 1024.0), 0)} MB"
                    }).ToList();

                _cachedTopMemoryProcesses = procMetaList
                    .OrderByDescending(p => p.Memory)
                    .Take(TopProcessCount)
                    .Select(p => new ProcessMetricItem
                    {
                        Name = p.Name,
                        DisplayName = p.Meta.DisplayName,
                        Icon = p.Meta.Icon,
                        CpuPercent = p.Cpu,
                        ValueText = $"{Math.Round(p.Memory / (1024.0 * 1024.0), 0)} MB",
                        MemoryUsageText = $"{Math.Round(p.Memory / (1024.0 * 1024.0), 0)} MB"
                    }).ToList();

                _cachedTopDiskProcesses = procMetaList
                    .OrderByDescending(p => p.IoRate)
                    .Take(TopProcessCount)
                    .Select(p => new ProcessMetricItem
                    {
                        Name = p.Name,
                        DisplayName = p.Meta.DisplayName,
                        Icon = p.Meta.Icon,
                        FormattedDiskRate = HardwareStats.FormatRate(p.IoRate / 2.5),
                        ValueText = HardwareStats.FormatRate(p.IoRate / 2.5),
                        MemoryUsageText = HardwareStats.FormatRate(p.IoRate / 2.5)
                    }).ToList();
            }
            catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Hardware", logEx); }
        }
        foreach (var p in _cachedTopProcesses) stats.TopProcesses.Add(p);
        foreach (var p in _cachedTopMemoryProcesses) stats.TopMemoryProcesses.Add(p);
        foreach (var p in _cachedTopDiskProcesses) stats.TopDiskProcesses.Add(p);
    }

    private static string SafeProcessName(Process p) { try { return p.ProcessName; } catch { return "Unavailable process"; } }

    private void UpdateDiskBandwidth()
    {
        try
        {
            if (_diskReadCounter != null)
            {
                _cachedDiskReadBytesPerSec = _diskReadCounter.NextValue();
            }
            if (_diskWriteCounter != null)
            {
                _cachedDiskWriteBytesPerSec = _diskWriteCounter.NextValue();
            }
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Hardware", logEx); }
    }

    private void UpdateNetworkBandwidth()
    {
        try
        {
            var now = DateTime.UtcNow;
            if (_lastNetworkSampleTime == DateTime.MinValue)
            {
                _lastNetworkSampleTime = now;
                GetTotalNetworkBytes(System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces(), out _, out _);
                _lastBytesReceived=_networkHistory.Received;_lastBytesSent=_networkHistory.Sent;
                return;
            }

            double elapsedSec = (now - _lastNetworkSampleTime).TotalSeconds;
            if (elapsedSec >= 0.75)
            {
                var allNics = System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces();
                var counters = new List<MochiDock.Core.AdapterCounter>();
                foreach (var nic in allNics) {
                    if(nic.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up || nic.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback || nic.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Tunnel) continue;
                    try { var c=nic.GetIPv4Statistics(); counters.Add(new(nic.Id,c.BytesReceived,c.BytesSent)); } catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Hardware", logEx); }
                }
                GetTotalNetworkBytes(allNics, out _, out _);
                _networkHistory.Update(Stopwatch.GetTimestamp()/(double)Stopwatch.Frequency,counters);
                _cachedNetDownloadBytesPerSec = _networkHistory.Down;
                _cachedNetUploadBytesPerSec = _networkHistory.Up;
                _cachedDownloadSpeedMbps = _networkHistory.Down/(1024*1024);
                _cachedUploadSpeedMbps = _networkHistory.Up/(1024*1024);
                _peakDownloadMbps = _networkHistory.PeakDown/(1024*1024);
                _peakUploadMbps = _networkHistory.PeakUp/(1024*1024);
                _lastBytesReceived = _networkHistory.Received;
                _lastBytesSent = _networkHistory.Sent;
                _lastNetworkSampleTime = now;
            }
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Hardware", logEx); }
    }

    private void GetTotalNetworkBytes(System.Net.NetworkInformation.NetworkInterface[] allNics, out long bytesReceived, out long bytesSent)
    {
        bytesReceived = 0;
        bytesSent = 0;
        try
        {
            var activeNics = allNics
                .Where(nic => nic.OperationalStatus == System.Net.NetworkInformation.OperationalStatus.Up &&
                              nic.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Loopback &&
                              nic.NetworkInterfaceType != System.Net.NetworkInformation.NetworkInterfaceType.Tunnel)
                .ToList();

            _isNicOnline = activeNics.Count > 0;
            if (activeNics.Count > 0)
            {
                var candidates = activeNics.Select(nic =>
                {
                    bool hasGw = false;
                    try
                    {
                        var gw = nic.GetIPProperties()?.GatewayAddresses;
                        if (gw != null && gw.Any(g => g.Address != null && g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork && !g.Address.Equals(System.Net.IPAddress.Any)))
                        {
                            hasGw = true;
                        }
                    }
                    catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Hardware", logEx); }

                    bool hasMac = false;
                    try
                    {
                        var bytes = nic.GetPhysicalAddress()?.GetAddressBytes();
                        hasMac = bytes != null && bytes.Length > 0 && bytes.Any(b => b != 0);
                    }
                    catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Hardware", logEx); }

                    return new MochiDock.Core.NetworkAdapterCandidate(
                        nic.Id,
                        nic.Name,
                        nic.Description,
                        nic.NetworkInterfaceType,
                        nic.Speed,
                        hasGw,
                        hasMac
                    );
                }).ToList();

                var primaryId = MochiDock.Core.NetworkPolicy.SelectPrimaryId(candidates);
                var primary = activeNics.FirstOrDefault(n => n.Id == primaryId) ?? activeNics.OrderByDescending(n => n.Speed).FirstOrDefault();
                if (primary != null)
                {
                    _detectedNicName = primary.Name;
                    _detectedNicSpeed = primary.Speed > 0 ? $"{(primary.Speed / 1_000_000_000.0 >= 1.0 ? primary.Speed / 1_000_000_000.0 : primary.Speed / 1_000_000.0):0.#} {(primary.Speed >= 1_000_000_000 ? "Gbps" : "Mbps")}" : "Auto";
                    var phys = primary.GetPhysicalAddress().ToString();
                    if (!string.IsNullOrEmpty(phys) && phys.Length >= 12)
                    {
                        _detectedNicMac = string.Join(":", Enumerable.Range(0, 6).Select(i => phys.Substring(i * 2, 2)));
                    }
                    var ipProp = primary.GetIPProperties();
                    var unicast = ipProp.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                    if (unicast != null)
                    {
                        _detectedNicIp = unicast.Address.ToString();
                    }
                }
            }

            foreach (var nic in activeNics)
            {
                try
                {
                    var stats = nic.GetIPv4Statistics();
                    bytesReceived += stats.BytesReceived;
                    bytesSent += stats.BytesSent;
                }
                catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Hardware", logEx); }
            }
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Hardware", logEx); }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _fpsService.Dispose(); } catch { }
        StopCpuTelemetry();
        try { _cpuCounter?.Dispose(); } catch { }
        try { _gpuCounter?.Dispose(); } catch { }
        try { _vramCounter?.Dispose(); } catch { }
        try { _diskReadCounter?.Dispose(); } catch { }
        try { _diskWriteCounter?.Dispose(); } catch { }
        try { _taskbarDiskIdle?.Dispose(); } catch { }
        _taskbarDiskIdle = null;
        _cpuCounter = null;
        _gpuCounter = null;
        _vramCounter = null;
        _diskReadCounter = null;
        _diskWriteCounter = null;
        if (_pdhGpuQuery != IntPtr.Zero)
        {
            try { PdhCloseQuery(_pdhGpuQuery); } catch { }
            _pdhGpuQuery = IntPtr.Zero;
        }
        if (Volatile.Read(ref _nvmlLoaded))
        {
            Volatile.Write(ref _nvmlLoaded, false);
            _nvmlDevice = IntPtr.Zero;
            try { nvmlShutdown(); } catch { }
        }
    }
}