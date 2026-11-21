using System.Text.Json;
using MochiDock.Core;
using MochiDock.Models;
using MochiDock.Services;

public class ImapAccountPolicyTests
{
    [Theory]
    [InlineData("me@gmail.com", "imap.gmail.com", 993, "")]
    [InlineData("me@example.org", "mail.example.org", 143, "")]
    [InlineData("no-at-sign", "imap.gmail.com", 993, "Enter the full email address.")]
    [InlineData("a@b@c.com", "imap.gmail.com", 993, "Enter the full email address.")]
    [InlineData("me@gmail.com", "", 993, "Enter the IMAP server name, for example imap.gmail.com.")]
    [InlineData("me@gmail.com", "https://imap.gmail.com", 993, "Enter the IMAP server name, for example imap.gmail.com.")]
    [InlineData("me@gmail.com", "imap.gmail.com", 0, "The port must be between 1 and 65535.")]
    public void ValidatesAccounts(string address, string host, int port, string expected) =>
        Assert.Equal(expected, ImapAccountPolicy.Validate(address, host, port));

    [Fact]
    public void OnlyMailNewerThanTheBaselineIsAnnouncedOldestFirstAndCapped()
    {
        var fresh = ImapAccountPolicy.NewUids(new uint[] { 5, 12, 9, 7, 20, 11, 10, 3 }, baseline: 8, max: 3);
        Assert.Equal(new uint[] { 11, 12, 20 }, fresh);
        Assert.Empty(ImapAccountPolicy.NewUids(new uint[] { 1, 2, 3 }, baseline: 3));
    }

    [Fact]
    public void GmailOpensTheRightAccountAndUnknownHostsOpenNothing()
    {
        string? gmail = ImapAccountPolicy.WebmailUrl(new ImapAccount { Address = "me+x@gmail.com", Host = "IMAP.gmail.com" });
        Assert.Equal("https://mail.google.com/mail/?authuser=me%2Bx%40gmail.com#inbox", gmail);
        Assert.DoesNotContain("/mail/u/", gmail);   // an escaped address in the path gives Gmail's "Temporary Error (404)"
        Assert.Null(ImapAccountPolicy.WebmailUrl(new ImapAccount { Address = "me@example.org", Host = "mail.example.org" }));
    }

    [Fact]
    public void PresetHelpLinksAreHttps() =>
        Assert.All(ImapAccountPolicy.Presets, p => { Assert.StartsWith("https://", p.HelpUrl); Assert.StartsWith("https://", p.WebmailUrl); Assert.Equal(993, p.Port); });
}

public class MailCredentialStoreTests : IDisposable
{
    sealed class PlainProtector : ISecretProtector
    {
        public byte[] Protect(byte[] plain) => plain.Select(b => (byte)(b ^ 0x5A)).ToArray();
        public byte[] Unprotect(byte[] sealedBytes) => sealedBytes.Select(b => (byte)(b ^ 0x5A)).ToArray();
    }

    readonly string _dir = Path.Combine(Path.GetTempPath(), "mochidock-mailcreds-" + Guid.NewGuid().ToString("N"));
    MailCredentialStore Store() => new(Path.Combine(_dir, "mail.dat"), new PlainProtector());
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    [Fact]
    public void SavesLoadsAndRemovesPerAddressIgnoringCase()
    {
        var store = Store();
        store.Save("Me@Gmail.com", "app pass one");
        store.Save("you@yahoo.com", "app pass two");
        Assert.Equal("app pass one", store.Load("me@gmail.com"));
        store.Remove("ME@gmail.com");
        Assert.Null(store.Load("me@gmail.com"));
        Assert.Equal("app pass two", store.Load("you@yahoo.com"));
        store.Remove("you@yahoo.com");
        Assert.False(File.Exists(Path.Combine(_dir, "mail.dat")));
    }

    [Fact]
    public void ThePasswordIsNotWrittenInTheClear()
    {
        Store().Save("me@gmail.com", "super-secret-app-password");
        string raw = System.Text.Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(_dir, "mail.dat")));
        Assert.DoesNotContain("super-secret-app-password", raw);
    }

    [Fact]
    public void SettingsKeepTheAddressButNeverAPassword()
    {
        var settings = new AppSettings();
        settings.ImapAccounts.Add(new ImapAccount { Address = "me@gmail.com", Host = "imap.gmail.com" });
        string json = JsonSerializer.Serialize(settings);
        Assert.Contains("me@gmail.com", json);
        Assert.DoesNotContain("assword", json);
        Assert.Empty(JsonSerializer.Deserialize<AppSettings>("{}")!.ImapAccounts);
    }
}

public class ImapMailServiceTests : IDisposable
{
    sealed class PlainProtector : ISecretProtector
    {
        public byte[] Protect(byte[] plain) => plain;
        public byte[] Unprotect(byte[] sealedBytes) => sealedBytes;
    }

    sealed class FakeFactory : IImapSessionFactory
    {
        public Queue<Func<IImapSession>> Connections { get; } = new();
        public int Attempts;
        public Task<IImapSession> ConnectAsync(ImapAccount account, string password, CancellationToken ct)
        {
            Attempts++;
            if (Connections.Count == 0) throw new IOException("no more connections");
            return Task.FromResult(Connections.Dequeue()());
        }
    }

    sealed class FakeSession : IImapSession
    {
        public uint Latest = 10;
        public Queue<IReadOnlyList<ImapMessage>> Batches { get; } = new();
        public Func<Task>? OnBatchesDone;
        public Task<uint> GetLatestUidAsync(CancellationToken ct) => Task.FromResult(Latest);
        public Task<IReadOnlyList<ImapMessage>> FetchAfterAsync(uint baseline, CancellationToken ct) =>
            Task.FromResult(Batches.Count > 0 ? Batches.Dequeue() : (IReadOnlyList<ImapMessage>)Array.Empty<ImapMessage>());
        public async Task WaitForChangeAsync(TimeSpan max, CancellationToken ct)
        {
            if (Batches.Count == 0 && OnBatchesDone != null) { await OnBatchesDone(); OnBatchesDone = null; }
            await Task.Yield();
            ct.ThrowIfCancellationRequested();
        }
        public Task MarkSeenAsync(uint uid, CancellationToken ct) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    readonly string _dir = Path.Combine(Path.GetTempPath(), "mochidock-imap-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    static ImapAccount Account => new() { Address = "me@gmail.com", Host = "imap.gmail.com", Port = 993 };

    (ImapMailService Service, FakeFactory Factory) Build()
    {
        var creds = new MailCredentialStore(Path.Combine(_dir, "mail.dat"), new PlainProtector());
        creds.Save("me@gmail.com", "app-password");
        var factory = new FakeFactory();
        var service = new ImapMailService(factory, creds) { WaitInterval = TimeSpan.FromMilliseconds(1), Delay = (t, ct) => Task.Delay(1, ct) };
        service.Configure(new[] { Account });
        return (service, factory);
    }

    static ImapMessage Msg(uint uid, string subject = "Hello", string preview = "Body text") => new(uid, "Alice", subject, DateTime.Now, preview);

    [Fact]
    public async Task AnnouncesOnlyMailThatArrivedAfterConnecting()
    {
        var (service, factory) = Build();
        var session = new FakeSession { Latest = 10 };
        session.Batches.Enqueue(new[] { Msg(11, "Your code", "Use 482913 to sign in") });
        var received = new TaskCompletionSource<EmailNotification>();
        service.EmailReceived += n => received.TrySetResult(n);
        factory.Connections.Enqueue(() => session);

        service.Start();
        var mail = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        service.Stop();

        Assert.Equal("Alice", mail.Sender);
        Assert.Equal("Your code", mail.Subject);
        Assert.Equal("me@gmail.com|11", mail.MessageId);
        Assert.Equal("Gmail", mail.FolderName);
        Assert.Equal("Webmail", mail.ClientSource);
        Assert.Equal("482913", mail.ExtractedVerificationCode);
    }

    [Fact]
    public async Task AMessageIsNeverAnnouncedTwice()
    {
        var (service, factory) = Build();
        var session = new FakeSession { Latest = 10 };
        session.Batches.Enqueue(new[] { Msg(11) });
        session.Batches.Enqueue(new[] { Msg(11), Msg(12, "Second") });
        var got = new List<string>();
        var both = new TaskCompletionSource();
        service.EmailReceived += n => { lock (got) { got.Add(n.MessageId); if (got.Count == 2) both.TrySetResult(); } };
        factory.Connections.Enqueue(() => session);

        service.Start();
        await both.Task.WaitAsync(TimeSpan.FromSeconds(5));
        service.Stop();

        Assert.Equal(new[] { "me@gmail.com|11", "me@gmail.com|12" }, got);
    }

    [Fact]
    public async Task ARejectedLoginIsReportedAndNotRetried()
    {
        var (service, factory) = Build();
        factory.Connections.Enqueue(() => throw new ImapAuthException("no"));
        service.Start();
        await service.WhenLoopsEnded().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("Login rejected.", service.StatusOf("me@gmail.com"));
        Assert.Equal(1, factory.Attempts);
    }

    [Fact]
    public async Task ANetworkFailureReconnectsAndKeepsWorking()
    {
        var (service, factory) = Build();
        var session = new FakeSession { Latest = 5 };
        session.Batches.Enqueue(new[] { Msg(6, "After reconnect") });
        factory.Connections.Enqueue(() => throw new IOException("down"));
        factory.Connections.Enqueue(() => session);
        var received = new TaskCompletionSource<EmailNotification>();
        service.EmailReceived += n => received.TrySetResult(n);

        service.Start();
        var mail = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        service.Stop();

        Assert.Equal("After reconnect", mail.Subject);
        Assert.True(factory.Attempts >= 2);
    }

    [Fact]
    public async Task NoSavedPasswordMeansNoConnectionAttempt()
    {
        var creds = new MailCredentialStore(Path.Combine(_dir, "empty.dat"), new PlainProtector());
        var factory = new FakeFactory();
        var service = new ImapMailService(factory, creds);
        service.Configure(new[] { Account });
        service.Start();
        await service.WhenLoopsEnded().WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(0, factory.Attempts);
        Assert.Equal("No password saved.", service.StatusOf("me@gmail.com"));
    }

    [Fact]
    public async Task AdvertisementsAreSkippedWhenFiltering()
    {
        var (service, factory) = Build();
        service.FilterAds = true;
        var session = new FakeSession { Latest = 1 };
        session.Batches.Enqueue(new[] { Msg(2, "Weekly newsletter", "Click to unsubscribe"), Msg(3, "Real mail") });
        var received = new TaskCompletionSource<EmailNotification>();
        service.EmailReceived += n => received.TrySetResult(n);
        factory.Connections.Enqueue(() => session);

        service.Start();
        var mail = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        service.Stop();

        Assert.Equal("Real mail", mail.Subject);
    }

    [Fact]
    public async Task TestAsyncExplainsAWrongPasswordAndAnUnreachableServer()
    {
        var (service, factory) = Build();
        factory.Connections.Enqueue(() => throw new ImapAuthException("no"));
        factory.Connections.Enqueue(() => throw new IOException("down"));
        Assert.StartsWith("Login rejected", await service.TestAsync(Account, "x", CancellationToken.None));
        Assert.StartsWith("Could not reach", await service.TestAsync(Account, "x", CancellationToken.None));
        factory.Connections.Enqueue(() => new FakeSession());
        Assert.Equal("", await service.TestAsync(Account, "x", CancellationToken.None));
    }
}
