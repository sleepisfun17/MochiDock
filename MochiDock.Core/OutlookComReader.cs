using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace MochiDock.Core;

/// <summary>
/// Reads new mail from classic Outlook through COM. It must run at the same elevation as Outlook, so MochiDock
/// (which runs as Administrator) hosts it inside a non-elevated helper process; a non-elevated MochiDock can run it directly.
/// All COM objects are created, polled, and released on a single dedicated STA thread.
/// </summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class OutlookComReader
{
    [DllImport("ole32.dll", PreserveSig = false)]
    static extern void CLSIDFromProgID([MarshalAs(UnmanagedType.LPWStr)] string progId, out Guid clsid);

    [DllImport("oleaut32.dll", PreserveSig = false)]
    static extern void GetActiveObject(ref Guid rclsid, IntPtr pvReserved, [MarshalAs(UnmanagedType.IUnknown)] out object ppunk);

    const int MaxReadAttempts = 3;
    const int MaxBodyChars = 4000;
    internal const int MaxUnreadPerFolder = 30;

    readonly Action<string> _log;
    readonly HashSet<string> _seenIds = new();
    readonly Dictionary<string, int> _failedAttempts = new();
    readonly ConcurrentQueue<Action> _staCommands = new();
    readonly AutoResetEvent _staWake = new(false);
    readonly object _lifecycleGate = new();
    CancellationTokenSource? _cts;
    Thread? _staThread;
    dynamic? _cachedOutlookApp;
    dynamic? _cachedMapiNamespace;
    readonly List<dynamic> _cachedInboxes = new();
    readonly HashSet<string> _folderIds = new();
    bool _justConnected;
    DateTime _watchSince = DateTime.MinValue;
    volatile IReadOnlyList<string> _trackedFolderNames = OutlookFolderPolicy.DefaultTrackedFolders;
    volatile bool _onlyListed;
    readonly List<string> _watchedPaths = new();
    string? _lastDiagnostic;

    public OutlookComReader(Action<string> log) => _log = log;

    public event Action<OutlookMailDto>? MailArrived;
    public int PollIntervalSeconds { get; set; } = 4;

    /// <summary>Folder names (besides every Inbox) to watch together with all their subfolders.</summary>
    public IReadOnlyList<string> TrackedFolderNames => _trackedFolderNames;

    /// <summary>Changes the tracked names; the folder list is rebuilt on the next poll.</summary>
    public void SetTrackedFolders(IEnumerable<string> names, bool onlyListed = false)
    {
        _trackedFolderNames = OutlookFolderPolicy.Split(OutlookFolderPolicy.Join(names));
        _onlyListed = onlyListed;
        EnqueueSta(ResetComCache);
    }

    public static bool IsInstalled => Type.GetTypeFromProgID("Outlook.Application") != null;

    public void Start()
    {
        Stop();
        lock (_lifecycleGate)
        {
            _cts = new CancellationTokenSource();
            var token = _cts.Token;
            _staThread = new Thread(() => StaLoop(token))
            {
                IsBackground = true,
                Name = "MochiDock_OutlookCom"
            };
            _staThread.SetApartmentState(ApartmentState.STA);
            _staThread.Start();
        }
    }

    public void Stop()
    {
        CancellationTokenSource? cts;
        Thread? thread;
        lock (_lifecycleGate)
        {
            cts = _cts;
            _cts = null;
            thread = _staThread;
            _staThread = null;
        }
        if (cts != null)
        {
            try { cts.Cancel(); } catch { }
            _staWake.Set();
            if (thread != null && thread != Thread.CurrentThread)
                thread.Join(2000);
            cts.Dispose();
        }
        while (_staCommands.TryDequeue(out _)) { }
    }

    void EnqueueSta(Action work)
    {
        _staCommands.Enqueue(work);
        _staWake.Set();
    }

    static void ReleaseCom(object? obj)
    {
        if (obj != null && Marshal.IsComObject(obj))
        {
            try { Marshal.ReleaseComObject(obj); } catch { }
        }
    }

    void LogStateChange(string message)
    {
        if (message == _lastDiagnostic) return;
        _lastDiagnostic = message;
        _log(message);
    }

    void ResetComCache()
    {
        foreach (var inbox in _cachedInboxes) ReleaseCom(inbox);
        _cachedInboxes.Clear();
        ReleaseCom(_cachedMapiNamespace);
        _cachedMapiNamespace = null;
        ReleaseCom(_cachedOutlookApp);
        _cachedOutlookApp = null;
    }

    bool EnsureConnected()
    {
        if (Process.GetProcessesByName("OUTLOOK").Length == 0)
        {
            ResetComCache();
            LogStateChange("Outlook is not running (classic Outlook.exe not found; the new Outlook has no COM support).");
            return false;
        }

        if (_cachedOutlookApp != null && _cachedInboxes.Count > 0) return true;

        try
        {
            dynamic? app = null;
            try
            {
                CLSIDFromProgID("Outlook.Application", out var clsid);
                GetActiveObject(ref clsid, IntPtr.Zero, out var unk);
                app = unk;
            }
            catch (Exception ex)
            {
                LogStateChange($"Could not attach to the running Outlook ({ex.Message}). If MochiDock and Outlook run at different privilege levels, Windows blocks the COM connection.");
            }

            if (app == null)
            {
                var type = Type.GetTypeFromProgID("Outlook.Application");
                if (type != null) app = Activator.CreateInstance(type);
            }

            if (app == null) { LogStateChange("Outlook.Application could not be created."); return false; }

            dynamic ns = app.GetNamespace("MAPI");
            _cachedInboxes.Clear();
            _folderIds.Clear();
            _watchedPaths.Clear();
            var tracked = TrackedFolderNames;
            bool onlyListed = _onlyListed;

            try
            {
                dynamic stores = ns.Stores;
                int storeCount = stores.Count;
                for (int i = 1; i <= storeCount; i++)
                {
                    try
                    {
                        dynamic store = stores.Item(i);
                        var excluded = new HashSet<string>();
                        foreach (int kind in new[] { 3, 4, 5, 16, 23 }) // Deleted Items, Outbox, Sent Items, Drafts, Junk
                        {
                            try { excluded.Add((string)store.GetDefaultFolder(kind).EntryID); } catch { /* this store has no such folder */ }
                        }
                        string? inboxId = null;
                        try { inboxId = (string)store.GetDefaultFolder(6).EntryID; } catch { /* not a mail store */ }
                        ScanFolder(store.GetRootFolder(), false, true, excluded, inboxId, tracked, onlyListed);
                    }
                    catch (Exception ex) { _log("Store scan failed: " + ex.Message); }
                }
            }
            catch (Exception ex) { _log("Scanning stores failed: " + ex.Message); }

            // In "only listed" scope the default Inbox must not come back as a fallback.
            if (_cachedInboxes.Count == 0 && !onlyListed)
            {
                try
                {
                    dynamic defaultInbox = ns.GetDefaultFolder(6); // 6 = olFolderInbox
                    if (defaultInbox != null) { _folderIds.Add((string)defaultInbox.EntryID); _cachedInboxes.Add(defaultInbox); }
                }
                catch (Exception ex) { _log("Default inbox lookup failed: " + ex.Message); }
            }

            _justConnected = true;
            _cachedOutlookApp = app;
            _cachedMapiNamespace = ns;
            if (_cachedInboxes.Count == 0)
            {
                LogStateChange(onlyListed ? "Connected to Outlook but none of the listed folders was found." : "Connected to Outlook but no Inbox folder was found.");
                return false;
            }
            LogStateChange(onlyListed
                ? $"Connected to Outlook; monitoring {_cachedInboxes.Count} mail folder(s): only the {tracked.Count} listed name(s) and their subfolders. {WatchedSummary()}"
                : $"Connected to Outlook; monitoring {_cachedInboxes.Count} mail folder(s) (Inbox and subfolders, plus {tracked.Count} tracked name(s)). {WatchedSummary()}");
            return true;
        }
        catch (Exception ex)
        {
            LogStateChange($"Outlook connection failed: {ex.Message}");
            ResetComCache();
            return false;
        }
    }

    /// <summary>
    /// Walks a folder tree. A folder is watched when it is an Inbox, is named in the tracked list, or lies below one of those;
    /// the walk skips Deleted/Junk/Sent/Drafts/Outbox and only adds folders that hold mail.
    /// </summary>
    void ScanFolder(dynamic folder, bool inside, bool isRoot, HashSet<string> excluded, string? inboxId, IReadOnlyList<string> tracked, bool onlyListed)
    {
        try
        {
            string id = (string)folder.EntryID;
            if (excluded.Contains(id)) return;
            string name = (string)folder.Name;

            bool watched = OutlookFolderPolicy.IsWatched(name, id == inboxId, inside, tracked, onlyListed);
            if (watched && !isRoot && !_folderIds.Contains(id) && HoldsMail(folder))
            {
                _folderIds.Add(id);
                _cachedInboxes.Add(folder);
                try { _watchedPaths.Add((string)folder.FolderPath); } catch { _watchedPaths.Add(name); }
            }

            dynamic folders = folder.Folders;
            int count = folders.Count;
            for (int i = 1; i <= count; i++) ScanFolder(folders.Item(i), watched, false, excluded, inboxId, tracked, onlyListed);
        }
        catch (Exception ex) { _log("Folder scan failed: " + ex.Message); }
    }

    // Which folders are watched, so a surprising one can be spotted in the log (the first 12 paths).
    string WatchedSummary()
    {
        const int Max = 12;
        string list = string.Join("; ", _watchedPaths.Take(Max));
        return "Watching: " + list + (_watchedPaths.Count > Max ? $"; and {_watchedPaths.Count - Max} more" : "");
    }

    static bool HoldsMail(dynamic folder)
    {
        try { return (int)folder.DefaultItemType == 0; } // 0 = olMailItem; skips calendars, contacts and the like
        catch { return true; }
    }

    void StaLoop(CancellationToken token)
    {
        bool initialized = false;
        try
        {
            while (!token.IsCancellationRequested)
            {
                while (_staCommands.TryDequeue(out var cmd))
                {
                    if (token.IsCancellationRequested) break;
                    try { cmd(); }
                    catch (Exception ex) { _log("Outlook command failed: " + ex.Message); }
                }
                if (token.IsCancellationRequested) break;

                try
                {
                    if (EnsureConnected())
                    {
                        if (!initialized || _justConnected) { _justConnected = false; InitializeBaseline(); initialized = true; }
                        else CheckNewEmails();
                    }
                    else initialized = false; // Outlook was closed; re-check when it reopens
                }
                catch (Exception ex)
                {
                    LogStateChange("Outlook polling failed: " + ex.Message);
                    ResetComCache();
                    initialized = false;
                }

                // Wait for the poll interval or an incoming command (Open/MarkRead/Stop)
                var deadline = Environment.TickCount64 + Math.Max(1, PollIntervalSeconds) * 1000L;
                while (!token.IsCancellationRequested)
                {
                    while (_staCommands.TryDequeue(out var cmd))
                    {
                        if (token.IsCancellationRequested) break;
                        try { cmd(); }
                        catch (Exception ex) { _log("Outlook command failed: " + ex.Message); }
                    }
                    long remaining = deadline - Environment.TickCount64;
                    if (remaining <= 0 || token.IsCancellationRequested) break;
                    _staWake.WaitOne((int)Math.Min(remaining, 250));
                }
            }
        }
        finally
        {
            ResetComCache();
        }
    }

    /// <summary>Outlook's Restrict wants the date in the user's short date/time format.</summary>
    string UnreadSinceFilter() =>
        "[UnRead] = true AND [ReceivedTime] >= '" + _watchSince.ToString("g", System.Globalization.CultureInfo.CurrentCulture) + "'";

    /// <summary>Orders unread items newest-first and returns at most <paramref name="maxCount"/> unseen items.</summary>
    public static IReadOnlyList<T> PickNewestUnseen<T>(IEnumerable<T> items, Func<T, DateTime> receivedAt, Func<T, string?> idOf, ISet<string> seenIds, int maxCount = MaxUnreadPerFolder)
    {
        var result = new List<T>();
        foreach (var item in items.OrderByDescending(receivedAt))
        {
            string? id = idOf(item);
            if (string.IsNullOrEmpty(id) || seenIds.Contains(id)) continue;
            result.Add(item);
            if (result.Count >= maxCount) break;
        }
        return result;
    }

    void InitializeBaseline()
    {
        try
        {
            var toNotify = new List<dynamic>();
            if (_watchSince == DateTime.MinValue) _watchSince = DateTime.Now.AddMinutes(-15);

            foreach (var inbox in _cachedInboxes)
            {
                dynamic? items = null;
                dynamic? unreadItems = null;
                try
                {
                    items = inbox.Items;

                    // Only unread mail that arrived since monitoring began (minus 15 minutes); older unread mail in many folders is never walked.
                    unreadItems = items.Restrict(UnreadSinceFilter());
                    try { unreadItems.Sort("[ReceivedTime]", true); } catch { }
                    int unreadCount = (int)unreadItems.Count;
                    for (int i = 1; i <= unreadCount; i++)
                    {
                        dynamic? mail = null;
                        try
                        {
                            mail = unreadItems.Item(i);
                            object? mailObj = mail;
                            if (mailObj == null) continue;
                            if (((int)mail.Class) != 43) { ReleaseCom(mail); continue; }

                            string? id = (string?)mail?.EntryID;
                            if (string.IsNullOrEmpty(id) || _seenIds.Contains(id)) { ReleaseCom(mail); continue; }

                            toNotify.Add(mail);
                        }
                        catch (Exception ex) { ReleaseCom(mail); _log("Baseline unread item failed: " + ex.Message); }
                    }
                }
                catch (Exception ex) { _log("Baseline folder failed: " + ex.Message); }
                finally { ReleaseCom(unreadItems); ReleaseCom(items); }
            }

            _log($"Baseline done: {toNotify.Count} recent unread mail(s) to notify, {_seenIds.Count} already known.");
            foreach (var mail in toNotify)
            {
                try { ProcessAndEmitMail(mail); }
                finally { ReleaseCom(mail); }
            }
        }
        catch (Exception ex) { _log("Baseline failed: " + ex.Message); }
    }

    void CheckNewEmails()
    {
        try
        {
            var newMails = new List<dynamic>();

            foreach (var inbox in _cachedInboxes)
            {
                dynamic? items = null;
                dynamic? unreadItems = null;
                try
                {
                    items = inbox.Items;
                    unreadItems = items.Restrict(UnreadSinceFilter());
                    // Sort newest-first so >30 accumulated unread messages never hide new arrivals.
                    try { unreadItems.Sort("[ReceivedTime]", true); } catch { }

                    int count = (int)unreadItems.Count;
                    if (count == 0) continue;

                    for (int i = 1; i <= Math.Min(count, MaxUnreadPerFolder); i++)
                    {
                        dynamic? mail = null;
                        try
                        {
                            mail = unreadItems.Item(i);
                            object? mailObj = mail;
                            if (mailObj == null) continue;
                            if (((int)mail.Class) != 43) { ReleaseCom(mail); continue; }

                            string? id = (string?)mail?.EntryID;
                            if (string.IsNullOrEmpty(id) || _seenIds.Contains(id)) { ReleaseCom(mail); continue; }

                            newMails.Add(mail);
                        }
                        catch (Exception ex) { ReleaseCom(mail); _log("Unread item failed: " + ex.Message); }
                    }
                }
                catch (Exception ex) { _log("Folder poll failed: " + ex.Message); }
                finally { ReleaseCom(unreadItems); ReleaseCom(items); }
            }

            if (newMails.Count > 0) _log($"Found {newMails.Count} new unread mail(s).");
            foreach (var mail in newMails)
            {
                try { ProcessAndEmitMail(mail); }
                finally { ReleaseCom(mail); }
            }
        }
        catch
        {
            ResetComCache();
        }
    }

    static string ReadText(Func<object?> read, string fallback, string field, Action<string> log)
    {
        try { return (read() as string) ?? fallback; }
        catch (Exception ex)
        {
            log($"Could not read mail {field}: {ex.Message}");
            return fallback;
        }
    }

    void ProcessAndEmitMail(dynamic mail)
    {
        string? id = null;
        try
        {
            id = (string?)mail.EntryID;
            if (string.IsNullOrEmpty(id)) return;
            if (_seenIds.Contains(id)) return;

            // Optional fields fall back instead of failing, so restricted object-model access never drops the mail.
            string sender = ReadText(() => mail.SenderName, "Unknown", "sender", _log);
            string subject = ReadText(() => mail.Subject, "(No Subject)", "subject", _log);
            string body = ReadText(() => mail.Body, "", "body", _log);
            if (body.Length > MaxBodyChars) body = body[..MaxBodyChars];

            string folderName = "Inbox";
            try
            {
                dynamic parent = mail.Parent;
                folderName = (string)parent.Name;
            }
            catch (Exception ex) { _log("Could not read mail folder: " + ex.Message); }

            var dto = new OutlookMailDto(id, sender, subject, body, folderName, (DateTime)mail.ReceivedTime);

            // Mark as seen only once the item was read successfully.
            _seenIds.Add(id);
            _failedAttempts.Remove(id);
            MailArrived?.Invoke(dto);
        }
        catch (Exception ex)
        {
            _log("Reading mail failed: " + ex.Message);
            if (!string.IsNullOrEmpty(id))
            {
                // Retry on the next poll, but give up after a few attempts so one bad item cannot loop forever.
                int attempts = _failedAttempts.TryGetValue(id, out var n) ? n + 1 : 1;
                if (attempts >= MaxReadAttempts) { _seenIds.Add(id); _failedAttempts.Remove(id); }
                else _failedAttempts[id] = attempts;
            }
        }
    }

    /// <summary>Opens a mail in Outlook, or just starts Outlook when the item cannot be found.</summary>
    public void Open(string? entryId)
    {
        EnqueueSta(() =>
        {
            try
            {
                if (!string.IsNullOrEmpty(entryId) && _cachedMapiNamespace is not null)
                {
                    dynamic? item = null;
                    try
                    {
                        item = _cachedMapiNamespace.GetItemFromID(entryId);
                        item.Display();
                        return;
                    }
                    finally { ReleaseCom(item); }
                }
            }
            catch (Exception ex) { _log("Opening mail failed: " + ex.Message); }

            try { Process.Start(new ProcessStartInfo("outlook.exe") { UseShellExecute = true }); }
            catch (Exception ex) { _log("Starting Outlook failed: " + ex.Message); }
        });
    }

    public void MarkRead(string? entryId)
    {
        if (string.IsNullOrEmpty(entryId)) return;
        EnqueueSta(() =>
        {
            dynamic? item = null;
            try
            {
                if (_cachedMapiNamespace is null) return;
                item = _cachedMapiNamespace.GetItemFromID(entryId);
                item.UnRead = false;
                item.Save();
            }
            catch (Exception ex) { _log("Marking mail read failed: " + ex.Message); }
            finally { ReleaseCom(item); }
        });
    }
}
