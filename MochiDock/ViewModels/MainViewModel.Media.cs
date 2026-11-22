using System.Collections.ObjectModel;
using MochiDock.Models;
using MochiDock.Services;
using MascotMood = MochiDock.Core.MascotMood;

namespace MochiDock.ViewModels;

public partial class MainViewModel
{
    private MediaInfo _currentMedia = new();
    private SongLyrics? _currentLyrics;
    private string _currentLyricLine = "";
    private string _lastFetchedTrack = "";
    private readonly MochiDock.Core.RequestGeneration _lyricRequests = new();
    private CancellationTokenSource? _lyricCancellation;

    public string CurrentLyricLine
    {
        get => _currentLyricLine;
        set { if (_currentLyricLine != value) { _currentLyricLine = value; OnPropertyChanged(); OnPropertyChanged(nameof(CompactCapsuleText)); OnPropertyChanged(nameof(CompactLineSeconds)); } }
    }

    private double _currentLyricLineSeconds;

    /// <summary>
    /// Seconds the capsule's current lyric line stays on screen (until the next line), so a line too wide for the capsule scrolls
    /// fast enough to be read to its end (MarqueeTextBlock.PaceSeconds). 0 whenever the capsule shows anything but a lyric line.
    /// </summary>
    public double CompactLineSeconds =>
        !string.IsNullOrWhiteSpace(_currentLyricLine) && CompactCapsuleText == _currentLyricLine ? _currentLyricLineSeconds : 0;

    /// <summary>Time from <paramref name="position"/> to the next lyric entry (a blank pause marker counts), or to the end of the track for the last line.</summary>
    private double LyricLineSecondsFrom(int activeIdx, TimeSpan position)
    {
        double seconds = 6.0;
        if (_currentLyrics != null && activeIdx + 1 < _currentLyrics.Lines.Count)
            seconds = (_currentLyrics.Lines[activeIdx + 1].Timestamp - position).TotalSeconds;
        else if (_currentMedia.Duration.TotalSeconds > 0)
            seconds = (_currentMedia.Duration - position).TotalSeconds;
        return double.IsFinite(seconds) ? Math.Clamp(seconds, 1.0, 20.0) : 6.0;
    }

    private string _currentLyricRomaji = "";
    public string CurrentLyricRomaji
    {
        get => _currentLyricRomaji;
        set { if (_currentLyricRomaji != value) { _currentLyricRomaji = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasCurrentLyricRomaji)); OnPropertyChanged(nameof(HasCompactLyricRomaji)); } }
    }

    public bool HasCurrentLyricRomaji => ShowLyricsPronunciation && !string.IsNullOrWhiteSpace(_currentLyricRomaji);
    public bool HasCompactLyricRomaji => !IsHardwareNoticeVisible && !IsPrayerReminderVisible && !IsPrayerArrivalVisible && HasActiveMedia && _currentMedia.IsPlaying && !_wasGaming && !_isStatsPinnedToCapsule && !IsNotificationInMainPill && !string.IsNullOrWhiteSpace(_currentLyricLine) && !string.IsNullOrWhiteSpace(_currentLyricRomaji);

    public ObservableCollection<RubyPiece> CurrentLyricPieces { get; } = new();
    public ObservableCollection<RubyPiece> PreviousLyricPieces { get; } = new();
    public ObservableCollection<RubyPiece> NextLyricPieces { get; } = new();
    public bool HasCurrentLyricRuby => CurrentLyricPieces.Count > 0;
    public bool HasPreviousLyricRuby => PreviousLyricPieces.Count > 0;
    public bool HasNextLyricRuby => NextLyricPieces.Count > 0;

    private string _previousLyricLine = "";
    public string PreviousLyricLine
    {
        get => _previousLyricLine;
        set { if (_previousLyricLine != value) { _previousLyricLine = value; OnPropertyChanged(); } }
    }

    private string _nextLyricLine = "";
    public string NextLyricLine
    {
        get => _nextLyricLine;
        set { if (_nextLyricLine != value) { _nextLyricLine = value; OnPropertyChanged(); } }
    }

    private bool _lyricsLoading, _lyricLookupFailed, _forceLyricsRefresh;
    public string PlainLyrics => _currentLyrics?.PlainLyrics ?? "";
    public string LyricsStatusText => !EnableSyncedLyrics ? "Synced lyrics are off" : _lyricsLoading ? "Loading lyrics…" : _currentLyrics?.IsSynced == true ? "Synced" : _currentLyrics?.IsInstrumental == true ? "Instrumental" : !string.IsNullOrWhiteSpace(PlainLyrics) ? "Plain text" : _lyricLookupFailed ? "Lyrics unavailable — retry" : "No lyrics found";
    private void RefreshLyricsStatus() { OnPropertyChanged(nameof(LyricsStatusText)); OnPropertyChanged(nameof(PlainLyrics)); OnPropertyChanged(nameof(HasLyrics)); OnPropertyChanged(nameof(CanRetryLyrics)); }
    public Task RetryLyricsAsync() { _forceLyricsRefresh = true; _lastFetchedTrack = ""; OnMediaUpdated(CurrentMedia); return Task.CompletedTask; }
    public bool HasLyrics => _currentLyrics != null && _currentLyrics.IsSynced;
    public bool CanRetryLyrics => MochiDock.Core.LyricsPolicy.CanRetryLyrics(HasActiveMedia, EnableSyncedLyrics, _lyricsLoading, HasLyrics);

    public bool EnableSyncedLyrics
    {
        get => _settings?.EnableSyncedLyrics ?? true;
        set
        {
            if (_settings == null || _settings.EnableSyncedLyrics == value) return;
            UpdateSettings(x => x.EnableSyncedLyrics = value);
            OnPropertyChanged();
            // Off: drop what is showing and stop looking up. On: look the current track up again.
            _lyricCancellation?.Cancel();
            _lyricRequests.Next();
            _currentLyrics = null;
            _lyricsLoading = false;
            ClearVisibleLyrics();
            _lastFetchedTrack = "";
            RefreshLyricsStatus();
            if (value && _currentMedia != null) OnMediaUpdated(_currentMedia);
        }
    }

    /// <summary>Lyric timing shift in ms; positive = lines appear earlier. Persisted, clamped to +/-5 s.</summary>
    public int LyricsOffsetMs
    {
        get => _settings?.LyricsOffsetMs ?? 0;
        set
        {
            int clamped = MochiDock.Core.LyricOffset.Clamp(value);
            if (_settings == null || _settings.LyricsOffsetMs == clamped) return;
            UpdateSettings(x => x.LyricsOffsetMs = clamped);
            OnPropertyChanged();
            OnPropertyChanged(nameof(LyricsOffsetText));
            // The tick timer is stopped while paused, so re-evaluate the active line immediately.
            _displayedLyricIndex = -2;
            if (_currentMedia != null) OnTimelineUpdated(_currentMedia.Position, _currentMedia.Duration);
        }
    }
    public string LyricsOffsetText => MochiDock.Core.LyricOffset.Label(LyricsOffsetMs);
    public void NudgeLyricsOffset(int deltaMs) => LyricsOffsetMs += deltaMs;
    public void ResetLyricsOffset() => LyricsOffsetMs = 0;

    public bool ShowLyricsPronunciation
    {
        get => _settings?.ShowLyricsPronunciation ?? true;
        set
        {
            if (_settings != null && _settings.ShowLyricsPronunciation != value)
            {
                UpdateSettings(x => x.ShowLyricsPronunciation = value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasCurrentLyricRomaji));
            }
        }
    }

    public bool ShowLyricsFurigana
    {
        get => _settings?.ShowLyricsFurigana ?? true;
        set
        {
            if (_settings != null && _settings.ShowLyricsFurigana != value)
            {
                UpdateSettings(x => x.ShowLyricsFurigana = value);
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasCurrentLyricRuby));
            }
        }
    }

    public MediaInfo CurrentMedia
    {
        get => _currentMedia;
        set
        {
            _currentMedia = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HeaderBadgeText));
            RaiseCapsuleStateChanged();
        }
    }

    private void OnMediaUpdated(MediaInfo info)
    {
        App.BeginOnUI(() =>
        {
            CurrentMedia = info;
            OnPropertyChanged(nameof(IsAudioWaveformVisible));
            string trackIdentity = LrclibService.CreateIdentity(info.SessionIdentity, info.Title, info.Artist, info.Album, info.Duration.TotalSeconds).Key;
            if (!trackIdentity.Equals(_lastFetchedTrack, StringComparison.Ordinal))
            {
                var forceLyrics = _forceLyricsRefresh; _forceLyricsRefresh = false; _lyricsLoading = true; _lyricLookupFailed = false;
                _lastFetchedTrack = trackIdentity;
                _lyricCancellation?.Cancel();
                _lyricCancellation?.Dispose();
                _lyricCancellation = new CancellationTokenSource();
                var lyricToken = _lyricCancellation.Token;
                long lyricGeneration = _lyricRequests.Next();
                _currentLyrics = null; RefreshLyricsStatus();
                CurrentLyricLine = "";
                CurrentLyricRomaji = "";
                PreviousLyricLine = "";
                NextLyricLine = "";
                CurrentLyricPieces.Clear();
                PreviousLyricPieces.Clear();
                NextLyricPieces.Clear();
                if (!EnableSyncedLyrics) { _lyricsLoading = false; RefreshLyricsStatus(); return; }
                _ = Task.Run(async () =>
                {
                    try
                    {
                        var displayWatch = System.Diagnostics.Stopwatch.StartNew();
                        var lyrics = await _lrclibService.FetchLyricsAsync(info.Title, info.Artist, album: info.Album, durationSeconds: info.Duration.TotalSeconds, cancellationToken: lyricToken, sessionIdentity: info.SessionIdentity, forceRefresh: forceLyrics);
                        App.BeginOnUI(() =>
                        {
                            if (lyricToken.IsCancellationRequested || !_lyricRequests.IsCurrent(lyricGeneration)) return;
                            _lyricsLoading = false; _lyricLookupFailed = _lrclibService.LastLookupFailed;
                            _currentLyrics = lyrics; RefreshLyricsStatus();
                            OnTimelineUpdated(CurrentMedia.Position, CurrentMedia.Duration);
                            System.Diagnostics.Debug.WriteLine($"Lyrics first display: {displayWatch.ElapsedMilliseconds}ms");
                            OnPropertyChanged(nameof(HasLyrics));
                        });
                        if (lyrics != null && !lyricToken.IsCancellationRequested)
                        {
                            var snapshot = new SongLyrics { TrackName = lyrics.TrackName, ArtistName = lyrics.ArtistName, IsSynced = lyrics.IsSynced, PlainLyrics = lyrics.PlainLyrics, Lines = lyrics.Lines.Select(l => new LyricLine { Text = l.Text, Timestamp = l.Timestamp }).ToList() };
                            var watch = System.Diagnostics.Stopwatch.StartNew();
                            await LyricRomanizerService.PronounceSongAsync(snapshot, lyricToken);
                            System.Diagnostics.Debug.WriteLine($"Lyrics pronunciation: {watch.ElapsedMilliseconds}ms");
                            await App.Current.Dispatcher.InvokeAsync(() =>
                            {
                                if (lyricToken.IsCancellationRequested || !_lyricRequests.IsCurrent(lyricGeneration)) return;
                                _currentLyrics = snapshot;
                                OnTimelineUpdated(CurrentMedia.Position, CurrentMedia.Duration);
                                OnPropertyChanged(nameof(HasLyrics));
                            });
                        }
                    }
                    catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("ViewModel", logEx); }
                });
            }
            if (info.IsPlaying)
            {
                if (!_wasGaming)
                {
                    MascotMood = MascotMood.Music;
                }
                if (info.AlbumArt != null)
                {
                    var color = ColorExtractor.ExtractDominantColor(info.AlbumArt);
                    AmbientGlowColor = System.Windows.Media.Color.FromArgb(160, color.R, color.G, color.B);
                }
                else
                {
                    AmbientGlowColor = System.Windows.Media.Color.FromArgb(140, 29, 185, 84); // Spotify green
                }
            }
            else
            {
                AmbientGlowColor = System.Windows.Media.Color.FromArgb(120, 10, 132, 255);
                if (!_wasGaming)
                {
                    MascotMood = MascotMood.Idle;
                }
                // When paused, clear the romaji reading line so pill collapses cleanly
                CurrentLyricRomaji = "";
            }

            OnPropertyChanged(nameof(HasCompactLyricRomaji));
            OnPropertyChanged(nameof(HasCurrentLyricRomaji));
            OnPropertyChanged(nameof(CompactCapsuleText));
        });
    }

    private SongLyrics? _displayedLyricSnapshot;
    private int _displayedLyricIndex = -2;
    // Display guard (also covers lyrics pronounced and cached before the policy existed): a line with no kana, Han or Hangul, or whose reading
    // only repeats it, shows no reading and no ruby, so an English or romaji line never appears twice.
    private static string ReadingFor(LyricLine line)
    {
        string text = line.Text ?? "", reading = line.RomajiText ?? "";
        return !MochiDock.Core.LyricPronunciationPolicy.NeedsReading(text) || MochiDock.Core.LyricPronunciationPolicy.IsRedundant(text, reading) ? "" : reading;
    }

    private static IEnumerable<RubyPiece> PiecesFor(LyricLine line)
    {
        bool plain = !MochiDock.Core.LyricPronunciationPolicy.NeedsReading(line.Text);
        if (!plain && line.RubyPieces is { Count: > 0 } pieces) return pieces;
        return new[] { new RubyPiece { Text = line.Text ?? "", Romaji = plain ? "" : ReadingFor(line) } };
    }

    private void ClearVisibleLyrics() { _displayedLyricSnapshot = null; _displayedLyricIndex = -2; CurrentLyricLine = ""; CurrentLyricRomaji = ""; PreviousLyricLine = ""; NextLyricLine = ""; if (CurrentLyricPieces.Count > 0) CurrentLyricPieces.Clear(); if (PreviousLyricPieces.Count > 0) PreviousLyricPieces.Clear(); if (NextLyricPieces.Count > 0) NextLyricPieces.Clear(); OnPropertyChanged(nameof(HasCurrentLyricRuby)); OnPropertyChanged(nameof(HasPreviousLyricRuby)); OnPropertyChanged(nameof(HasNextLyricRuby)); }
    private void OnTimelineUpdated(TimeSpan pos, TimeSpan dur)
    {
        App.BeginOnUI(() =>
        {
            if (_currentMedia != null)
            {
                _currentMedia.Position = pos;
                _currentMedia.Duration = dur;
                MediaPositionUpdated?.Invoke();
                if (_currentLyrics != null && _currentLyrics.IsSynced && _currentLyrics.Lines.Count > 0)
                {
                    // The playback clock is untouched (Spotify keeps its timestamp-corrected position); only the lookup position is shifted by the user's lyric offset.
                    var leadPos = TimeSpan.FromSeconds(MochiDock.Core.LyricOffset.Apply(pos.TotalSeconds, LyricsOffsetMs));
                    int activeIdx = MochiDock.Core.MediaPresentation.ActiveLine(_currentLyrics.Lines, static line => line.Timestamp.TotalSeconds, leadPos.TotalSeconds);
                    if (activeIdx < 0 || string.IsNullOrWhiteSpace(_currentLyrics.Lines[activeIdx].Text)) { ClearVisibleLyrics(); return; }
                    if (ReferenceEquals(_displayedLyricSnapshot, _currentLyrics) && _displayedLyricIndex == activeIdx) return;
                    _displayedLyricSnapshot = _currentLyrics; _displayedLyricIndex = activeIdx;
                    if (activeIdx >= 0)
                    {
                        var activeLine = _currentLyrics.Lines[activeIdx];

                        // If the active line is an empty pause marker
                        if (string.IsNullOrWhiteSpace(activeLine.Text))
                        {
                            // A pause of 3 s or more before the next line clears the current lyric.
                            double gapToNext = 10.0;
                            if (activeIdx + 1 < _currentLyrics.Lines.Count)
                            {
                                gapToNext = (_currentLyrics.Lines[activeIdx + 1].Timestamp - leadPos).TotalSeconds;
                            }
                            if (gapToNext >= 3.0)
                            {
                                CurrentLyricLine = "";
                                CurrentLyricRomaji = "";
                                CurrentLyricPieces.Clear();
                                OnPropertyChanged(nameof(HasCurrentLyricRuby));
                            }
                            return;
                        }

                        // Pronunciation is prepared in the background lookup, never on the timeline dispatcher.

                        _currentLyricLineSeconds = LyricLineSecondsFrom(activeIdx, leadPos);
                        CurrentLyricLine = activeLine.Text;
                        OnPropertyChanged(nameof(CompactLineSeconds));
                        CurrentLyricRomaji = ReadingFor(activeLine);

                        // Find preceding non-empty line
                        int prevIdx = activeIdx - 1;
                        while (prevIdx >= 0 && string.IsNullOrWhiteSpace(_currentLyrics.Lines[prevIdx].Text))
                        {
                            prevIdx--;
                        }
                        PreviousLyricLine = prevIdx >= 0 ? _currentLyrics.Lines[prevIdx].Text : "";

                        // Find succeeding non-empty line
                        int nextIdx = activeIdx + 1;
                        while (nextIdx < _currentLyrics.Lines.Count && string.IsNullOrWhiteSpace(_currentLyrics.Lines[nextIdx].Text))
                        {
                            nextIdx++;
                        }
                        NextLyricLine = nextIdx < _currentLyrics.Lines.Count ? _currentLyrics.Lines[nextIdx].Text : "";

                        CurrentLyricPieces.Clear();
                        foreach (var p in PiecesFor(activeLine)) CurrentLyricPieces.Add(p);

                        PreviousLyricPieces.Clear();
                        if (prevIdx >= 0)
                        {
                            var prevLine = _currentLyrics.Lines[prevIdx];

                            foreach (var p in PiecesFor(prevLine)) PreviousLyricPieces.Add(p);
                        }

                        NextLyricPieces.Clear();
                        if (nextIdx < _currentLyrics.Lines.Count)
                        {
                            var nxtLine = _currentLyrics.Lines[nextIdx];

                            foreach (var p in PiecesFor(nxtLine)) NextLyricPieces.Add(p);
                        }

                        OnPropertyChanged(nameof(HasCurrentLyricRuby));
                        OnPropertyChanged(nameof(HasPreviousLyricRuby));
                        OnPropertyChanged(nameof(HasNextLyricRuby));
                    }
                }
            }
        });
    }

    public async Task<MochiDock.Core.MediaSeekResult> SeekMediaAsync(double seconds)
    {
        if (!double.IsFinite(seconds)) return MochiDock.Core.MediaSeekResult.Unsupported;
        var result = await _mediaService.SeekToAsync(TimeSpan.FromSeconds(seconds));
        return result;
    }

    public async Task TogglePlayPauseAsync() => await _mediaService.TogglePlayPauseAsync();
    public async Task SkipNextAsync() => await _mediaService.SkipNextAsync();
    public async Task SkipPreviousAsync() => await _mediaService.SkipPreviousAsync();
}
