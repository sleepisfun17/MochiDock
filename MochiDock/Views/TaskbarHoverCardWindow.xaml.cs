using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using MochiDock.Core;
using MochiDock.Services;

namespace MochiDock.Views;

/// <summary>
/// MochiDock-themed hover popup card for the taskbar readout blocks.
/// Mirrors <c>ModernTrayMenuStyle</c> using dynamic theme resources, fades in softly without activating or capturing mouse clicks,
/// and positions itself strictly on the taskbar's own monitor.
/// </summary>
public partial class TaskbarHoverCardWindow : Window
{
    private const double ShadowLeft = 14, ShadowTop = 10, ShadowRight = 14, ShadowBottom = 16;
    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TOOLWINDOW = 0x00000080, WS_EX_NOACTIVATE = 0x08000000, WS_EX_TRANSPARENT = 0x00000020, WS_EX_TOPMOST = 0x00000008;
    private const uint SWP_NOSIZE = 0x0001, SWP_NOACTIVATE = 0x0010, SWP_SHOWWINDOW = 0x0040;
    private static readonly IntPtr HWND_TOPMOST = new(-1);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtrW(IntPtr hwnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtrW(IntPtr hwnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    private string? _contentKey;

    public TaskbarHoverCardWindow()
    {
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        long exStyle = GetWindowLongPtrW(hwnd, GWL_EXSTYLE).ToInt64();
        exStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_TOPMOST;
        SetWindowLongPtrW(hwnd, GWL_EXSTYLE, new IntPtr(exStyle));
    }

    /// <summary>Updates content if changed, reveals the card with a fade when hidden, and clamps placement inside <paramref name="monitorBounds"/>.</summary>
    public void ShowAt(TaskbarHoverInfo info, int anchorX, int taskbarTop, int taskbarBottom, MonitorRect monitorBounds, double monitorDpi)
    {
        string key = $"{info.Title}|{info.IconKey}|{info.HasWarning}|{string.Join("\n", info.Lines)}";
        if (!string.Equals(key, _contentKey, StringComparison.Ordinal))
        {
            _contentKey = key;
            TitleText.Text = info.Title;
            WarnBadge.Visibility = info.HasWarning ? Visibility.Visible : Visibility.Collapsed;

            object? icon = System.Windows.Application.Current?.TryFindResource(info.IconKey);
            if (icon is FrameworkElement fe)
            {
                fe.Width = 14;
                fe.Height = 14;
            }
            IconHost.Content = icon;

            LinesHost.Children.Clear();
            foreach (string line in info.Lines)
            {
                var tb = new TextBlock
                {
                    Text = line,
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 1, 0, 1),
                };
                tb.SetResourceReference(TextBlock.ForegroundProperty, "IslandInkBrush");
                tb.SetResourceReference(TextBlock.FontFamilyProperty, "IslandUiFont");
                tb.SetResourceReference(TextBlock.FontWeightProperty, "IslandBodyWeight");
                LinesHost.Children.Add(tb);
            }
        }

        bool wasVisible = IsVisible;
        if (!wasVisible)
        {
            RootGrid.BeginAnimation(OpacityProperty, null);
            RootGrid.Opacity = 0;
            Show();
        }

        UpdateLayout();

        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        double scale = monitorDpi > 0 ? monitorDpi / 96.0 : 1.0;
        (int padL, int padT, int winW, int winH) = MeasurePhysicalWindow(hwnd, scale);
        int gapPx = (int)Math.Round(6 * scale);

        PositionOnMonitor(hwnd, anchorX, taskbarTop, taskbarBottom, monitorBounds, padL, padT, winW, winH, gapPx, scale);

        // If moving across different-DPI monitors caused PerMonitorV2 scaling to resize the window, re-anchor with the new size.
        if (GetWindowRect(hwnd, out var afterRect))
        {
            int newW = afterRect.Right - afterRect.Left;
            int newH = afterRect.Bottom - afterRect.Top;
            if (newW > 0 && newH > 0 && (Math.Abs(newW - winW) > 1 || Math.Abs(newH - winH) > 1))
                PositionOnMonitor(hwnd, anchorX, taskbarTop, taskbarBottom, monitorBounds, padL, padT, newW, newH, gapPx, scale);
        }

        if (!wasVisible)
        {
            var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(110))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            };
            RootGrid.BeginAnimation(OpacityProperty, fade);
        }
    }

    private void PositionOnMonitor(
        IntPtr hwnd,
        int anchorX,
        int taskbarTop,
        int taskbarBottom,
        MonitorRect monitorBounds,
        int padL,
        int padT,
        int winW,
        int winH,
        int gapPx,
        double scale)
    {
        int padR = (int)Math.Round(ShadowRight * scale);
        int padB = (int)Math.Round(ShadowBottom * scale);
        int cardW = Math.Max(1, winW - padL - padR);
        int cardH = Math.Max(1, winH - padT - padB);

        var (visX, visY) = TaskbarLayout.ComputeTooltipPosition(
            anchorX,
            cardW,
            cardH,
            taskbarTop,
            taskbarBottom,
            monitorBounds.Left,
            monitorBounds.Top,
            monitorBounds.Right,
            monitorBounds.Bottom,
            gapPx);

        SetWindowPos(hwnd, HWND_TOPMOST, visX - padL, visY - padT, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    private (int PadL, int PadT, int WinW, int WinH) MeasurePhysicalWindow(IntPtr hwnd, double scale)
    {
        int padL = (int)Math.Round(ShadowLeft * scale);
        int padT = (int)Math.Round(ShadowTop * scale);
        if (GetWindowRect(hwnd, out var wr) && wr.Right > wr.Left && wr.Bottom > wr.Top)
            return (padL, padT, wr.Right - wr.Left, wr.Bottom - wr.Top);

        return (padL, padT, Math.Max(1, (int)Math.Ceiling(ActualWidth * scale)), Math.Max(1, (int)Math.Ceiling(ActualHeight * scale)));
    }

    public void Dismiss()
    {
        RootGrid.BeginAnimation(OpacityProperty, null);
        if (IsVisible) Hide();
    }

    public void CloseSafely()
    {
        try
        {
            RootGrid.BeginAnimation(OpacityProperty, null);
            Close();
        }
        catch { }
    }
}
