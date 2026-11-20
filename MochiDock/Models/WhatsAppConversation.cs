using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace MochiDock.Models;

public class WhatsAppConversation : INotifyPropertyChanged
{
    private string _chatJid = "";
    private string _title = "";
    private bool _isGroup;
    private int _unreadCount;
    private WhatsAppNotification? _latestMessage;

    public string ChatJid
    {
        get => _chatJid;
        set { _chatJid = value; OnPropertyChanged(); }
    }

    /// <summary>The WhatsApp account (GOWA device) this thread belongs to; the same contact on two accounts has two threads.</summary>
    public string DeviceId { get; set; } = "";

    public string Title
    {
        get => _title;
        set { _title = value; OnPropertyChanged(); }
    }

    public bool IsGroup
    {
        get => _isGroup;
        set { _isGroup = value; OnPropertyChanged(); }
    }

    public int UnreadCount
    {
        get => _unreadCount;
        set
        {
            _unreadCount = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CounterBadgeText));
            OnPropertyChanged(nameof(TabBadgeText));
        }
    }

    public string CounterBadgeText => _unreadCount > 10 ? "10+ msgs" : $"{_unreadCount} msg{(_unreadCount == 1 ? "" : "s")}";
    public string TabBadgeText => _unreadCount > 10 ? "10+" : $"{_unreadCount}";

    public WhatsAppNotification? LatestMessage
    {
        get => _latestMessage;
        set { _latestMessage = value; OnPropertyChanged(); }
    }

    public ObservableCollection<WhatsAppNotification> Messages { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

