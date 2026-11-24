using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MochiDock.Core;
using MochiDock.ViewModels;
using Microsoft.Win32;

namespace MochiDock.Services;

/// <summary>
/// Layered child window of the Windows taskbar (left of the tray) that shows the taskbar readout: two-line blocks for CPU/GPU, memory/disk,
/// network, the next prayer, the weather and wireless device batteries (<see cref="TaskbarReadouts"/> decides what, <see cref="TaskbarReadoutRenderer"/> draws it).
/// A left click on a block raises <see cref="BlockClicked"/>; a right click raises <see cref="ContextMenuRequested"/>.
/// Owned by <c>MainWindow</c>; created, updated and destroyed on the UI thread. Primary, horizontal, non-auto-hidden taskbar only.
/// The window class and the render surface are the only native resources; <see cref="Dispose"/> releases both and restores the Windows 10 rebar.
/// </summary>
public sealed class TaskbarReadoutWidget : IDisposable
{
    const int GWL_EXSTYLE = -20;
    const long WS_EX_LAYOUTRTL = 0x00400000;
    const string ClassName = "MochiDockTaskbarReadout";
    const uint WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, WS_CLIPSIBLINGS = 0x04000000, WS_POPUP = 0x80000000;
    const uint WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOPMOST = 8;
    const uint SWP_NOZORDER = 4, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
    const int WM_MOUSEMOVE = 0x0200, WM_RBUTTONUP = 0x0205, WM_LBUTTONUP = 0x0202, WM_NCHITTEST = 0x0084, HTCLIENT = 1, WM_MOUSEACTIVATE = 0x21, MA_NOACTIVATE = 3;
    const int TTM_ADDTOOLW = 0x0432, TTM_UPDATETIPTEXTW = 0x0439, TTM_SETMAXTIPWIDTH = 0x0418;
    const uint TTF_SUBCLASS = 0x10, TTS_ALWAYSTIP = 1, TTS_NOPREFIX = 2;

    delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WndClassEx { public uint Size, Style; public IntPtr WndProc; public int ClsExtra, WndExtra; public IntPtr Instance, Icon, Cursor, Background; public string? Menu, Class; public IntPtr IconSm; }
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct Size { public int Cx, Cy; }
    [StructLayout(LayoutKind.Sequential)] struct Blend { public byte Op, Flags, Alpha, Format; }
    [StructLayout(LayoutKind.Sequential)] struct AppBarData { public uint Size; public IntPtr Hwnd; public uint Callback, Edge; public Rect Rc; public IntPtr Param; }
    [StructLayout(LayoutKind.Sequential)] struct BitmapInfoHeader { public uint Size; public int Width, Height; public ushort Planes, BitCount; public uint Compression, SizeImage; public int XPels, YPels; public uint ClrUsed, ClrImportant; }
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct ToolInfo { public uint Size, Flags; public IntPtr Hwnd; public UIntPtr Id; public Rect Rc; public IntPtr Instance; public IntPtr Text; }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern ushort RegisterClassExW(ref WndClassEx cls);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern bool UnregisterClassW(string name, IntPtr instance);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)] static extern IntPtr CreateWindowExW(uint ex, string cls, string? title, uint style, int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr DefWindowProcW(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowW(string cls, string? title);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern IntPtr FindWindowExW(IntPtr parent, IntPtr after, string cls, string? title);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
    [DllImport("user32.dll")] static extern int MapWindowPoints(IntPtr from, IntPtr to, ref Rect rect, int count);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, ref ToolInfo info);
    [DllImport("user32.dll", EntryPoint = "SendMessageW")] static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr dst, IntPtr pos, ref Size size, IntPtr src, ref Point srcPos, int key, ref Blend blend, int flags);
    [DllImport("shell32.dll")] static extern UIntPtr SHAppBarMessage(uint message, ref AppBarData data);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfoHeader info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandleW(string? name);
    [DllImport("comctl32.dll")] static extern bool InitCommonControlsEx(ref InitCommonControls init);
    [StructLayout(LayoutKind.Sequential)] struct InitCommonControls { public uint Size, Classes; }

    readonly MainViewModel viewModel;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly WndProcDelegate wndProc; // kept in a field so the native callback is not garbage-collected
    bool classRegistered, disposed;
    IntPtr widget, tooltip, parent;
    string renderKey = "";
    int widgetWidth, widgetHeight;
    IntPtr rebar;
    int rebarOriginalWidth;
    string tooltipText = "";
    IntPtr tooltipBuffer;
    IReadOnlyList<TaskbarBlock> shownBlocks = Array.Empty<TaskbarBlock>();
    IReadOnlyList<TaskbarBlockRect> blockRects = Array.Empty<TaskbarBlockRect>();
    TaskbarBlockKind? hovered;

    /// <summary>Raised on the UI thread when the widget is right-clicked.</summary>
    public event Action? ContextMenuRequested;

    /// <summary>Raised on the UI thread when a block is left-clicked (the owner opens the matching card).</summary>
    public event Action<TaskbarBlockKind>? BlockClicked;

    public TaskbarReadoutWidget(MainViewModel viewModel)
    {
        this.viewModel = viewModel;
        wndProc = WndProc;
        timer.Tick += (_, _) => Tick();
        viewModel.TaskbarBlocksChanged += Refresh;
        viewModel.DeviceBatterySettingsChanged += Refresh;   // the plate behind the text
        Refresh();
    }

    // The widget exists while any block has content, and is hidden when none does.
    bool ShouldShow => !disposed && viewModel.TaskbarBlocks.Count > 0;

    void Refresh()
    {
        if (disposed) return;
        if (ShouldShow) { if (!timer.IsEnabled) timer.Start(); Tick(); }
        else { timer.Stop(); Hide(); }
    }

    void Tick()
    {
        try
        {
            if (!ShouldShow) { Hide(); return; }
            // MochiDock is elevated, so the TaskbarCreated broadcast may be filtered; instead re-find the taskbar every second.
            var taskbar = FindWindowW("Shell_TrayWnd", null);
            if (taskbar == IntPtr.Zero || !GetWindowRect(taskbar, out var taskbarRect) || IsVerticalOrAutoHidden(taskbar, taskbarRect)) { Hide(); return; }
            var tray = FindWindowExW(taskbar, IntPtr.Zero, "TrayNotifyWnd", null);
            if (tray == IntPtr.Zero || !GetWindowRect(tray, out var trayRect)) { Hide(); return; }

            if (widget != IntPtr.Zero && (parent != taskbar || !IsWindow(widget))) Destroy();
            var dpi = GetDpiForWindow(taskbar);
            var scale = (dpi == 0 ? 96 : dpi) / 96.0;
            var blocks = viewModel.TaskbarBlocks;
            var background = viewModel.ShowDeviceBatteryBackground;
            var light = TaskbarUsesLightTheme();
            var height = taskbarRect.Bottom - taskbarRect.Top;
            var key = string.Join("\n", blocks.Select(b => b.ToString())) + $"@{dpi}/{light}/{height}/{background}";
            if (key != renderKey || widget == IntPtr.Zero)
            {
                var surface = TaskbarReadoutRenderer.Render(blocks, dpi == 0 ? 96 : dpi, !light, height, background);
                if (surface is null) { Hide(); return; }
                widgetWidth = surface.Width; widgetHeight = surface.Height;
                if (widget == IntPtr.Zero && !Create(taskbar)) return;
                Present(surface);
                renderKey = key;
                shownBlocks = blocks;
                blockRects = surface.Blocks;
                hovered = null;
                UpdateTooltip(null);
            }

            // Position in the taskbar's client coordinates: immediately beside the tray (stepping past the Widgets button), vertically centred.
            var rtl = (GetWindowLongPtr(taskbar, GWL_EXSTYLE).ToInt64() & WS_EX_LAYOUTRTL) != 0;
            var widgets = WidgetsButtonRect(taskbar);
            int x;
            if (rtl)
            {
                // Right-to-left Windows: client x runs from the taskbar's right edge, so the widget goes on the tray's leading (right) side.
                var screenLeft = trayRect.Right;
                if (widgets is { } w && w.Left < screenLeft + widgetWidth && w.Right > screenLeft) screenLeft = (int)Math.Ceiling(w.Right);
                x = taskbarRect.Right - screenLeft - widgetWidth;
            }
            else
            {
                var screenLeft = trayRect.Left - widgetWidth;
                // Windows 11 left-aligned taskbar: the Widgets (weather) button sits next to the tray; step past it.
                if (widgets is { } w && w.Right > screenLeft && w.Left < trayRect.Left) screenLeft = (int)Math.Floor(w.Left) - widgetWidth;
                x = screenLeft - taskbarRect.Left;
            }
            var y = (height - widgetHeight) / 2;
            // HWND_TOP (0): on Windows 11 the XAML bridge child covers the whole taskbar, so the widget must stay above it.
            SetWindowPos(widget, IntPtr.Zero, x, y, widgetWidth, widgetHeight, SWP_NOACTIVATE | SWP_SHOWWINDOW);
            if (!rtl) ShrinkRebar(taskbar, x);
        }
        catch (Exception ex) { AppLog.Warn("Taskbar", ex); }
    }

    // ---- Windows 11 Widgets (weather) button -----------------------------------------------------------------------------------------------
    // The button is XAML, so only UI Automation knows its rectangle. Looked up off the UI thread, at most every 5 s, and only while the
    // taskbar is left-aligned (centre-aligned taskbars put it at the far left, away from the tray).
    System.Windows.Rect? widgetsRect;
    DateTime widgetsCheckedUtc;
    bool widgetsLookupRunning;

    System.Windows.Rect? WidgetsButtonRect(IntPtr taskbar)
    {
        if (!TaskbarIsLeftAligned()) return widgetsRect = null;
        if (!widgetsLookupRunning && DateTime.UtcNow - widgetsCheckedUtc > TimeSpan.FromSeconds(5))
        {
            widgetsLookupRunning = true;
            widgetsCheckedUtc = DateTime.UtcNow;
            var handle = taskbar;
            _ = Task.Run(() =>
            {
                System.Windows.Rect? found = null;
                try
                {
                    var root = System.Windows.Automation.AutomationElement.FromHandle(handle);
                    var button = root.FindFirst(System.Windows.Automation.TreeScope.Descendants,
                        new System.Windows.Automation.PropertyCondition(System.Windows.Automation.AutomationElement.AutomationIdProperty, "WidgetsButton"));
                    if (button is not null && !button.Current.IsOffscreen) found = button.Current.BoundingRectangle;
                }
                catch (Exception ex) when (ex is System.Windows.Automation.ElementNotAvailableException or InvalidOperationException or System.Runtime.InteropServices.COMException) { }
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => { widgetsRect = found is { IsEmpty: false } r ? r : null; widgetsLookupRunning = false; });
            });
        }
        return widgetsRect;
    }

    static bool TaskbarIsLeftAligned()
    {
        try { return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced", "TaskbarAl", 1) is int v && v == 0; }
        catch { return false; }
    }

    static bool IsVerticalOrAutoHidden(IntPtr taskbar, Rect rect)
    {
        if (rect.Bottom - rect.Top > rect.Right - rect.Left) return true;
        var data = new AppBarData { Size = (uint)Marshal.SizeOf<AppBarData>(), Hwnd = taskbar };
        return ((long)SHAppBarMessage(0x4 /* ABM_GETSTATE */, ref data) & 1 /* ABS_AUTOHIDE */) != 0;
    }

    static bool TaskbarUsesLightTheme()
    {
        try { return Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "SystemUsesLightTheme", 0) is int v && v != 0; }
        catch { return false; }
    }

    // ---- rendering -------------------------------------------------------------------------------------------------------------------------

    void Present(TaskbarRender surface)
    {
        var screen = GetDC(IntPtr.Zero);
        var memory = CreateCompatibleDC(screen);
        var header = new BitmapInfoHeader { Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(), Width = surface.Width, Height = -surface.Height, Planes = 1, BitCount = 32 }; // negative height: top-down
        var dib = CreateDIBSection(screen, ref header, 0, out var bits, IntPtr.Zero, 0);
        try
        {
            if (dib == IntPtr.Zero || bits == IntPtr.Zero) return;
            Marshal.Copy(surface.Pixels, 0, bits, surface.Pixels.Length);
            var old = SelectObject(memory, dib);
            var size = new Size { Cx = surface.Width, Cy = surface.Height };
            var origin = new Point();
            var blend = new Blend { Op = 0 /* AC_SRC_OVER */, Alpha = 255, Format = 1 /* AC_SRC_ALPHA */ };
            UpdateLayeredWindow(widget, screen, IntPtr.Zero, ref size, memory, ref origin, 0, ref blend, 2 /* ULW_ALPHA */);
            SelectObject(memory, old);
        }
        finally { if (dib != IntPtr.Zero) DeleteObject(dib); DeleteDC(memory); ReleaseDC(IntPtr.Zero, screen); }
    }

    // ---- window ----------------------------------------------------------------------------------------------------------------------------

    bool Create(IntPtr taskbar)
    {
        var instance = GetModuleHandleW(null);
        if (!classRegistered)
        {
            var cls = new WndClassEx { Size = (uint)Marshal.SizeOf<WndClassEx>(), WndProc = Marshal.GetFunctionPointerForDelegate(wndProc), Instance = instance, Class = ClassName };
            classRegistered = RegisterClassExW(ref cls) != 0 || Marshal.GetLastWin32Error() == 1410; // 1410: already registered
            if (!classRegistered) return false;
        }
        // Layered child windows need Windows 8 or later (always true for this app's target).
        widget = CreateWindowExW(WS_EX_LAYERED | WS_EX_NOACTIVATE, ClassName, null, WS_CHILD | WS_CLIPSIBLINGS | WS_VISIBLE, 0, 0, widgetWidth, widgetHeight, taskbar, IntPtr.Zero, instance, IntPtr.Zero);
        if (widget == IntPtr.Zero)
        {
            AppLog.Warn("Taskbar", $"Taskbar widget window could not be created (Win32 error {Marshal.GetLastWin32Error()}).");
            return false;
        }
        parent = taskbar;
        CreateTooltip(instance);
        return true;
    }

    void CreateTooltip(IntPtr instance)
    {
        var init = new InitCommonControls { Size = (uint)Marshal.SizeOf<InitCommonControls>(), Classes = 0xFF };
        InitCommonControlsEx(ref init);
        tooltip = CreateWindowExW(WS_EX_TOPMOST, "tooltips_class32", null, WS_POPUP | TTS_ALWAYSTIP | TTS_NOPREFIX, 0, 0, 0, 0, widget, IntPtr.Zero, instance, IntPtr.Zero);
        if (tooltip == IntPtr.Zero) return;
        tooltipBuffer = Marshal.AllocHGlobal(2048 * 2);
        Marshal.WriteInt16(tooltipBuffer, 0);
        SendMessage(tooltip, TTM_SETMAXTIPWIDTH, IntPtr.Zero, new IntPtr(600));
        var info = new ToolInfo { Size = (uint)Marshal.SizeOf<ToolInfo>(), Flags = TTF_SUBCLASS, Hwnd = widget, Id = (UIntPtr)1, Rc = new Rect { Right = 4000, Bottom = 400 }, Text = tooltipBuffer };
        SendMessage(tooltip, TTM_ADDTOOLW, IntPtr.Zero, ref info);
    }

    // The tooltip shows the hovered block's detail (every block's, one under the other, when no block is hovered).
    void UpdateTooltip(TaskbarBlockKind? kind)
    {
        if (tooltip == IntPtr.Zero || tooltipBuffer == IntPtr.Zero) return;
        var shown = kind is { } k ? shownBlocks.Where(b => b.Kind == k) : shownBlocks;
        var text = string.Join("\r\n", shown.Select(b => b.Tooltip));
        tooltipText = text.Length > 2000 ? text[..2000] : text;
        var bytes = Encoding.Unicode.GetBytes(tooltipText + "\0");
        Marshal.Copy(bytes, 0, tooltipBuffer, bytes.Length);
        var info = new ToolInfo { Size = (uint)Marshal.SizeOf<ToolInfo>(), Hwnd = widget, Id = (UIntPtr)1, Text = tooltipBuffer };
        SendMessage(tooltip, TTM_UPDATETIPTEXTW, IntPtr.Zero, ref info);
    }

    IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_MOUSEACTIVATE: return new IntPtr(MA_NOACTIVATE);
            case WM_NCHITTEST: return new IntPtr(HTCLIENT);
            case WM_MOUSEMOVE:
                {
                    // Track the hovered block so the tooltip shows its detail.
                    var over = TaskbarReadoutRenderer.HitTest(blockRects, (short)(lParam.ToInt64() & 0xFFFF));
                    if (over != hovered) { hovered = over; UpdateTooltip(over); }
                    return IntPtr.Zero;
                }
            case WM_LBUTTONUP:
                {
                    // A deliberate click on a block: the owner opens the matching card.
                    var clicked = TaskbarReadoutRenderer.HitTest(blockRects, (short)(lParam.ToInt64() & 0xFFFF));
                    if (clicked is { } kind) System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => { if (!disposed) BlockClicked?.Invoke(kind); });
                    return IntPtr.Zero;
                }
            case WM_RBUTTONUP:
                // Right-click: the owner shows a menu (refresh, open Battery, hide widget).
                System.Windows.Application.Current?.Dispatcher.BeginInvoke(() => { if (!disposed) ContextMenuRequested?.Invoke(); });
                return IntPtr.Zero;
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    // Windows 10 only: the ReBar (task buttons) spans the full width, so shrink it to keep the widget from covering buttons.
    void ShrinkRebar(IntPtr taskbar, int widgetLeft)
    {
        var bar = FindWindowExW(taskbar, IntPtr.Zero, "ReBarWindow32", null);
        if (bar == IntPtr.Zero || FindWindowExW(taskbar, IntPtr.Zero, "Windows.UI.Composition.DesktopWindowContentBridge", null) != IntPtr.Zero) return;
        if (!GetWindowRect(bar, out var r)) return;
        MapWindowPoints(IntPtr.Zero, taskbar, ref r, 2);
        if (rebar != bar) { rebar = bar; rebarOriginalWidth = r.Right - r.Left; }
        else if (r.Right > widgetLeft && r.Right - r.Left > rebarOriginalWidth) rebarOriginalWidth = r.Right - r.Left; // Explorer widened it again
        var target = Math.Max(0, widgetLeft - r.Left);
        if (r.Right - r.Left != target) SetWindowPos(bar, IntPtr.Zero, r.Left, r.Top, target, r.Bottom - r.Top, SWP_NOZORDER | SWP_NOACTIVATE);
    }

    void RestoreRebar()
    {
        if (rebar != IntPtr.Zero && IsWindow(rebar) && GetWindowRect(rebar, out var r) && FindWindowW("Shell_TrayWnd", null) is var taskbar && taskbar != IntPtr.Zero)
        {
            MapWindowPoints(IntPtr.Zero, taskbar, ref r, 2);
            SetWindowPos(rebar, IntPtr.Zero, r.Left, r.Top, rebarOriginalWidth, r.Bottom - r.Top, SWP_NOZORDER | SWP_NOACTIVATE);
        }
        rebar = IntPtr.Zero;
    }

    void Hide()
    {
        if (widget != IntPtr.Zero) ShowWindow(widget, 0);
        RestoreRebar();
        renderKey = "";
    }

    void Destroy()
    {
        if (tooltip != IntPtr.Zero) { DestroyWindow(tooltip); tooltip = IntPtr.Zero; }
        if (widget != IntPtr.Zero) { DestroyWindow(widget); widget = IntPtr.Zero; }
        if (tooltipBuffer != IntPtr.Zero) { Marshal.FreeHGlobal(tooltipBuffer); tooltipBuffer = IntPtr.Zero; }
        parent = IntPtr.Zero;
        renderKey = "";
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        timer.Stop();
        viewModel.TaskbarBlocksChanged -= Refresh;
        viewModel.DeviceBatterySettingsChanged -= Refresh;
        RestoreRebar();
        Destroy();
        if (classRegistered) UnregisterClassW(ClassName, GetModuleHandleW(null));
    }
}
