using System;
using System.ComponentModel;

namespace MochiDock.Models;

public class ClipboardHistoryItem : INotifyPropertyChanged
{
    private bool _isPinned;

    public event PropertyChangedEventHandler? PropertyChanged;

    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Text { get; set; } = "";
    public DateTime CopiedAt { get; set; } = DateTime.Now;
    public bool IsPinned
    {
        get => _isPinned;
        set
        {
            if (_isPinned == value) return;
            _isPinned = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPinned)));
        }
    }

    public string PreviewText => Text.Length > 80 ? Text.Substring(0, 80).Replace("\r", " ").Replace("\n", " ") + "..." : Text.Replace("\r", " ").Replace("\n", " ");
    public string FormattedTime => CopiedAt.ToString("HH:mm");
}
