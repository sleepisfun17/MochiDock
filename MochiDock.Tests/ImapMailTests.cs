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
    public void RemovingAnAccountPointsToWhereItsAppPasswordIsRevoked()
    {
        var gmail = new ImapAccount { Address = " me@gmail.com ", Host = "imap.gmail.com" };
        Assert.Equal("https://myaccount.google.com/apppasswords", ImapAccountPolicy.RevokeUrl(gmail));
        Assert.Equal("me@gmail.com was removed from this PC. Its app password still works for your Gmail account until you revoke it there.",
            ImapAccountPolicy.RemovedNotice(gmail));

        var custom = new ImapAccount { Address = "me@example.org", Host = "mail.example.org" };
        Assert.Null(ImapAccountPolicy.RevokeUrl(custom));
        Assert.Contains("until you revoke it with your provider", ImapAccountPolicy.RemovedNotice(custom));
        Assert.All(ImapAccountPolicy.Presets, p => Assert.Equal(p.HelpUrl, ImapAccountPolicy.RevokeUrl(new ImapAccount { Address = "a@b.c", Host = p.Host })));
    }

    [Fact]
    public void UninstallAlwaysDeletesSavedSecrets()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "installer", "MochiDock.iss"))) dir = dir.Parent;
        string iss = File.ReadAllText(Path.Combine(dir!.FullName, "installer", "MochiDock.iss"));
        int question = iss.IndexOf("Also delete your MochiDock settings and data?", StringComparison.Ordinal);
        Assert.True(question > 0);
        // Each secret file is deleted unconditionally, before (so not inside) the "keep settings?" question.
        foreach (string file in new[] { "mail-imap.dat", "gowa-remote.dat", "gcal.dat" })
        {
            int delete = iss.IndexOf($"DeleteFile(ExpandConstant('{{localappdata}}\\MochiDock\\{file}'));", StringComparison.Ordinal);
            Assert.True(delete > 0 && delete < question, file);
        }
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

    [Fact]
    public void SaveThrowsAndPreservesExistingEntriesWhenStoreIsLocked()
    {
        string path = Path.Combine(_dir, "mail.dat");
        var store = new MailCredentialStore(path, new PlainProtector());
        store.Save("first@gmail.com", "pass-one");

        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.ThrowsAny<IOException>(() => store.Save("second@gmail.com", "pass-two"));
        }

        Assert.Equal("pass-one", store.Load("first@gmail.com"));
        Assert.Null(store.Load("second@gmail.com"));
    }

    [Fact]
    public void SaveThrowsAndDoesNotOverwriteCorruptStore()
    {
        string path = Path.Combine(_dir, "mail.dat");
        Directory.CreateDirectory(_dir);
        byte[] garbage = [1, 2, 3, 4];
        File.WriteAllBytes(path, garbage);
        var store = new MailCredentialStore(path, new PlainProtector());

        Assert.ThrowsAny<JsonException>(() => store.Save("second@gmail.com", "pass-two"));
        Assert.Equal(garbage, File.ReadAllBytes(path));
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
        public uint UidValidity { get; set; } = 1;
        public uint Latest = 10;
        public Queue<IReadOnlyList<ImapMessage>> Batches { get; } = new();
        public List<uint> BaselinesRequested { get; } = new();
        public Func<Task>? OnBatchesDone;
        public Task<uint> GetLatestUidAsync(CancellationToken ct) => Task.FromResult(Latest);
        public Task<IReadOnlyList<ImapMessage>> FetchAfterAsync(uint baseline, CancellationToken ct)
        {
            BaselinesRequested.Add(baseline);
            return Task.FromResult(Batches.Count > 0 ? Batches.Dequeue() : (IReadOnlyList<ImapMessage>)Array.Empty<ImapMessage>());
        }
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
    public async Task ReconnectRetainsLastUidAndAnnouncesMailReceivedWhileOffline()
    {
        var (service, factory) = Build();
        var s1 = new FakeSession { UidValidity = 42, Latest = 10 };
        s1.Batches.Enqueue(new[] { Msg(11, "Before drop") });
        s1.OnBatchesDone = () => throw new IOException("connection lost");

        var s2 = new FakeSession { UidValidity = 42, Latest = 15 };
        s2.Batches.Enqueue(new[] { Msg(12, "While offline"), Msg(13, "Also offline") });

        factory.Connections.Enqueue(() => s1);
        factory.Connections.Enqueue(() => s2);

        var subjects = new List<string>();
        var allDone = new TaskCompletionSource();
        service.EmailReceived += n => { lock (subjects) { subjects.Add(n.Subject); if (subjects.Count == 3) allDone.TrySetResult(); } };

        service.Start();
        await allDone.Task.WaitAsync(TimeSpan.FromSeconds(5));
        service.Stop();

        Assert.Equal(new[] { "Before drop", "While offline", "Also offline" }, subjects);
        Assert.Contains(11u, s2.BaselinesRequested);
    }

    [Fact]
    public async Task ConfigureRestartsCleanlyWithoutDuplicateLoops()
    {
        var (service, factory) = Build();
        var s1 = new FakeSession { Latest = 1 };
        var s2 = new FakeSession { Latest = 2 };
        s2.Batches.Enqueue(new[] { Msg(3, "After configure") });
        factory.Connections.Enqueue(() => s1);
        factory.Connections.Enqueue(() => s2);

        var received = new TaskCompletionSource<EmailNotification>();
        service.EmailReceived += n => received.TrySetResult(n);

        service.Start();
        await Task.Delay(30);
        service.Configure(new[] { Account });
        var mail = await received.Task.WaitAsync(TimeSpan.FromSeconds(5));
        service.Stop();
        await service.WhenLoopsEnded().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal("After configure", mail.Subject);
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

    [Fact]
    public void PickNewestUnseenFindsNewArrivalsBeyondThirtyUnreadItems()
    {
        var baseTime = new DateTime(2026, 11, 22, 10, 0, 0);
        var items = Enumerable.Range(1, 35)
            .Select(i => (Id: $"id-{i}", Received: baseTime.AddMinutes(i)))
            .ToList();
        var seen = Enumerable.Range(1, 34).Select(i => $"id-{i}").ToHashSet();

        var picked = OutlookComReader.PickNewestUnseen(items, x => x.Received, x => x.Id, seen);
        Assert.Single(picked);
        Assert.Equal("id-35", picked[0].Id);
    }
}
