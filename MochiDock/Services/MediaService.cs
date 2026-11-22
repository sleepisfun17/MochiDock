using System.IO;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Windows.Media.Control;
using Windows.Storage.Streams;
using MochiDock.Models;
using MochiDock.Core;
using System.Diagnostics;

namespace MochiDock.Services;

public class MediaService : IDisposable
{
    private readonly Dispatcher _dispatcher = System.Windows.Application.Current.Dispatcher;
    private readonly MediaSeekExecutor _seekExecutor=new();
    private PlaybackSnapshot _snapshot;
    private readonly LyricClock _lyricClock = new();
    private PendingMediaSeek? _pendingSeek;
    private long _generation, _refreshVersion, _seekVersion, _startTicks;
    private bool _suspended, _disposed;
    private DispatcherTimer? _refreshTimer;
    private double _lastRefresh;
    private static double MonoNow => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
    private void OnUI(Action action) { if (_disposed) return; if (_dispatcher.CheckAccess()) action(); else _dispatcher.BeginInvoke(() => { if (!_disposed) action(); }); }
    private void ScheduleRefresh()
    {
        OnUI(() => {
            _refreshTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Background, async (_, _) => {
                if (MonoNow - _lastRefresh < .1) return;
                _refreshTimer!.Stop(); _lastRefresh = MonoNow; await RefreshMediaInfoAsync();
            }, _dispatcher);
            if (!_suspended) _refreshTimer.Start();
        });
    }
    private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
    private GlobalSystemMediaTransportControlsSession? _currentSession;
    private DispatcherTimer? _sessionPollTimer;
    private DispatcherTimer? _timelineTickTimer;
    private DateTime _lastTimelineUpdate = DateTime.MinValue;
    private TimeSpan _basePosition = TimeSpan.Zero;
    private TimeSpan _totalDuration = TimeSpan.Zero;
    private bool _isPlaying;
    private bool _isIdle;
    private MediaInfo _lastMediaInfo = new();

    public event Action<MediaInfo>? MediaUpdated;
    public event Action<TimeSpan, TimeSpan>? TimelineUpdated;

    public async Task InitializeAsync()
    {
        try
        {
            _sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            if(_disposed)return;
            if (_sessionManager != null)
            {
                _sessionManager.CurrentSessionChanged += SessionManager_CurrentSessionChanged;
                _sessionManager.SessionsChanged += SessionManager_SessionsChanged;
                OnUI(() => UpdateCurrentSession(_sessionManager.GetCurrentSession()));
            }

            var uiDispatcher = System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

            // Periodic session check (2.5s)
            _sessionPollTimer = new DispatcherTimer(
                TimeSpan.FromMilliseconds(2500),
                DispatcherPriority.Background,
                (s, e) => CheckSessionValidity(),
                uiDispatcher);
            _sessionPollTimer.Start();

            // Extrapolate locally; this timer does not poll metadata or the network.
            _timelineTickTimer = new DispatcherTimer(
                TimeSpan.FromMilliseconds(50),
                DispatcherPriority.Normal,
                (s, e) => OnTimelineTick(),
                uiDispatcher);
            if (_isPlaying) _timelineTickTimer.Start();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to init GSMTC: {ex.Message}");
        }
    }

    private void OnTimelineTick()
    {
        if (_suspended || _disposed || _totalDuration <= TimeSpan.Zero) return;
        if (_pendingSeek != null && MonoNow - _lastRefresh >= .3) { _lastRefresh = MonoNow; UpdateTimelineFromSession(); }
        var currentPos = TimeSpan.FromSeconds(_lyricClock.Tick(MonoNow, _snapshot.At(MonoNow), _isPlaying, _snapshot.Rate));

        _lastMediaInfo.Position = currentPos;
        _lastMediaInfo.Duration = _totalDuration;
        TimelineUpdated?.Invoke(currentPos, _totalDuration);
    }

    private void SessionManager_CurrentSessionChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        CurrentSessionChangedEventArgs args)
    {
        OnUI(()=>UpdateCurrentSession(sender.GetCurrentSession()));
    }

    private void SessionManager_SessionsChanged(
        GlobalSystemMediaTransportControlsSessionManager sender,
        SessionsChangedEventArgs args)
    {
        OnUI(() => UpdateCurrentSession(sender.GetCurrentSession()));
    }

    private void CheckSessionValidity()
    {
        if (_sessionManager == null) return;

        try
        {
            var sessions = _sessionManager.GetSessions();
            if (sessions == null || sessions.Count == 0)
            {
                if (_currentSession != null)
                {
                    UpdateCurrentSession(null);
                }
                return;
            }

            if (_currentSession != null)
            {
                bool stillExists = false;
                foreach (var s in sessions)
                {
                    if (s.SourceAppUserModelId == _currentSession.SourceAppUserModelId)
                    {
                        stillExists = true;
                        break;
                    }
                }

                if (!stillExists)
                {
                    UpdateCurrentSession(_sessionManager.GetCurrentSession());
                }
                else
                {
                    var playback = _currentSession.GetPlaybackInfo();
                    if (playback != null)
                    {
                        if (playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed ||
                            playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped)
                        {
                            EmitIdleMedia();
                        }
                    }
                }
            }
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Media", logEx); }
    }

    private void UpdateCurrentSession(GlobalSystemMediaTransportControlsSession? session)
    {
        if (_disposed) return;
        if (_currentSession != null)
        {
            try
            {
                _currentSession.MediaPropertiesChanged -= CurrentSession_MediaPropertiesChanged;
                _currentSession.PlaybackInfoChanged -= CurrentSession_PlaybackInfoChanged;
                _currentSession.TimelinePropertiesChanged -= CurrentSession_TimelinePropertiesChanged;
            }
            catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Media", logEx); }
        }

        _seekExecutor.Cancel(); _generation++; _seekVersion++; _pendingSeek=null; _lyricClock.Reset(); _snapshot=default;
        _currentSession = session;

        if (_currentSession != null)
        {
            try
            {
                _currentSession.MediaPropertiesChanged += CurrentSession_MediaPropertiesChanged;
                _currentSession.PlaybackInfoChanged += CurrentSession_PlaybackInfoChanged;
                _currentSession.TimelinePropertiesChanged += CurrentSession_TimelinePropertiesChanged;
            }
            catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Media", logEx); }
            ScheduleRefresh();
        }
        else
        {
            EmitIdleMedia();
        }
    }

    private void CurrentSession_PlaybackInfoChanged(
        GlobalSystemMediaTransportControlsSession sender,
        PlaybackInfoChangedEventArgs args)
    {
        ScheduleRefresh();
    }

    private void CurrentSession_MediaPropertiesChanged(
        GlobalSystemMediaTransportControlsSession sender,
        MediaPropertiesChangedEventArgs args)
    {
        ScheduleRefresh();
    }

    private void CurrentSession_TimelinePropertiesChanged(
        GlobalSystemMediaTransportControlsSession sender,
        TimelinePropertiesChangedEventArgs args)
    {
        OnUI(UpdateTimelineFromSession);
    }

    private void UpdateTimelineFromSession()
    {
        if (_disposed || _currentSession == null) return;
        try
        {
            var timeline = _currentSession.GetTimelineProperties();
            if (timeline != null)
            {
                var playback = _currentSession.GetPlaybackInfo();
                if (playback == null) return;
                var duration=timeline.EndTime-timeline.StartTime;
                var position=timeline.Position-timeline.StartTime;
                var rate=playback.PlaybackRate??1;
                var age=PlaybackSnapshot.ValidAge((DateTimeOffset.UtcNow-timeline.LastUpdatedTime).TotalSeconds);
                var report=new PlaybackSnapshot(Math.Max(0,position.TotalSeconds),Math.Max(0,duration.TotalSeconds),MonoNow-age,playback.PlaybackStatus==GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing,rate);
                if(report.Duration<=0&&_snapshot.Duration>0)report=report with{Duration=_snapshot.Duration,Position=report.Position>0?report.Position:_snapshot.At(MonoNow),SampledAt=MonoNow};
                if(_pendingSeek is {} pending){if(pending.Holds(report,MonoNow))return;_pendingSeek=null;}
                _snapshot=report;_startTicks=timeline.StartTime.Ticks;_isPlaying=report.Playing;
                _basePosition=TimeSpan.FromSeconds(report.At(MonoNow));_totalDuration=TimeSpan.FromSeconds(report.Duration);
                if(_isPlaying&&!_suspended)_timelineTickTimer?.Start();else _timelineTickTimer?.Stop();

                _lastMediaInfo.Position = _basePosition;
                _lastMediaInfo.Duration = _totalDuration;
                _lastMediaInfo.CanSeek = _totalDuration > TimeSpan.Zero && (playback.Controls?.IsPlaybackPositionEnabled ?? false);
                OnTimelineTick();
            }
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Media", logEx); }
    }

    private void EmitIdleMedia()
    {
        if (_isIdle) return;
        _isIdle = true;
        _timelineTickTimer?.Stop();_pendingSeek=null;_lyricClock.Reset();
        _isPlaying = false;
        _basePosition = TimeSpan.Zero;
        _totalDuration = TimeSpan.Zero;

        _lastMediaInfo = new MediaInfo
        {
            Title = "Mochi Dock",
            Artist = "Ready",
            AppSource = "",
            IsPlaying = false,
            AlbumArt = null,
            CanPlay = false,
            CanPause = false,
            CanSkipNext = false,
            CanSkipPrev = false,
            Position = TimeSpan.Zero,
            Duration = TimeSpan.Zero,
            CanSeek = false
        };
        MediaUpdated?.Invoke(_lastMediaInfo);
    }

    public async Task RefreshMediaInfoAsync()
    {
        if(_disposed)return;
        if(!_dispatcher.CheckAccess()){OnUI(ScheduleRefresh);return;}
        var captured=_currentSession;var generation=_generation;var version=++_refreshVersion;
        if (captured == null)
        {
            EmitIdleMedia();
            return;
        }

        try
        {
            var playback = captured.GetPlaybackInfo();

            if (playback == null ||
                playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Closed ||
                playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Stopped)
            {
                EmitIdleMedia();
                return;
            }

            var props = await captured.TryGetMediaPropertiesAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(2));
            if(_disposed||generation!=_generation||version!=_refreshVersion)return;
            if (props == null || string.IsNullOrWhiteSpace(props.Title))
            {
                EmitIdleMedia();
                return;
            }

            bool trackChanged = props.Title!=_lastMediaInfo.Title || (props.Artist??"")!=_lastMediaInfo.Artist || (props.AlbumTitle??"")!=_lastMediaInfo.Album;
            if (trackChanged)
            {
                _seekExecutor.Cancel();_snapshot=default;_pendingSeek=null;_seekVersion++;_lyricClock.Reset();
                _basePosition = TimeSpan.Zero;
                _lastTimelineUpdate = DateTime.UtcNow;
            }

            _isPlaying = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            var controls = playback.Controls;

            var info = new MediaInfo
            {
                Title = props.Title,
                Album = props.AlbumTitle ?? "",
                SessionIdentity = captured.SourceAppUserModelId,
                Artist = props.Artist ?? "",
                AppSource = CleanAppSource(captured.SourceAppUserModelId),
                IsPlaying = _isPlaying,
                CanPlay = controls?.IsPlayEnabled ?? false,
                CanPause = controls?.IsPauseEnabled ?? false,
                CanSkipNext = controls?.IsNextEnabled ?? false,
                CanSkipPrev = controls?.IsPreviousEnabled ?? false
            };

            // Pull Timeline
            try
            {
                var timeline = captured.GetTimelineProperties();
                if (timeline != null)
                {
                    _basePosition = timeline.Position;
                    _totalDuration = timeline.EndTime - timeline.StartTime;
                    _lastTimelineUpdate = DateTime.UtcNow;

                    info.Position = _basePosition;
                    info.Duration = _totalDuration;
                    info.CanSeek = _totalDuration > TimeSpan.Zero && (controls?.IsPlaybackPositionEnabled ?? false);
                }
            }
            catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Media", logEx); }

            _lastMediaInfo = info;
            UpdateTimelineFromSession();
            _isIdle = false;
            MediaUpdated?.Invoke(info);
            if(props.Thumbnail!=null){var art=await LoadThumbnailAsync(props.Thumbnail);if(!_disposed&&generation==_generation&&version==_refreshVersion){info.AlbumArt=art;MediaUpdated?.Invoke(info);}}
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error reading media properties: {ex.Message}");
            if(!_disposed&&generation==_generation&&version==_refreshVersion)EmitIdleMedia();
        }
    }

    public async Task<MediaSeekResult> SeekToAsync(TimeSpan position)
    {
        var captured=_currentSession;var generation=_generation;var version=++_seekVersion;
        if (captured == null || !_lastMediaInfo.CanSeek) return MediaSeekResult.Unsupported;
        position=TimeSpan.FromSeconds(Math.Clamp(position.TotalSeconds,0,_totalDuration.TotalSeconds));
        try
        {
            long position100NanoSeconds = checked(_startTicks+position.Ticks);
            var outcome = await _seekExecutor.ExecuteAsync(token=>captured.TryChangePlaybackPositionAsync(position100NanoSeconds).AsTask(token));
            bool success=outcome==MediaSeekResult.Accepted;
            if(_disposed||generation!=_generation||version!=_seekVersion)return MediaSeekResult.Cancelled;
            if (success)
            {
                _pendingSeek=new(position.TotalSeconds,MonoNow);_snapshot=_snapshot with{Position=position.TotalSeconds,SampledAt=MonoNow};_lyricClock.Reset();
                _timelineTickTimer?.Start();
                _basePosition = position;
                _lastTimelineUpdate = DateTime.UtcNow;
                _lastMediaInfo.Position = position;
                TimelineUpdated?.Invoke(position, _totalDuration);
            }
            if(!success)UpdateTimelineFromSession();
            return outcome;
        }
        catch(TimeoutException){if(generation!=_generation||version!=_seekVersion)return MediaSeekResult.Cancelled;UpdateTimelineFromSession();return MediaSeekResult.TimedOut;}
        catch
        {
            return MediaSeekResult.Rejected;
        }
    }

    public void SetSuspended(bool value)=>OnUI(()=>{_suspended=value;_lyricClock.Reset();if(value){_timelineTickTimer?.Stop();_refreshTimer?.Stop();}else{UpdateTimelineFromSession();ScheduleRefresh();}});
    public void Dispose(){if(_disposed)return;_generation++;_seekVersion++;_sessionPollTimer?.Stop();_timelineTickTimer?.Stop();_refreshTimer?.Stop();UpdateCurrentSession(null);_disposed=true;_seekExecutor.Dispose();if(_sessionManager!=null){_sessionManager.CurrentSessionChanged-=SessionManager_CurrentSessionChanged;_sessionManager.SessionsChanged-=SessionManager_SessionsChanged;}}
    private static string CleanAppSource(string appId)
    {
        if (string.IsNullOrEmpty(appId)) return "Media";
        var lower = appId.ToLowerInvariant();
        if (lower.Contains("spotify")) return "Spotify";
        if (lower.Contains("youtubemusic")) return "YouTube Music";
        if (lower.Contains("chrome")) return "YouTube (Chrome)";
        if (lower.Contains("edge")) return "YouTube (Edge)";
        if (lower.Contains("firefox")) return "YouTube (Firefox)";
        return Path.GetFileNameWithoutExtension(appId);
    }

    private static async Task<BitmapSource?> LoadThumbnailAsync(IRandomAccessStreamReference thumbRef)
    {
        try
        {
            using var stream = await thumbRef.OpenReadAsync();
            using var netStream = stream.AsStreamForRead();
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = netStream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    public async Task TogglePlayPauseAsync()
    {
        if (_currentSession == null) return;
        try
        {
            await _currentSession.TryTogglePlayPauseAsync();
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Media", logEx); }
    }

    public async Task SkipNextAsync()
    {
        if (_currentSession == null) return;
        try
        {
            await _currentSession.TrySkipNextAsync();
            _ = Task.Run(async () => { await Task.Delay(400); await RefreshMediaInfoAsync(); await Task.Delay(800); await RefreshMediaInfoAsync(); });
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Media", logEx); }
    }

    public async Task SkipPreviousAsync()
    {
        if (_currentSession == null) return;
        try
        {
            await _currentSession.TrySkipPreviousAsync();
            _ = Task.Run(async () => { await Task.Delay(400); await RefreshMediaInfoAsync(); await Task.Delay(800); await RefreshMediaInfoAsync(); });
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Media", logEx); }
    }
}
