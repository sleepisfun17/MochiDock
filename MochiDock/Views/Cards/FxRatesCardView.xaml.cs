using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using MochiDock.Services;
using MochiDock.ViewModels;
using Border = System.Windows.Controls.Border;
using Path = System.Windows.Shapes.Path;
using Point = System.Windows.Point;
using RadioButton = System.Windows.Controls.RadioButton;
using UserControl = System.Windows.Controls.UserControl;
using MediaColor = System.Windows.Media.Color;
using MediaBrush = System.Windows.Media.Brush;
using MediaBrushes = System.Windows.Media.Brushes;
using DropShadowEffect = System.Windows.Media.Effects.DropShadowEffect;

namespace MochiDock.Views.Cards;

public partial class FxRatesCardView : UserControl
{
    static readonly string[] Pairs = ["USD/JPY", "USD/IDR", "JPY/IDR"];
    FxRatesService? owner;
    string pair = "JPY/IDR";
    string span = "7D";
    IReadOnlyDictionary<string, MarketQuote>? marketFixture;
    string fixtureStatus = "Ready";
    DateTimeOffset? fixtureChecked;
    private MainViewModel? ViewModel => DataContext as MainViewModel;

    public FxRatesCardView()
    {
        InitializeComponent();
        JpyIdr.IsChecked = true;
        Loaded += (_, _) => Attach();
        Unloaded += (_, _) => Detach();
        DataContextChanged += (_, _) => { if (IsLoaded) Attach(); };
        MochiDock.Services.ThemeService.RedrawOnThemeChange(this, Render);   // the chart copies theme brushes when drawn
        IsVisibleChanged += (_, _) => { owner?.SetVisible(IsVisible); if (IsVisible) Render(); };
    }

    // Used by isolated rendering fixtures without constructing application services.
    public void RenderMarkets(IReadOnlyDictionary<string, MarketQuote> quotes, string selectedPair,
        string status, DateTimeOffset? checkedAt)
    {
        marketFixture = quotes;
        fixtureStatus = status;
        fixtureChecked = checkedAt;
        SelectPair(selectedPair, false);
        Render();
    }

    void Attach()
    {
        Detach();
        owner = (DataContext as MainViewModel)?.Fx;
        if (owner is not null)
        {
            owner.Changed += Changed;
            pair = owner.SelectedPair;
            span = owner.SelectedSpan;
        }
        if (ViewModel?.Settings?.FxHistorySpan is { Length: > 0 } savedSpan)
        {
            span = savedSpan;
            owner?.SelectSpan(span);
        }
        UpdateSpanRadios();
        owner?.SetVisible(IsVisible);
        SelectPair(pair, false);
        Render();
    }

    void UpdateSpanRadios()
    {
        if (Span7DRadio != null) Span7DRadio.IsChecked = span == "7D";
        if (Span1MRadio != null) Span1MRadio.IsChecked = span == "1M";
        if (Span1YRadio != null) Span1YRadio.IsChecked = span == "1Y";
    }

    void Span_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { IsChecked: true, Tag: string selectedSpan } || selectedSpan == span) return;
        span = selectedSpan;
        owner?.SelectSpan(span);
        if (ViewModel?.Settings != null)
        {
            ViewModel.Settings.FxHistorySpan = span;
            SettingsService.Save(ViewModel.Settings);
        }
        Render();
    }

    void Detach()
    {
        if (owner is not null)
        {
            owner.Changed -= Changed;
            owner.SetVisible(false);
        }
        owner = null;
    }

    void Changed()
    {
        if (!Dispatcher.HasShutdownStarted)
            Dispatcher.BeginInvoke(() => { if (IsLoaded) Render(); });
    }

    void Pair_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton { IsChecked: true, Tag: string selected }) return;
        SelectPair(selected, IsLoaded);
    }

    void SelectPair(string selected, bool request)
    {
        if (!Pairs.Contains(selected)) selected = "JPY/IDR";
        pair = selected;
        if (UsdJpy is not null) UsdJpy.IsChecked = pair == "USD/JPY";
        if (UsdIdr is not null) UsdIdr.IsChecked = pair == "USD/IDR";
        if (JpyIdr is not null) JpyIdr.IsChecked = pair == "JPY/IDR";
        if (request)
        {
            owner?.SelectMarket(pair, span == "7D");
            if (span != "7D") owner?.SelectSpan(span);
        }
        Render();
    }

    async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        if (owner is null) return;
        await MochiDock.Controls.RefreshFeedback.RunAsync(RefreshButton, async () => await owner.RefreshAsync(true));
    }

    void Chart_SizeChanged(object sender, SizeChangedEventArgs e) => DrawChart(CurrentMarkets());

    IReadOnlyDictionary<string, MarketQuote> CurrentMarkets() =>
        owner?.Markets ?? marketFixture ?? new Dictionary<string, MarketQuote>();

    MarketBar[] currentBars = [];
    Point[] currentPoints = [];
    decimal currentAvg;

    void Render()
    {
        if (UsdJpy is null) return;
        UpdateSpanRadios();
        var markets = CurrentMarkets();
        UpdateTile("USD/JPY", UsdJpyValue, UsdJpyBadge, UsdJpyChange, markets);
        UpdateTile("USD/IDR", UsdIdrValue, UsdIdrBadge, UsdIdrChange, markets);
        UpdateTile("JPY/IDR", JpyIdrValue, JpyIdrBadge, JpyIdrChange, markets);

        markets.TryGetValue(pair, out var selected);
        var headerChange = selected?.ChangePercent ?? (selected != null ? Calculate24hDelta(selected.Value, pair, markets) : null);
        ApplyChange(HeaderTrendBadge, HeaderTrend, headerChange, true);
        string spanLabel = span switch { "1M" => "1-Month", "1Y" => "1-Year", _ => "7-Day" };
        ChartTitle.Text = $"{spanLabel} Trajectory: {pair.Replace("/", " / ")} ({PairDirection(pair)})";
        DrawChart(markets);
        DrawFooter(markets, selected);
    }

    static string PairDirection(string p) => p switch
    {
        "USD/JPY" => "$ → ¥",
        "USD/IDR" => "$ → Rp",
        "JPY/IDR" => "¥ → Rp",
        _ => p
    };

    void UpdateTile(string name, TextBlock valueText, Border badge, TextBlock changeText,
        IReadOnlyDictionary<string, MarketQuote> markets)
    {
        if (markets.TryGetValue(name, out var quote))
        {
            valueText.Text = FormatRate(quote.Value, name);
            var change = quote.ChangePercent ?? Calculate24hDelta(quote.Value, name, markets);
            ApplyChange(badge, changeText, change, false);
            valueText.ToolTip = $"1 {name[..3]} = {quote.Value.ToString(CultureInfo.InvariantCulture)} {name[4..]} · {quote.Provider}";
        }
        else
        {
            valueText.Text = name == "USD/JPY" ? "¥—" : "Rp —";
            ApplyChange(badge, changeText, null, false);
            valueText.ToolTip = "Rate unavailable";
        }
    }

    static decimal? Calculate24hDelta(decimal currentValue, string pairName, IReadOnlyDictionary<string, MarketQuote> markets) =>
        FxRatesService.Delta24h(currentValue, pairName, markets);

    static string FormatRate(decimal value, string name) => name switch
    {
        "USD/JPY" => "¥" + value.ToString("N2", CultureInfo.InvariantCulture),
        "USD/IDR" => "Rp " + value.ToString("N0", CultureInfo.InvariantCulture),
        _ => "Rp " + value.ToString("N2", CultureInfo.InvariantCulture)
    };

    static string FormatRange(decimal value, string name) => FormatRate(value, name);

    void ApplyChange(Border badge, TextBlock text, decimal? percent, bool header)
    {
        if (!percent.HasValue)
        {
            text.Text = header ? "— (24h)" : "—";
            text.SetResourceReference(TextBlock.ForegroundProperty, "IslandMutedInkBrush");
            badge.SetResourceReference(Border.BackgroundProperty, "IslandChipBackgroundBrush");
            return;
        }

        var value = percent.Value;
        text.Text = value.ToString(header
            ? "+0.00'% (24h)';-0.00'% (24h)';0.00'% (24h)'"
            : "+0.0'%';-0.0'%';0.0'%'", CultureInfo.InvariantCulture);
        if (value > 0)
        {
            text.SetResourceReference(TextBlock.ForegroundProperty, "IslandPositiveBrush");
            badge.SetResourceReference(Border.BackgroundProperty, "IslandPositiveSubtleBrush");
        }
        else if (value < 0)
        {
            text.SetResourceReference(TextBlock.ForegroundProperty, "IslandNegativeBrush");
            badge.SetResourceReference(Border.BackgroundProperty, "IslandNegativeSubtleBrush");
        }
        else
        {
            text.SetResourceReference(TextBlock.ForegroundProperty, "IslandSecondaryInkBrush");
            badge.SetResourceReference(Border.BackgroundProperty, "IslandChipBackgroundBrush");
        }
    }

    void DrawChart(IReadOnlyDictionary<string, MarketQuote> markets)
    {
        if (Chart is null) return;
        Chart.Children.Clear();
        string spanKey = span switch { "1M" => ":1M", "1Y" => ":1Y", _ => ":7D" };
        markets.TryGetValue(pair + spanKey, out var history);
        if (history is null && spanKey == ":7D") markets.TryGetValue(pair + ":1h", out history);
        if (history is null) markets.TryGetValue(pair + ":1h", out history);
        var bars = history is { Provider: "Yahoo Finance" }
            ? history.Bars.Where(b => b.Value > 0).OrderBy(b => b.At).ToArray()
            : [];
        if (bars.Length < 2 || Chart.ActualWidth < 2 || Chart.ActualHeight < 2)
        {
            string spanName = span switch { "1M" => "1-month", "1Y" => "1-year", _ => "7-day" };
            ChartRange.Text = "Low: —  ·  Avg: —  ·  High: —";
            ChartEmpty.Text = history is null ? $"Loading {spanName} trajectory…" : $"{spanName} Yahoo trajectory unavailable";
            // Loading is quiet meta text; a failed or missing trajectory reads as an error.
            ChartEmpty.SetResourceReference(TextBlock.ForegroundProperty, history is null ? "IslandMutedInkBrush" : "IslandNegativeBrush");
            ChartEmpty.Visibility = Visibility.Visible;
            if (AvgLine != null) AvgLine.Visibility = Visibility.Collapsed;
            if (AvgBadge != null) AvgBadge.Visibility = Visibility.Collapsed;
            return;
        }

        ChartEmpty.Visibility = Visibility.Collapsed;
        var low = bars.Min(b => b.Value);
        var high = bars.Max(b => b.Value);
        var avg = bars.Average(b => b.Value);
        currentAvg = avg;
        ChartRange.Text = $"Low: {FormatRange(low, pair)}  ·  Avg: {FormatRange(avg, pair)}  ·  High: {FormatRange(high, pair)}";
        var first = bars[0].At;
        var last = bars[^1].At;
        var width = Math.Max(1, Chart.ActualWidth - 16);
        var height = Math.Max(1, Chart.ActualHeight - 16);
        var range = high - low;
        Point PointFor(MarketBar bar)
        {
            var x = 8 + Array.IndexOf(bars, bar) / (double)(bars.Length - 1) * width;
            var normalized = range == 0 ? 0.5 : (double)((bar.Value - low) / range);
            return new Point(x, 8 + (1 - normalized) * height);
        }

        var avgNormalized = range == 0 ? 0.5 : (double)((avg - low) / range);
        double yAvg = 8 + (1 - avgNormalized) * height;

        if (AvgLine != null)
        {
            AvgLine.X1 = 8;
            AvgLine.X2 = width + 8;
            AvgLine.Y1 = yAvg;
            AvgLine.Y2 = yAvg;
            AvgLine.Visibility = Visibility.Visible;
        }
        if (AvgBadge != null && AvgBadgeText != null)
        {
            AvgBadgeText.Text = $"Avg {FormatRange(avg, pair)}";
            AvgBadge.Visibility = Visibility.Visible;
            Canvas.SetRight(AvgBadge, 8);
            Canvas.SetTop(AvgBadge, Math.Clamp(yAvg - 9, 2, Math.Max(2, Chart.ActualHeight - 20)));
        }

        var points = bars.Select(PointFor).ToArray();
        currentBars = bars;
        currentPoints = points;
        ResetHover();

        var positiveBrush = (TryFindResource("IslandPositiveBrush") as SolidColorBrush) ?? new SolidColorBrush(MediaColor.FromRgb(52, 211, 153));
        var negativeBrush = (TryFindResource("IslandNegativeBrush") as SolidColorBrush) ?? new SolidColorBrush(MediaColor.FromRgb(251, 113, 133));
        var posColor = positiveBrush.Color;
        var negColor = negativeBrush.Color;

        double tAvg = Math.Clamp(yAvg / Math.Max(1.0, Chart.ActualHeight), 0.05, 0.95);
        double tGap = 0.012;
        double t1 = Math.Max(0.0, tAvg - tGap);
        double t2 = Math.Min(1.0, tAvg + tGap);

        var lineBrush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            GradientStops = new GradientStopCollection
            {
                new GradientStop(posColor, 0.0),
                new GradientStop(posColor, t1),
                new GradientStop(negColor, t2),
                new GradientStop(negColor, 1.0)
            }
        };
        lineBrush.Freeze();

        var areaBrush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(0, 1),
            GradientStops = new GradientStopCollection
            {
                new GradientStop(MediaColor.FromArgb((byte)(0.28 * 255), posColor.R, posColor.G, posColor.B), 0.0),
                new GradientStop(MediaColor.FromArgb((byte)(0.08 * 255), posColor.R, posColor.G, posColor.B), t1),
                new GradientStop(MediaColor.FromArgb((byte)(0.08 * 255), negColor.R, negColor.G, negColor.B), t2),
                new GradientStop(MediaColor.FromArgb((byte)(0.20 * 255), negColor.R, negColor.G, negColor.B), 0.90),
                new GradientStop(MediaColor.FromArgb(0, negColor.R, negColor.G, negColor.B), 1.0)
            }
        };
        areaBrush.Freeze();

        bool isLatestAboveAvg = bars[^1].Value >= avg;
        var latestColor = isLatestAboveAvg ? posColor : negColor;
        var latestBrush = isLatestAboveAvg ? positiveBrush : negativeBrush;

        {
            var area = new Path
            {
                Data = SmoothGeometry(points, Chart.ActualHeight, true),
                Fill = areaBrush,
                IsHitTestVisible = false
            };
            Chart.Children.Add(area);

            var line = new Path
            {
                Data = SmoothGeometry(points, Chart.ActualHeight, false),
                Stroke = lineBrush,
                StrokeThickness = 2.4,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Effect = new DropShadowEffect
                {
                    Color = latestColor,
                    BlurRadius = 10,
                    ShadowDepth = 0,
                    Opacity = 0.55
                },
                IsHitTestVisible = false
            };
            Chart.Children.Add(line);
        }

        var finalPoint = PointFor(bars[^1]);
        var dot = new Ellipse
        {
            Width = 7, Height = 7, StrokeThickness = 1.5,
            Fill = latestBrush,
            Stroke = (MediaBrush)TryFindResource("IslandInkBrush") ?? MediaBrushes.White,
            Effect = new DropShadowEffect
            {
                Color = latestColor,
                BlurRadius = 6,
                ShadowDepth = 0,
                Opacity = 0.85
            },
            ToolTip = $"{bars[^1].At:yyyy-MM-dd HH:mm zzz} · {FormatRate(bars[^1].Value, pair)} · Yahoo Finance"
        };
        Canvas.SetLeft(dot, finalPoint.X - 3.5);
        Canvas.SetTop(dot, finalPoint.Y - 3.5);
        Chart.Children.Add(dot);

        // Update hover indicators with active trend color
        if (HoverDot is not null)
        {
            HoverDot.Fill = latestBrush;
            if (HoverDot.Effect is DropShadowEffect dotGlow) dotGlow.Color = latestColor;
        }
        if (HoverLine is not null)
        {
            HoverLine.Stroke = latestBrush;
        }
    }

    void Chart_MouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (currentBars.Length < 2 || currentPoints.Length < 2 || ChartHoverLayer is null) return;
        var pos = e.GetPosition(Chart);
        double width = Math.Max(1, Chart.ActualWidth - 16);
        double relativeX = Math.Clamp(pos.X - 8, 0, width);
        int index = Math.Clamp((int)Math.Round(relativeX / width * (currentBars.Length - 1)), 0, currentBars.Length - 1);

        var bar = currentBars[index];
        var pt = currentPoints[index];

        bool isPointAboveAvg = bar.Value >= currentAvg;
        var hoverBrush = isPointAboveAvg
            ? ((TryFindResource("IslandPositiveBrush") as SolidColorBrush) ?? new SolidColorBrush(MediaColor.FromRgb(52, 211, 153)))
            : ((TryFindResource("IslandNegativeBrush") as SolidColorBrush) ?? new SolidColorBrush(MediaColor.FromRgb(251, 113, 133)));
        var hoverColor = hoverBrush.Color;

        if (HoverLine is not null)
        {
            HoverLine.X1 = pt.X;
            HoverLine.X2 = pt.X;
            HoverLine.Y1 = 8;
            HoverLine.Y2 = Math.Max(8, Chart.ActualHeight - 8);
            HoverLine.Stroke = hoverBrush;
            HoverLine.Visibility = Visibility.Visible;
        }

        if (HoverDot is not null)
        {
            Canvas.SetLeft(HoverDot, pt.X - 3.5);
            Canvas.SetTop(HoverDot, pt.Y - 3.5);
            HoverDot.Fill = hoverBrush;
            if (HoverDot.Effect is DropShadowEffect hGlow) hGlow.Color = hoverColor;
            HoverDot.Visibility = Visibility.Visible;
        }

        if (HoverTooltip is not null && HoverDate is not null && HoverValue is not null)
        {
            string dateFmt = (span == "1M" || span == "1Y") ? "yyyy/MM/dd" : "yyyy/MM/dd HH:mm";
            HoverDate.Text = bar.At.ToLocalTime().ToString(dateFmt, CultureInfo.InvariantCulture);
            HoverValue.Text = FormatRate(bar.Value, pair);
            HoverTooltip.Visibility = Visibility.Visible;

            HoverTooltip.Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            double tipW = HoverTooltip.DesiredSize.Width;
            double tipH = HoverTooltip.DesiredSize.Height;

            double tipX = (pt.X + tipW + 14 > Chart.ActualWidth) ? pt.X - tipW - 8 : pt.X + 8;
            double tipY = Math.Clamp(pt.Y - tipH / 2, 4, Math.Max(4, Chart.ActualHeight - tipH - 4));

            Canvas.SetLeft(HoverTooltip, tipX);
            Canvas.SetTop(HoverTooltip, tipY);
        }
    }

    void Chart_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e) => ResetHover();

    void ResetHover()
    {
        if (HoverLine is not null) HoverLine.Visibility = Visibility.Collapsed;
        if (HoverDot is not null) HoverDot.Visibility = Visibility.Collapsed;
        if (HoverTooltip is not null) HoverTooltip.Visibility = Visibility.Collapsed;
    }

    static Geometry SmoothGeometry(IReadOnlyList<Point> points, double bottom, bool closeArea)
    {
        var figure = new PathFigure { StartPoint = points[0], IsClosed = closeArea, IsFilled = closeArea };
        for (var i = 0; i < points.Count - 1; i++)
        {
            var p0 = points[Math.Max(0, i - 1)];
            var p1 = points[i];
            var p2 = points[i + 1];
            var p3 = points[Math.Min(points.Count - 1, i + 2)];
            var c1 = new Point(p1.X + (p2.X - p0.X) / 6, p1.Y + (p2.Y - p0.Y) / 6);
            var c2 = new Point(p2.X - (p3.X - p1.X) / 6, p2.Y - (p3.Y - p1.Y) / 6);
            figure.Segments.Add(new BezierSegment(c1, c2, p2, true));
        }
        if (closeArea)
        {
            figure.Segments.Add(new LineSegment(new Point(points[^1].X, bottom), false));
            figure.Segments.Add(new LineSegment(new Point(points[0].X, bottom), false));
        }
        return new PathGeometry([figure]);
    }

    void DrawFooter(IReadOnlyDictionary<string, MarketQuote> markets, MarketQuote? selected)
    {
        string spanKey = span switch { "1M" => ":1M", "1Y" => ":1Y", _ => ":7D" };
        markets.TryGetValue(pair + spanKey, out var history);
        if (history is null && spanKey == ":7D") markets.TryGetValue(pair + ":1h", out history);
        if (history is null) markets.TryGetValue(pair + ":1h", out history);
        var status = owner?.State.Status ?? fixtureStatus;
        var checkedAt = owner?.State.Checked ?? fixtureChecked;
        var provider = selected?.Provider ?? "Waiting for rates";
        var chartSource = history is { Provider: "Yahoo Finance" } && selected?.Provider != "Yahoo Finance"
            ? " · chart Yahoo Finance" : "";
        var staleAfter = selected?.Provider == "Yahoo Finance" ? TimeSpan.FromHours(2) : TimeSpan.FromHours(48);
        var stale = selected is not null && DateTimeOffset.UtcNow - selected.SourceAt > staleAfter;
        var checkedText = checkedAt.HasValue
            ? MochiDock.Core.DateDisplay.Short(checkedAt.Value.UtcDateTime) + checkedAt.Value.UtcDateTime.ToString(" HH:mm 'UTC'", CultureInfo.InvariantCulture) : "—";
        SourceText.Text = $"{provider}{chartSource} · checked {checkedText}" + (stale ? " · stale" : "") +
            (status is "Ready" or "Fixture" ? "" : $" · {status}");
        SourceText.ToolTip = selected is null ? "Automatic keyless FX rates" :
            $"Source timestamp: {selected.SourceAt.ToUniversalTime():yyyy-MM-dd HH:mm:ss} UTC\nIndicative rates; bank and remittance spreads may differ.";
    }
}
