using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using MochiDock.ViewModels;
using MochiDock.Views.Settings;

namespace MochiDock.Services;

/// <summary>
/// Owns the single Advanced Settings window: opens it, re-activates it when already open, places it on the dock's monitor,
/// and closes it before the app exits. The window has no owner on purpose (see <see cref="AdvancedSettingsWindow"/>).
/// </summary>
public sealed class SettingsWindowService
{
    private readonly Window _dock;
    private readonly MainViewModel _viewModel;
    private AdvancedSettingsWindow? _window;

    public SettingsWindowService(Window dock, MainViewModel viewModel)
    {
        _dock = dock;
        _viewModel = viewModel;
    }

    public bool IsOpen => _window != null;

    /// <summary>Opens the window (or activates it if open) and shows the section named by id or deep-link alias.</summary>
    public void Show(string? section = null)
    {
        if (_window != null)
        {
            if (_window.WindowState == WindowState.Minimized) _window.WindowState = WindowState.Normal;
            _window.NavigateTo(section);
            _window.Activate();
            return;
        }

        var window = new AdvancedSettingsWindow(_viewModel);
        window.SourceInitialized += (s, e) => PlaceOnDockMonitor(window);
        double savedWidth = _viewModel.Settings.SettingsWindowWidth, savedHeight = _viewModel.Settings.SettingsWindowHeight;
        if (savedWidth >= window.MinWidth && savedHeight >= window.MinHeight) { window.Width = savedWidth; window.Height = savedHeight; }
        window.Closed += (s, e) =>
        {
            if (ReferenceEquals(_window, window)) _window = null;
            if (window.WindowState != WindowState.Normal) return;
            double w = window.ActualWidth, h = window.ActualHeight;
            if (w >= window.MinWidth && h >= window.MinHeight && (w != _viewModel.Settings.SettingsWindowWidth || h != _viewModel.Settings.SettingsWindowHeight))
                _viewModel.UpdateSettings(x => { x.SettingsWindowWidth = w; x.SettingsWindowHeight = h; }, SettingsApply.None);
        };
        _window = window;
        window.NavigateTo(section);
        window.Show();
        window.Activate();
    }

    /// <summary>Re-applies the capsule theme to the window's caption; the content follows through DynamicResource brushes.</summary>
    public void ApplyTheme(string theme) => _window?.ApplyChrome(theme);

    /// <summary>Commits pending edits and closes the window. Call before the final settings flush on exit.</summary>
    public void CloseForExit()
    {
        var window = _window;
        _window = null;
        try { window?.Close(); }
        catch (Exception ex) { AppLog.Warn("Settings", ex); }
    }

    // Centres the window on the work area of the monitor the dock lives on, using that monitor's scale,
    // and shrinks it when the work area is smaller than the window.
    private void PlaceOnDockMonitor(AdvancedSettingsWindow window)
    {
        try
        {
            var screen = MultiMonitorHelper.GetTargetScreen(_viewModel.Settings.TargetMonitor);
            var (dpiX, dpiY) = MultiMonitorHelper.GetDpiScale(_dock);
            var work = screen.WorkingArea;
            window.Width = Math.Max(window.MinWidth, Math.Min(window.Width, work.Width / dpiX - 32));
            window.Height = Math.Max(window.MinHeight, Math.Min(window.Height, work.Height / dpiY - 32));
            int widthPx = (int)Math.Round(window.Width * dpiX);
            int heightPx = (int)Math.Round(window.Height * dpiY);
            int x = work.Left + Math.Max(0, (work.Width - widthPx) / 2);
            int y = work.Top + Math.Max(0, (work.Height - heightPx) / 2);
            var handle = new WindowInteropHelper(window).Handle;
            if (handle != IntPtr.Zero) SetWindowPos(handle, IntPtr.Zero, x, y, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
        }
        catch (Exception ex) { AppLog.Warn("Settings", ex); }
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);
    private const uint SWP_NOSIZE = 0x0001, SWP_NOZORDER = 0x0004, SWP_NOACTIVATE = 0x0010;
}
