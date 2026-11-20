using MochiDock.Models;

namespace MochiDock.Services;

public class CompositeMailService : IMailService
{
    public string ServiceName => "CompositeMail";
    public bool IsAvailable => _services.Any(s => s.IsAvailable);

    public event Action<EmailNotification>? EmailReceived;

    private readonly List<IMailService> _services = new();

    public CompositeMailService(params IMailService[] services)
    {
        foreach (var s in services)
        {
            _services.Add(s);
            s.EmailReceived += (email) => EmailReceived?.Invoke(email);
        }
    }

    public T? Find<T>() where T : class => _services.OfType<T>().FirstOrDefault();

    public void Start()
    {
        foreach (var s in _services)
        {
            try { s.Start(); } catch { }
        }
    }

    public void Stop()
    {
        foreach (var s in _services)
        {
            try { s.Stop(); } catch { }
        }
    }

    public void OpenEmail(EmailNotification email)
    {
        var target = _services.FirstOrDefault(s => s.ServiceName.Equals(email.ClientSource, StringComparison.OrdinalIgnoreCase))
                     ?? _services.FirstOrDefault(s => s.IsAvailable);

        target?.OpenEmail(email);
    }

    public void MarkAsRead(EmailNotification email) => Source(email)?.MarkAsRead(email);

    public bool CanMarkAsRead(EmailNotification email) => Source(email)?.CanMarkAsRead(email) == true;

    private IMailService? Source(EmailNotification email) =>
        _services.FirstOrDefault(s => s.ServiceName.Equals(email.ClientSource, StringComparison.OrdinalIgnoreCase))
        ?? _services.FirstOrDefault(s => s.IsAvailable);
}
