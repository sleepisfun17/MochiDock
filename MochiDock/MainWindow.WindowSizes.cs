using System.Windows.Interop;
using MochiDock.Core;
using MochiDock.Services;

namespace MochiDock;

// Window-size shortcuts: global hotkeys that resize the active window to a preset size, or swap its width and height
// (Settings > Gaming & Display > Window sizes). They replace the AutoHotkey WindowResizer script.
public partial class MainWindow
{
    private const int HOTKEY_ID_WINDOW_SIZE_BASE = 9100; // + preset index, up to WindowSizing.MaxPresets
    private const int HOTKEY_ID_WINDOW_FLIP = 9199;

    /// <summary>Registers the window-size shortcuts to match the settings; safe to call repeatedly. Problems go to Settings.</summary>
    private void ApplyWindowSizeShortcuts()
    {
        try
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            UnregisterWindowSizeShortcuts(hwnd);

            var settings = _viewModel.Settings;
            if (!settings.EnableWindowSizeShortcuts) { _viewModel.WindowSizeShortcutProblem = ""; return; }

            var presets = settings.WindowSizePresets ?? new List<WindowSizePreset>();
            var reserved = new Dictionary<string, string>(StringComparer.Ordinal) { [HotkeyParser.ScriptHotkey] = "Script reader" };
            if (settings.EnableResolutionHotkey && HotkeyParser.TryParse(settings.ResolutionHotkey, out var resolution, out _))
                reserved[resolution.Text] = "screen resolution";
            var problems = WindowSizing.FindShortcutProblems(presets, settings.WindowFlipHotkey, settings.EnableWindowFlip, reserved);
            var messages = problems.Values.Distinct().ToList();

            for (int i = 0; i < presets.Count && i < WindowSizing.MaxPresets; i++)
            {
                var preset = presets[i];
                if (!preset.Enabled || problems.ContainsKey(i) || !HotkeyParser.TryParse(preset.Hotkey, out var hotkey, out _)) continue;
                if (!RegisterHotKey(hwnd, HOTKEY_ID_WINDOW_SIZE_BASE + i, hotkey.Modifiers | MOD_NOREPEAT, hotkey.VirtualKey))
                    messages.Add($"{hotkey.Text} is already used by another program.");
            }
            if (presets.Count(p => p.Enabled) > WindowSizing.MaxPresets)
                messages.Add($"Only the first {WindowSizing.MaxPresets} sizes get a shortcut.");

            if (settings.EnableWindowFlip && !problems.ContainsKey(-1) && HotkeyParser.TryParse(settings.WindowFlipHotkey, out var flip, out _)
                && !RegisterHotKey(hwnd, HOTKEY_ID_WINDOW_FLIP, flip.Modifiers | MOD_NOREPEAT, flip.VirtualKey))
                messages.Add($"{flip.Text} is already used by another program.");

            _viewModel.WindowSizeShortcutProblem = string.Join(" ", messages);
            if (messages.Count > 0) AppLog.Warn("WindowSizes", new InvalidOperationException(_viewModel.WindowSizeShortcutProblem));
        }
        catch (Exception ex) { AppLog.Warn("WindowSizes", ex); }
    }

    private static void UnregisterWindowSizeShortcuts(IntPtr hwnd)
    {
        for (int i = 0; i < WindowSizing.MaxPresets; i++) UnregisterHotKey(hwnd, HOTKEY_ID_WINDOW_SIZE_BASE + i);
        UnregisterHotKey(hwnd, HOTKEY_ID_WINDOW_FLIP);
    }

    /// <summary>Handles a WM_HOTKEY id if it is one of the window-size shortcuts; returns false otherwise.</summary>
    private bool HandleWindowSizeHotkey(int id)
    {
        bool isFlip = id == HOTKEY_ID_WINDOW_FLIP;
        bool isPreset = id >= HOTKEY_ID_WINDOW_SIZE_BASE && id < HOTKEY_ID_WINDOW_SIZE_BASE + WindowSizing.MaxPresets;
        if (!isFlip && !isPreset) return false;

        IntPtr target = WindowResizerService.CaptureForegroundWindow();
        IntPtr own = new WindowInteropHelper(this).Handle;
        Dispatcher.BeginInvoke(() =>
        {
            if (_windowClosed) return;
            WindowResizerService.Result? result = null;
            if (isFlip) result = WindowResizerService.Flip(own, target);
            else
            {
                var presets = _viewModel.Settings.WindowSizePresets;
                int index = id - HOTKEY_ID_WINDOW_SIZE_BASE;
                if (presets != null && index < presets.Count)
                    result = WindowResizerService.Resize(presets[index].Width, presets[index].Height, own, target);
            }
            if (result != null) ShowResolutionNotice(result.Message);
        });
        return true;
    }
}
