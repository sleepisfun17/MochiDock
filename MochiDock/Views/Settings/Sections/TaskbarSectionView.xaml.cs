using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using MochiDock.Core;
using MochiDock.ViewModels;
using Binding = System.Windows.Data.Binding;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using Color = System.Windows.Media.Color;
using ComboBox = System.Windows.Controls.ComboBox;
using FontFamily = System.Windows.Media.FontFamily;
using Orientation = System.Windows.Controls.Orientation;
using TextBox = System.Windows.Controls.TextBox;

namespace MochiDock.Views.Settings.Sections;

/// <summary>
/// Advanced Settings > Taskbar: per-item readout checkboxes, block order, placement/offset, font, arrow style, and optional colours.
/// All options live-apply and are kept per PC.
/// </summary>
public partial class TaskbarSectionView : SettingsSectionBase
{
    private static readonly double[] FontSizeChoices = { 0, 9, 10, 11, 12, 13, 14, 16, 18, 20, 22, 24 };
    private bool _listsPopulated;

    public TaskbarSectionView() => InitializeComponent();

    protected override void LoadFromSettings()
    {
        var vm = ViewModel;
        if (vm == null) return;
        var s = vm.Settings;

        EnsureComboLists();
        BuildBlockOrderList(vm);

        PlaceTrayRadio.IsChecked = vm.CurrentTaskbarPlacement == TaskbarPlacementMode.LeftOfTray;
        PlaceTaskButtonsRadio.IsChecked = vm.CurrentTaskbarPlacement == TaskbarPlacementMode.AfterTaskButtons;

        if (!OffsetBox.IsKeyboardFocusWithin)
        {
            OffsetBox.Text = vm.TaskbarOffsetPx.ToString(CultureInfo.InvariantCulture);
            OffsetRow.Validation = "";
        }

        SelectFontFamily(vm.TaskbarFontFamily);
        SelectFontSize(vm.TaskbarFontSize);
        BoldCheck.IsChecked = vm.TaskbarFontBold;

        var arrows = TaskbarStyle.ParseArrowStyle(vm.TaskbarNetworkArrows);
        ArrowClassicRadio.IsChecked = arrows == TaskbarArrowStyle.Arrow;
        ArrowTriangleRadio.IsChecked = arrows == TaskbarArrowStyle.Triangle;
        ArrowEmojiRadio.IsChecked = arrows == TaskbarArrowStyle.Emoji;

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
        {
            string label = size <= 0 ? "Auto (fit taskbar)" : $"{size:0} px";
            FontSizeCombo.Items.Add(new ComboBoxItem { Content = label, Tag = size });
        }
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

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
        var up = new Button
        {
            Style = (Style)FindResource("ActionButton"),
            Content = "▲",
            Width = 28,
            Padding = new Thickness(0, 3, 0, 3),
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

        buttons.Children.Add(up);
        buttons.Children.Add(down);
        Grid.SetColumn(buttons, 1);
        grid.Children.Add(buttons);

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

    // ---- Placement & offset ----------------------------------------------------------------------------------------------------------------

    private void Placement_Checked(object sender, RoutedEventArgs e)
    {
        if (Loading || ViewModel == null) return;
        ViewModel.TaskbarPlacement = PlaceTaskButtonsRadio.IsChecked == true ? "AfterTaskButtons" : "LeftOfTray";
    }

    private void OffsetMinus_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        ViewModel.TaskbarOffsetPx -= 4;
        OffsetBox.Text = ViewModel.TaskbarOffsetPx.ToString(CultureInfo.InvariantCulture);
        OffsetRow.Validation = "";
    }

    private void OffsetPlus_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        ViewModel.TaskbarOffsetPx += 4;
        OffsetBox.Text = ViewModel.TaskbarOffsetPx.ToString(CultureInfo.InvariantCulture);
        OffsetRow.Validation = "";
    }

    private void OffsetReset_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        ViewModel.TaskbarOffsetPx = 0;
        OffsetBox.Text = "0";
        OffsetRow.Validation = "";
    }

    private void OffsetBox_LostFocus(object sender, RoutedEventArgs e) => CommitOffset();

    private void OffsetBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { CommitOffset(); e.Handled = true; }
    }

    private void CommitOffset()
    {
        if (Loading || ViewModel == null) return;
        if (int.TryParse(OffsetBox.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int px))
        {
            ViewModel.TaskbarOffsetPx = px;
            OffsetBox.Text = ViewModel.TaskbarOffsetPx.ToString(CultureInfo.InvariantCulture);
            OffsetRow.Validation = "";
        }
        else
        {
            OffsetRow.Validation = $"Enter a whole number between {TaskbarStyle.MinOffsetPx} and {TaskbarStyle.MaxOffsetPx}.";
            OffsetBox.Text = ViewModel.TaskbarOffsetPx.ToString(CultureInfo.InvariantCulture);
        }
    }

    // ---- Appearance ------------------------------------------------------------------------------------------------------------------------

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

    private void Arrows_Checked(object sender, RoutedEventArgs e)
    {
        if (Loading || ViewModel == null) return;
        if (ArrowEmojiRadio.IsChecked == true) ViewModel.TaskbarNetworkArrows = "Emoji";
        else if (ArrowTriangleRadio.IsChecked == true) ViewModel.TaskbarNetworkArrows = "Triangle";
        else ViewModel.TaskbarNetworkArrows = "Arrow";
    }

    private void LabelColor_LostFocus(object sender, RoutedEventArgs e) =>
        CommitColor(LabelColorBox, LabelColorRow, LabelColorSwatch, ViewModel?.TaskbarLabelColor ?? "", c => { if (ViewModel != null) ViewModel.TaskbarLabelColor = c; });

    private void LabelColor_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            CommitColor(LabelColorBox, LabelColorRow, LabelColorSwatch, ViewModel?.TaskbarLabelColor ?? "", c => { if (ViewModel != null) ViewModel.TaskbarLabelColor = c; });
            e.Handled = true;
        }
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
        if (e.Key == Key.Enter)
        {
            CommitColor(ValueColorBox, ValueColorRow, ValueColorSwatch, ViewModel?.TaskbarValueColor ?? "", c => { if (ViewModel != null) ViewModel.TaskbarValueColor = c; });
            e.Handled = true;
        }
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
