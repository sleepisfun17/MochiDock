using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MochiDock.Models;

public class TodoTaskItem : INotifyPropertyChanged
{
    private string _id = Guid.NewGuid().ToString("N");
    private string _title = "";
    private DateTime? _dueTime;
    private bool _isCompleted;
    private DateTime _createdAt = DateTime.Now;

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    public string Id
    {
        get => _id;
        set { if (_id != value) { _id = value; OnPropertyChanged(); } }
    }

    public string Title
    {
        get => _title;
        set { if (_title != value) { _title = value; OnPropertyChanged(); } }
    }

    public DateTime? DueTime
    {
        get => _dueTime;
        set
        {
            if (_dueTime != value)
            {
                _dueTime = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FormattedDueTime));
                OnPropertyChanged(nameof(IsDueSoon));
                OnPropertyChanged(nameof(CountdownText));
            }
        }
    }

    public bool IsCompleted
    {
        get => _isCompleted;
        set
        {
            if (_isCompleted != value)
            {
                _isCompleted = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsDueSoon));
                OnPropertyChanged(nameof(CountdownText));
            }
        }
    }

    public DateTime CreatedAt
    {
        get => _createdAt;
        set { if (_createdAt != value) { _createdAt = value; OnPropertyChanged(); } }
    }

    public string FormattedDueTime => DueTime.HasValue ? DueTime.Value.ToString("HH:mm") : "";
    public bool IsDueSoon => DueTime.HasValue && !IsCompleted && (DueTime.Value - DateTime.Now).TotalMinutes <= 15 && (DueTime.Value - DateTime.Now).TotalSeconds > -60;

    public string CountdownText => MochiDock.Core.AgendaLogic.TaskStatus(DueTime, IsCompleted, DateTime.Now);

    /// <summary>True when the task is open and more than a minute past its due time.</summary>
    public bool IsOverdue => MochiDock.Core.AgendaLogic.IsOverdue(DueTime, IsCompleted, DateTime.Now);

    public void RefreshTimeState()
    {
        OnPropertyChanged(nameof(IsOverdue));
        OnPropertyChanged(nameof(IsDueSoon));
        OnPropertyChanged(nameof(CountdownText));
    }
}
