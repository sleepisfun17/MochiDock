using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Threading;
using MochiDock.Core;
using MochiDock.ViewModels;
using Microsoft.Win32;

namespace MochiDock.Services;

/// <summary>
/// Layered child windows of the Windows taskbar(s) showing the Left and/or Right readout strips on the chosen monitor(s).
/// <see cref="TaskbarReadouts"/> decides the blocks per zone; <see cref="TaskbarReadoutRenderer"/> draws each bitmap.
/// A left click on a block raises <see cref="BlockClicked"/>; a right click raises <see cref="ContextMenuRequested"/>.
/// Primary and secondary horizontal, non-auto-hidden taskbars are supported; created, updated and destroyed on the UI thread.
/// </summary>
public sealed class TaskbarReadoutWidget : IDisposable
{
    const int GWL_EXSTYLE = -20;
    const long WS_EX_LAYOUTRTL = 0x00400000;
    const string ClassName = "MochiDockTaskbarReadout";
    const uint WS_CHILD = 0x40000000, WS_VISIBLE = 0x10000000, WS_CLIPSIBLINGS = 0x04000000, WS_POPUP = 0x80000000;
    const uint WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TOPMOST = 8;
    const uint SWP_NOZORDER = 4, SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;
    const int WM_MOUSEMOVE = 0x0200, WM_MOUSELEAVE = 0x02A3, WM_RBUTTONUP = 0x0205, WM_LBUTTONUP = 0x0202, WM_NCHITTEST = 0x0084, HTCLIENT = 1, WM_MOUSEACTIVATE = 0x21, MA_NOACTIVATE = 3;
    const uint TME_LEAVE = 0x00000002;

    delegate IntPtr WndProcDelegate(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct WndClassEx { public uint Size, Style; public IntPtr WndProc; public int ClsExtra, WndExtra; public IntPtr Instance, Icon, Cursor, Background; public string? Menu, Class; public IntPtr IconSm; }
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct Point { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] struct Size { public int Cx, Cy; }
    [StructLayout(LayoutKind.Sequential)] struct Blend { public byte Op, Flags, Alpha, Format; }
    [StructLayout(LayoutKind.Sequential)] struct AppBarData { public uint Size; public IntPtr Hwnd; public uint Callback, Edge; public Rect Rc; public IntPtr Param; }
    [StructLayout(LayoutKind.Sequential)] struct BitmapInfoHeader { public uint Size; public int Width, Height; public ushort Planes, BitCount; public uint Compression, SizeImage; public int XPels, YPels; public uint ClrUsed, ClrImportant; }
    [StructLayout(LayoutKind.Sequential)]
    struct TrackMouseEventStruct { public uint Size, Flags; public IntPtr HwndTrack; public uint HoverTime; }

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
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Point pt);
    [DllImport("user32.dll")] static extern bool ScreenToClient(IntPtr hwnd, ref Point pt);
    [DllImport("user32.dll", SetLastError = true)] static extern bool TrackMouseEvent(ref TrackMouseEventStruct tme);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int w, int h, uint flags);
    [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr hwnd);
    [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);
    [DllImport("user32.dll")] static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr dst, IntPtr pos, ref Size size, IntPtr src, ref Point srcPos, int key, ref Blend blend, int flags);
    [DllImport("shell32.dll")] static extern UIntPtr SHAppBarMessage(uint message, ref AppBarData data);
    [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr dc);
    [DllImport("gdi32.dll")] static extern IntPtr CreateDIBSection(IntPtr dc, ref BitmapInfoHeader info, uint usage, out IntPtr bits, IntPtr section, uint offset);
    [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr dc, IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr dc);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandleW(string? name);

    private sealed class StripWindow
    {
        public IntPtr Widget, Parent;
        public string RenderKey = "";
        public int Width, Height;
        public int LastX = int.MinValue, LastY = int.MinValue, LastWidth = -1, LastHeight = -1;
        public IReadOnlyList<TaskbarBlock> ShownBlocks = Array.Empty<TaskbarBlock>();
        public IReadOnlyList<TaskbarBlockRect> BlockRects = Array.Empty<TaskbarBlockRect>();
        public TaskbarBlockKind? Hovered;
        public bool TrackingMouse;
    }

    private sealed class UiaCacheEntry
    {
        public System.Windows.Rect? WidgetsRect;
        public System.Windows.Rect? TaskButtonsRect;
        public System.Windows.Rect? SecondaryClockRect;
        public DateTime CheckedUtc;
        public bool Running;
    }

    private readonly record struct TaskbarEntry(IntPtr Hwnd, bool IsPrimary, string DeviceName, Rect Bounds);

    readonly MainViewModel viewModel;
    readonly DispatcherTimer timer = new() { Interval = TimeSpan.FromSeconds(1) };
    readonly DispatcherTimer hoverTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    readonly WndProcDelegate wndProc; // kept in a field so the native callback is not garbage-collected
    readonly Dictionary<(IntPtr Taskbar, TaskbarZone Zone), StripWindow> strips = new();
    readonly Dictionary<IntPtr, StripWindow> stripsByHwnd = new();
    readonly Dictionary<IntPtr, UiaCacheEntry> uiaCache = new();
    bool classRegistered, disposed;
    IntPtr rebar;
    int rebarOriginalWidth;
    Views.TaskbarHoverCardWindow? hoverCard;
    StripWindow? hoveredStrip;

    /// <summary>Raised on the UI thread when any strip is right-clicked, carrying the block under the cursor when any.</summary>
    public event Action<TaskbarBlockKind?>? ContextMenuRequested;

    /// <summary>Raised on the UI thread when a block is left-clicked (the owner opens the matching card).</summary>
    public event Action<TaskbarBlockKind>? BlockClicked;

    public TaskbarReadoutWidget(MainViewModel viewModel)
    {
        this.viewModel = viewModel;
        wndProc = WndProc;
        timer.Tick += (_, _) => Tick();
        hoverTimer.Tick += (_, _) => OnHoverTimerTick();
        viewModel.TaskbarBlocksChanged += Refresh;
        viewModel.TaskbarStyleChanged += Refresh;
        viewModel.DeviceBatterySettingsChanged += Refresh;   // the plate behind the text
        Refresh();
    }

    // At least one strip is needed while Left or Right blocks have content.
    bool ShouldShow => !disposed && (viewModel.TaskbarLeftBlocks.Count > 0 || viewModel.TaskbarRightBlocks.Count > 0);

    void Refresh()
    {
        if (disposed) return;
        if (ShouldShow) { if (!timer.IsEnabled) timer.Start(); Tick(); }
        else { timer.Stop(); HideAll(); }
    }

    void Tick()
    {
        try
        {
            if (!ShouldShow) { HideAll(); return; }

            var allTaskbars = DiscoverTaskbars();
            if (allTaskbars.Count == 0) { HideAll(); return; }

            // Prune UIA cache for destroyed taskbar windows (e.g. Explorer restart).
            foreach (var stale in uiaCache.Keys.Where(k => allTaskbars.All(t => t.Hwnd != k)).ToList())
                uiaCache.Remove(stale);

            var selectionInputs = allTaskbars.Select(t => (t.DeviceName, t.IsPrimary)).ToList();
            var selectedIndices = TaskbarMonitorSelection.SelectIndices(viewModel.TaskbarMonitor, viewModel.Settings.TargetMonitor, selectionInputs);

            var leftBlocks = viewModel.TaskbarLeftBlocks;
            var rightBlocks = viewModel.TaskbarRightBlocks;
            var background = viewModel.ShowDeviceBatteryBackground;
            var style = viewModel.CurrentTaskbarAppearance;
            var placement = viewModel.CurrentTaskbarPlacement;
            int rightOffset = viewModel.EffectiveTaskbarOffsetPx;
            int leftOffset = viewModel.EffectiveTaskbarLeftOffsetPx;
            int verticalOffset = viewModel.EffectiveTaskbarVerticalOffsetPx;
            bool light = TaskbarUsesLightTheme();
            bool leftAligned = TaskbarIsLeftAligned();
            var styleKey = $"{style.FontFamily}|{style.FontSize}|{style.Bold}|{style.BlockSpacing}|{style.StableWidth}|{style.BatteryLayout}|{style.LabelColorHex}|{style.ValueColorHex}";

            var wantedKeys = new HashSet<(IntPtr Taskbar, TaskbarZone Zone)>();
            bool shrunkPrimaryRebar = false;

            foreach (int idx in selectedIndices)
            {
                if ((uint)idx >= (uint)allTaskbars.Count) continue;
                var tb = allTaskbars[idx];
                int height = tb.Bounds.Bottom - tb.Bounds.Top;
                uint rawDpi = GetDpiForWindow(tb.Hwnd);
                double dpi = rawDpi == 0 ? 96 : rawDpi;
                double scale = dpi / 96.0;
                int gapPx = (int)Math.Round(8 * scale);

                bool rtl = (GetWindowLongPtr(tb.Hwnd, GWL_EXSTYLE).ToInt64() & WS_EX_LAYOUTRTL) != 0;
                bool isWin11 = FindWindowExW(tb.Hwnd, IntPtr.Zero, "Windows.UI.Composition.DesktopWindowContentBridge", null) != IntPtr.Zero;
                bool hasLeft = leftBlocks.Count > 0;
                bool hasRight = rightBlocks.Count > 0;

                // Resolve Right-side tray / secondary-clock bounds.
                int trayLeft = rtl ? tb.Bounds.Left : tb.Bounds.Right - gapPx;
                int trayRight = rtl ? tb.Bounds.Left + gapPx : tb.Bounds.Right;
                bool haveClockWin32 = false;
                if (tb.IsPrimary)
                {
                    var tray = FindWindowExW(tb.Hwnd, IntPtr.Zero, "TrayNotifyWnd", null);
                    if (tray != IntPtr.Zero && GetWindowRect(tray, out var trayRect))
                    {
                        trayLeft = trayRect.Left;
                        trayRight = trayRect.Right;
                    }
                    else
                    {
                        hasRight = false;
                    }
                }
                else if (hasRight)
                {
                    var clock = FindWindowExW(tb.Hwnd, IntPtr.Zero, "ClockButton", null);
                    if (clock != IntPtr.Zero && GetWindowRect(clock, out var clockRect) && clockRect.Right > clockRect.Left)
                    {
                        trayLeft = clockRect.Left;
                        trayRight = clockRect.Right;
                        haveClockWin32 = true;
                    }
                }

                bool needWidgets = isWin11 && (hasLeft || (hasRight && leftAligned));
                bool needTaskButtons = hasRight && placement == TaskbarPlacementMode.AfterTaskButtons;
                bool needSecondaryClock = hasRight && !tb.IsPrimary && !haveClockWin32;
                var uia = QueryTaskbarElements(tb.Hwnd, tb.Bounds, needWidgets, needTaskButtons, needSecondaryClock, isWin11);

                if (!tb.IsPrimary && !haveClockWin32 && uia.SecondaryClockRect is { IsEmpty: false } sc)
                {
                    trayLeft = (int)Math.Floor(sc.Left);
                    trayRight = (int)Math.Ceiling(sc.Right);
                }

                (int Left, int Right)? widgetsBounds = uia.WidgetsRect is { IsEmpty: false } wr
                    ? ((int)Math.Floor(wr.Left), (int)Math.Ceiling(wr.Right)) : null;
                (int Left, int Right)? taskBounds = uia.TaskButtonsRect is { IsEmpty: false } tbr
                    ? ((int)Math.Floor(tbr.Left), (int)Math.Ceiling(tbr.Right)) : null;

                if (hasLeft)
                {
                    var key = (tb.Hwnd, TaskbarZone.Left);
                    wantedKeys.Add(key);
                    var strip = UpdateStrip(key, tb.Hwnd, leftBlocks, dpi, light, height, background, style, styleKey);
                    if (strip != null)
                    {
                        int x = TaskbarLayout.ComputeLeftZoneClientX(leftOffset, rtl, tb.Bounds.Left, tb.Bounds.Right, strip.Width, gapPx, widgetsBounds);
                        int y = Math.Clamp((height - strip.Height) / 2 + verticalOffset, 0, Math.Max(0, height - strip.Height));
                        PositionStrip(strip, x, y);
                    }
                }

                if (hasRight)
                {
                    var key = (tb.Hwnd, TaskbarZone.Right);
                    wantedKeys.Add(key);
                    var strip = UpdateStrip(key, tb.Hwnd, rightBlocks, dpi, light, height, background, style, styleKey);
                    if (strip != null)
                    {
                        int x = TaskbarLayout.ComputeClientX(
                            placement, rightOffset, rtl,
                            tb.Bounds.Left, tb.Bounds.Right,
                            trayLeft, trayRight,
                            strip.Width, gapPx,
                            widgetsBounds, taskBounds);
                        int y = Math.Clamp((height - strip.Height) / 2 + verticalOffset, 0, Math.Max(0, height - strip.Height));
                        PositionStrip(strip, x, y);

                        if (tb.IsPrimary && !rtl && !isWin11 && placement == TaskbarPlacementMode.LeftOfTray)
                        {
                            ShrinkRebar(tb.Hwnd, x);
                            shrunkPrimaryRebar = true;
                        }
                    }
                }
            }

            if (!shrunkPrimaryRebar) RestoreRebar();

            foreach (var obsolete in strips.Keys.Where(k => !wantedKeys.Contains(k)).ToList())
            {
                DestroyStrip(strips[obsolete]);
                strips.Remove(obsolete);
            }

            if (hoverCard is { IsVisible: true } && hoveredStrip is { } hs)
            {
                if (hs.Widget == IntPtr.Zero || hs.Hovered is not { } hk
                    || !GetWindowRect(hs.Widget, out var wr) || !GetCursorPos(out var cur)
                    || cur.X < wr.Left || cur.X >= wr.Right || cur.Y < wr.Top || cur.Y >= wr.Bottom)
                {
                    ClearHover();
                }
                else
                {
                    ShowHoverCard(hs, hk);
                }
            }
        }
        catch (Exception ex) { AppLog.Warn("Taskbar", ex); }
    }

    StripWindow? UpdateStrip(
        (IntPtr Taskbar, TaskbarZone Zone) key,
        IntPtr taskbar,
        IReadOnlyList<TaskbarBlock> blocks,
        double dpi,
        bool light,
        int height,
        bool background,
        TaskbarAppearance style,
        string styleKey)
    {
        if (strips.TryGetValue(key, out var existing) && (existing.Parent != taskbar || !IsWindow(existing.Widget)))
        {
            DestroyStrip(existing);
            strips.Remove(key);
            existing = null;
        }

        var strip = existing ?? new StripWindow();
        var renderKey = string.Join("\n", blocks.Select(b => b.ToString())) + $"@{dpi}/{light}/{height}/{background}/{styleKey}";
        if (renderKey != strip.RenderKey || strip.Widget == IntPtr.Zero)
        {
            var surface = TaskbarReadoutRenderer.Render(blocks, dpi, !light, height, background, style);
            if (surface is null)
            {
                if (existing != null) { DestroyStrip(existing); strips.Remove(key); }
                return null;
            }
            strip.Width = surface.Width;
            strip.Height = surface.Height;
            if (strip.Widget == IntPtr.Zero && !CreateStripWindow(strip, taskbar))
                return null;

            Present(strip.Widget, surface);
            strip.RenderKey = renderKey;
            var previousHovered = strip.Hovered;
            strip.ShownBlocks = blocks;
            strip.BlockRects = surface.Blocks;
            if (previousHovered != null)
            {
                if (GetCursorPos(out var pt) && ScreenToClient(strip.Widget, ref pt)
                    && pt.X >= 0 && pt.X < strip.Width && pt.Y >= 0 && pt.Y < strip.Height)
                {
                    strip.Hovered = TaskbarReadoutRenderer.HitTest(strip.BlockRects, pt.X);
                }
                else if (!blocks.Any(b => b.Kind == previousHovered))
                {
                    strip.Hovered = null;
                }
            }
            strips[key] = strip;
        }
        return strip;
    }

    static void PositionStrip(StripWindow strip, int x, int y)
    {
        bool isNew = strip.LastWidth < 0;
        if (!isNew && x == strip.LastX && y == strip.LastY && strip.Width == strip.LastWidth && strip.Height == strip.LastHeight)
            return;

        uint flags = isNew ? (SWP_NOACTIVATE | SWP_SHOWWINDOW) : (SWP_NOZORDER | SWP_NOACTIVATE);
        SetWindowPos(strip.Widget, IntPtr.Zero, x, y, strip.Width, strip.Height, flags);
        strip.LastX = x;
        strip.LastY = y;
        strip.LastWidth = strip.Width;
        strip.LastHeight = strip.Height;
    }

    static List<TaskbarEntry> DiscoverTaskbars()
    {
        var list = new List<TaskbarEntry>();
        var primary = FindWindowW("Shell_TrayWnd", null);
        if (primary != IntPtr.Zero && GetWindowRect(primary, out var pRect) && !IsVerticalOrAutoHidden(primary, pRect))
        {
            list.Add(new TaskbarEntry(primary, IsPrimary: true, MultiMonitorHelper.FromHandle(primary)?.DeviceName ?? "", pRect));
        }
        IntPtr sec = IntPtr.Zero;
        while ((sec = FindWindowExW(IntPtr.Zero, sec, "Shell_SecondaryTrayWnd", null)) != IntPtr.Zero)
        {
            if (GetWindowRect(sec, out var sRect) && !IsVerticalOrAutoHidden(sec, sRect))
            {
                list.Add(new TaskbarEntry(sec, IsPrimary: false, MultiMonitorHelper.FromHandle(sec)?.DeviceName ?? "", sRect));
            }
        }
        return list;
    }

    // ---- UI Automation per-taskbar lookup (Widgets button, task buttons, Win11 secondary clock) ---------------------------------------------

    UiaCacheEntry QueryTaskbarElements(IntPtr taskbar, Rect taskbarRect, bool needWidgets, bool needTaskButtons, bool needSecondaryClock, bool isWin11)
    {
        if (!uiaCache.TryGetValue(taskbar, out var entry))
            uiaCache[taskbar] = entry = new UiaCacheEntry();

        if (!needWidgets) entry.WidgetsRect = null;
        if (!needTaskButtons) entry.TaskButtonsRect = null;
        if (!needSecondaryClock) entry.SecondaryClockRect = null;
        if (!needWidgets && !needTaskButtons && !needSecondaryClock) return entry;

        var interval = needTaskButtons ? TimeSpan.FromSeconds(2) : TimeSpan.FromSeconds(5);
        if (entry.Running || DateTime.UtcNow - entry.CheckedUtc < interval) return entry;

        entry.Running = true;
        entry.CheckedUtc = DateTime.UtcNow;
        var handle = taskbar;
        var bounds = taskbarRect;
        _ = Task.Run(() =>
        {
            System.Windows.Rect? foundWidgets = null;
            System.Windows.Rect? foundTasks = null;
            System.Windows.Rect? foundClock = null;
            try
            {
                if (needWidgets || isWin11 || needSecondaryClock)
                {
                    var root = AutomationElement.FromHandle(handle);
                    if (needWidgets)
                    {
                        var button = root.FindFirst(TreeScope.Descendants,
                            new PropertyCondition(AutomationElement.AutomationIdProperty, "WidgetsButton"));
                        if (button is not null && !button.Current.IsOffscreen && !button.Current.BoundingRectangle.IsEmpty)
                            foundWidgets = button.Current.BoundingRectangle;
                    }
                    if (needTaskButtons && isWin11)
                    {
                        foundTasks = FindWin11TaskButtonsBounds(root, bounds);
                    }
                    if (needSecondaryClock)
                    {
                        foundClock = FindSecondaryClockBounds(root, bounds);
                    }
                }
                if (needTaskButtons && !isWin11)
                {
                    foundTasks = FindWin10TaskButtonsBounds(handle, bounds);
                }
            }
            catch (Exception ex) when (ex is ElementNotAvailableException or InvalidOperationException or COMException) { }
            Application.Current?.Dispatcher.BeginInvoke(() =>
            {
                if (needWidgets) entry.WidgetsRect = foundWidgets is { IsEmpty: false } wr ? wr : null;
                if (needTaskButtons) entry.TaskButtonsRect = foundTasks is { IsEmpty: false } tr ? tr : null;
                if (needSecondaryClock) entry.SecondaryClockRect = foundClock is { IsEmpty: false } cr ? cr : null;
                entry.Running = false;
            });
        });
        return entry;
    }

    static System.Windows.Rect? FindWin11TaskButtonsBounds(AutomationElement root, Rect taskbarBounds)
    {
        var buttons = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
        var taskUnion = System.Windows.Rect.Empty;
        var fallbackUnion = System.Windows.Rect.Empty;

        for (int i = 0; i < buttons.Count; i++)
        {
            try
            {
                var cur = buttons[i].Current;
                if (cur.IsOffscreen) continue;
                var r = cur.BoundingRectangle;
                if (r.IsEmpty || r.Width <= 0 || r.Height <= 0) continue;
                if (r.Left < taskbarBounds.Left - 2 || r.Right > taskbarBounds.Right + 2) continue;

                string cls = cur.ClassName ?? "";
                string id = cur.AutomationId ?? "";
                if (cls == "Taskbar.TaskListButton" || id.StartsWith("Appid:", StringComparison.Ordinal))
                    taskUnion.Union(r);
                else if (id is "StartButton" or "SearchButton" or "TaskViewButton")
                    fallbackUnion.Union(r);
            }
            catch (ElementNotAvailableException) { }
            catch (COMException) { }
        }

        if (!taskUnion.IsEmpty) return taskUnion;
        return fallbackUnion.IsEmpty ? null : fallbackUnion;
    }

    static System.Windows.Rect? FindSecondaryClockBounds(AutomationElement root, Rect taskbarBounds)
    {
        var buttons = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button));
        var clockUnion = System.Windows.Rect.Empty;
        int minTrailX = taskbarBounds.Left + (taskbarBounds.Right - taskbarBounds.Left) / 2;
        for (int i = 0; i < buttons.Count; i++)
        {
            try
            {
                var cur = buttons[i].Current;
                if (cur.IsOffscreen) continue;
                var r = cur.BoundingRectangle;
                if (r.IsEmpty || r.Width <= 0 || r.Height <= 0) continue;
                string id = cur.AutomationId ?? "";
                string cls = cur.ClassName ?? "";
                if (id is "ClockButton" or "SystemTrayIcon" || cls.Contains("Clock", StringComparison.OrdinalIgnoreCase)
                    || cls.Contains("SystemTray", StringComparison.OrdinalIgnoreCase))
                {
                    if (r.Left >= minTrailX)
                        clockUnion.Union(r);
                }
            }
            catch (ElementNotAvailableException) { }
            catch (COMException) { }
        }
        return clockUnion.IsEmpty ? null : clockUnion;
    }

    static System.Windows.Rect? FindWin10TaskButtonsBounds(IntPtr taskbar, Rect taskbarBounds)
    {
        var rebar = FindWindowExW(taskbar, IntPtr.Zero, "ReBarWindow32", null);
        if (rebar == IntPtr.Zero) return null;
        var taskSw = FindWindowExW(rebar, IntPtr.Zero, "MSTaskSwWClass", null);
        var taskList = taskSw != IntPtr.Zero ? FindWindowExW(taskSw, IntPtr.Zero, "MSTaskListWClass", null) : IntPtr.Zero;
        if (taskList == IntPtr.Zero) return null;

        var listEl = AutomationElement.FromHandle(taskList);
        var children = listEl.FindAll(TreeScope.Children, System.Windows.Automation.Condition.TrueCondition);
        var union = System.Windows.Rect.Empty;
        for (int i = 0; i < children.Count; i++)
        {
            try
            {
                var cur = children[i].Current;
                if (cur.IsOffscreen) continue;
                var r = cur.BoundingRectangle;
                if (r.IsEmpty || r.Width <= 0 || r.Height <= 0) continue;
                if (r.Left >= taskbarBounds.Left - 2 && r.Right <= taskbarBounds.Right + 2)
                    union.Union(r);
            }
            catch (ElementNotAvailableException) { }
            catch (COMException) { }
        }
        return union.IsEmpty ? null : union;
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

    static void Present(IntPtr widget, TaskbarRender surface)
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

    // ---- native child window management ----------------------------------------------------------------------------------------------------

    bool CreateStripWindow(StripWindow strip, IntPtr taskbar)
    {
        var instance = GetModuleHandleW(null);
        if (!classRegistered)
        {
            var cls = new WndClassEx { Size = (uint)Marshal.SizeOf<WndClassEx>(), WndProc = Marshal.GetFunctionPointerForDelegate(wndProc), Instance = instance, Class = ClassName };
            classRegistered = RegisterClassExW(ref cls) != 0 || Marshal.GetLastWin32Error() == 1410; // 1410: already registered
            if (!classRegistered) return false;
        }
        var hwnd = CreateWindowExW(WS_EX_LAYERED | WS_EX_NOACTIVATE, ClassName, null, WS_CHILD | WS_CLIPSIBLINGS | WS_VISIBLE, 0, 0, strip.Width, strip.Height, taskbar, IntPtr.Zero, instance, IntPtr.Zero);
        if (hwnd == IntPtr.Zero)
        {
            AppLog.Warn("Taskbar", $"Taskbar widget window could not be created (Win32 error {Marshal.GetLastWin32Error()}).");
            return false;
        }
        strip.Widget = hwnd;
        strip.Parent = taskbar;
        stripsByHwnd[hwnd] = strip;
        return true;
    }

    void OnHoverTimerTick()
    {
        hoverTimer.Stop();
        if (disposed || hoveredStrip is not { } strip || strip.Hovered is not { } kind || strip.Widget == IntPtr.Zero) return;
        if (!GetWindowRect(strip.Widget, out var wr) || !GetCursorPos(out var pt)
            || pt.X < wr.Left || pt.X >= wr.Right || pt.Y < wr.Top || pt.Y >= wr.Bottom)
        {
            ClearHover();
            return;
        }
        ShowHoverCard(strip, kind);
    }

    void ShowHoverCard(StripWindow strip, TaskbarBlockKind kind)
    {
        var info = TaskbarReadouts.BuildHoverInfo(kind, strip.ShownBlocks);
        if (info is null) { DismissHoverCard(); return; }
        if (strip.Parent == IntPtr.Zero || strip.Widget == IntPtr.Zero
            || !GetWindowRect(strip.Parent, out var tbRect)
            || !GetWindowRect(strip.Widget, out var widgetRect))
            return;

        var mon = MultiMonitorHelper.FromHandle(strip.Parent)?.Bounds
            ?? new MonitorRect(tbRect.Left, tbRect.Top - 1080, Math.Max(1, tbRect.Right - tbRect.Left), 1080 + Math.Max(1, tbRect.Bottom - tbRect.Top));

        int anchorX;
        if (strip.BlockRects.FirstOrDefault(r => r.Kind == kind && r.Pixels.Width > 0) is { } rect)
            anchorX = widgetRect.Left + (int)Math.Round(rect.Pixels.Left + rect.Pixels.Width / 2.0);
        else
            anchorX = GetCursorPos(out var pt) ? pt.X : (widgetRect.Left + widgetRect.Right) / 2;

        uint rawDpi = GetDpiForWindow(strip.Parent);
        double dpi = rawDpi == 0 ? 96 : rawDpi;

        hoverCard ??= new Views.TaskbarHoverCardWindow();
        hoverCard.ShowAt(info, anchorX, tbRect.Top, tbRect.Bottom, mon, dpi);
    }

    void DismissHoverCard()
    {
        hoverTimer.Stop();
        hoverCard?.Dismiss();
    }

    void ClearHover()
    {
        if (hoveredStrip != null)
        {
            hoveredStrip.Hovered = null;
            hoveredStrip = null;
        }
        DismissHoverCard();
    }

    IntPtr WndProc(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case WM_MOUSEACTIVATE: return new IntPtr(MA_NOACTIVATE);
            case WM_NCHITTEST: return new IntPtr(HTCLIENT);
            case WM_MOUSEMOVE:
                {
                    if (stripsByHwnd.TryGetValue(hwnd, out var strip))
                    {
                        if (!strip.TrackingMouse)
                        {
                            var tme = new TrackMouseEventStruct { Size = (uint)Marshal.SizeOf<TrackMouseEventStruct>(), Flags = TME_LEAVE, HwndTrack = hwnd };
                            strip.TrackingMouse = TrackMouseEvent(ref tme);
                        }
                        var over = TaskbarReadoutRenderer.HitTest(strip.BlockRects, (short)(lParam.ToInt64() & 0xFFFF));
                        if (over != strip.Hovered || !ReferenceEquals(hoveredStrip, strip))
                        {
                            if (hoveredStrip != null && !ReferenceEquals(hoveredStrip, strip))
                                hoveredStrip.Hovered = null;
                            strip.Hovered = over;
                            hoveredStrip = over is not null ? strip : null;
                            if (over is null)
                            {
                                DismissHoverCard();
                            }
                            else if (hoverCard is { IsVisible: true })
                            {
                                hoverTimer.Stop();
                                ShowHoverCard(strip, over.Value);
                            }
                            else
                            {
                                hoverTimer.Stop();
                                hoverTimer.Start();
                            }
                        }
                    }
                    return IntPtr.Zero;
                }
            case WM_MOUSELEAVE:
                {
                    if (stripsByHwnd.TryGetValue(hwnd, out var strip))
                    {
                        strip.TrackingMouse = false;
                        if (ReferenceEquals(hoveredStrip, strip)) ClearHover();
                        else strip.Hovered = null;
                    }
                    return IntPtr.Zero;
                }
            case WM_LBUTTONUP:
                {
                    ClearHover();
                    if (stripsByHwnd.TryGetValue(hwnd, out var strip))
                    {
                        var clicked = TaskbarReadoutRenderer.HitTest(strip.BlockRects, (short)(lParam.ToInt64() & 0xFFFF));
                        if (clicked is { } kind) Application.Current?.Dispatcher.BeginInvoke(() => { if (!disposed) BlockClicked?.Invoke(kind); });
                    }
                    return IntPtr.Zero;
                }
            case WM_RBUTTONUP:
                {
                    ClearHover();
                    TaskbarBlockKind? clicked = null;
                    if (stripsByHwnd.TryGetValue(hwnd, out var strip))
                        clicked = TaskbarReadoutRenderer.HitTest(strip.BlockRects, (short)(lParam.ToInt64() & 0xFFFF));
                    Application.Current?.Dispatcher.BeginInvoke(() => { if (!disposed) ContextMenuRequested?.Invoke(clicked); });
                    return IntPtr.Zero;
                }
        }
        return DefWindowProcW(hwnd, msg, wParam, lParam);
    }

    // Windows 10 only: the ReBar (task buttons) spans the full width, so shrink it to keep the Right widget from covering buttons.
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

    void HideAll()
    {
        ClearHover();
        RestoreRebar();
        foreach (var strip in strips.Values) DestroyStrip(strip);
        strips.Clear();
        stripsByHwnd.Clear();
    }

    void DestroyStrip(StripWindow strip)
    {
        if (ReferenceEquals(hoveredStrip, strip)) ClearHover();
        if (strip.Widget != IntPtr.Zero) stripsByHwnd.Remove(strip.Widget);
        if (strip.Widget != IntPtr.Zero) { DestroyWindow(strip.Widget); strip.Widget = IntPtr.Zero; }
        strip.Parent = IntPtr.Zero;
        strip.RenderKey = "";
        strip.LastX = int.MinValue;
        strip.LastY = int.MinValue;
        strip.LastWidth = -1;
        strip.LastHeight = -1;
        strip.Hovered = null;
        strip.TrackingMouse = false;
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        timer.Stop();
        hoverTimer.Stop();
        viewModel.TaskbarBlocksChanged -= Refresh;
        viewModel.TaskbarStyleChanged -= Refresh;
        viewModel.DeviceBatterySettingsChanged -= Refresh;
        HideAll();
        hoverCard?.CloseSafely();
        hoverCard = null;
        if (classRegistered) UnregisterClassW(ClassName, GetModuleHandleW(null));
    }
}
