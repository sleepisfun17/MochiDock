using System.Diagnostics;
using MochiDock.Core;
using MochiDock.Helpers;
using MochiDock.Models;

namespace MochiDock.Services;

/// <summary>One new message as read from the server: envelope and a short preview only.</summary>
public sealed record ImapMessage(uint Uid, string From, string Subject, DateTime Received, string Preview);

/// <summary>The server refused the user name or password. Not retried: retrying could lock the account.</summary>
public sealed class ImapAuthException : Exception
{
    public ImapAuthException(string message, Exception? inner = null) : base(message, inner) { }
}

/// <summary>An open, read-only view of one INBOX. Only <see cref="MarkSeenAsync"/> ever writes, and only when the user allowed it.</summary>
public interface IImapSession : IAsyncDisposable
{
    Task<uint> GetLatestUidAsync(CancellationToken ct);
    Task<IReadOnlyList<ImapMessage>> FetchAfterAsync(uint baseline, CancellationToken ct);
    /// <summary>Returns when the mailbox changed or <paramref name="max"/> passed, whichever is first.</summary>
    Task WaitForChangeAsync(TimeSpan max, CancellationToken ct);
    Task MarkSeenAsync(uint uid, CancellationToken ct);
}

public interface IImapSessionFactory
{
    Task<IImapSession> ConnectAsync(ImapAccount account, string password, CancellationToken ct);
}

/// <summary>
/// Notifies about new mail in any IMAP mailbox (Gmail, Yahoo, iCloud, ...), signed in with an app password kept in
/// <see cref="MailCredentialStore"/>. Only mail that arrives after MochiDock connected is announced. One cancellable loop per account.
/// </summary>
public sealed class ImapMailService : IMailService
{
    private readonly IImapSessionFactory _factory;
    private readonly MailCredentialStore _credentials;
    private readonly object _gate = new();
    private readonly Dictionary<string, string> _status = new(StringComparer.OrdinalIgnoreCase);
    private List<ImapAccount> _accounts = new();
    private CancellationTokenSource? _cts;
    private readonly List<Task> _loops = new();

    public ImapMailService(IImapSessionFactory factory, MailCredentialStore credentials)
    {
        _factory = factory;
        _credentials = credentials;
    }

    public string ServiceName => "Webmail";
    public bool IsAvailable { get { lock (_gate) return _accounts.Count > 0; } }
    public bool FilterAds { get; set; }
    public TimeSpan WaitInterval { get; set; } = TimeSpan.FromMinutes(5);
    public Func<TimeSpan, CancellationToken, Task> Delay { get; set; } = (t, ct) => Task.Delay(t, ct);

    public event Action<EmailNotification>? EmailReceived;
    public event Action? StatusChanged;

    /// <summary>Completes when every account loop has ended (after <see cref="Stop"/>, a rejected login or a missing password). For tests.</summary>
    public Task WhenLoopsEnded() { Task[] all; lock (_gate) all = _loops.ToArray(); return Task.WhenAll(all); }

    public string StatusOf(string address)
    {
        lock (_gate) return _status.TryGetValue(address.Trim(), out var s) ? s : "";
    }

    private void SetStatus(string address, string status)
    {
        lock (_gate) _status[address] = status;
        StatusChanged?.Invoke();
    }

    /// <summary>Replaces the account list; a running service restarts so removed accounts stop and new ones start.</summary>
    public void Configure(IEnumerable<ImapAccount> accounts)
    {
        bool running;
        lock (_gate)
        {
            _accounts = accounts.Select(a => new ImapAccount { Address = a.Address.Trim(), Host = a.Host.Trim(), Port = a.Port, MarkAsRead = a.MarkAsRead }).ToList();
            running = _cts != null;
        }
        if (running) { Stop(); Start(); }
    }

    public void Start()
    {
        lock (_gate)
        {
            if (_cts != null) return;
            _cts = new CancellationTokenSource();
            _status.Clear();
            _loops.Clear();
            foreach (var account in _accounts)
            {
                var token = _cts.Token;
                var a = account;
                _loops.Add(Task.Run(() => RunAsync(a, token)));
            }
        }
    }

    public void Stop()
    {
        CancellationTokenSource? cts;
        lock (_gate) { cts = _cts; _cts = null; }
        try { cts?.Cancel(); } catch (Exception ex) { AppLog.Warn("Mail", ex); }
    }

    /// <summary>Test connection for the settings page: "" when the sign-in worked, otherwise a short reason.</summary>
    public async Task<string> TestAsync(ImapAccount account, string password, CancellationToken ct)
    {
        try
        {
            await using var session = await _factory.ConnectAsync(account, password, ct).ConfigureAwait(false);
            await session.GetLatestUidAsync(ct).ConfigureAwait(false);
            return "";
        }
        catch (ImapAuthException) { return "Login rejected. Use an app password, not your normal password."; }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { AppLog.Warn("Mail", ex); return "Could not reach the server. Check the server name and your connection."; }
    }

    private async Task RunAsync(ImapAccount account, CancellationToken ct)
    {
        int failures = 0;
        while (!ct.IsCancellationRequested)
        {
            try
            {
                string? password = _credentials.Load(account.Address);
                if (string.IsNullOrEmpty(password)) { SetStatus(account.Address, "No password saved."); return; }

                await using var session = await _factory.ConnectAsync(account, password, ct).ConfigureAwait(false);
                uint last = await session.GetLatestUidAsync(ct).ConfigureAwait(false);
                SetStatus(account.Address, "Connected");
                failures = 0;

                while (!ct.IsCancellationRequested)
                {
                    await session.WaitForChangeAsync(WaitInterval, ct).ConfigureAwait(false);
                    var fresh = await session.FetchAfterAsync(last, ct).ConfigureAwait(false);
                    foreach (var message in fresh.OrderBy(m => m.Uid))
                    {
                        if (message.Uid <= last) continue;
                        last = message.Uid;
                        Announce(account, message);
                    }
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
            catch (ImapAuthException)
            {
                SetStatus(account.Address, "Login rejected.");
                return;
            }
            catch (Exception ex)
            {
                AppLog.Warn("Mail", ex);
                failures++;
                SetStatus(account.Address, "Cannot reach the server. Retrying.");
            }

            try { await Delay(TimeSpan.FromSeconds(Math.Min(60, 5 * Math.Max(1, failures))), ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    private void Announce(ImapAccount account, ImapMessage message)
    {
        if (FilterAds && MailAdFilter.IsAdvertisement(message.From, message.Subject, message.Preview))
        {
            AppLog.Warn("Mail", "Skipped as advertisement.");
            return;
        }
        string subject = string.IsNullOrWhiteSpace(message.Subject) ? "(No Subject)" : message.Subject;
        EmailReceived?.Invoke(new EmailNotification
        {
            MessageId = $"{account.Address}|{message.Uid}",
            Sender = message.From,
            Subject = subject,
            BodyPreview = CleanPreview(message.Preview),
            FolderName = ImapAccountPolicy.PresetFor(account.Host)?.Name ?? account.Address,
            AccountEmail = account.Address,
            ClientSource = ServiceName,
            ReceivedTime = message.Received,
            ExtractedVerificationCode = CodeDetectionHelper.ExtractVerificationCode(subject, message.Preview)
        });
    }

    private static string CleanPreview(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "(No content)";
        var clean = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();
        return clean.Length > 90 ? clean[..90] + "..." : clean;
    }

    private ImapAccount? Find(string address)
    {
        lock (_gate) return _accounts.FirstOrDefault(a => ImapAccountPolicy.SameAccount(a, address));
    }

    public void OpenEmail(EmailNotification email)
    {
        var account = Find(email.AccountEmail);
        string? url = account == null ? null : ImapAccountPolicy.WebmailUrl(account);
        if (url == null) return;
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch (Exception ex) { AppLog.Warn("Mail", ex); }
    }

    // Only accounts with "Mark as read on the server" turned on; the others are read only.
    public bool CanMarkAsRead(EmailNotification email) => Find(email.AccountEmail) is { MarkAsRead: true };

    public void MarkAsRead(EmailNotification email)
    {
        var account = Find(email.AccountEmail);
        if (account is not { MarkAsRead: true }) return;
        int split = email.MessageId.LastIndexOf('|');
        if (split < 0 || !uint.TryParse(email.MessageId[(split + 1)..], out uint uid)) return;
        string? password = _credentials.Load(account.Address);
        if (string.IsNullOrEmpty(password)) return;
        _ = Task.Run(async () =>
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                await using var session = await _factory.ConnectAsync(account, password, cts.Token).ConfigureAwait(false);
                await session.MarkSeenAsync(uid, cts.Token).ConfigureAwait(false);
            }
            catch (Exception ex) { AppLog.Warn("Mail", ex); }
        });
    }
}
