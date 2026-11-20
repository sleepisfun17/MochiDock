using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MochiDock.Services;
using MochiDock.ViewModels;

namespace MochiDock.Views.Cards;

public partial class AiUsageCardView : System.Windows.Controls.UserControl
{
    private AiUsageService? _usageService;
    private bool _isScanning;

    public AiUsageCardView()
    {
        InitializeComponent();
        Loaded += AiUsageCardView_Loaded;
        Unloaded += AiUsageCardView_Unloaded;
        IsVisibleChanged += (_, _) => _usageService?.SetVisible(IsVisible);
        // The provider rows copy theme brushes when built, so rebuild them when the theme changes.
        ThemeService.RedrawOnThemeChange(this, () => { if (_usageService != null) RenderSnapshots(_usageService.Snapshots); });
    }

    /// <summary>Height this card needs to show every provider without scrolling (header plus the provider grid); 0 until laid out.</summary>
    public double PreferredHeight { get; private set; }
    /// <summary>Raised when <see cref="PreferredHeight"/> changed, so the dock can fit the card to its list.</summary>
    public event Action? PreferredHeightChanged;

    // The grid sits in a vertical ScrollViewer, so its height is always the full height of its rows.
    private void Content_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        double height = HeaderGrid.ActualHeight + HeaderGrid.Margin.Top + HeaderGrid.Margin.Bottom + ProvidersPanel.ActualHeight;
        if (ProvidersPanel.ActualHeight <= 0 || Math.Abs(height - PreferredHeight) < 1) return;
        PreferredHeight = height;
        PreferredHeightChanged?.Invoke();
    }

    private void AiUsageCardView_Unloaded(object sender, RoutedEventArgs e)
    {
        // Polling only runs while the card is on screen; Loaded re-attaches everything.
        if (_usageService == null) return;
        _usageService.SetVisible(false);
        _usageService.Refreshed -= OnRefreshed;
        _usageService = null;
    }

    private void AiUsageCardView_Loaded(object sender, RoutedEventArgs e)
    {
        if (_usageService != null) return;

        if (DataContext is MainViewModel vm)
        {
            _usageService = vm.AiUsageService;
            if (_usageService != null)
            {
                _usageService.Refreshed += OnRefreshed;
                RenderSnapshots(_usageService.Snapshots);
                if (!_usageService.HasRefreshed)
                {
                    _ = RefreshAsync();
                }
                _usageService.SetVisible(IsVisible);
            }
        }
    }

    private void OnRefreshed()
    {
        // May be raised from the service's timer thread.
        Dispatcher.BeginInvoke(() =>
        {
            if (_usageService != null)
            {
                RenderSnapshots(_usageService.Snapshots);
                RefreshLabel.Text = $"Updated {DateTime.Now:HH:mm}";
            }
        });
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        // The button keeps its own "Updated HH:mm" label (set when the snapshots arrive); the shared helper adds the spinner and the busy state.
        await MochiDock.Controls.RefreshFeedback.RunAsync(RefreshButton, RefreshAsync, null);
    }

    private async Task RefreshAsync()
    {
        if (_isScanning || _usageService == null) return;
        _isScanning = true;
        RefreshLabel.Text = "Reading...";

        try
        {
            await _usageService.RefreshAllAsync();
        }
        finally
        {
            _isScanning = false;
        }
    }

    private void RenderSnapshots(IReadOnlyList<AiProviderSnapshot> snapshots)
    {
        ProvidersLeft.Children.Clear();
        ProvidersRight.Children.Clear();
        foreach (var note in ProvidersPanel.Children.OfType<TextBlock>().ToList()) ProvidersPanel.Children.Remove(note);
        double leftHeight = 0, rightHeight = 0;
        // Column width for measuring a card before it is placed (the dock is 620 wide, so about 280 before the first layout).
        double columnWidth = ProvidersPanel.ActualWidth > 0 ? ProvidersPanel.ActualWidth / 2 : 280;

        if (snapshots.Count == 0)
        {
            bool done = _usageService?.HasRefreshed == true;
            var empty = new TextBlock
            {
                Text = done
                    ? "No signed-in AI tools were found on this PC. Sign in to Claude Code, Codex, Cursor, the Gemini CLI or Copilot."
                    : "Reading usage from the AI tools signed in on this PC...",
                FontSize = 11,
                Foreground = (System.Windows.Media.Brush)FindResource("IslandMutedInkBrush"),
                Margin = new Thickness(12, 20, 12, 12),
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumnSpan(empty, 2);
            ProvidersPanel.Children.Add(empty);
            return;
        }

        foreach (var p in snapshots)
        {
            var cardBorder = new Border
            {
                Background = (System.Windows.Media.Brush)FindResource("IslandSubCardBackgroundBrush"),
                BorderBrush = (System.Windows.Media.Brush)FindResource("IslandSubCardBorderBrush"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(10, 8, 10, 8),
                Margin = new Thickness(0, 0, 8, 8)
            };

            var stack = new StackPanel();

            // Row 1: Provider Title and Status Badge
            var topGrid = new Grid();
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Auto) });

            var titleBlock = new TextBlock
            {
                Text = p.ProviderName,
                FontWeight = (FontWeight)FindResource("IslandSmallLabelWeight"),
                FontSize = 13,
                Foreground = (System.Windows.Media.Brush)FindResource("IslandInkBrush"),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(titleBlock, 0);
            topGrid.Children.Add(titleBlock);

            var statusBadge = new Border
            {
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(5, 1, 5, 1),
                Background = !p.IsError ? (System.Windows.Media.Brush)FindResource("IslandChipActiveBrush") : (System.Windows.Media.Brush)FindResource("IslandControlBackgroundBrush"),
                BorderBrush = (System.Windows.Media.Brush)FindResource("IslandSubCardBorderBrush"),
                BorderThickness = new Thickness(p.IsError ? 1 : 0)
            };
            bool attention = p.State is AiProviderState.Expired or AiProviderState.Offline;
            var statusText = new TextBlock
            {
                Text = p.StatusMessage,
                FontSize = 11,
                FontWeight = (FontWeight)FindResource("IslandSmallLabelWeight"),
                Foreground = !p.IsError ? (System.Windows.Media.Brush)FindResource("IslandPositiveBrush")
                    : attention ? (System.Windows.Media.Brush)FindResource("IslandWarningBrush") : (System.Windows.Media.Brush)FindResource("IslandMutedInkBrush"),
                VerticalAlignment = VerticalAlignment.Center
            };
            statusBadge.Child = statusText;
            Grid.SetColumn(statusBadge, 1);
            topGrid.Children.Add(statusBadge);

            stack.Children.Add(topGrid);

            // Row 2: Details / Plan / Masked Account
            string detail = !string.IsNullOrEmpty(p.MaskedAccount)
                ? $"{p.Plan} · {p.MaskedAccount}"
                : p.Plan;

            var detailsText = new TextBlock
            {
                Text = string.IsNullOrEmpty(detail) ? "Plan not reported" : detail,
                FontSize = 11,
                Foreground = (System.Windows.Media.Brush)FindResource("IslandSecondaryInkBrush"),
                Margin = new Thickness(0, 3, 0, 8),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            stack.Children.Add(detailsText);

            // Why a reading is missing, and what to do about it.
            if (!string.IsNullOrEmpty(p.Hint))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = p.Hint,
                    FontSize = 11,
                    Foreground = (System.Windows.Media.Brush)FindResource("IslandSecondaryInkBrush"),
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 4)
                });
            }

            // Row 3: Windows (e.g. Quota progress bars)
            foreach (var win in p.Windows)
            {
                var winGrid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
                // Wide enough for the longest limit label ("Gemini, 5 hours") in the card font at 11 px.
                winGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(82) });
                winGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                winGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(84) });

                var winLabel = new TextBlock
                {
                    Text = win.Label,
                    FontSize = 11,
                    FontWeight = (FontWeight)FindResource("IslandSmallLabelWeight"),
                    Foreground = (System.Windows.Media.Brush)FindResource("IslandInkBrush"),
                    VerticalAlignment = VerticalAlignment.Center,
                    TextTrimming = TextTrimming.CharacterEllipsis
                };
                Grid.SetColumn(winLabel, 0);
                winGrid.Children.Add(winLabel);

                var progressBarBorder = new Border
                {
                    Height = 7,
                    CornerRadius = new CornerRadius(3.5),
                    Background = (System.Windows.Media.Brush)FindResource("IslandGaugeTrackBrush"),
                    BorderBrush = (System.Windows.Media.Brush)FindResource("IslandSubCardBorderBrush"),
                    BorderThickness = new Thickness(0.5),
                    ClipToBounds = true,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 0, 6, 0)
                };

                double usedPct = Math.Clamp(win.UsedRatio * 100.0, 0, 100);
                double leftPct = Math.Clamp(100.0 - usedPct, 0, 100);

                // The fill is a proportion of the track (star columns), so it always spans the bar it sits in.
                var barTrack = new Grid();
                barTrack.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(leftPct, 2), GridUnitType.Star) });
                barTrack.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(100 - leftPct, 0.001), GridUnitType.Star) });
                var barFill = new Border
                {
                    Height = 7,
                    CornerRadius = new CornerRadius(3.5),
                    Background = leftPct <= 15 ? (System.Windows.Media.Brush)FindResource("IslandNegativeBrush")
                        : leftPct <= 35 ? (System.Windows.Media.Brush)FindResource("IslandWarningBrush")
                        : (System.Windows.Media.Brush)FindResource("IslandPositiveBrush")
                };
                barTrack.Children.Add(barFill);
                progressBarBorder.Child = barTrack;
                Grid.SetColumn(progressBarBorder, 1);
                winGrid.Children.Add(progressBarBorder);

                var pctStack = new StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Vertical,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var pctText = new TextBlock
                {
                    Text = $"{leftPct:F0}% left",
                    FontSize = 11,
                    FontWeight = (FontWeight)FindResource("IslandSmallLabelWeight"),
                    Foreground = leftPct <= 15 ? (System.Windows.Media.Brush)FindResource("IslandNegativeBrush") : (System.Windows.Media.Brush)FindResource("IslandInkBrush"),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Right
                };
                pctStack.Children.Add(pctText);

                if (!string.IsNullOrEmpty(win.ResetsInText) && win.ResetsInText != "Ready")
                {
                    var resetText = new TextBlock
                    {
                        Text = win.ResetsInText,
                        FontSize = 11,
                        Foreground = (System.Windows.Media.Brush)FindResource("IslandMutedInkBrush"),
                        HorizontalAlignment = System.Windows.HorizontalAlignment.Right
                    };
                    pctStack.Children.Add(resetText);
                }

                Grid.SetColumn(pctStack, 2);
                winGrid.Children.Add(pctStack);

                stack.Children.Add(winGrid);
            }

            cardBorder.Child = stack;
            // Into the shorter column, so tall and short cards pack without gaps and the list is as short as it can be.
            cardBorder.Measure(new System.Windows.Size(columnWidth, double.PositiveInfinity));
            double cardHeight = cardBorder.DesiredSize.Height;
            if (leftHeight <= rightHeight) { ProvidersLeft.Children.Add(cardBorder); leftHeight += cardHeight; }
            else { ProvidersRight.Children.Add(cardBorder); rightHeight += cardHeight; }
        }
    }
}
