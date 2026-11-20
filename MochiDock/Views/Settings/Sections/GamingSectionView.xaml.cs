using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using MochiDock.Core;
using MochiDock.Services;
using MochiDock.ViewModels;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;
using UserControl = System.Windows.Controls.UserControl;

namespace MochiDock.Views.Settings.Sections;

/// <summary>Gaming and display settings. Writes go through <see cref="MainViewModel.UpdateSettings"/>; resolution changes go through the view model to the dock window.</summary>
public partial class GamingSectionView : UserControl
{
    private bool _loading;
    private MainViewModel? _subscribed;

    public GamingSectionView()
    {
        InitializeComponent();
        Loaded += (s, e) => { Subscribe(); LoadFromSettings(); };
        Unloaded += (s, e) => Unsubscribe();
        IsVisibleChanged += (s, e) => { if (IsVisible) LoadFromSettings(); };
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void Subscribe()
    {
        if (ReferenceEquals(_subscribed, ViewModel)) return;
        Unsubscribe();
        _subscribed = ViewModel;
        if (_subscribed != null) _subscribed.SettingsChanged += OnSettingsChanged;
    }

    private void Unsubscribe()
    {
        if (_subscribed != null) _subscribed.SettingsChanged -= OnSettingsChanged;
        _subscribed = null;
    }

    private void OnSettingsChanged(SettingsApply effects) => Dispatcher.BeginInvoke(LoadFromSettings);

    private void LoadFromSettings()
    {
        var vm = ViewModel;
        if (vm == null) return;
        var settings = vm.Settings;
        _loading = true;
        try
        {
            if (!PreferredBox.IsKeyboardFocused) PreferredBox.Text = settings.PreferredGamingResolution ?? "";
            RefreshDisplay(settings);
            HotkeyCheck.IsChecked = settings.EnableResolutionHotkey;
            HotkeyBox.Text = HotkeyParser.TryParse(settings.ResolutionHotkey, out var hotkey, out _) ? hotkey.Text : HotkeyParser.Default;
            HotkeyBox.IsEnabled = settings.EnableResolutionHotkey;
            LoadWindowSizes(settings);
            ClickThroughCheck.IsChecked = settings.EnableGamingClickThrough;
            SessionSummaryCheck.IsChecked = settings.EnableGameSessionSummary;
            PingCapsuleCheck.IsChecked = settings.ShowPingInGameCapsule;
            LolStatsCheck.IsChecked = settings.EnableLolMatchStats;
            PubgCheck.IsChecked = settings.EnablePubgStats;
            if (!PubgNameBox.IsKeyboardFocused) PubgNameBox.Text = settings.PubgPlayerName ?? "";
            foreach (ComboBoxItem item in PubgPlatformCombo.Items)
                if ((item.Tag as string) == PubgStatsService.NormalizePlatform(settings.PubgPlatform)) { PubgPlatformCombo.SelectedItem = item; break; }
            PubgKeyStatus.Text = vm.HasPubgKey() ? "A key is saved." : "No key saved: PUBG stats stay off until you save one.";
            foreach (ComboBoxItem item in BreakCombo.Items)
                if ((item.Tag as string) == BreakReminder.NormalizeInterval(settings.BreakReminderMinutes).ToString()) { BreakCombo.SelectedItem = item; break; }
            if (!PingHostBox.IsKeyboardFocused) PingHostBox.Text = settings.PingHost ?? "1.1.1.1";
            if (!OffsetSlider.IsMouseCaptureWithin) OffsetSlider.Value = settings.EffectiveGamingTopOffset;
            // The offset only applies to the Floating dock (MainWindow ignores it for Notch), so the row is off in Notch mode.
            OffsetRow.IsEnabled = !string.Equals(settings.DockStyle, "Notch", StringComparison.OrdinalIgnoreCase);
            if (!ProfilesPanel.IsKeyboardFocusWithin) RebuildProfiles();
        }
        finally { _loading = false; }
    }

    // ---- Detected display and the size / shape pickers. Everything comes from the modes the dock's monitor reports, so a new
    // computer shows its own native resolution and only combinations it can actually switch to.
    private IReadOnlyList<GamingResolutionOption> _options = Array.Empty<GamingResolutionOption>();
    private (int Width, int Height) _native;
    private string? _displayKey;

    private void RefreshDisplay(MochiDock.Models.AppSettings settings)
    {
        var screen = MultiMonitorHelper.GetTargetScreen(settings.TargetMonitor);
        var service = new DisplayResolutionService();
        var modes = service.GetSupportedModes(screen?.DeviceName);
        var current = service.GetCurrentMode(screen?.DeviceName);
        string key = string.Join(",", modes.Select(m => $"{m.Width}x{m.Height}").Distinct()) + "|" + screen?.DeviceName;

        if (modes.Count == 0)
        {
            _native = (0, 0);
            _options = Array.Empty<GamingResolutionOption>();
            DetectedText.Text = "Could not read this display's modes.";
            ResNativeButton.Visibility = ResGamingButton.Visibility = QuickPickRow.Visibility = Visibility.Collapsed;
            _displayKey = null;
            return;
        }
        ResNativeButton.Visibility = QuickPickRow.Visibility = Visibility.Visible;

        _native = DisplayResolutionPolicy.PickNative(modes.Select(m => (m.Width, m.Height)), 0, 0);
        int nativeRefresh = modes.Where(m => m.Width == _native.Width && m.Height == _native.Height).Select(m => m.RefreshRate).DefaultIfEmpty(0).Max();
        DetectedText.Text = $"{current?.Width ?? _native.Width}×{current?.Height ?? _native.Height} now · native {DisplayResolutionPolicy.FormatLabel(_native.Width, _native.Height)}" + (nativeRefresh > 0 ? $" · {nativeRefresh} Hz" : "");
        ResNativeButton.Content = $"Native {_native.Width}×{_native.Height}";

        if (key != _displayKey)
        {
            _displayKey = key;
            _options = GamingResolutionOptions.Build(modes.Select(m => (m.Width, m.Height)));
            SizeCombo.Items.Clear();
            foreach (var size in GamingResolutionOptions.Sizes(_options))
                SizeCombo.Items.Add(new ComboBoxItem { Content = $"{size.SizeName} ({size.TierHeight})", Tag = size.TierHeight });
        }

        var preferred = GamingResolutionOptions.Resolve(_options, _native, settings.PreferredGamingResolution);
        // Nothing stored yet (new install): the box stays empty and its hint names the suggestion the hotkey and tray will use.
        MochiDock.Controls.TextBoxHint.SetText(PreferredBox, preferred == null ? "none" : $"auto: {preferred.Value}");
        ResGamingButton.Visibility = preferred == null ? Visibility.Collapsed : Visibility.Visible;
        if (preferred != null) ResGamingButton.Content = $"Gaming {preferred.Width}×{preferred.Height}";
        PreferredRow.Validation = preferred == null && DisplayResolutionPolicy.TryParse(settings.PreferredGamingResolution, out var pw, out var ph)
            ? $"This display does not offer {pw}x{ph}. Pick a size and shape above."
            : "";
        SyncPickers(preferred);
    }

    private void SyncPickers(GamingResolutionOption? option)
    {
        bool wasLoading = _loading;
        _loading = true;
        try
        {
            SizeCombo.SelectedItem = option == null ? null : SizeCombo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => (int)i.Tag == option.TierHeight);
            FillAspects(option?.TierHeight);
            AspectCombo.SelectedItem = option == null ? null : AspectCombo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => i.Tag is GamingResolutionOption o && o.Value == option.Value);
        }
        finally { _loading = wasLoading; }
    }

    private void FillAspects(int? tierHeight)
    {
        AspectCombo.Items.Clear();
        if (tierHeight is not { } tier) return;
        foreach (var o in GamingResolutionOptions.AspectsFor(_options, tier))
            AspectCombo.Items.Add(new ComboBoxItem { Content = $"{o.Aspect} · {o.Width}×{o.Height}", Tag = o });
    }

    private void Size_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || SizeCombo.SelectedItem is not ComboBoxItem { Tag: int tier }) return;
        FillAspects(tier);
        // Keep the current shape when the new size offers it, otherwise the first one (16:9 first).
        var parsed = ParsedPreferred();
        var shape = DisplayResolutionPolicy.GetAspectRatio(parsed.Width, parsed.Height);
        var pick = AspectCombo.Items.Cast<ComboBoxItem>().FirstOrDefault(i => i.Tag is GamingResolutionOption o && o.Aspect == shape)
                   ?? AspectCombo.Items.Cast<ComboBoxItem>().FirstOrDefault();
        if (pick != null) AspectCombo.SelectedItem = pick; // raises Aspect_Changed, which saves
    }

    private void Aspect_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || AspectCombo.SelectedItem is not ComboBoxItem { Tag: GamingResolutionOption option }) return;
        PreferredBox.Text = option.Value;
        CommitPreferred();
    }

    private (int Width, int Height) ParsedPreferred() =>
        DisplayResolutionPolicy.TryParse(PreferredBox.Text, out var w, out var h) ? (w, h) : (0, 0);

    private void ResNative_Click(object sender, RoutedEventArgs e)
    {
        if (_native.Width > 0) ViewModel?.RequestResolutionPreset(_native.Width, _native.Height, DisplayResolutionPolicy.FormatLabel(_native.Width, _native.Height, isNative: true));
    }

    private void ResGaming_Click(object sender, RoutedEventArgs e)
    {
        var option = GamingResolutionOptions.Resolve(_options, _native, ViewModel?.Settings.PreferredGamingResolution);
        if (option != null) ViewModel?.RequestResolutionPreset(option.Width, option.Height, DisplayResolutionPolicy.FormatLabel(option.Width, option.Height));
    }

    private void Option_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded || _loading) return;
        bool hotkey = HotkeyCheck.IsChecked == true, clickThrough = ClickThroughCheck.IsChecked == true, sessions = SessionSummaryCheck.IsChecked == true, pingCapsule = PingCapsuleCheck.IsChecked == true, lolStats = LolStatsCheck.IsChecked == true, pubg = PubgCheck.IsChecked == true;
        ViewModel?.UpdateSettings(x => { x.EnableResolutionHotkey = hotkey; x.EnableGamingClickThrough = clickThrough; x.EnableGameSessionSummary = sessions; x.ShowPingInGameCapsule = pingCapsule; x.EnableLolMatchStats = lolStats; x.EnablePubgStats = pubg; },
            SettingsApply.Hotkey | SettingsApply.Reposition | SettingsApply.CapsuleRefresh);
    }

    // Records the pressed combination instead of typing text, so the shortcut is always valid.
    private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        var key = e.Key == System.Windows.Input.Key.System ? e.SystemKey : e.Key;
        if (key is System.Windows.Input.Key.LeftCtrl or System.Windows.Input.Key.RightCtrl or System.Windows.Input.Key.LeftAlt or System.Windows.Input.Key.RightAlt
            or System.Windows.Input.Key.LeftShift or System.Windows.Input.Key.RightShift or System.Windows.Input.Key.LWin or System.Windows.Input.Key.RWin
            or System.Windows.Input.Key.ImeProcessed or System.Windows.Input.Key.None) return;
        if (ViewModel == null) return;

        string name = KeyName(key);
        var mods = System.Windows.Input.Keyboard.Modifiers;
        string? text = HotkeyParser.Format(mods.HasFlag(System.Windows.Input.ModifierKeys.Control), mods.HasFlag(System.Windows.Input.ModifierKeys.Alt),
            mods.HasFlag(System.Windows.Input.ModifierKeys.Shift), mods.HasFlag(System.Windows.Input.ModifierKeys.Windows), name);
        if (text == null)
        {
            // Say why in the same place the registration result is shown.
            HotkeyParser.TryParse("Ctrl+Alt+Shift+Win+" + name, out _, out var keyError);
            ViewModel.ResolutionHotkeyProblem = keyError ?? (name == "S" && mods == (System.Windows.Input.ModifierKeys.Alt | System.Windows.Input.ModifierKeys.Shift)
                ? "Alt+Shift+S is already used by the Script reader."
                : "Include Ctrl, Alt or Win so the shortcut does not interfere with typing.");
            return;
        }
        HotkeyBox.Text = text;
        if (!string.Equals(text, ViewModel.Settings.ResolutionHotkey, StringComparison.Ordinal))
            ViewModel.UpdateSettings(x => x.ResolutionHotkey = text, SettingsApply.Hotkey);
        else ViewModel.ResolutionHotkeyProblem = ViewModel.ResolutionHotkeyProblem;
    }

    // Letters, F-keys and digits use their key names; punctuation keys use their character (the key's position on a US layout).
    private static string KeyName(System.Windows.Input.Key key)
    {
        if (key >= System.Windows.Input.Key.D0 && key <= System.Windows.Input.Key.D9) return ((int)key - (int)System.Windows.Input.Key.D0).ToString();
        int vk = System.Windows.Input.KeyInterop.VirtualKeyFromKey(key);
        return HotkeyParser.PunctuationName((uint)vk) ?? key.ToString();
    }

    private void HotkeyReset_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null || ViewModel.Settings.ResolutionHotkey == HotkeyParser.Default) return;
        ViewModel.UpdateSettings(x => x.ResolutionHotkey = HotkeyParser.Default, SettingsApply.Hotkey);
    }

    private void PingHost_LostFocus(object sender, RoutedEventArgs e) => CommitPingHost();

    private void PingHost_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        CommitPingHost();
        e.Handled = true;
    }

    private void CommitPingHost()
    {
        if (!IsLoaded || _loading || ViewModel == null) return;
        PingHostRow.Validation = "";
        if (!SettingsValidation.TryNormalizePingHost(PingHostBox.Text, out string host))
        {
            PingHostRow.Validation = $"Use an IP address or a host name, for example 1.1.1.1. Keeping {ViewModel.Settings.PingHost}.";
            return;
        }
        PingHostBox.Text = host;
        if (!string.Equals(host, ViewModel.Settings.PingHost, StringComparison.Ordinal))
            ViewModel.UpdateSettings(x => x.PingHost = host, SettingsApply.None);
    }

    private void Break_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _loading || ViewModel == null) return;
        if (BreakCombo.SelectedItem is ComboBoxItem { Tag: string tag } && int.TryParse(tag, out int minutes))
        {
            int interval = BreakReminder.NormalizeInterval(minutes);
            if (interval != ViewModel.Settings.BreakReminderMinutes) ViewModel.UpdateSettings(x => x.BreakReminderMinutes = interval);
        }
    }

    private void PubgName_LostFocus(object sender, RoutedEventArgs e) => CommitPubgName();

    private void PubgName_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        CommitPubgName();
        e.Handled = true;
    }

    private void CommitPubgName()
    {
        if (!IsLoaded || _loading || ViewModel == null) return;
        string name = PubgNameBox.Text.Trim();
        if (name.Length > 64) { PubgNameRow.Validation = "A PUBG name has at most 64 characters."; return; }
        PubgNameRow.Validation = "";
        if (!string.Equals(name, ViewModel.Settings.PubgPlayerName, StringComparison.Ordinal))
            ViewModel.UpdateSettings(x => x.PubgPlayerName = name);
    }

    private void PubgPlatform_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _loading || ViewModel == null) return;
        if (PubgPlatformCombo.SelectedItem is ComboBoxItem { Tag: string tag } && tag != ViewModel.Settings.PubgPlatform)
            ViewModel.UpdateSettings(x => x.PubgPlatform = PubgStatsService.NormalizePlatform(tag));
    }

    private void PubgSaveKey_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        PubgKeyRow.Validation = "";
        try
        {
            ViewModel.SavePubgKey(PubgKeyBox.Password);
            PubgKeyBox.Password = "";                     // never keep the key on screen after saving
            PubgKeyStatus.Text = "A key is saved.";
        }
        catch (Exception ex) when (ex is ArgumentException or System.IO.IOException)
        {
            PubgKeyRow.Validation = ex.Message;
        }
    }

    private void PubgRemoveKey_Click(object sender, RoutedEventArgs e)
    {
        if (ViewModel == null) return;
        PubgKeyRow.Validation = "";
        try
        {
            ViewModel.RemovePubgKey();
            PubgKeyStatus.Text = "No key saved: PUBG stats stay off until you save one.";
        }
        catch (System.IO.IOException ex) { PubgKeyRow.Validation = ex.Message; }
    }

    private void ClearPlaytime_Click(object sender, RoutedEventArgs e) => ViewModel?.ClearPlaytime();

    private void ClearSessions_Click(object sender, RoutedEventArgs e) => ViewModel?.ClearGameSessions();

    private void Offset_Changed(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded || _loading) return;
        double offset = SettingsValidation.ClampGamingOffset(OffsetSlider.Value);
        ViewModel?.UpdateSettings(x => x.GamingTopOffset = offset, SettingsApply.Reposition);
    }

    private void Preferred_LostFocus(object sender, RoutedEventArgs e) => CommitPreferred();

    private void Preferred_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        CommitPreferred();
        e.Handled = true;
    }

    private void CommitPreferred()
    {
        if (!IsLoaded || _loading || ViewModel == null) return;
        var settings = ViewModel.Settings;
        PreferredRow.Validation = "";
        if (string.IsNullOrWhiteSpace(PreferredBox.Text))
        {
            // Clearing the box goes back to the automatic suggestion for this display.
            if (!string.IsNullOrEmpty(settings.PreferredGamingResolution))
                ViewModel.UpdateSettings(x => x.PreferredGamingResolution = "", SettingsApply.None);
            return;
        }
        if (!DisplayResolutionPolicy.TryParse(PreferredBox.Text, out int width, out int height))
        {
            PreferredRow.Validation = $"Use width x height, for example 1920x1080. Keeping {settings.PreferredGamingResolution}.";
            return;
        }
        var screen = MultiMonitorHelper.GetTargetScreen(settings.TargetMonitor);
        var modes = new DisplayResolutionService().GetSupportedModes(screen?.DeviceName);
        if (modes.Count > 0 && !modes.Any(m => m.Width == width && m.Height == height))
        {
            PreferredRow.Validation = $"This display does not offer {width}x{height}. Keeping {settings.PreferredGamingResolution}.";
            return;
        }
        string value = $"{width}x{height}";
        PreferredBox.Text = value;
        if (!string.Equals(value, settings.PreferredGamingResolution, StringComparison.Ordinal))
            ViewModel.UpdateSettings(x => x.PreferredGamingResolution = value, SettingsApply.None);
    }

    // ---- Per-game profiles: one row per game, built in code because the rows edit plain settings objects.
    private void RebuildProfiles()
    {
        var vm = ViewModel;
        if (vm == null) return;
        ProfilesPanel.Children.Clear();
        foreach (var profile in vm.Settings.GameProfiles.ToList())
        {
            var game = profile.Game;
            var card = new Border { CornerRadius = new CornerRadius(8), Padding = new Thickness(10, 8, 10, 8), Margin = new Thickness(0, 0, 0, 6), BorderThickness = new Thickness(1) };
            card.SetResourceReference(Border.BackgroundProperty, "IslandSubCardBackgroundBrush");
            card.SetResourceReference(Border.BorderBrushProperty, "IslandSubCardBorderBrush");
            var stack = new StackPanel();

            var header = new Grid();
            header.ColumnDefinitions.Add(new ColumnDefinition());
            header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var title = new TextBlock { Text = game, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, ToolTip = game };
            title.SetResourceReference(TextBlock.StyleProperty, "SettingsRowLabel");
            var remove = new Button { Content = "Remove", Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(8, 3, 8, 3) };
            remove.SetResourceReference(Button.StyleProperty, "ActionButton");
            remove.Click += (s, e) => vm.RemoveGameProfile(game);
            Grid.SetColumn(remove, 1);
            header.Children.Add(title);
            header.Children.Add(remove);
            stack.Children.Add(header);

            var fields = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            var resolution = ResolutionChoice(profile.Resolution);
            var click = MakeChoice(GameProfiles.NormalizeClickThrough(profile.ClickThrough), new[] { GameProfiles.Default, GameProfiles.On, GameProfiles.Off }, 96, "Click-through while this game runs");
            var notes = MakeChoice(GameProfiles.NormalizeNotifications(profile.Notifications), new[] { GameProfiles.Default, GameProfiles.Hold, GameProfiles.Allow }, 96, "Notifications while this game runs");
            fields.Children.Add(Labeled("Resolution while playing", resolution));
            fields.Children.Add(Labeled("Click-through", click));
            fields.Children.Add(Labeled("Notifications", notes));
            stack.Children.Add(fields);

            var message = new TextBlock { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), Visibility = Visibility.Collapsed };
            message.SetResourceReference(TextBlock.StyleProperty, "CardCaptionText");
            message.SetResourceReference(TextBlock.ForegroundProperty, "IslandWarningBrush");
            stack.Children.Add(message);

            void Commit()
            {
                if (_loading) return;
                string text = ((resolution.SelectedItem as ComboBoxItem)?.Tag as string ?? "").Trim(), normalized = "", problem = "";
                message.Visibility = Visibility.Collapsed;
                if (text.Length > 0 && !ResolutionIsUsable(text, out normalized, out problem))
                {
                    message.Text = problem + " Keeping " + (string.IsNullOrEmpty(profile.Resolution) ? "no change" : profile.Resolution) + ".";
                    message.Visibility = Visibility.Visible;
                    return;
                }
                vm.SaveGameProfile(new GameProfile
                {
                    Game = game,
                    Resolution = text.Length == 0 ? "" : normalized,
                    ClickThrough = (string)click.SelectedItem,
                    Notifications = (string)notes.SelectedItem,
                });
            }
            resolution.SelectionChanged += (s, e) => Commit();
            click.SelectionChanged += (s, e) => Commit();
            notes.SelectionChanged += (s, e) => Commit();

            card.Child = stack;
            ProfilesPanel.Children.Add(card);
        }
    }

    // The profile's resolution list: "No change", then every size / shape this display offers (for example "FHD · 16:10 · 1728×1080").
    // A stored value this display does not offer stays visible as "Custom" so nothing is lost silently.
    private ComboBox ResolutionChoice(string? stored)
    {
        var box = new ComboBox { Width = 230, Margin = new Thickness(0, 0, 14, 0), ToolTip = "Resolution to switch to while this game runs. The display goes back to its previous resolution afterwards." };
        box.SetResourceReference(ComboBox.StyleProperty, "ModernDarkComboBox");
        box.Items.Add(new ComboBoxItem { Content = "No change", Tag = "" });
        foreach (var option in _options) box.Items.Add(new ComboBoxItem { Content = option.Label, Tag = option.Value });
        string current = (stored ?? "").Trim();
        if (current.Length > 0 && GamingResolutionOptions.Find(_options, current) == null)
            box.Items.Add(new ComboBoxItem { Content = $"Custom: {current}", Tag = current });
        box.SelectedItem = box.Items.Cast<ComboBoxItem>().FirstOrDefault(i => string.Equals((string)i.Tag, current, StringComparison.OrdinalIgnoreCase)) ?? box.Items[0];
        return box;
    }

    private static ComboBox MakeChoice(string selected, string[] values, double width, string tooltip)
    {
        var box = new ComboBox { Width = width, ItemsSource = values, SelectedItem = selected, ToolTip = tooltip, Margin = new Thickness(0, 0, 14, 0) };
        box.SetResourceReference(ComboBox.StyleProperty, "ModernDarkComboBox");
        return box;
    }

    private static StackPanel Labeled(string label, FrameworkElement control)
    {
        var panel = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
        var text = new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 2) };
        text.SetResourceReference(TextBlock.StyleProperty, "CardCaptionText");
        panel.Children.Add(text);
        panel.Children.Add(control);
        return panel;
    }

    // The resolution must be written as width x height and be one the target display offers (when the display's modes can be read).
    private bool ResolutionIsUsable(string text, out string normalized, out string problem)
    {
        normalized = ""; problem = "";
        if (!DisplayResolutionPolicy.TryParse(text, out int width, out int height))
        {
            problem = "Use width x height, for example 1920x1080.";
            return false;
        }
        var screen = MultiMonitorHelper.GetTargetScreen(ViewModel?.Settings.TargetMonitor);
        var modes = new DisplayResolutionService().GetSupportedModes(screen?.DeviceName);
        if (modes.Count > 0 && !modes.Any(m => m.Width == width && m.Height == height))
        {
            problem = $"This display does not offer {width}x{height}.";
            return false;
        }
        normalized = $"{width}x{height}";
        return true;
    }

    private void AddProfile_Click(object sender, RoutedEventArgs e) => AddProfile(ProfileNameBox.Text);

    private void ProfileName_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != System.Windows.Input.Key.Enter) return;
        AddProfile(ProfileNameBox.Text);
        e.Handled = true;
    }

    private void AddCurrentGameProfile_Click(object sender, RoutedEventArgs e)
    {
        string name = ViewModel?.CurrentGameNameForProfile() ?? "";
        if (name.Length == 0)
        {
            ProfileAddRow.Validation = "No game is running right now. Start the game, or type its name.";
            return;
        }
        AddProfile(name);
    }

    private void AddProfile(string? name)
    {
        ProfileAddRow.Validation = "";
        string game = (name ?? "").Trim();
        if (ViewModel == null) return;
        if (game.Length == 0) { ProfileAddRow.Validation = "Enter the game's name."; return; }
        if (ViewModel.Settings.GameProfiles.Any(p => GameProfiles.Key(p.Game) == GameProfiles.Key(game))) { ProfileAddRow.Validation = "This game already has a profile."; return; }
        ProfileNameBox.Text = "";
        ViewModel.SaveGameProfile(new GameProfile { Game = game });
    }
}
