using System.IO;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using MochiDock.Core;

namespace MochiDock.Services;

/// <summary>
/// MochiDock runs elevated, but classic Outlook does not, and Windows blocks COM between the two.
/// This host starts MochiDock.OutlookHelper.exe with the user's normal (non-elevated) token, accepts its private pipe
/// connection, and relays new mail and open/mark-read commands.
/// </summary>
public sealed class OutlookHelperHost : IDisposable
{
    static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(20);

    CancellationTokenSource? _cts;
    StreamWriter? _writer;
    int _activeHelperPid;
    readonly object _writeGate = new();

    public event Action<OutlookMailDto>? MailArrived;

    /// <summary>Folder names to watch besides every Inbox (and their subfolders); sent to the helper on connect.</summary>
    public IReadOnlyList<string> TrackedFolders { get; set; } = OutlookFolderPolicy.DefaultTrackedFolders;
    public bool OnlyListedFolders { get; set; }

    public static string HelperPath
    {
        get
        {
            string root = Path.Combine(AppContext.BaseDirectory, "MochiDock.OutlookHelper.exe");
            return File.Exists(root) ? root : Path.Combine(AppContext.BaseDirectory, "OutlookHelper", "MochiDock.OutlookHelper.exe");
        }
    }
    public static bool IsHelperAvailable => File.Exists(HelperPath);

    /// <summary>True when this process runs with an elevated (administrator) token.</summary>
    public static bool IsElevated()
    {
        using var identity = WindowsIdentity.GetCurrent();
        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    public void Start()
    {
        Stop();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        Task.Run(() => RunAsync(token));
    }

    public void Stop()
    {
        var cts = _cts;
        _cts = null;
        if (cts is null) return;
        int pid = Volatile.Read(ref _activeHelperPid);
        Send(OutlookPipeMessage.Stop());
        try { cts.Cancel(); } catch (ObjectDisposedException) { }
        cts.Dispose();
        WaitOrKillHelper(pid, 2000);
    }

    /// <summary>Sends a new folder list to a running helper (it rebuilds its watched folders).</summary>
    public void SendTrackedFolders(IReadOnlyList<string> names, bool onlyListed)
    {
        TrackedFolders = names;
        OnlyListedFolders = onlyListed;
        Send(OutlookPipeMessage.Config(OutlookFolderPolicy.Join(names), onlyListed));
    }

    public void Open(string? entryId) => Send(OutlookPipeMessage.Open(entryId ?? ""));
    public void MarkRead(string? entryId) { if (!string.IsNullOrEmpty(entryId)) Send(OutlookPipeMessage.MarkRead(entryId)); }

    void Send(OutlookPipeMessage message)
    {
        lock (_writeGate)
        {
            try { _writer?.WriteLine(message.Encode()); }
            catch (Exception ex) { AppLog.Warn("Outlook", "Helper write failed: " + ex.Message); }
        }
    }

    async Task RunAsync(CancellationToken token)
    {
        int retrySeconds = 5;
        while (!token.IsCancellationRequested)
        {
            int helperPid = 0;
            try
            {
                string pipeName = "MochiDock.Outlook." + Guid.NewGuid().ToString("N");
                string secret = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
                using var server = CreateServer(pipeName);

                helperPid = LaunchDeElevated(HelperPath, $"{pipeName} {secret}");
                Volatile.Write(ref _activeHelperPid, helperPid);
                AppLog.Warn("Outlook", "Started the non-elevated Outlook helper.");

                using (var connectCts = CancellationTokenSource.CreateLinkedTokenSource(token))
                {
                    connectCts.CancelAfter(ConnectTimeout);
                    await server.WaitForConnectionAsync(connectCts.Token);
                }

                var utf8 = new UTF8Encoding(false);
                using var reader = new StreamReader(server, utf8);
                using var writer = new StreamWriter(server, utf8) { AutoFlush = true };

                var hello = OutlookPipeMessage.TryDecode(await reader.ReadLineAsync(token));
                if (hello?.T != "hello" || hello.Secret is null
                    || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(hello.Secret), Encoding.UTF8.GetBytes(secret)))
                {
                    throw new InvalidOperationException("The helper did not present the expected secret.");
                }

                lock (_writeGate) _writer = writer;
                Send(OutlookPipeMessage.Config(OutlookFolderPolicy.Join(TrackedFolders), OnlyListedFolders));
                retrySeconds = 5;

                string? line;
                while ((line = await reader.ReadLineAsync(token)) != null)
                {
                    var message = OutlookPipeMessage.TryDecode(line);
                    if (message is null) continue;
                    if (message.T == "mail" && message.Mail is { } mail) MailArrived?.Invoke(mail);
                    else if (message.T == "log" && !string.IsNullOrEmpty(message.Msg)) AppLog.Warn("Outlook", message.Msg);
                }
                AppLog.Warn("Outlook", "The Outlook helper disconnected.");
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception ex) { AppLog.Warn("Outlook", "Outlook helper: " + ex.Message); }
            finally
            {
                Volatile.Write(ref _activeHelperPid, 0);
                lock (_writeGate) _writer = null;
                KillHelper(helperPid);
            }

            try { await Task.Delay(TimeSpan.FromSeconds(retrySeconds), token); }
            catch (OperationCanceledException) { break; }
            retrySeconds = Math.Min(retrySeconds * 2, 60);
        }
    }

    /// <summary>A pipe only this user can open, so another account on the PC cannot connect to it.</summary>
    static NamedPipeServerStream CreateServer(string name)
    {
        var security = new PipeSecurity();
        var user = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("No current user SID.");
        security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 0, 0, security);
    }

    static void KillHelper(int pid)
    {
        if (pid <= 0) return;
        try
        {
            using var process = Process.GetProcessById(pid);
            if (!process.HasExited)
            {
                process.Kill();
                process.WaitForExit(2000);
            }
        }
        catch { /* already gone */ }
    }

    static void WaitOrKillHelper(int pid, int timeoutMs)
    {
        if (pid <= 0) return;
        try
        {
            using var process = Process.GetProcessById(pid);
            if (!process.HasExited && !process.WaitForExit(timeoutMs))
            {
                process.Kill();
                process.WaitForExit(timeoutMs);
            }
        }
        catch { /* already gone */ }
    }

    public void Dispose() => Stop();

    // --- Starting a process with the user's normal token (the shell's token), from an elevated process ---

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct STARTUPINFO
    {
        public int cb; public string? lpReserved, lpDesktop, lpTitle;
        public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
        public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_INFORMATION { public IntPtr hProcess, hThread; public int dwProcessId, dwThreadId; }

    [DllImport("user32.dll")] static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
    [DllImport("advapi32.dll", SetLastError = true)] static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool DuplicateTokenEx(IntPtr token, uint access, IntPtr attributes, int impersonationLevel, int tokenType, out IntPtr newToken);
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    static extern bool CreateProcessWithTokenW(IntPtr token, uint logonFlags, string? application, string commandLine, uint creationFlags,
        IntPtr environment, string? currentDirectory, ref STARTUPINFO startupInfo, out PROCESS_INFORMATION processInformation);

    const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000, TOKEN_DUPLICATE = 0x2, MAXIMUM_ALLOWED = 0x02000000;
    const uint CREATE_NO_WINDOW = 0x08000000;

    /// <summary>Starts the helper using the token of the running shell (Explorer), which is the user's non-elevated token.</summary>
    static int LaunchDeElevated(string exePath, string arguments)
    {
        if (!File.Exists(exePath)) throw new FileNotFoundException("Outlook helper not found.", exePath);

        IntPtr shell = GetShellWindow();
        if (shell == IntPtr.Zero) throw new InvalidOperationException("No desktop shell is running to take a normal-privilege token from.");
        GetWindowThreadProcessId(shell, out uint shellPid);

        IntPtr process = IntPtr.Zero, shellToken = IntPtr.Zero, token = IntPtr.Zero;
        try
        {
            process = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, shellPid);
            if (process == IntPtr.Zero) throw new InvalidOperationException($"OpenProcess failed ({Marshal.GetLastWin32Error()}).");
            if (!OpenProcessToken(process, TOKEN_DUPLICATE, out shellToken)) throw new InvalidOperationException($"OpenProcessToken failed ({Marshal.GetLastWin32Error()}).");
            if (!DuplicateTokenEx(shellToken, MAXIMUM_ALLOWED, IntPtr.Zero, 2 /* SecurityImpersonation */, 1 /* TokenPrimary */, out token))
                throw new InvalidOperationException($"DuplicateTokenEx failed ({Marshal.GetLastWin32Error()}).");

            var startup = new STARTUPINFO { cb = Marshal.SizeOf<STARTUPINFO>() };
            string commandLine = $"\"{exePath}\" {arguments}";
            if (!CreateProcessWithTokenW(token, 0, null, commandLine, CREATE_NO_WINDOW, IntPtr.Zero, Path.GetDirectoryName(exePath), ref startup, out var info))
                throw new InvalidOperationException($"CreateProcessWithTokenW failed ({Marshal.GetLastWin32Error()}).");

            CloseHandle(info.hThread);
            CloseHandle(info.hProcess);
            return info.dwProcessId;
        }
        finally
        {
            if (token != IntPtr.Zero) CloseHandle(token);
            if (shellToken != IntPtr.Zero) CloseHandle(shellToken);
            if (process != IntPtr.Zero) CloseHandle(process);
        }
    }
}
