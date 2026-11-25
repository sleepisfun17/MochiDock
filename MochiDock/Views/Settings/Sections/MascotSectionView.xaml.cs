using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MochiDock.Controls;
using MochiDock.Core;

namespace MochiDock.Views.Settings.Sections;

/// <summary>Mascot character, status badge style with live preview, expressions and birthday.</summary>
public partial class MascotSectionView : SettingsSectionBase
{
    public MascotSectionView()
    {
        InitializeComponent();
        InitPreviewMoods();
    }

    private void InitPreviewMoods()
    {
        var items = new (string Label, MascotMood Mood)[]
        {
            ("New message (!)", MascotMood.Alert),
            ("Coffee (cup)", MascotMood.Coffee),
            ("Sleeping (Z)", MascotMood.Sleeping),
            ("Rainy (umbrella)", MascotMood.Rainy),
            ("Sunny", MascotMood.Sunny),
            ("Victory (trophy)", MascotMood.Victory),
            ("Low battery", MascotMood.Drained),
            ("Overdue (clock)", MascotMood.Angry),
            ("Studying (timer)", MascotMood.Studying),
            ("Gaming (Neko controller)", MascotMood.Focused),
            ("Meeting (Neko mic)", MascotMood.Meeting),
            ("Prayer (bell)", MascotMood.Praying),
        };
        foreach (var (label, mood) in items)
            PreviewMoodCombo.Items.Add(new ComboBoxItem { Content = label, Tag = mood });
        PreviewMoodCombo.SelectedIndex = 0;
    }

    protected override void LoadFromSettings()
    {
        var vm = ViewModel;
        if (vm == null) return;
        var settings = vm.Settings;

        if (MascotCombo.Items.Count == 0)
        {
            MascotCombo.Items.Add(new ComboBoxItem { Content = "Auto", Tag = MascotChoice.Auto });
            MascotCombo.Items.Add(new ComboBoxItem { Content = "Mochi (grey)", Tag = MascotChoice.Mochi });
            MascotCombo.Items.Add(new ComboBoxItem { Content = "Matcha Mochi (green)", Tag = MascotChoice.MatchaMochi });
            MascotCombo.Items.Add(new ComboBoxItem { Content = "Hojicha Mochi (tan)", Tag = MascotChoice.HojichaMochi });
            MascotCombo.Items.Add(new ComboBoxItem { Content = "Kuro (black cat)", Tag = MascotChoice.Kuro });
            MascotCombo.Items.Add(new ComboBoxItem { Content = "Shiro (white cat)", Tag = MascotChoice.Shiro });
        }
        string mascot = MascotChoice.Normalize(settings.Mascot);
        MascotCombo.SelectedItem = MascotCombo.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == mascot);

        var badgeStyle = MascotBadge.ParseStyle(settings.MascotBadgeStyle);
        BadgeCharmRadio.IsChecked = badgeStyle == MascotBadgeStyle.Charm;
        BadgeBubbleRadio.IsChecked = badgeStyle == MascotBadgeStyle.Bubble;
        BadgeStickerRadio.IsChecked = badgeStyle == MascotBadgeStyle.Sticker;
        BadgeOffRadio.IsChecked = badgeStyle == MascotBadgeStyle.Off;

        ExpressiveCheck.IsChecked = settings.ExpressiveMochi;
        LoadBirthday(settings.BirthdayMonthDay);
        RefreshPreview();
    }

    private void RefreshPreview()
    {
        var vm = ViewModel;
        if (vm == null) return;

        var theme = MascotControl.ToMascotTheme(vm.Settings.CapsuleTheme);
        var kind = MascotChoice.Resolve(vm.Settings.Mascot, vm.Settings.CapsuleTheme);
        var style = MascotBadge.ParseStyle(vm.Settings.MascotBadgeStyle);
        var mood = (PreviewMoodCombo.SelectedItem as ComboBoxItem)?.Tag is MascotMood m ? m : MascotMood.Alert;

        foreach (var preview in new[] { PreviewCompact, PreviewExpanded, PreviewLarge })
        {
            preview.BadgeStyleOverride = style;
            preview.ApplyTheme(theme, kind);
            preview.Mood = mood;
        }
        PreviewCompactBadge.BadgeStyleOverride = style;
        PreviewCompactBadge.ApplyTheme(theme, kind);
        PreviewCompactBadge.Mood = mood;
    }

    private void PreviewMood_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded) return;
        RefreshPreview();
    }

    private void Mascot_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || Loading) return;
        if (MascotCombo.SelectedItem is ComboBoxItem { Tag: string choice })
        {
            ViewModel?.ApplyMascotChoice(choice);
            RefreshPreview();
        }
    }

    private void BadgeStyle_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || Loading) return;
        if (sender is System.Windows.Controls.RadioButton { Tag: string style })
        {
            ViewModel?.ApplyMascotBadgeStyle(style);
            RefreshPreview();
        }
    }

    private void Expressive_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || Loading) return;
        bool on = ExpressiveCheck.IsChecked == true;
        ViewModel?.UpdateSettings(x => x.ExpressiveMochi = on);
    }

    // Month and day pickers; "None" in either clears the birthday. The month list is built once.
    private void LoadBirthday(string? monthDay)
    {
        if (BirthdayMonthCombo.Items.Count == 0)
        {
            BirthdayMonthCombo.Items.Add(new ComboBoxItem { Content = "None", Tag = 0 });
            for (int m = 1; m <= 12; m++)
                BirthdayMonthCombo.Items.Add(new ComboBoxItem { Content = CultureInfo.InvariantCulture.DateTimeFormat.GetMonthName(m), Tag = m });
            BirthdayDayCombo.Items.Add(new ComboBoxItem { Content = "-", Tag = 0 });
            for (int d = 1; d <= 31; d++) BirthdayDayCombo.Items.Add(new ComboBoxItem { Content = d.ToString(CultureInfo.InvariantCulture), Tag = d });
        }
        var parsed = MascotTriggers.ParseMonthDay(monthDay);
        BirthdayMonthCombo.SelectedIndex = parsed?.Month ?? 0;
        BirthdayDayCombo.SelectedIndex = parsed?.Day ?? 0;
    }

    private void Birthday_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || Loading) return;
        int month = (BirthdayMonthCombo.SelectedItem as ComboBoxItem)?.Tag is int m ? m : 0;
        int day = (BirthdayDayCombo.SelectedItem as ComboBoxItem)?.Tag is int d ? d : 0;
        if (month == 0 || day == 0)
        {
            BirthdayRow.Validation = month == 0 && day == 0 ? "" : "Choose both a month and a day.";
            if (month == 0 && day == 0) ViewModel?.UpdateSettings(x => x.BirthdayMonthDay = "");
            return;
        }
        string value = $"{month:00}-{day:00}";
        if (MascotTriggers.ParseMonthDay(value) == null) { BirthdayRow.Validation = "That day does not exist in this month."; return; }
        BirthdayRow.Validation = "";
        ViewModel?.UpdateSettings(x => x.BirthdayMonthDay = value);
        ViewModel?.RefreshDayFace();
    }
}
