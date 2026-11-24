using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MochiDock.Core;
using MochiDock.Services;
using MochiDock.ViewModels;
using Binding = System.Windows.Data.Binding;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using Color = System.Windows.Media.Color;
using ComboBox = System.Windows.Controls.ComboBox;
using Orientation = System.Windows.Controls.Orientation;
using TextBox = System.Windows.Controls.TextBox;

namespace MochiDock.Views.Settings.Sections;

/// <summary>
/// Advanced Settings > Taskbar: live preview bar, per-item readout checkboxes, Left/Right zone toggle, block ordering,
/// multi-monitor display picker, offsets, font, spacing, stable width, network unit/arrows, refresh rate, and adaptive colours.
/// </summary>
public partial class TaskbarSectionView : SettingsSectionBase
{
    private static readonly double[] FontSizeChoices = { 0, 9, 10, 11, 12, 13, 14, 16, 18, 20, 22, 24 };
    private static readonly int[] BlockSpacingChoices = { 0, 6, 8, 10, 12, 16, 20, 24, 32 };
    private static readonly int[] RefreshChoices = { 1, 2, 3 };

    private static readonly TaskbarSnapshot PreviewFallback = new()
    {
        CpuPercent = 14,
        CpuTempC = 48,
        CpuTempMeasured = false,
        GpuPercent = 22,
        GpuTempC = 41,
        RamPercent = 46,
        DiskPercent = 3,
        NetUpBytesPerSecond = 128 * 1024,
        NetDownBytesPerSecond = 2.4 * 1024 * 1024,
        PrayerName = "Asr",
        PrayerTime = "15:20",
        WeatherTempC = 18,
        WindMetersPerSecond = 6,
        WeatherGlyph = "\u2601",
        WeatherCondition = "Cloudy",
        Batteries = new[]
        {
            new TaskbarBattery("\uE962", "Mouse", "2.4G", 78, false, false, DateTimeOffset.UtcNow),
            new TaskbarBattery("\uE765", "Keyboard", "Bluetooth", 64, true, false, DateTimeOffset.UtcNow),
        },
    };

    private bool _listsPopulated;
    private bool _previewDark = true;
    private MainViewModel? _previewVm;

    public TaskbarSectionView()
    {
        InitializeComponent();
        Loaded += (_, _) => HookPreviewEvents();
        Unloaded += (_, _) => UnhookPreviewEvents();
    }

    private void HookPreviewEvents()
    {
        var vm = ViewModel;
        if (ReferenceEquals(_previewVm, vm)) return;
        UnhookPreviewEvents();
        _previewVm = vm;
        if (_previewVm != null)
        {
            _previewVm.TaskbarBlocksChanged += OnLiveTaskbarChanged;
            _previewVm.TaskbarStyleChanged += OnLiveTaskbarChanged;
        }
    }

    private void UnhookPreviewEvents()
    {
        if (_previewVm != null)
        {
            _previewVm.TaskbarBlocksChanged -= OnLiveTaskbarChanged;
            _previewVm.TaskbarStyleChanged -= OnLiveTaskbarChanged;
            _previewVm = null;
        }
    }

    private void OnLiveTaskbarChanged() => Dispatcher.BeginInvoke(UpdatePreview);

    protected override void LoadFromSettings()
    {
        var vm = ViewModel;
        if (vm == null) return;

        HookPreviewEvents();
        EnsureComboLists();
        PopulateMonitorCombo(vm.TaskbarMonitor);
        BuildBlockOrderList(vm);

        PlaceTrayRadio.IsChecked = vm.CurrentTaskbarPlacement == TaskbarPlacementMode.LeftOfTray;
        PlaceTaskButtonsRadio.IsChecked = vm.CurrentTaskbarPlacement == TaskbarPlacementMode.AfterTaskButtons;

        if (!LeftOffsetBox.IsKeyboardFocusWithin)
        {
            LeftOffsetBox.Text = vm.TaskbarLeftOffsetPx.ToString(CultureInfo.InvariantCulture);
            LeftOffsetRow.Validation = "";
        }
        if (!OffsetBox.IsKeyboardFocusWithin)
        {
            OffsetBox.Text = vm.TaskbarOffsetPx.ToString(CultureInfo.InvariantCulture);
            OffsetRow.Validation = "";
        }
        if (!VerticalOffsetBox.IsKeyboardFocusWithin)
        {
            VerticalOffsetBox.Text = vm.TaskbarVerticalOffsetPx.ToString(CultureInfo.InvariantCulture);
            VerticalOffsetRow.Validation = "";
        }

        SelectFontFamily(vm.TaskbarFontFamily);
        SelectFontSize(vm.TaskbarFontSize);
        SelectBlockSpacing(vm.TaskbarBlockSpacing);
        SelectBatteryLayout(vm.TaskbarBatteryLayout);
        SelectRefreshSeconds(vm.TaskbarRefreshSeconds);

        BoldCheck.IsChecked = vm.TaskbarFontBold;
        StableWidthCheck.IsChecked = vm.TaskbarStableWidth;
        CompactLabelsCheck.IsChecked = vm.TaskbarCompactLabels;
        SwapNetCheck.IsChecked = vm.TaskbarSwapNetworkLines;

        var arrows = TaskbarStyle.ParseArrowStyle(vm.TaskbarNetworkArrows);
        ArrowClassicRadio.IsChecked = arrows == TaskbarArrowStyle.Arrow;
        ArrowTriangleRadio.IsChecked = arrows == TaskbarArrowStyle.Triangle;
        ArrowEmojiRadio.IsChecked = arrows == TaskbarArrowStyle.Emoji;

        var unit = TaskbarStyle.ParseNetworkUnit(vm.TaskbarNetworkUnit);
        UnitBytesRadio.IsChecked = unit == TaskbarNetworkUnit.Bytes;
        UnitBitsRadio.IsChecked = unit == TaskbarNetworkUnit.Bits;

        if (!LabelColorBox.IsKeyboardFocusWithin)
        {
            LabelColorBox.Text = vm.TaskbarLabelColor;
            LabelColorRow.Validation = "";
        }
        UpdateColorSwatch(LabelColorSwatch, vm.TaskbarLabelColor);

        if (!ValueColorBox.IsKeyboardFocusWithin)
        {
            ValueColorBox.Text = vm.TaskbarValueColor;
            ValueColorRow.Validation = "";
        }
        UpdateColorSwatch(ValueColorSwatch, vm.TaskbarValueColor);

        UpdatePreview();
    }

    private void EnsureComboLists()
    {
        if (_listsPopulated) return;
        _listsPopulated = true;

        FontFamilyCombo.Items.Add(new ComboBoxItem { Content = "Windows default (Segoe UI)", Tag = "" });
        FontFamilyCombo.Items.Add(new ComboBoxItem { Content = "Quicksand (MochiDock)", Tag = "Quicksand" });

        var systemNames = Fonts.SystemFontFamilies
            .Select(f => f.Source)
            .Where(n => !string.IsNullOrWhiteSpace(n) && !string.Equals(n, "Quicksand", StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase);
        foreach (var name in systemNames)
            FontFamilyCombo.Items.Add(new ComboBoxItem { Content = name, Tag = name });

        foreach (double size in FontSizeChoices)
            FontSizeCombo.Items.Add(new ComboBoxItem { Content = size <= 0 ? "Auto (fit taskbar)" : $"{size:0} px", Tag = size });

        foreach (int spacing in BlockSpacingChoices)
            BlockSpacingCombo.Items.Add(new ComboBoxItem { Content = spacing <= 0 ? "Auto (with font)" : $"{spacing} px", Tag = spacing });

        foreach (int sec in RefreshChoices)
            RefreshCombo.Items.Add(new ComboBoxItem { Content = sec == 1 ? "1 second" : $"{sec} seconds", Tag = sec });

        BatteryLayoutCombo.Items.Add(new ComboBoxItem { Content = "Auto (1 big, 2+ stacked)", Tag = "Auto" });
        BatteryLayoutCombo.Items.Add(new ComboBoxItem { Content = "Side by side (classic)", Tag = "Horizontal" });
        BatteryLayoutCombo.Items.Add(new ComboBoxItem { Content = "Always stacked (2-line)", Tag = "Stacked" });
    }

    private void PopulateMonitorCombo(string currentMonitor)
    {
        MonitorCombo.Items.Clear();
        MonitorCombo.Items.Add(new ComboBoxItem { Content = "Primary display", Tag = TaskbarMonitorSelection.Primary });
        MonitorCombo.Items.Add(new ComboBoxItem { Content = "Same as MochiDock", Tag = TaskbarMonitorSelection.FollowDock });
        MonitorCombo.Items.Add(new ComboBoxItem { Content = "All displays", Tag = TaskbarMonitorSelection.All });

        var monitors = MultiMonitorHelper.GetAllMonitors();
        foreach (var m in monitors)
            MonitorCombo.Items.Add(new ComboBoxItem { Content = m.DisplayLabel, Tag = m.DeviceName });

        string target = currentMonitor?.Trim() ?? "";
        foreach (ComboBoxItem item in MonitorCombo.Items)
        {
            if (string.Equals(item.Tag as string ?? "", target, StringComparison.OrdinalIgnoreCase))
            {
                MonitorCombo.SelectedItem = item;
                return;
            }
        }
        var disconnected = new ComboBoxItem { Content = $"{target} (disconnected)", Tag = target };
        MonitorCombo.Items.Add(disconnected);
        MonitorCombo.SelectedItem = disconnected;
    }

    private void SelectFontFamily(string family)
    {
        string target = family?.Trim() ?? "";
        foreach (ComboBoxItem item in FontFamilyCombo.Items)
        {
            if (string.Equals(item.Tag as string ?? "", target, StringComparison.OrdinalIgnoreCase))
            {
                FontFamilyCombo.SelectedItem = item;
                return;
            }
        }
        var custom = new ComboBoxItem { Content = target, Tag = target };
        FontFamilyCombo.Items.Add(custom);
        FontFamilyCombo.SelectedItem = custom;
    }

    private void SelectFontSize(double size)
    {
        double clamped = TaskbarStyle.ClampFontSize(size);
        foreach (ComboBoxItem item in FontSizeCombo.Items)
        {
            if (item.Tag is double d && Math.Abs(d - clamped) < 0.05)
            {
                FontSizeCombo.SelectedItem = item;
                return;
            }
        }
        var custom = new ComboBoxItem { Content = $"{clamped:0.#} px", Tag = clamped };
        FontSizeCombo.Items.Add(custom);
        FontSizeCombo.SelectedItem = custom;
    }

    private void SelectBlockSpacing(int spacing)
    {
        int clamped = TaskbarStyle.ClampBlockSpacing(spacing);
        foreach (ComboBoxItem item in BlockSpacingCombo.Items)
        {
            if (item.Tag is int s && s == clamped)
            {
                BlockSpacingCombo.SelectedItem = item;
                return;
            }
        }
        var custom = new ComboBoxItem { Content = $"{clamped} px", Tag = clamped };
        BlockSpacingCombo.Items.Add(custom);
        BlockSpacingCombo.SelectedItem = custom;
    }

    private void SelectRefreshSeconds(int sec)
    {
        int clamped = TaskbarStyle.ClampRefreshSeconds(sec);
        foreach (ComboBoxItem item in RefreshCombo.Items)
        {
            if (item.Tag is int s && s == clamped)
            {
                RefreshCombo.SelectedItem = item;
                return;
            }
        }
    }

    private void SelectBatteryLayout(string layout)
    {
        string target = TaskbarStyle.FormatBatteryLayout(TaskbarStyle.ParseBatteryLayout(layout));
        foreach (ComboBoxItem item in BatteryLayoutCombo.Items)
        {
            if (string.Equals(item.Tag as string, target, StringComparison.OrdinalIgnoreCase))
            {
                BatteryLayoutCombo.SelectedItem = item;
                return;
            }
        }
    }

    // ---- Live preview ----------------------------------------------------------------------------------------------------------------------

    private void PreviewTheme_Click(object sender, RoutedEventArgs e)
    {
        _previewDark = ReferenceEquals(sender, PreviewDarkBtn);
        PreviewDarkBtn.Tag = _previewDark ? "Selected" : null;
        PreviewLightBtn.Tag = _previewDark ? null : "Selected";
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        var vm = ViewModel;
        if (vm == null) return;

        PreviewBar.Background = new SolidColorBrush(_previewDark ? Color.FromRgb(0x20, 0x20, 0x20) : Color.FromRgb(0xF3, 0xF3, 0xF3));
        PreviewEmptyHint.Foreground = new SolidColorBrush(_previewDark ? Color.FromRgb(0xB8, 0xB8, 0xB8) : Color.FromRgb(0x55, 0x55, 0x55));

        var (left, right) = vm.BuildTaskbarPreview(PreviewFallback);
        var dpiInfo = VisualTreeHelper.GetDpi(this);
        double dpi = dpiInfo.PixelsPerInchX > 0 ? dpiInfo.PixelsPerInchX : 96.0;
        double scale = dpiInfo.DpiScaleY > 0 ? dpiInfo.DpiScaleY : 1.0;
        int heightPx = Math.Max(24, (int)Math.Round(48 * scale));
        var style = vm.CurrentTaskbarAppearance;
        bool bg = vm.ShowDeviceBatteryBackground;

        PreviewLeftImage.Source = RenderPreviewBitmap(left, dpi, _previewDark, heightPx, bg, style);
        PreviewRightImage.Source = RenderPreviewBitmap(right, dpi, _previewDark, heightPx, bg, style);
        PreviewEmptyHint.Visibility = left.Count == 0 && right.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static BitmapSource? RenderPreviewBitmap(
        IReadOnlyList<TaskbarBlock> blocks,
        double dpi,
        bool darkTaskbar,
        int heightPx,
        bool background,
        TaskbarAppearance style)
    {
        var render = TaskbarReadoutRenderer.Render(blocks, dpi, darkTaskbar, heightPx, background, style);
        if (render == null) return null;
        var bmp = BitmapSource.Create(render.Width, render.Height, dpi, dpi, PixelFormats.Pbgra32, null, render.Pixels, render.Width * 4);
        bmp.Freeze();
        return bmp;
    }

    // ---- Blocks, side & order --------------------------------------------------------------------------------------------------------------

    private void BuildBlockOrderList(MainViewModel vm)
    {
        BlockOrderList.Children.Clear();
        var order = vm.TaskbarBlockOrder;
        for (int i = 0; i < order.Count; i++)
        {
            var kind = order[i];
            BlockOrderList.Children.Add(BuildBlockCard(vm, kind, i == 0, i == order.Count - 1));
        }
    }

    private Border BuildBlockCard(MainViewModel vm, TaskbarBlockKind kind, bool isFirst, bool isLast)
    {
        var card = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(12, 9, 12, 9),
            Margin = new Thickness(0, 0, 0, 6),
        };
        card.SetResourceReference(Border.BackgroundProperty, "IslandSubCardBackgroundBrush");
        card.SetResourceReference(Border.BorderBrushProperty, "IslandSubCardBorderBrush");

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var left = new StackPanel();
        var title = new TextBlock { Text = BlockTitle(kind), FontSize = 13 };
        title.Style = (Style)FindResource("CardBodyText");
        left.Children.Add(title);

        var items = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 0, 0) };
        var s = vm.Settings;
        switch (kind)
        {
            case TaskbarBlockKind.System:
                items.Children.Add(ItemCheck("CPU load", nameof(vm.TaskbarShowCpuLoad), "Show CPU load on the taskbar"));
                items.Children.Add(ItemCheck("CPU temp", nameof(vm.TaskbarShowCpuTemp), "Show CPU temperature on the taskbar (~ indicates estimate)"));
                items.Children.Add(ItemCheck("GPU load", nameof(vm.TaskbarShowGpuLoad), "Show GPU load on the taskbar"));
                items.Children.Add(ItemCheck("GPU temp", nameof(vm.TaskbarShowGpuTemp), "Show GPU temperature on the taskbar"));
                break;
            case TaskbarBlockKind.MemoryDisk:
                items.Children.Add(ItemCheck("Memory", nameof(vm.TaskbarShowMemory), "Show memory usage on the taskbar"));
                items.Children.Add(ItemCheck("Disk", nameof(vm.TaskbarShowDisk), "Show disk activity on the taskbar"));
                break;
            case TaskbarBlockKind.Network:
                items.Children.Add(ItemCheck("Upload", nameof(vm.TaskbarShowUpload), "Show network upload rate on the taskbar"));
                items.Children.Add(ItemCheck("Download", nameof(vm.TaskbarShowDownload), "Show network download rate on the taskbar"));
                break;
            case TaskbarBlockKind.Prayer:
                items.Children.Add(ItemCheck("Next prayer", nameof(vm.TaskbarShowPrayer), "Show the next prayer on the taskbar", s.EnablePrayerTimes));
                if (!s.EnablePrayerTimes) left.Children.Add(NoteText("Turn on prayer times in Location & Prayer first."));
                break;
            case TaskbarBlockKind.Weather:
                items.Children.Add(ItemCheck("Temperature", nameof(vm.TaskbarShowWeatherTemp), "Show weather temperature on the taskbar", s.ShowWeatherTab));
                items.Children.Add(ItemCheck("Wind", nameof(vm.TaskbarShowWeatherWind), "Show wind speed on the taskbar", s.ShowWeatherTab));
                if (!s.ShowWeatherTab) left.Children.Add(NoteText("Turn on the Weather tab in Tabs first."));
                break;
            case TaskbarBlockKind.Batteries:
                items.Children.Add(ItemCheck("Device batteries", nameof(vm.ShowDeviceBatteriesOnTaskbar), "Show wireless device batteries on the taskbar", s.EnableDeviceBatteries));
                if (!s.EnableDeviceBatteries) left.Children.Add(NoteText("Turn on wireless device batteries in Hardware & power first."));
                break;
        }
        left.Children.Add(items);
        Grid.SetColumn(left, 0);
        grid.Children.Add(left);

        var zone = vm.GetTaskbarBlockZone(kind);
        var controls = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };

        var leftBtn = new Button
        {
            Style = (Style)FindResource("MonitorSelectionButton"),
            Content = "Left",
            FontSize = 11,
            Padding = new Thickness(8, 3, 8, 3),
            Tag = zone == TaskbarZone.Left ? "Selected" : null,
            ToolTip = "Place this block on the left edge of the taskbar",
        };
        System.Windows.Automation.AutomationProperties.SetName(leftBtn, $"Place {BlockTitle(kind)} on left side");
        leftBtn.Click += (_, _) => { ViewModel?.SetTaskbarBlockZone(kind, TaskbarZone.Left); if (ViewModel != null) BuildBlockOrderList(ViewModel); };

        var rightBtn = new Button
        {
            Style = (Style)FindResource("MonitorSelectionButton"),
            Content = "Right",
            FontSize = 11,
            Padding = new Thickness(8, 3, 8, 3),
            Margin = new Thickness(6, 0, 0, 0),
            Tag = zone == TaskbarZone.Right ? "Selected" : null,
            ToolTip = "Place this block on the right side of the taskbar",
        };
        System.Windows.Automation.AutomationProperties.SetName(rightBtn, $"Place {BlockTitle(kind)} on right side");
        rightBtn.Click += (_, _) => { ViewModel?.SetTaskbarBlockZone(kind, TaskbarZone.Right); if (ViewModel != null) BuildBlockOrderList(ViewModel); };

        var up = new Button
        {
            Style = (Style)FindResource("ActionButton"),
            Content = "▲",
            Width = 28,
            Padding = new Thickness(0, 3, 0, 3),
            Margin = new Thickness(10, 0, 0, 0),
            IsEnabled = !isFirst,
            ToolTip = "Move block up",
        };
        System.Windows.Automation.AutomationProperties.SetName(up, $"Move {BlockTitle(kind)} up");
        up.Click += (_, _) => { ViewModel?.MoveTaskbarBlock(kind, -1); if (ViewModel != null) BuildBlockOrderList(ViewModel); };

        var down = new Button
        {
            Style = (Style)FindResource("ActionButton"),
            Content = "▼",
            Width = 28,
            Padding = new Thickness(0, 3, 0, 3),
            Margin = new Thickness(8, 0, 0, 0),
            IsEnabled = !isLast,
            ToolTip = "Move block down",
        };
        System.Windows.Automation.AutomationProperties.SetName(down, $"Move {BlockTitle(kind)} down");
        down.Click += (_, _) => { ViewModel?.MoveTaskbarBlock(kind, +1); if (ViewModel != null) BuildBlockOrderList(ViewModel); };

        controls.Children.Add(leftBtn);
        controls.Children.Add(rightBtn);
        controls.Children.Add(up);
        controls.Children.Add(down);
        Grid.SetColumn(controls, 1);
        grid.Children.Add(controls);

        card.Child = grid;
        return card;
    }

    private CheckBox ItemCheck(string label, string propertyName, string automationName, bool enabled = true)
    {
        var box = new CheckBox
        {
            Style = (Style)FindResource("ModernDarkCheckBox"),
            Content = label,
            IsEnabled = enabled,
            Margin = new Thickness(0, 4, 14, 0),
        };
        System.Windows.Automation.AutomationProperties.SetName(box, automationName);
        box.SetBinding(CheckBox.IsCheckedProperty, new Binding(propertyName) { Mode = BindingMode.TwoWay });
        return box;
    }

    private TextBlock NoteText(string message)
    {
        var tb = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
        tb.Style = (Style)FindResource("CardCaptionText");
        return tb;
    }

    private static string BlockTitle(TaskbarBlockKind kind) => kind switch
    {
        TaskbarBlockKind.System => "CPU & GPU",
        TaskbarBlockKind.MemoryDisk => "Memory & Disk",
        TaskbarBlockKind.Network => "Network",
        TaskbarBlockKind.Prayer => "Next prayer",
        TaskbarBlockKind.Weather => "Weather",
        TaskbarBlockKind.Batteries => "Device batteries",
        _ => kind.ToString(),
    };

    // ---- Display & placement ---------------------------------------------------------------------------------------------------------------

    private void Monitor_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (Loading || ViewModel == null) return;
        if (MonitorCombo.SelectedItem is ComboBoxItem item)
            ViewModel.TaskbarMonitor = item.Tag as string ?? "";
    }

    private void Placement_Checked(object sender, RoutedEventArgs e)
    {
        if (Loading || ViewModel == null) return;
        ViewModel.TaskbarPlacement = PlaceTaskButtonsRadio.IsChecked == true ? "AfterTaskButtons" : "LeftOfTray";
    }

    private void LeftOffsetMinus_Click(object sender, RoutedEventArgs e) => NudgeInt(() => ViewModel!.TaskbarLeftOffsetPx -= 4, () => ViewModel!.TaskbarLeftOffsetPx, LeftOffsetBox, LeftOffsetRow);
    private void LeftOffsetPlus_Click(object sender, RoutedEventArgs e) => NudgeInt(() => ViewModel!.TaskbarLeftOffsetPx += 4, () => ViewModel!.TaskbarLeftOffsetPx, LeftOffsetBox, LeftOffsetRow);
    private void LeftOffsetReset_Click(object sender, RoutedEventArgs e) => NudgeInt(() => ViewModel!.TaskbarLeftOffsetPx = 0, () => ViewModel!.TaskbarLeftOffsetPx, LeftOffsetBox, LeftOffsetRow);
    private void LeftOffsetBox_LostFocus(object sender, RoutedEventArgs e) => CommitInt(LeftOffsetBox, LeftOffsetRow, TaskbarStyle.MinOffsetPx, TaskbarStyle.MaxOffsetPx, () => ViewModel!.TaskbarLeftOffsetPx, v => ViewModel!.TaskbarLeftOffsetPx = v);
    private void LeftOffsetBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e) { if (e.Key == Key.Enter) { LeftOffsetBox_LostFocus(sender, e); e.Handled = true; } }

    private void OffsetMinus_Click(object sender, RoutedEventArgs e) => NudgeInt(() => ViewModel!.TaskbarOffsetPx -= 4, () => ViewModel!.TaskbarOffsetPx, OffsetBox, OffsetRow);
    private void OffsetPlus_Click(object sender, RoutedEventArgs e) => NudgeInt(() => ViewModel!.TaskbarOffsetPx += 4, () => ViewModel!.TaskbarOffsetPx, OffsetBox, OffsetRow);
    private void OffsetReset_Click(object sender, RoutedEventArgs e) => NudgeInt(() => ViewModel!.TaskbarOffsetPx = 0, () => ViewModel!.TaskbarOffsetPx, OffsetBox, OffsetRow);
    private void OffsetBox_LostFocus(object sender, RoutedEventArgs e) => CommitInt(OffsetBox, OffsetRow, TaskbarStyle.MinOffsetPx, TaskbarStyle.MaxOffsetPx, () => ViewModel!.TaskbarOffsetPx, v => ViewModel!.TaskbarOffsetPx = v);
    private void OffsetBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e) { if (e.Key == Key.Enter) { OffsetBox_LostFocus(sender, e); e.Handled = true; } }

    private void VerticalOffsetMinus_Click(object sender, RoutedEventArgs e) => NudgeInt(() => ViewModel!.TaskbarVerticalOffsetPx -= 2, () => ViewModel!.TaskbarVerticalOffsetPx, VerticalOffsetBox, VerticalOffsetRow);
    private void VerticalOffsetPlus_Click(object sender, RoutedEventArgs e) => NudgeInt(() => ViewModel!.TaskbarVerticalOffsetPx += 2, () => ViewModel!.TaskbarVerticalOffsetPx, VerticalOffsetBox, VerticalOffsetRow);
    private void VerticalOffsetReset_Click(object sender, RoutedEventArgs e) => NudgeInt(() => ViewModel!.TaskbarVerticalOffsetPx = 0, () => ViewModel!.TaskbarVerticalOffsetPx, VerticalOffsetBox, VerticalOffsetRow);
    private void VerticalOffsetBox_LostFocus(object sender, RoutedEventArgs e) => CommitInt(VerticalOffsetBox, VerticalOffsetRow, TaskbarStyle.MinVerticalOffsetPx, TaskbarStyle.MaxVerticalOffsetPx, () => ViewModel!.TaskbarVerticalOffsetPx, v => ViewModel!.TaskbarVerticalOffsetPx = v);
    private void VerticalOffsetBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e) { if (e.Key == Key.Enter) { VerticalOffsetBox_LostFocus(sender, e); e.Handled = true; } }

    private void NudgeInt(Action apply, Func<int> read, TextBox box, SettingRow row)
    {
        if (ViewModel == null) return;
        apply();
        box.Text = read().ToString(CultureInfo.InvariantCulture);
        row.Validation = "";
    }

    private void CommitInt(TextBox box, SettingRow row, int min, int max, Func<int> read, Action<int> write)
    {
        if (Loading || ViewModel == null) return;
        if (int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int px))
        {
            write(px);
            box.Text = read().ToString(CultureInfo.InvariantCulture);
            row.Validation = "";
        }
        else
        {
            row.Validation = $"Enter a whole number between {min} and {max}.";
            box.Text = read().ToString(CultureInfo.InvariantCulture);
        }
    }

    // ---- Appearance & formatting -----------------------------------------------------------------------------------------------------------

    private void FontFamily_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (Loading || ViewModel == null) return;
        if (FontFamilyCombo.SelectedItem is ComboBoxItem item)
            ViewModel.TaskbarFontFamily = item.Tag as string ?? "";
    }

    private void FontSize_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (Loading || ViewModel == null) return;
        if (FontSizeCombo.SelectedItem is ComboBoxItem item && item.Tag is double d)
            ViewModel.TaskbarFontSize = d;
    }

    private void BlockSpacing_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (Loading || ViewModel == null) return;
        if (BlockSpacingCombo.SelectedItem is ComboBoxItem item && item.Tag is int s)
            ViewModel.TaskbarBlockSpacing = s;
    }

    private void Refresh_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (Loading || ViewModel == null) return;
        if (RefreshCombo.SelectedItem is ComboBoxItem item && item.Tag is int sec)
            ViewModel.TaskbarRefreshSeconds = sec;
    }

    private void BatteryLayout_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (Loading || ViewModel == null) return;
        if (BatteryLayoutCombo.SelectedItem is ComboBoxItem item && item.Tag is string layout)
            ViewModel.TaskbarBatteryLayout = layout;
    }

    private void Arrows_Checked(object sender, RoutedEventArgs e)
    {
        if (Loading || ViewModel == null) return;
        if (ArrowEmojiRadio.IsChecked == true) ViewModel.TaskbarNetworkArrows = "Emoji";
        else if (ArrowTriangleRadio.IsChecked == true) ViewModel.TaskbarNetworkArrows = "Triangle";
        else ViewModel.TaskbarNetworkArrows = "Arrow";
    }

    private void NetUnit_Checked(object sender, RoutedEventArgs e)
    {
        if (Loading || ViewModel == null) return;
        ViewModel.TaskbarNetworkUnit = UnitBitsRadio.IsChecked == true ? "Bits" : "Bytes";
    }

    private void LabelColor_LostFocus(object sender, RoutedEventArgs e) =>
        CommitColor(LabelColorBox, LabelColorRow, LabelColorSwatch, ViewModel?.TaskbarLabelColor ?? "", c => { if (ViewModel != null) ViewModel.TaskbarLabelColor = c; });

    private void LabelColor_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { LabelColor_LostFocus(sender, e); e.Handled = true; }
    }

    private void LabelColorReset_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        ViewModel.TaskbarLabelColor = "";
        LabelColorBox.Text = "";
        LabelColorRow.Validation = "";
        UpdateColorSwatch(LabelColorSwatch, "");
    }

    private void ValueColor_LostFocus(object sender, RoutedEventArgs e) =>
        CommitColor(ValueColorBox, ValueColorRow, ValueColorSwatch, ViewModel?.TaskbarValueColor ?? "", c => { if (ViewModel != null) ViewModel.TaskbarValueColor = c; });

    private void ValueColor_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { ValueColor_LostFocus(sender, e); e.Handled = true; }
    }

    private void ValueColorReset_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        ViewModel.TaskbarValueColor = "";
        ValueColorBox.Text = "";
        ValueColorRow.Validation = "";
        UpdateColorSwatch(ValueColorSwatch, "");
    }

    private void CommitColor(TextBox box, SettingRow row, Border swatch, string current, Action<string> apply)
    {
        if (Loading || ViewModel == null) return;
        string raw = box.Text.Trim();
        if (raw.Length == 0)
        {
            apply("");
            row.Validation = "";
            UpdateColorSwatch(swatch, "");
            return;
        }
        string norm = TaskbarStyle.NormalizeHexColor(raw);
        if (norm.Length == 0)
        {
            row.Validation = "Enter a 6-digit hex colour such as #80CBC4, or clear for Auto.";
            box.Text = current;
            UpdateColorSwatch(swatch, current);
            return;
        }
        apply(norm);
        box.Text = norm;
        row.Validation = "";
        UpdateColorSwatch(swatch, norm);
    }

    private static void UpdateColorSwatch(Border swatch, string hex)
    {
        if (TaskbarStyle.TryParseHexColor(hex, out byte r, out byte g, out byte b))
            swatch.Background = new SolidColorBrush(Color.FromRgb(r, g, b));
        else
            swatch.SetResourceReference(Border.BackgroundProperty, "IslandSecondaryInkBrush");
    }
}
