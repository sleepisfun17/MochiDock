using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace MochiDock.Core;

/// <summary>
/// Reads new mail from classic Outlook through COM. It must run at the same elevation as Outlook, so MochiDock
/// (which runs as Administrator) hosts it inside a non-elevated helper process; a non-elevated MochiDock can run it directly.
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

    readonly Action<string> _log;
    readonly HashSet<string> _seenIds = new();
    readonly Dictionary<string, int> _failedAttempts = new();
    readonly object _comGate = new();
    CancellationTokenSource? _cts;
    dynamic? _cachedOutlookApp;
    dynamic? _cachedMapiNamespace;
    List<dynamic> _cachedInboxes = new();
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
        lock (_comGate) ResetComCache();
    }

    public static bool IsInstalled => Type.GetTypeFromProgID("Outlook.Application") != null;

    public void Start()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        Task.Run(() => MonitorLoopAsync(token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        lock (_comGate) ResetComCache();
    }

    void LogStateChange(string message)
    {
        if (message == _lastDiagnostic) return;
        _lastDiagnostic = message;
        _log(message);
    }

    void ResetComCache()
    {
        _cachedInboxes.Clear();
        _cachedMapiNamespace = null;
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

    async Task MonitorLoopAsync(CancellationToken token)
    {
        bool initialized = false;

        while (!token.IsCancellationRequested)
        {
            try
            {
                lock (_comGate)
                {
                    if (EnsureConnected())
                    {
                        if (!initialized || _justConnected) { _justConnected = false; InitializeBaseline(); initialized = true; }
                        else CheckNewEmails();
                    }
                    else initialized = false; // Outlook was closed; re-check when it reopens
                }
            }
            catch (Exception ex)
            {
                LogStateChange("Outlook polling failed: " + ex.Message);
                lock (_comGate) ResetComCache();
                initialized = false;
            }

            try { await Task.Delay(TimeSpan.FromSeconds(PollIntervalSeconds), token); }
            catch (OperationCanceledException) { break; }
        }
    }

    /// <summary>Outlook's Restrict wants the date in the user's short date/time format.</summary>
    string UnreadSinceFilter() =>
        "[UnRead] = true AND [ReceivedTime] >= '" + _watchSince.ToString("g", System.Globalization.CultureInfo.CurrentCulture) + "'";

    void InitializeBaseline()
    {
        try
        {
            var toNotify = new List<dynamic>();
            if (_watchSince == DateTime.MinValue) _watchSince = DateTime.Now.AddMinutes(-15);

            foreach (var inbox in _cachedInboxes)
            {
                try
                {
                    dynamic items = inbox.Items;

                    // Only unread mail that arrived since monitoring began (minus 15 minutes); older unread mail in many folders is never walked.
                    dynamic unreadItems = items.Restrict(UnreadSinceFilter());
                    int unreadCount = (int)unreadItems.Count;
                    for (int i = 1; i <= unreadCount; i++)
                    {
                        try
                        {
                            dynamic mail = unreadItems.Item(i);
                            object? mailObj = mail;
                            if (mailObj == null) continue;
                            if (((int)mail.Class) != 43) continue;

                            string? id = (string?)mail?.EntryID;
                            if (string.IsNullOrEmpty(id) || _seenIds.Contains(id)) continue;

                            toNotify.Add(mail);
                        }
                        catch (Exception ex) { _log("Baseline unread item failed: " + ex.Message); }
                    }
                }
                catch (Exception ex) { _log("Baseline folder failed: " + ex.Message); }
            }

            _log($"Baseline done: {toNotify.Count} recent unread mail(s) to notify, {_seenIds.Count} already known.");
            foreach (var mail in toNotify) ProcessAndEmitMail(mail);
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
                try
                {
                    dynamic items = inbox.Items;
                    dynamic unreadItems = items.Restrict(UnreadSinceFilter());

                    int count = (int)unreadItems.Count;
                    if (count == 0) continue;

                    for (int i = 1; i <= Math.Min(count, 30); i++)
                    {
                        try
                        {
                            dynamic mail = unreadItems.Item(i);
                            object? mailObj = mail;
                            if (mailObj == null) continue;
                            if (((int)mail.Class) != 43) continue;

                            string? id = (string?)mail?.EntryID;
                            if (string.IsNullOrEmpty(id) || _seenIds.Contains(id)) continue;

                            newMails.Add(mail);
                        }
                        catch (Exception ex) { _log("Unread item failed: " + ex.Message); }
                    }
                }
                catch (Exception ex) { _log("Folder poll failed: " + ex.Message); }
            }

            if (newMails.Count > 0) _log($"Found {newMails.Count} new unread mail(s).");
            foreach (var mail in newMails) ProcessAndEmitMail(mail);
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
        Task.Run(() =>
        {
            try
            {
                if (!string.IsNullOrEmpty(entryId))
                {
                    lock (_comGate)
                    {
                        if (_cachedMapiNamespace is not null)
                        {
                            dynamic item = _cachedMapiNamespace.GetItemFromID(entryId);
                            item.Display();
                            return;
                        }
                    }
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
        Task.Run(() =>
        {
            try
            {
                lock (_comGate)
                {
                    if (_cachedMapiNamespace is null) return;
                    dynamic item = _cachedMapiNamespace.GetItemFromID(entryId);
                    item.UnRead = false;
                    item.Save();
                }
            }
            catch (Exception ex) { _log("Marking mail read failed: " + ex.Message); }
        });
    }
}
