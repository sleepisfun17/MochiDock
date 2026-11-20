using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace MochiDock.Models;

public class MediaInfo : INotifyPropertyChanged
{
    public string Album { get; set; } = "";
    public string SessionIdentity { get; set; } = "";
    private string _title = "Mochi Dock";
    private string _artist = "Ready";
    private string _appSource = "";
    private bool _isPlaying;
    private BitmapSource? _albumArt;
    private bool _canPlay;
    private bool _canPause;
    private bool _canSkipNext;
    private bool _canSkipPrev;
    private TimeSpan _position = TimeSpan.Zero;
    private TimeSpan _duration = TimeSpan.Zero;
    private bool _canSeek;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public string Title
    {
        get => _title;
        set { if (_title != value) { _title = value; OnPropertyChanged(); } }
    }

    public string Artist
    {
        get => _artist;
        set { if (_artist != value) { _artist = value; OnPropertyChanged(); } }
    }

    public string AppSource
    {
        get => _appSource;
        set { if (_appSource != value) { _appSource = value; OnPropertyChanged(); } }
    }

    public bool IsPlaying
    {
        get => _isPlaying;
        set { if (_isPlaying != value) { _isPlaying = value; OnPropertyChanged(); } }
    }

    public BitmapSource? AlbumArt
    {
        get => _albumArt;
        set { if (_albumArt != value) { _albumArt = value; OnPropertyChanged(); } }
    }

    public bool CanPlay
    {
        get => _canPlay;
        set { if (_canPlay != value) { _canPlay = value; OnPropertyChanged(); } }
    }

    public bool CanPause
    {
        get => _canPause;
        set { if (_canPause != value) { _canPause = value; OnPropertyChanged(); } }
    }

    public bool CanSkipNext
    {
        get => _canSkipNext;
        set { if (_canSkipNext != value) { _canSkipNext = value; OnPropertyChanged(); } }
    }

    public bool CanSkipPrev
    {
        get => _canSkipPrev;
        set { if (_canSkipPrev != value) { _canSkipPrev = value; OnPropertyChanged(); } }
    }

    // Interactive Timeline & Scrubber
    public TimeSpan Position
    {
        get => _position;
        set
        {
            if (_position != value)
            {
                _position = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PositionSeconds));
                OnPropertyChanged(nameof(PositionString));
            }
        }
    }

    public TimeSpan Duration
    {
        get => _duration;
        set
        {
            if (_duration != value)
            {
                _duration = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(DurationSeconds));
                OnPropertyChanged(nameof(DurationString));
            }
        }
    }

    public bool CanSeek
    {
        get => _canSeek;
        set { if (_canSeek != value) { _canSeek = value; OnPropertyChanged(); } }
    }

    public double PositionSeconds => Position.TotalSeconds;
    public double DurationSeconds => Duration.TotalSeconds > 0 ? Duration.TotalSeconds : 1;

    public string PositionString => $"{Position:mm\\:ss}";
    public string DurationString => $"{Duration:mm\\:ss}";
}
