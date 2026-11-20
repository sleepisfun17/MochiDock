using MochiDock.Models;

namespace MochiDock.Services;

public interface IMailService
{
    string ServiceName { get; }
    bool IsAvailable { get; }
    event Action<EmailNotification>? EmailReceived;
    void Start();
    void Stop();
    void OpenEmail(EmailNotification email);
    void MarkAsRead(EmailNotification email);
    /// <summary>Whether <see cref="MarkAsRead"/> really marks this email read at its source; the email card hides Mark Read otherwise.</summary>
    bool CanMarkAsRead(EmailNotification email);
}
