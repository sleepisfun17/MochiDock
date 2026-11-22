using System.Runtime.InteropServices;

namespace MochiDock.Services;

/// <summary>
/// Minimal Win32 <c>Shell_NotifyIconW</c> tray icon attached to an existing window HWND.
/// Avoids pulling in <c>System.Windows.Forms</c> / <c>System.Drawing</c>.
/// </summary>
public sealed class Win32TrayIcon : IDisposable
{
    public const int CallbackMessage = 0x8101; // WM_APP + 257

    private const uint NIF_MESSAGE = 0x00000001;
    private const uint NIF_ICON = 0x00000002;
    private const uint NIF_TIP = 0x00000004;
    private const uint NIF_INFO = 0x00000010;
    private const uint NIM_ADD = 0x00000000;
    private const uint NIM_MODIFY = 0x00000001;
    private const uint NIM_DELETE = 0x00000002;
    private const uint NIIF_INFO = 0x00000001;
    private const uint IMAGE_ICON = 1;
    private const uint LR_LOADFROMFILE = 0x00000010;
    private const uint LR_DEFAULTSIZE = 0x00000040;
    private const int WM_LBUTTONDBLCLK = 0x0203;
    private const int WM_RBUTTONUP = 0x0205;
    private const int WM_CONTEXTMENU = 0x007B;

    private static readonly uint TaskbarCreatedMessage = RegisterWindowMessageW("TaskbarCreated");

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct NOTIFYICONDATAW
    {
        public uint cbSize;
        public IntPtr hWnd;
        public uint uID;
        public uint uFlags;
        public uint uCallbackMessage;
        public IntPtr hIcon;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string szTip;
        public uint dwState;
        public uint dwStateMask;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szInfo;
        public uint uTimeoutOrVersion;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)] public string szInfoTitle;
        public uint dwInfoFlags;
        public Guid guidItem;
        public IntPtr hBalloonIcon;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpData);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern uint RegisterWindowMessageW(string lpString);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadImageW(IntPtr hInst, string name, uint type, int cx, int cy, uint fuLoad);

    [DllImport("user32.dll")]
    private static extern IntPtr LoadIconW(IntPtr hInstance, IntPtr lpIconName);

    [DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);

    private readonly string _tooltip;
    private readonly IntPtr _iconHandle;
    private readonly bool _ownsIcon;
    private IntPtr _hwnd;
    private bool _visible;
    private bool _added;
    private bool _disposed;

    public event Action? DoubleClick;
    public event Action? RightClick;

    public Win32TrayIcon(string? iconPath, string tooltip)
    {
        _tooltip = tooltip.Length > 127 ? tooltip[..127] : tooltip;
        if (!string.IsNullOrWhiteSpace(iconPath) && System.IO.File.Exists(iconPath))
        {
            _iconHandle = LoadImageW(IntPtr.Zero, iconPath, IMAGE_ICON, 0, 0, LR_LOADFROMFILE | LR_DEFAULTSIZE);
            _ownsIcon = _iconHandle != IntPtr.Zero;
        }
        if (_iconHandle == IntPtr.Zero)
        {
            _iconHandle = LoadIconW(IntPtr.Zero, new IntPtr(32512)); // IDI_APPLICATION
        }
    }

    public bool Visible
    {
        get => _visible;
        set
        {
            _visible = value;
            if (_disposed || _hwnd == IntPtr.Zero) return;
            if (value) AddOrUpdate();
            else Delete();
        }
    }

    public void Attach(IntPtr hwnd)
    {
        if (_disposed || hwnd == IntPtr.Zero) return;
        _hwnd = hwnd;
        if (_visible) AddOrUpdate();
    }

    public bool ProcessMessage(int msg, IntPtr wParam, IntPtr lParam)
    {
        if (_disposed) return false;
        if (TaskbarCreatedMessage != 0 && (uint)msg == TaskbarCreatedMessage)
        {
            _added = false;
            if (_visible && _hwnd != IntPtr.Zero) AddOrUpdate();
            return false;
        }
        if (msg != CallbackMessage) return false;

        int notifyMsg = unchecked((int)(lParam.ToInt64() & 0xFFFF));
        switch (notifyMsg)
        {
            case WM_LBUTTONDBLCLK:
                DoubleClick?.Invoke();
                return true;
            case WM_RBUTTONUP:
            case WM_CONTEXTMENU:
                RightClick?.Invoke();
                return true;
            default:
                return false;
        }
    }

    public void ShowBalloonTip(int timeoutMs, string title, string text)
    {
        if (_disposed || _hwnd == IntPtr.Zero) return;
        if (!_added) AddOrUpdate();
        var data = CreateBaseData();
        data.uFlags = NIF_INFO;
        data.szInfoTitle = title.Length > 63 ? title[..63] : title;
        data.szInfo = text.Length > 255 ? text[..255] : text;
        data.dwInfoFlags = NIIF_INFO;
        data.uTimeoutOrVersion = (uint)Math.Clamp(timeoutMs, 1000, 30000);
        Shell_NotifyIconW(NIM_MODIFY, ref data);
    }

    private NOTIFYICONDATAW CreateBaseData() => new()
    {
        cbSize = (uint)Marshal.SizeOf<NOTIFYICONDATAW>(),
        hWnd = _hwnd,
        uID = 1,
        uCallbackMessage = CallbackMessage,
        hIcon = _iconHandle,
        szTip = _tooltip,
        szInfo = "",
        szInfoTitle = ""
    };

    private void AddOrUpdate()
    {
        var data = CreateBaseData();
        data.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
        bool ok = Shell_NotifyIconW(_added ? NIM_MODIFY : NIM_ADD, ref data);
        if (!ok && _added) ok = Shell_NotifyIconW(NIM_ADD, ref data);
        _added = ok;
    }

    private void Delete()
    {
        if (!_added || _hwnd == IntPtr.Zero) return;
        var data = CreateBaseData();
        Shell_NotifyIconW(NIM_DELETE, ref data);
        _added = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Delete();
        if (_ownsIcon && _iconHandle != IntPtr.Zero) DestroyIcon(_iconHandle);
    }
}
