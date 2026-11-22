using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MochiDock.Core;
using MochiDock.Models;
using MochiDock.ViewModels;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Orientation = System.Windows.Controls.Orientation;
using TextBox = System.Windows.Controls.TextBox;

namespace MochiDock.Views.Settings.Sections;

// Settings > Gaming & Display > Window sizes: the presets list, the on/off switch and the flip shortcut.
public partial class GamingSectionView
{
    private void LoadWindowSizes(AppSettings settings)
    {
        WindowSizesCheck.IsChecked = settings.EnableWindowSizeShortcuts;
        WindowFlipCheck.IsChecked = settings.EnableWindowFlip;
        WindowFlipBox.Text = HotkeyParser.TryParse(settings.WindowFlipHotkey, out var flip, out _) ? flip.Text : "";
        bool on = settings.EnableWindowSizeShortcuts;
        WindowSizesRow.IsEnabled = on;
        WindowFlipRow.IsEnabled = on;
        WindowFlipBox.IsEnabled = settings.EnableWindowFlip;

        // Rebuilding while a size box is being typed in would throw the typing away.
        if (WindowSizeList.Children.OfType<FrameworkElement>().Any(row => row.IsKeyboardFocusWithin)) return;
        WindowSizeList.Children.Clear();
        var presets = settings.WindowSizePresets ?? new List<WindowSizePreset>();
        for (int i = 0; i < presets.Count; i++) WindowSizeList.Children.Add(BuildPresetRow(i, presets[i]));
        AddWindowSizeBtn.IsEnabled = presets.Count < WindowSizing.MaxPresets;
    }

    private FrameworkElement BuildPresetRow(int index, WindowSizePreset preset)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = System.Windows.HorizontalAlignment.Right, Margin = new Thickness(0, 0, 0, 6) };

        var enabled = new CheckBox { Style = (Style)FindResource("ModernDarkCheckBox"), IsChecked = preset.Enabled, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        System.Windows.Automation.AutomationProperties.SetName(enabled, $"Size {index + 1} shortcut on");
        enabled.Checked += (_, _) => UpdatePreset(index, p => p.Enabled = true);
        enabled.Unchecked += (_, _) => UpdatePreset(index, p => p.Enabled = false);

        var size = new TextBox { Style = (Style)FindResource("ModernDarkTextBox"), Width = 120, Text = $"{preset.Width}x{preset.Height}" };
        System.Windows.Automation.AutomationProperties.SetName(size, $"Size {index + 1}");
        void CommitSize()
        {
            if (WindowSizing.TryParseSize(size.Text, out int w, out int h, out var error))
            {
                if (ViewModel != null) ViewModel.WindowSizeShortcutProblem = "";
                UpdatePreset(index, p => { p.Width = w; p.Height = h; });
            }
            else if (ViewModel != null) ViewModel.WindowSizeShortcutProblem = error ?? "";
        }
        size.LostFocus += (_, _) => CommitSize();
        size.KeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Enter) { CommitSize(); e.Handled = true; } };

        var hotkey = new TextBox { Style = (Style)FindResource("ModernDarkTextBox"), Width = 120, IsReadOnly = true, IsReadOnlyCaretVisible = false, Margin = new Thickness(8, 0, 0, 0),
            Text = HotkeyParser.TryParse(preset.Hotkey, out var parsed, out _) ? parsed.Text : "" };
        System.Windows.Automation.AutomationProperties.SetName(hotkey, $"Size {index + 1} shortcut");
        hotkey.PreviewKeyDown += (_, e) =>
        {
            if (CaptureHotkey(e) is { } text) { hotkey.Text = text; UpdatePreset(index, p => p.Hotkey = text); }
        };

        var remove = new Button { Style = (Style)FindResource("ActionButton"), Content = "Remove", Margin = new Thickness(8, 0, 0, 0) };
        System.Windows.Automation.AutomationProperties.SetName(remove, $"Remove size {index + 1}");
        remove.Click += (_, _) => EditPresets(list => { if (index < list.Count) list.RemoveAt(index); });

        row.Children.Add(enabled);
        row.Children.Add(size);
        row.Children.Add(hotkey);
        row.Children.Add(remove);
        return row;
    }

    private void UpdatePreset(int index, Action<WindowSizePreset> change) =>
        EditPresets(list => { if (index < list.Count) change(list[index]); });

    // Works on copies so a half-applied change never reaches the live settings; SettingsApply.Hotkey re-registers the shortcuts.
    private void EditPresets(Action<List<WindowSizePreset>> change)
    {
        if (Loading || ViewModel == null) return;
        var copy = (ViewModel.Settings.WindowSizePresets ?? new List<WindowSizePreset>()).Select(p => p.Clone()).ToList();
        change(copy);
        ViewModel.UpdateSettings(x => x.WindowSizePresets = copy, SettingsApply.Hotkey);
    }

    private void AddWindowSize_Click(object sender, RoutedEventArgs e)
    {
        var used = new HashSet<string>((ViewModel?.Settings.WindowSizePresets ?? new()).Select(p => p.Hotkey), StringComparer.Ordinal);
        // The next free Ctrl+Alt+digit, or no shortcut until the user presses one.
        string hotkey = Enumerable.Range(1, 9).Select(n => $"Ctrl+Alt+{n}").FirstOrDefault(h => !used.Contains(h)) ?? "";
        EditPresets(list => { if (list.Count < WindowSizing.MaxPresets) list.Add(new WindowSizePreset(1280, 720, hotkey)); });
    }

    private void WindowSizes_Changed(object sender, RoutedEventArgs e)
    {
        if (Loading || ViewModel == null) return;
        bool on = WindowSizesCheck.IsChecked == true, flip = WindowFlipCheck.IsChecked == true;
        ViewModel.UpdateSettings(x => { x.EnableWindowSizeShortcuts = on; x.EnableWindowFlip = flip; }, SettingsApply.Hotkey);
    }

    private void WindowFlipBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (CaptureHotkey(e) is not { } text || ViewModel == null) return;
        WindowFlipBox.Text = text;
        if (!string.Equals(text, ViewModel.Settings.WindowFlipHotkey, StringComparison.Ordinal))
            ViewModel.UpdateSettings(x => x.WindowFlipHotkey = text, SettingsApply.Hotkey);
    }

    /// <summary>The shortcut pressed in a shortcut box, or null (a lone modifier, or an invalid key, which is explained in Settings).</summary>
    private string? CaptureHotkey(KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
        if (key is System.Windows.Input.Key.LeftCtrl or System.Windows.Input.Key.RightCtrl or System.Windows.Input.Key.LeftAlt or System.Windows.Input.Key.RightAlt
            or System.Windows.Input.Key.LeftShift or System.Windows.Input.Key.RightShift or System.Windows.Input.Key.LWin or System.Windows.Input.Key.RWin
            or System.Windows.Input.Key.ImeProcessed or System.Windows.Input.Key.None or System.Windows.Input.Key.Tab) { e.Handled = key != System.Windows.Input.Key.Tab; return null; }
        string name = KeyName(key);
        var mods = System.Windows.Input.Keyboard.Modifiers;
        string? text = HotkeyParser.Format(mods.HasFlag(System.Windows.Input.ModifierKeys.Control), mods.HasFlag(System.Windows.Input.ModifierKeys.Alt),
            mods.HasFlag(System.Windows.Input.ModifierKeys.Shift), mods.HasFlag(System.Windows.Input.ModifierKeys.Windows), name);
        if (text == null && ViewModel != null)
        {
            HotkeyParser.TryParse("Ctrl+Alt+Shift+Win+" + name, out _, out var keyError);
            ViewModel.WindowSizeShortcutProblem = keyError ?? "Include Ctrl, Alt or Win so the shortcut does not interfere with typing.";
        }
        return text;
    }
}
