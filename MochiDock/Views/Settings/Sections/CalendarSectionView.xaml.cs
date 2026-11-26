using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Shapes;
using MochiDock.Core;
using MochiDock.Services;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;

namespace MochiDock.Views.Settings.Sections;

/// <summary>
/// Connected calendar feed links (kept together in the DPAPI store by <see cref="CalendarFeedService"/>) and calendar display options.
/// Saved links are never shown again; each calendar shows its colour dot, name, live status, colour selector, and Remove button.
/// </summary>
public partial class CalendarSectionView : SettingsSectionBase
{
    private CalendarFeedService? _calendar;
    private bool _syncingColors;

    public CalendarSectionView()
    {
        InitializeComponent();
        PopulateColorCombo(ColorCombo);
        Unloaded += (s, e) => Detach();
    }

    private static void PopulateColorCombo(ComboBox combo)
    {
        combo.Items.Clear();
        foreach (string key in CalendarFeedPolicy.ColorKeys)
            combo.Items.Add(key);
        combo.SelectedIndex = 0;
    }

    protected override void LoadFromSettings()
    {
        var service = ViewModel?.CalendarFeed;
        if (!ReferenceEquals(service, _calendar))
        {
            Detach();
            _calendar = service;
            if (_calendar != null) _calendar.Changed += OnCalendarChanged;
        }
        RefreshStatus();
    }

    private void Detach()
    {
        if (_calendar != null) _calendar.Changed -= OnCalendarChanged;
        _calendar = null;
    }

    private void OnCalendarChanged() => Dispatcher.BeginInvoke(RefreshStatus);

    private void RefreshStatus()
    {
        var feeds = _calendar?.Feeds ?? Array.Empty<CalendarFeedInfo>();
        bool hasFeeds = feeds.Count > 0;
        EmptyFeedsText.Visibility = hasFeeds ? Visibility.Collapsed : Visibility.Visible;
        RefreshBtn.Visibility = hasFeeds ? Visibility.Visible : Visibility.Collapsed;
        AddBtn.IsEnabled = _calendar != null && LinkBox.Password.Trim().Length > 0 && feeds.Count < CalendarFeedPolicy.MaxFeeds;
        NameBox.IsEnabled = _calendar != null && feeds.Count < CalendarFeedPolicy.MaxFeeds;
        ColorCombo.IsEnabled = _calendar != null && feeds.Count < CalendarFeedPolicy.MaxFeeds;
        LinkBox.IsEnabled = _calendar != null && feeds.Count < CalendarFeedPolicy.MaxFeeds;

        if (string.IsNullOrWhiteSpace(NameBox.Text))
            ColorCombo.SelectedItem = CalendarFeedPolicy.PickNextColor(feeds.Select(f => f.Color));

        RebuildFeedRows(feeds);

        if (_calendar == null)
        {
            StatusText.Text = "Calendar unavailable";
            return;
        }
        bool refreshing = _calendar.IsRefreshing;
        RefreshBtn.IsEnabled = !refreshing;
        RefreshBtn.Content = refreshing ? "Refreshing…" : "Refresh all";
        StatusText.Text = hasFeeds ? _calendar.StatusText : "";
        StatusText.SetResourceReference(TextBlock.ForegroundProperty,
            _calendar.State == CalendarFeedState.Invalid ? "IslandNegativeBrush" : _calendar.State == CalendarFeedState.Updated ? "IslandPositiveBrush" : "IslandMutedInkBrush");
    }

    private void RebuildFeedRows(IReadOnlyList<CalendarFeedInfo> feeds)
    {
        FeedsPanel.Children.Clear();
        _syncingColors = true;
        try
        {
            foreach (var feed in feeds)
            {
                var row = new Grid { Margin = new Thickness(0, 0, 0, 6) };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var dot = new Ellipse
                {
                    Width = 8,
                    Height = 8,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 0),
                };
                dot.SetResourceReference(Shape.FillProperty, CalendarFeedPolicy.BrushKey(feed.Color));
                Grid.SetColumn(dot, 0);
                row.Children.Add(dot);

                var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
                info.Children.Add(new TextBlock
                {
                    Text = feed.Name,
                    Style = (Style)FindResource("CardBodyText"),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
                var stateBlock = new TextBlock
                {
                    Text = DescribeFeedState(feed),
                    Style = (Style)FindResource("CardCaptionText"),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    ToolTip = string.IsNullOrEmpty(feed.Message) ? null : feed.Message,
                };
                stateBlock.SetResourceReference(TextBlock.ForegroundProperty,
                    feed.State == CalendarFeedState.Invalid ? "IslandNegativeBrush"
                    : feed.State == CalendarFeedState.Updated ? "IslandPositiveBrush"
                    : "IslandMutedInkBrush");
                info.Children.Add(stateBlock);
                Grid.SetColumn(info, 1);
                row.Children.Add(info);

                var colorBox = new ComboBox
                {
                    Width = 88,
                    Margin = new Thickness(0, 0, 6, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Style = (Style)FindResource("ModernDarkComboBox"),
                    Tag = feed,
                };
                PopulateColorCombo(colorBox);
                colorBox.SelectedItem = CalendarFeedPolicy.NormalizeColor(feed.Color);
                colorBox.SelectionChanged += FeedColor_SelectionChanged;
                Grid.SetColumn(colorBox, 2);
                row.Children.Add(colorBox);

                var removeBtn = new Button
                {
                    Content = "×",
                    FontSize = 15,
                    Width = 24,
                    Height = 24,
                    Padding = new Thickness(0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Style = (Style)FindResource("ActionButton"),
                    Tag = feed.Id,
                    ToolTip = "Disconnect this calendar",
                };
                System.Windows.Automation.AutomationProperties.SetName(removeBtn, $"Remove {feed.Name}");
                removeBtn.Click += RemoveFeed_Click;
                Grid.SetColumn(removeBtn, 3);
                row.Children.Add(removeBtn);

                FeedsPanel.Children.Add(row);
            }
        }
        finally { _syncingColors = false; }
    }

    private static string DescribeFeedState(CalendarFeedInfo feed) => feed.State switch
    {
        CalendarFeedState.Loading => "Loading…",
        CalendarFeedState.Updated => $"Updated {feed.UpdatedAt:HH:mm} · {feed.EventCount} events",
        CalendarFeedState.Offline => feed.UpdatedAt is { } at ? $"Offline · {at:HH:mm}" : "Offline",
        CalendarFeedState.Invalid => string.IsNullOrEmpty(feed.Message) ? "Link not accepted" : feed.Message,
        _ => "Not connected",
    };

    private void LinkBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        AddRow.Validation = "";
        int count = _calendar?.Feeds.Count ?? 0;
        AddBtn.IsEnabled = _calendar != null && LinkBox.Password.Trim().Length > 0 && count < CalendarFeedPolicy.MaxFeeds;
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (_calendar == null || LinkBox.Password.Trim().Length == 0) return;
        AddRow.Validation = "";
        string? color = ColorCombo.SelectedItem as string;
        string error = _calendar.AddFeed(NameBox.Text, LinkBox.Password, color);
        if (error.Length > 0)
        {
            AddRow.Validation = error;
            return;
        }
        NameBox.Clear();
        LinkBox.Clear(); // the link is a secret: never leave it in the box once saved
        RefreshStatus();
    }

    private void FeedColor_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingColors || _calendar == null) return;
        if (sender is ComboBox { Tag: CalendarFeedInfo feed, SelectedItem: string chosen })
            _calendar.UpdateFeed(feed.Id, feed.Name, chosen);
    }

    private void RemoveFeed_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { Tag: string id })
        {
            AddRow.Validation = "";
            _calendar?.RemoveFeed(id);
            RefreshStatus();
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (_calendar == null) return;
        await MochiDock.Controls.RefreshFeedback.RunAsync(RefreshBtn, async () => await _calendar.RefreshAsync(force: true));
        RefreshStatus();
    }

    private void OpenNotifications_Click(object sender, RoutedEventArgs e) => App.OpenSettings(MochiDock.Core.SettingsSections.Notifications);
}
