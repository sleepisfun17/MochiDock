using System.Windows.Threading;

namespace MochiDock.Services;

public enum PomodoroState
{
    Stopped,
    Working,
    Break,
    Paused
}

public class PomodoroService
{
    private readonly DispatcherTimer _timer;
    private TimeSpan _remainingTime = TimeSpan.FromMinutes(25);
    private TimeSpan _totalDuration = TimeSpan.FromMinutes(25);
    private PomodoroState _state = PomodoroState.Stopped;
    private PomodoroState _previousRunningState = PomodoroState.Working;

    public event Action? Tick;
    public event Action? StateChanged;
    public event Action<PomodoroState>? Completed;

    public PomodoroState State => _state;
    public TimeSpan RemainingTime => _remainingTime;
    public TimeSpan TotalDuration => _totalDuration;
    public bool IsActive => _state != PomodoroState.Stopped;
    public bool IsRunning => _state == PomodoroState.Working || _state == PomodoroState.Break;

    public string FormattedTime => $"{_remainingTime:mm\\:ss}";
    public double ProgressPercentage => _totalDuration.TotalSeconds > 0
        ? Math.Clamp((1.0 - (_remainingTime.TotalSeconds / _totalDuration.TotalSeconds)) * 100.0, 0, 100)
        : 0;

    public PomodoroService()
    {
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _timer.Tick += Timer_Tick;
    }

    public void StartWork(int minutes = 25)
    {
        _totalDuration = TimeSpan.FromMinutes(minutes);
        _remainingTime = _totalDuration;
        _state = PomodoroState.Working;
        _previousRunningState = PomodoroState.Working;
        _timer.Start();
        StateChanged?.Invoke();
    }

    public void StartBreak(int minutes = 5)
    {
        _totalDuration = TimeSpan.FromMinutes(minutes);
        _remainingTime = _totalDuration;
        _state = PomodoroState.Break;
        _previousRunningState = PomodoroState.Break;
        _timer.Start();
        StateChanged?.Invoke();
    }

    public void Pause()
    {
        if (IsRunning)
        {
            _previousRunningState = _state;
            _state = PomodoroState.Paused;
            _timer.Stop();
            StateChanged?.Invoke();
        }
    }

    public void Resume()
    {
        if (_state == PomodoroState.Paused)
        {
            _state = _previousRunningState;
            _timer.Start();
            StateChanged?.Invoke();
        }
    }

    public void TogglePlayPause()
    {
        if (IsRunning)
        {
            Pause();
        }
        else if (_state == PomodoroState.Paused)
        {
            Resume();
        }
        else
        {
            StartWork(25);
        }
    }

    public void Reset()
    {
        _timer.Stop();
        _state = PomodoroState.Stopped;
        _remainingTime = TimeSpan.FromMinutes(25);
        _totalDuration = TimeSpan.FromMinutes(25);
        StateChanged?.Invoke();
    }

    private void Timer_Tick(object? sender, EventArgs e)
    {
        if (_remainingTime.TotalSeconds > 1)
        {
            _remainingTime = _remainingTime.Subtract(TimeSpan.FromSeconds(1));
            Tick?.Invoke();
        }
        else
        {
            var finishedState = _state;
            _timer.Stop();
            _state = PomodoroState.Stopped;
            _remainingTime = TimeSpan.Zero;
            StateChanged?.Invoke();
            Completed?.Invoke(finishedState);
        }
    }
}

