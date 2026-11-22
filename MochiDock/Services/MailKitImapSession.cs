using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MailKit.Security;
using MochiDock.Core;

namespace MochiDock.Services;

/// <summary>Real IMAP over TLS with MailKit. Certificates are validated; there is no plaintext fallback.</summary>
public sealed class MailKitImapSessionFactory : IImapSessionFactory
{
    public async Task<IImapSession> ConnectAsync(ImapAccount account, string password, CancellationToken ct)
    {
        var client = new ImapClient { Timeout = 30000 };
        try
        {
            await client.ConnectAsync(account.Host, account.Port,
                account.Port == 993 ? SecureSocketOptions.SslOnConnect : SecureSocketOptions.StartTls, ct).ConfigureAwait(false);
            client.AuthenticationMechanisms.Remove("XOAUTH2");
            await client.AuthenticateAsync(account.Address, password, ct).ConfigureAwait(false);
            // MarkSeenAsync needs write access; every other call only reads. EXAMINE keeps the mailbox untouched.
            await client.Inbox.OpenAsync(account.MarkAsRead ? FolderAccess.ReadWrite : FolderAccess.ReadOnly, ct).ConfigureAwait(false);
            return new Session(client);
        }
        catch (AuthenticationException ex)
        {
            client.Dispose();
            throw new ImapAuthException("The server rejected the login.", ex);
        }
        catch
        {
            client.Dispose();
            throw;
        }
    }

    private sealed class Session : IImapSession
    {
        private readonly ImapClient _client;
        public Session(ImapClient client) => _client = client;

        public uint UidValidity => _client.Inbox.UidValidity;

        public async Task<uint> GetLatestUidAsync(CancellationToken ct)
        {
            if (_client.Inbox.UidNext is { Id: > 0 } next)
                return next.Id - 1;
            if (_client.Inbox.Count == 0)
                return 0;
            int lastIndex = _client.Inbox.Count - 1;
            var items = await _client.Inbox.FetchAsync(lastIndex, lastIndex, MessageSummaryItems.UniqueId, ct).ConfigureAwait(false);
            return items.FirstOrDefault()?.UniqueId.Id ?? 0;
        }

        public async Task<IReadOnlyList<ImapMessage>> FetchAfterAsync(uint baseline, CancellationToken ct)
        {
            var range = new UniqueIdRange(new UniqueId(baseline + 1), UniqueId.MaxValue);
            var items = await _client.Inbox.FetchAsync(range,
                MessageSummaryItems.UniqueId | MessageSummaryItems.Envelope | MessageSummaryItems.InternalDate | MessageSummaryItems.PreviewText, ct).ConfigureAwait(false);
            // "N:*" always includes the newest message even when it is older than N, so filter again.
            var byUid = items.Where(i => i.UniqueId.Id > baseline).ToDictionary(i => i.UniqueId.Id);
            return ImapAccountPolicy.NewUids(byUid.Keys, baseline).Select(uid =>
            {
                var s = byUid[uid];
                var from = s.Envelope?.From?.Mailboxes.FirstOrDefault();
                string sender = !string.IsNullOrWhiteSpace(from?.Name) ? from!.Name : from?.Address ?? "Unknown sender";
                return new ImapMessage(uid, sender, s.Envelope?.Subject ?? "", (s.InternalDate ?? DateTimeOffset.Now).LocalDateTime, s.PreviewText ?? "");
            }).ToList();
        }

        public async Task WaitForChangeAsync(TimeSpan max, CancellationToken ct)
        {
            if (_client.Capabilities.Contains("IDLE"))
            {
                using var done = CancellationTokenSource.CreateLinkedTokenSource(ct);
                done.CancelAfter(max);
                void OnChanged(object? s, EventArgs e) { try { done.Cancel(); } catch (ObjectDisposedException) { } }
                _client.Inbox.CountChanged += OnChanged;
                try { await _client.IdleAsync(done.Token, ct).ConfigureAwait(false); }
                finally { _client.Inbox.CountChanged -= OnChanged; }
            }
            else
            {
                await Task.Delay(TimeSpan.FromSeconds(Math.Min(60, max.TotalSeconds)), ct).ConfigureAwait(false);
                await _client.NoOpAsync(ct).ConfigureAwait(false);
            }
        }

        public Task MarkSeenAsync(uint uid, CancellationToken ct) =>
            _client.Inbox.AddFlagsAsync(new UniqueId(uid), MessageFlags.Seen, true, ct);

        public async ValueTask DisposeAsync()
        {
            try
            {
                if (_client.IsConnected)
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                    await _client.DisconnectAsync(true, cts.Token).ConfigureAwait(false);
                }
            }
            catch { }
            _client.Dispose();
        }
    }
}
