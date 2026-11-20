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
}
