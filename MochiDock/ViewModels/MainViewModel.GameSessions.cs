using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows.Media;
using MochiDock.Core;
using MochiDock.Services;

namespace MochiDock.ViewModels;

public partial class MainViewModel
{
    private readonly GameSessionStore _gameSessions = new();
    private GameSessionRecord? _pendingGameSession;
    private DateTime _pendingGameSessionAt;
    private string _pendingTrendLine = "";
    private const int RecentSessionsShown = 2;

    /// <summary>The latest finished game sessions (newest first), shown in the Games module of the Stats tab.</summary>
    public ObservableCollection<GameSessionRecord> RecentGameSessions { get; } = new();
    public bool HasRecentGameSessions => RecentGameSessions.Count > 0;

    /// <summary>Size of the summary graph in DIPs; the card's canvas has the same size.</summary>
    public const double SummaryGraphWidth = 260, SummaryGraphHeight = 76;

    /// <summary>The rows of a finished game's summary (label and text), so the card can line them up. Empty for the other summaries.</summary>
    public ObservableCollection<SummaryLine> SummaryLines { get; } = new();
    public bool HasSummaryLines => SummaryLines.Count > 0;
    public bool ShowSummaryDetails => SummaryLines.Count == 0;

    private void SetSummaryLines(IEnumerable<SummaryLine>? lines)
    {
        SummaryLines.Clear();
        if (lines != null) foreach (var line in lines) SummaryLines.Add(line);
        OnPropertyChanged(nameof(HasSummaryLines));
        OnPropertyChanged(nameof(ShowSummaryDetails));
    }

    private PointCollection _summaryFpsPoints = new(), _summaryPingPoints = new();
    private string _summaryStutterGeometry = "";
    private bool _hasSummaryGraph;
    public PointCollection SummaryFpsPoints { get => _summaryFpsPoints; private set { _summaryFpsPoints = value; OnPropertyChanged(); } }
    public PointCollection SummaryPingPoints { get => _summaryPingPoints; private set { _summaryPingPoints = value; OnPropertyChanged(); } }
    public string SummaryStutterGeometry { get => _summaryStutterGeometry; private set { _summaryStutterGeometry = value; OnPropertyChanged(); } }
    public bool HasSummaryGraph { get => _hasSummaryGraph; private set { _hasSummaryGraph = value; OnPropertyChanged(); } }

    // The graph's legend: what each line is and the scale it is drawn at, so the lines can be read as numbers.
    private string _summaryGraphFpsLabel = "", _summaryGraphPingLabel = "", _summaryGraphDuration = "";
    private bool _hasSummaryGraphPing, _hasSummaryGraphStutters;
    public string SummaryGraphFpsLabel { get => _summaryGraphFpsLabel; private set { _summaryGraphFpsLabel = value; OnPropertyChanged(); } }
    public string SummaryGraphPingLabel { get => _summaryGraphPingLabel; private set { _summaryGraphPingLabel = value; OnPropertyChanged(); } }
    public string SummaryGraphDuration { get => _summaryGraphDuration; private set { _summaryGraphDuration = value; OnPropertyChanged(); } }
    public bool HasSummaryGraphPing { get => _hasSummaryGraphPing; private set { _hasSummaryGraphPing = value; OnPropertyChanged(); } }
    public bool HasSummaryGraphStutters { get => _hasSummaryGraphStutters; private set { _hasSummaryGraphStutters = value; OnPropertyChanged(); } }

    // Draws the session's FPS (and ping) line with a tick at every stutter; hidden when the session kept no graph points.
    private void SetSummaryGraph(GameSessionRecord? session)
    {
        var shape = GameGraphLayout.Layout(session?.Timeline, SummaryGraphWidth, SummaryGraphHeight);
        if (shape == null)
        {
            HasSummaryGraph = false;
            HasSummaryGraphPing = HasSummaryGraphStutters = false;
            SummaryFpsPoints = new(); SummaryPingPoints = new(); SummaryStutterGeometry = "";
            return;
        }
        var fps = new PointCollection(shape.Fps.Select(p => new System.Windows.Point(p.X, p.Y)));
        var ping = new PointCollection(shape.Ping.Select(p => new System.Windows.Point(p.X, p.Y)));
        fps.Freeze(); ping.Freeze();
        SummaryFpsPoints = fps;
        SummaryPingPoints = ping;
        // One tick per 2 DIPs at most, so a long session cannot paint the whole graph.
        SummaryStutterGeometry = string.Join(" ", shape.StutterX.Select(x => Math.Round(x / 2) * 2).Distinct().Select(x => string.Create(System.Globalization.CultureInfo.InvariantCulture, $"M {x:0.#},0 L {x:0.#},{SummaryGraphHeight:0.#}")));
        SummaryGraphFpsLabel = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"FPS (top = {shape.MaxFps:0})");
        SummaryGraphPingLabel = string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Ping (top = {shape.MaxPing:0} ms)");
        SummaryGraphDuration = GameSessionFormat.Duration(session!.Seconds);
        HasSummaryGraphPing = shape.Ping.Count > 0;
        HasSummaryGraphStutters = shape.StutterX.Count > 0;
        HasSummaryGraph = true;
    }

    private void InitGameSessions()
    {
        RefreshRecentGameSessions();
        _hardwareService.GameSessionEnded += OnGameSessionEnded;
    }

    private void RefreshRecentGameSessions()
    {
        RecentGameSessions.Clear();
        var all = _gameSessions.Sessions;
        for (int i = 0; i < Math.Min(RecentSessionsShown, all.Count); i++)
        {
            // Compare each listed session with the sessions that came before it.
            var trend = GameTrend.Compare(all[i], all.Skip(i + 1));
            all[i].TrendText = trend == null ? "" : GameTrend.Line(trend);
            RecentGameSessions.Add(all[i]);
        }
        OnPropertyChanged(nameof(HasRecentGameSessions));
    }

    // Runs on the UI thread (the hardware timer ends the capture). The summary card is shown by the gaming-stopped transition.
    private void OnGameSessionEnded(GameSessionRecord record)
    {
        if (!_settings.EnableGameSessionSummary) return;
        // The trend is worked out from the sessions before this one, so it comes before the record joins the history.
        var trend = GameTrend.Compare(record, _gameSessions.Sessions);
        _pendingTrendLine = trend == null ? "" : GameTrend.Line(trend);
        AttachMatch(record);
        _gameSessions.Add(record);
        StartPubgFetch(record);
        RefreshRecentGameSessions();
        _pendingGameSession = record;
        _pendingGameSessionAt = DateTime.UtcNow;
        // The poll tick that noticed the end of the game sets the resting face; show the result just after it.
        if (record.Match is { } match) App.Current.Dispatcher.BeginInvoke(new Action(() => ShowMatchResultMoment(match)), System.Windows.Threading.DispatcherPriority.Background);
    }

    private bool HasFreshGameSession() => _pendingGameSession != null && DateTime.UtcNow - _pendingGameSessionAt < TimeSpan.FromMinutes(2);

    private GameSessionRecord? TakePendingGameSession()
    {
        var session = HasFreshGameSession() ? _pendingGameSession : null;
        _pendingGameSession = null;
        if (session != null) session.TrendText = _pendingTrendLine;
        _pendingTrendLine = "";
        return session;
    }

    /// <summary>Deletes the stored game session history.</summary>
    public void ClearGameSessions()
    {
        _gameSessions.Clear();
        _pendingGameSession = null;
        RefreshRecentGameSessions();
    }
}
