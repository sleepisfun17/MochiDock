using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using MochiDock.Models;

namespace MochiDock.Services;

public class GowaService
{
    private Process? _gowaProcess;
    private HttpListener? _httpListener;
    private CancellationTokenSource? _cts;
    private readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(4) };
    // Message ids are remembered per account: a message between two of your own accounts has the same id on both sides,
    // and the incoming copy must not be dropped because the outgoing copy was seen first.
    private readonly HashSet<string> _seenMessageIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _seenMessageOrder = new();
    private readonly object _seenGate = new();
    private const int MaxSeenMessageIds = 4096;
    private const int MaxWebhookBodyBytes = 256 * 1024;
    private readonly SemaphoreSlim _webhookConcurrency = new(16, 16);
    // Phone number (or LID) -> display name, learned from senders and from the contact list, to name "@number" mentions.
    private readonly ConcurrentDictionary<string, string> _knownNames = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _contactsLoadedUtc = DateTime.MinValue;
    private readonly HashSet<string> _archivedChatJids = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _groupNamesCache = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastMetadataRefreshUtc = DateTime.MinValue;
    private DateTime _serviceStartedUtc = DateTime.UtcNow;
    private volatile string? _authHeader;
    private volatile System.Net.Http.Headers.AuthenticationHeaderValue? _authHeaderValue;
    private int _failedChecks;
    private string _lastDevicesBody = "";
    private IReadOnlyList<MochiDock.Core.GowaAccount> _accounts = Array.Empty<MochiDock.Core.GowaAccount>();
    private readonly Dictionary<string, DateTime> _lastSyncByDevice = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _seededDevices = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _pollStartedUtc = DateTime.UtcNow;

    /// <summary>Every WhatsApp account (GOWA device) the server reports; kept while the server is briefly unreachable.</summary>
    public IReadOnlyList<MochiDock.Core.GowaAccount> Accounts => _accounts;
    public event Action? AccountsChanged;

    /// <summary>Why the server is not usable right now ("login rejected", "unreachable", ...), empty while connected.</summary>
    public string ConnectionProblem { get; private set; } = "";

    public bool UseRemoteServer { get; set; }
    public string RemoteServerUrl { get; set; } = ""; // empty: the local engine is used (see ActiveBaseUrl)
    public int GowaPort { get; set; } = 3099;
    public int WebhookPort { get; set; } = 47823;
    public string RemoteWebhookToken { get; private set; } = "";
    public bool IgnoreArchivedChats { get; set; } = true;
    public bool MuteAllWhatsAppGroups { get; set; }
    public bool OnlyNotifyListedGroups { get; set; }
    public bool OnlyNotifyListedPeople { get; set; }
    public HashSet<string> AllowedWhatsAppGroups { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> MutedWhatsAppChatJids { get; } = new(StringComparer.OrdinalIgnoreCase);

    public event Action<WhatsAppNotification>? MessageReceived;
    /// <summary>A chat was read or answered: chat JID, the read message ids (null = the whole chat) and the account (device id) it happened on.</summary>
    public event Action<string, List<string>?, string>? MessageReadOrDismissed;

    public string ActiveBaseUrl
    {
        get
        {
            if (UseRemoteServer && !string.IsNullOrWhiteSpace(RemoteServerUrl))
            {
                string url = RemoteServerUrl.Trim().TrimEnd('/');
                if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                    !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    url = "http://" + url;
                }
                return url;
            }
            return $"http://127.0.0.1:{GowaPort}";
        }
    }

    public string DashboardUrl => $"{ActiveBaseUrl}/";
    public string ConnectedDeviceName { get; private set; } = "";
    public string ConnectedDeviceId { get; private set; } = "";
    public bool IsHealthy => _isApiConnected || (!UseRemoteServer && _gowaProcess is { HasExited: false } && _httpListener?.IsListening == true);
    private bool _isApiConnected;

    /// <summary>False until the user turns WhatsApp on; while false the service runs nothing.</summary>
    public bool Enabled { get; private set; }

    /// <summary>A new random token for the webhook URL a shared GOWA server calls.</summary>
    public static string NewWebhookToken() => Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

    public void ConfigureFromSettings(AppSettings settings)
    {
        Enabled = settings.EnableWhatsApp;
        UseRemoteServer = string.Equals(settings.GowaMode, "Remote", StringComparison.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(settings.RemoteGowaUrl))
        {
            RemoteServerUrl = settings.RemoteGowaUrl.Trim();
        }
        if (settings.GowaPort > 0)
        {
            GowaPort = settings.GowaPort;
        }
        if (settings.WebhookPort > 0)
        {
            WebhookPort = settings.WebhookPort;
        }
        // SettingsService.Load gives every settings file a token and saves it. A settings object that has none (an import, a test)
        // gets one in memory only: this service never writes settings itself, the owner of the object does.
        if (string.IsNullOrWhiteSpace(settings.RemoteWebhookToken))
            settings.RemoteWebhookToken = NewWebhookToken();
        RemoteWebhookToken = settings.RemoteWebhookToken;
        ApplyRemoteAuth();
        IgnoreArchivedChats = settings.IgnoreArchivedChats;
        MuteAllWhatsAppGroups = settings.MuteAllWhatsAppGroups;
        OnlyNotifyListedGroups = settings.WhatsAppOnlyListedGroups;
        OnlyNotifyListedPeople = settings.WhatsAppOnlyListedPeople;
        lock (AllowedWhatsAppGroups)
        {
            AllowedWhatsAppGroups.Clear();
            foreach (var g in settings.WhatsAppAllowedGroups ?? new()) if (!string.IsNullOrWhiteSpace(g)) AllowedWhatsAppGroups.Add(g.Trim());
        }
        lock (MutedWhatsAppChatJids)
        {
            MutedWhatsAppChatJids.Clear();
            if (settings.MutedWhatsAppChatJids != null)
            {
                foreach (var j in settings.MutedWhatsAppChatJids)
                {
                    if (!string.IsNullOrWhiteSpace(j)) MutedWhatsAppChatJids.Add(j.Trim());
                }
            }
        }
    }

    private static string? CurrentAuthHeader(bool remote)
    {
        if (!remote) return null;
        var credentials = GowaCredentialStore.Load();
        return credentials is { } c ? MochiDock.Core.GowaRemotePolicy.BasicAuthHeader(c.User, c.Password) : null;
    }

    /// <summary>True when the saved server credentials differ from the ones this service is using (they live outside the settings file).</summary>
    public bool RemoteCredentialsChanged => !string.Equals(CurrentAuthHeader(UseRemoteServer), _authHeader, StringComparison.Ordinal);

    // A shared GOWA server is protected with --basic-auth; local GOWA is bound to 127.0.0.1 and needs none.
    private void ApplyRemoteAuth()
    {
        var header = CurrentAuthHeader(UseRemoteServer);
        _authHeader = header;
        _authHeaderValue = header == null
            ? null
            : System.Net.Http.Headers.AuthenticationHeaderValue.Parse(header);
    }

    internal bool TryMarkSeen(string key)
    {
        lock (_seenGate)
        {
            if (!_seenMessageIds.Add(key)) return false;
            _seenMessageOrder.Enqueue(key);
            while (_seenMessageOrder.Count > MaxSeenMessageIds)
            {
                _seenMessageIds.Remove(_seenMessageOrder.Dequeue());
            }
            return true;
        }
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string url)
    {
        var req = new HttpRequestMessage(method, url);
        if (_authHeaderValue is { } auth) req.Headers.Authorization = auth;
        return req;
    }

    public async Task<bool> CheckConnectionAsync()
    {
        if (!Enabled) { _isApiConnected = false; ConnectionProblem = ""; return false; }
        ConnectionProblem = "";
        try
        {
            // GET /devices lists every account and needs no device id. /app/devices refuses to answer without one once
            // more than one account is linked (DEVICE_ID_REQUIRED), so it is only the fallback for older servers.
            using var listReq = CreateRequest(HttpMethod.Get, $"{ActiveBaseUrl}/devices");
            using var list = await _httpClient.SendAsync(listReq);
            if (list.IsSuccessStatusCode)
            {
                var listBody = await list.Content.ReadAsStringAsync();
                if (listBody != _lastDevicesBody) { _lastDevicesBody = listBody; Log($"[Accounts] /devices answered: {listBody}"); }
                using var listDoc = JsonDocument.Parse(listBody);
                if (listDoc.RootElement.TryGetProperty("results", out var listResults))
                {
                    var linked = MochiDock.Core.GowaAccountPolicy.ParseAccounts(listResults);
                    if (linked.Count > 0) return UseAccounts(linked);
                    ConnectionProblem = "no WhatsApp device linked";
                    SetAccounts(linked);
                    _isApiConnected = false;
                    ConnectedDeviceName = "";
                    return false;
                }
            }

            using var resReq = CreateRequest(HttpMethod.Get, $"{ActiveBaseUrl}/app/devices");
            using var res = await _httpClient.SendAsync(resReq);
            if (res.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            {
                ConnectionProblem = "login rejected";
            }
            else if (!res.IsSuccessStatusCode)
            {
                ConnectionProblem = $"server answered {(int)res.StatusCode}";
            }
            else
            {
                var content = await res.Content.ReadAsStringAsync();
                if (content != _lastDevicesBody) { _lastDevicesBody = content; Log($"[Accounts] /app/devices answered: {content}"); }
                using var doc = JsonDocument.Parse(content);
                if (doc.RootElement.TryGetProperty("results", out var results) &&
                    results.ValueKind == JsonValueKind.Array &&
                    results.GetArrayLength() > 0)
                {
                    var accounts = MochiDock.Core.GowaAccountPolicy.ParseAccounts(results);
                    if (accounts.Count == 0) { ConnectionProblem = "no WhatsApp device linked"; SetAccounts(accounts); _isApiConnected = false; return false; }
                    return UseAccounts(accounts);
                }
                ConnectionProblem = "no WhatsApp device linked";
                SetAccounts(Array.Empty<MochiDock.Core.GowaAccount>());
            }
        }
        catch (System.Exception logEx)
        {
            ConnectionProblem = "unreachable";
            MochiDock.Services.AppLog.Warn("Gowa", logEx);
        }

        _isApiConnected = false;
        ConnectedDeviceName = "";
        return false;
    }

    // Adopts a non-empty account list: the first account stays the primary one for calls that are not tied to a message.
    private bool UseAccounts(IReadOnlyList<MochiDock.Core.GowaAccount> accounts)
    {
        SetAccounts(accounts);
        ConnectedDeviceId = accounts[0].DeviceId;
        ConnectedDeviceName = string.Join(", ", accounts.Select(AccountLabel));
        _isApiConnected = true;

        // Trigger periodic metadata refresh if due
        if ((DateTime.UtcNow - _lastMetadataRefreshUtc).TotalMinutes > 5)
        {
            _lastMetadataRefreshUtc = DateTime.UtcNow;
            _ = Task.Run(RefreshMetadataAsync);
        }
        return true;
    }

    private void SetAccounts(IReadOnlyList<MochiDock.Core.GowaAccount> accounts)
    {
        var old = _accounts;
        bool changed = old.Count != accounts.Count || !old.Zip(accounts).All(p => p.First == p.Second);
        _accounts = accounts;
        if (changed) AccountsChanged?.Invoke();
    }

    private string AccountLabel(MochiDock.Core.GowaAccount account)
    {
        bool nameIsUnique = !string.IsNullOrWhiteSpace(account.Name) &&
                            _accounts.Count(a => string.Equals(a.Name, account.Name, StringComparison.OrdinalIgnoreCase)) <= 1;
        if (nameIsUnique) return account.Name;
        // Same display name on several accounts (common when one person owns them): the device id is the clearer label.
        if (!string.IsNullOrWhiteSpace(account.DeviceId) && !MochiDock.Core.MentionResolver.NumberOf(account.DeviceId).Any()) return account.DeviceId;
        return !string.IsNullOrWhiteSpace(account.Jid) ? FormatPhoneNumber(account.Jid) : account.Name;
    }

    /// <summary>The display name of the account behind <paramref name="deviceId"/>, or "" when it is unknown.</summary>
    public string AccountNameFor(string? deviceId)
    {
        var account = MochiDock.Core.GowaAccountPolicy.Find(_accounts, deviceId);
        return account == null ? "" : AccountLabel(account);
    }

    // The device id to use for a message or event: the matching account's own id, else the id given, else the primary account.
    private string ResolveDevice(string? deviceId)
    {
        var account = MochiDock.Core.GowaAccountPolicy.Find(_accounts, deviceId);
        if (account != null) return account.DeviceId;
        return string.IsNullOrEmpty(deviceId) ? ConnectedDeviceId : deviceId;
    }

    private IEnumerable<string> DeviceIds() =>
        _accounts.Count > 0 ? _accounts.Select(a => a.DeviceId).ToList() : new List<string> { ConnectedDeviceId };

    private string ArchivedKey(string deviceId, string chatJid) => ResolveDevice(deviceId) + "|" + chatJid;

    // A request scoped to one account (device_id query and X-Device-Id header); no scope when the server reports no device id.
    private HttpRequestMessage DeviceRequest(HttpMethod method, string path, string? deviceId)
    {
        string device = ResolveDevice(deviceId);
        string url = ActiveBaseUrl + path;
        if (!string.IsNullOrEmpty(device)) url += (path.Contains('?') ? "&" : "?") + "device_id=" + Uri.EscapeDataString(device);
        var req = CreateRequest(method, url);
        if (!string.IsNullOrEmpty(device)) req.Headers.TryAddWithoutValidation("X-Device-Id", device);
        return req;
    }

    private static void Log(string msg) => AppLog.Warn("Gowa", msg);

    public bool IsChatMuted(string chatJid, bool isGroup, string? deviceId = null)
    {
        if (string.IsNullOrWhiteSpace(chatJid)) return false;
        if (isGroup && MuteAllWhatsAppGroups) return true;
        lock (AllowedWhatsAppGroups)
            if (MochiDock.Core.GowaAccountPolicy.IsHiddenByAllowList(OnlyNotifyListedGroups, OnlyNotifyListedPeople, AllowedWhatsAppGroups, chatJid, deviceId, isGroup)) return true;
        lock (MutedWhatsAppChatJids) return MochiDock.Core.GowaAccountPolicy.IsMuted(MutedWhatsAppChatJids, chatJid, deviceId);
    }

    /// <summary>True when a person you silenced wrote in a group (the sender, not the chat). Only the muted list counts here, never the notify list.</summary>
    private bool IsSenderMuted(string senderJid, string? deviceId)
    {
        if (string.IsNullOrWhiteSpace(senderJid)) return false;
        lock (MutedWhatsAppChatJids) return MochiDock.Core.GowaAccountPolicy.IsMuted(MutedWhatsAppChatJids, senderJid, deviceId);
    }

    /// <summary>The chats of every linked account for the settings picker (newest first); accounts the server cannot answer for are skipped.</summary>
    public async Task<IReadOnlyList<MochiDock.Core.WhatsAppChatEntry>> FetchChatListAsync(CancellationToken token = default)
    {
        var all = new List<MochiDock.Core.WhatsAppChatEntry>();
        var devices = _accounts.Count > 0 ? _accounts.Select(a => a.DeviceId).ToList() : new List<string> { ConnectedDeviceId };
        foreach (var deviceId in devices)
        {
            try
            {
                string device = ResolveDevice(deviceId);
                using var req = DeviceRequest(HttpMethod.Get, "/chats?limit=300", device);
                using var res = await _httpClient.SendAsync(req, token).ConfigureAwait(false);
                if (!res.IsSuccessStatusCode) continue;
                using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(token).ConfigureAwait(false));
                all.AddRange(MochiDock.Core.WhatsAppChatList.Parse(doc.RootElement, device, AccountNameFor(device)));
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { MochiDock.Services.AppLog.Warn("Gowa", ex); }
        }
        return all;
    }

    /// <summary>Adds a muted-list entry to the running service so the mute works without a restart.</summary>
    public void AddMutedChat(string entry)
    {
        if (string.IsNullOrWhiteSpace(entry)) return;
        lock (MutedWhatsAppChatJids) MutedWhatsAppChatJids.Add(entry.Trim());
    }

    /// <summary>Adds a group to the notify list of the running service.</summary>
    public void AddAllowedGroup(string entry)
    {
        if (string.IsNullOrWhiteSpace(entry)) return;
        lock (AllowedWhatsAppGroups) AllowedWhatsAppGroups.Add(entry.Trim());
    }

    /// <summary>The muted-list entry that mutes <paramref name="chatJid"/> for the account behind <paramref name="deviceId"/>.</summary>
    public string MuteKeyFor(string chatJid, string? deviceId) =>
        MochiDock.Core.GowaAccountPolicy.MuteKey(deviceId, chatJid, _accounts.Count);

    public async Task<List<WhatsAppNotification>> FetchRecentChatHistoryAsync(string chatJid, int limit = 10, string deviceId = "")
    {
        var list = new List<WhatsAppNotification>();
        string device = ResolveDevice(deviceId);
        try
        {
            using var req = DeviceRequest(HttpMethod.Get, $"/chat/{Uri.EscapeDataString(chatJid)}/messages?limit={limit}", device);

            using var res = await _httpClient.SendAsync(req).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return list;

            var json = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("results", out var results) ||
                !results.TryGetProperty("data", out var dataArr) ||
                dataArr.ValueKind != JsonValueKind.Array)
            {
                return list;
            }

            bool isGroup = chatJid.EndsWith("@g.us", StringComparison.OrdinalIgnoreCase);
            string groupName = "";
            if (isGroup)
            {
                groupName = await ResolveGroupNameAsync(chatJid, device).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(groupName)) groupName = "Group";
            }

            var items = new List<JsonElement>();
            foreach (var item in dataArr.EnumerateArray())
            {
                items.Add(item.Clone());
            }
            items.Reverse(); // Chronological order

            foreach (var msg in items)
            {
                string id = msg.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
                string content = msg.TryGetProperty("content", out var cProp) ? cProp.GetString() ?? "" : "";
                string senderDisplay = msg.TryGetProperty("sender_display_name", out var sProp) ? sProp.GetString() ?? "" : "";
                string participantJid = msg.TryGetProperty("sender", out var sndProp) ? sndProp.GetString() ?? "" : "";
                string tsStr = msg.TryGetProperty("timestamp", out var tsProp) ? tsProp.GetString() ?? "" : "";
                bool isFromMe = msg.TryGetProperty("is_from_me", out var fmProp) && fmProp.GetBoolean();

                if (!DateTime.TryParse(tsStr, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var msgUtc))
                {
                    msgUtc = DateTime.UtcNow;
                }

                if (string.IsNullOrWhiteSpace(content)) continue;
                if (!isFromMe) RememberName(participantJid, senderDisplay);
                content = await NameMentionsAsync(content).ConfigureAwait(false);

                string senderName;
                string sender;

                if (isFromMe)
                {
                    senderName = "You";
                    sender = isGroup ? $"{groupName}: You" : "You";
                }
                else if (isGroup)
                {
                    senderName = !string.IsNullOrWhiteSpace(senderDisplay) ? senderDisplay : FormatPhoneNumber(participantJid);
                    sender = $"{groupName}: {senderName}";
                }
                else
                {
                    senderName = !string.IsNullOrWhiteSpace(senderDisplay) ? senderDisplay : FormatPhoneNumber(chatJid);
                    sender = senderName;
                }

                list.Add(new WhatsAppNotification
                {
                    RemoteId = id,
                    Sender = sender,
                    Message = content,
                    FromJid = chatJid,
                    DeviceId = device,
                    AccountName = AccountNameFor(device),
                    IsGroup = isGroup,
                    GroupName = groupName,
                    SenderName = senderName,
                    ParticipantJid = participantJid,
                    Timestamp = msgUtc.ToLocalTime()
                });
            }
        }
        catch (Exception ex)
        {
            Log($"Error fetching recent history for {chatJid}: {ex.Message}");
        }

        return list;
    }

    private void RememberName(string? jid, string? name)
    {
        string number = MochiDock.Core.MentionResolver.NumberOf(jid);
        if (number.Length == 0 || string.IsNullOrWhiteSpace(name)) return;
        string clean = name.Trim();
        if (MochiDock.Core.MentionResolver.NumberOf(clean).Length > 0 || clean.StartsWith('+')) return; // a number is not a name
        _knownNames[number] = clean;
    }

    /// <summary>Replaces "@number" mentions with the saved or known name. Loads the contact list at most every 10 minutes when a number is unknown.</summary>
    private async Task<string> NameMentionsAsync(string text)
    {
        if (!MochiDock.Core.MentionResolver.HasMention(text)) return text;
        string Lookup(string n) => _knownNames.TryGetValue(n, out var name) ? name : "";
        string result = MochiDock.Core.MentionResolver.Replace(text, Lookup);
        if (MochiDock.Core.MentionResolver.HasMention(result) && DateTime.UtcNow - _contactsLoadedUtc > TimeSpan.FromMinutes(10))
        {
            await LoadContactsAsync().ConfigureAwait(false);
            result = MochiDock.Core.MentionResolver.Replace(text, Lookup);
        }
        return result;
    }

    private async Task LoadContactsAsync()
    {
        _contactsLoadedUtc = DateTime.UtcNow;
        foreach (var device in DeviceIds()) await LoadContactsForAsync(device).ConfigureAwait(false);
    }

    private async Task LoadContactsForAsync(string deviceId)
    {
        try
        {
            using var req = DeviceRequest(HttpMethod.Get, "/user/my/contacts", deviceId);
            using var res = await _httpClient.SendAsync(req).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return;
            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync().ConfigureAwait(false));
            if (!doc.RootElement.TryGetProperty("results", out var results) || !results.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) return;
            foreach (var contact in data.EnumerateArray())
            {
                string jid = contact.TryGetProperty("jid", out var j) ? j.GetString() ?? "" : "";
                string name = contact.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(name) && contact.TryGetProperty("full_name", out var fn)) name = fn.GetString() ?? "";
                if (string.IsNullOrWhiteSpace(name) && contact.TryGetProperty("push_name", out var pn)) name = pn.GetString() ?? "";
                RememberName(jid, name);
            }
        }
        catch (Exception ex) { MochiDock.Services.AppLog.Warn("Gowa", ex); }
    }

    public static string FormatPhoneNumber(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "Unknown";
        string clean = raw.Replace("@s.whatsapp.net", "").Replace("@c.us", "").Replace("@g.us", "").Replace("@lid", "").Trim();

        // Indonesian numbers (628000000001 -> +62 800-0000-0001)
        if (clean.StartsWith("62") && clean.Length >= 10)
        {
            string prefix = "+62";
            string rest = clean.Substring(2);
            if (rest.Length <= 4) return $"{prefix} {rest}";
            if (rest.Length <= 8) return $"{prefix} {rest[..3]}-{rest[3..]}";
            return $"{prefix} {rest[..3]}-{rest.Substring(3, 4)}-{rest[7..]}";
        }

        if (clean.StartsWith("+")) return clean;
        return "+" + clean;
    }

    public async Task RefreshMetadataAsync()
    {
        if (!_isApiConnected) return;
        foreach (var device in DeviceIds()) await RefreshMetadataForAsync(device).ConfigureAwait(false);
    }

    private async Task RefreshMetadataForAsync(string deviceId)
    {
        try
        {
            // 1. Fetch group list and cache names
            using var reqGroups = DeviceRequest(HttpMethod.Get, "/user/my/groups", deviceId);

            using var resGroups = await _httpClient.SendAsync(reqGroups).ConfigureAwait(false);
            if (resGroups.IsSuccessStatusCode)
            {
                var content = await resGroups.Content.ReadAsStringAsync().ConfigureAwait(false);
                using var doc = JsonDocument.Parse(content);
                if (doc.RootElement.TryGetProperty("results", out var results) &&
                    results.TryGetProperty("data", out var dataArr) &&
                    dataArr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in dataArr.EnumerateArray())
                    {
                        string jid = item.TryGetProperty("JID", out var jProp) ? jProp.GetString() ?? "" : "";
                        string name = item.TryGetProperty("Name", out var nProp) ? nProp.GetString() ?? "" : "";
                        if (!string.IsNullOrEmpty(jid) && !string.IsNullOrWhiteSpace(name))
                        {
                            _groupNamesCache[jid] = name.Trim();
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Error refreshing group metadata: {ex.Message}");
        }

        try
        {
            // 2. Fetch recent chats to update archived list
            using var reqChats = DeviceRequest(HttpMethod.Get, "/chats?limit=100", deviceId);

            using var resChats = await _httpClient.SendAsync(reqChats).ConfigureAwait(false);
            if (resChats.IsSuccessStatusCode)
            {
                var content = await resChats.Content.ReadAsStringAsync().ConfigureAwait(false);
                using var doc = JsonDocument.Parse(content);
                if (doc.RootElement.TryGetProperty("results", out var results) &&
                    results.TryGetProperty("data", out var dataArr) &&
                    dataArr.ValueKind == JsonValueKind.Array)
                {
                    string prefix = ResolveDevice(deviceId) + "|";
                    lock (_archivedChatJids)
                    {
                        _archivedChatJids.RemoveWhere(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
                        foreach (var chat in dataArr.EnumerateArray())
                        {
                            string jid = chat.TryGetProperty("jid", out var jProp) ? jProp.GetString() ?? "" : "";
                            bool isArchived = chat.TryGetProperty("archived", out var aProp) && aProp.GetBoolean();
                            if (isArchived && !string.IsNullOrEmpty(jid))
                            {
                                _archivedChatJids.Add(ArchivedKey(deviceId, jid));
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Error refreshing archived chats list: {ex.Message}");
        }
    }

    public async Task<string> ResolveGroupNameAsync(string groupJid, string deviceId = "")
    {
        if (string.IsNullOrWhiteSpace(groupJid)) return "";
        if (_groupNamesCache.TryGetValue(groupJid, out var cached) && !string.IsNullOrWhiteSpace(cached))
        {
            return cached;
        }

        try
        {
            using var req = DeviceRequest(HttpMethod.Get, $"/group/info?group_id={Uri.EscapeDataString(groupJid)}", deviceId);

            using var res = await _httpClient.SendAsync(req).ConfigureAwait(false);
            if (res.IsSuccessStatusCode)
            {
                var content = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
                using var doc = JsonDocument.Parse(content);
                if (doc.RootElement.TryGetProperty("results", out var results))
                {
                    string name = results.TryGetProperty("Name", out var nProp) ? nProp.GetString() ?? "" : "";
                    if (!string.IsNullOrWhiteSpace(name))
                    {
                        _groupNamesCache[groupJid] = name.Trim();
                        return name.Trim();
                    }
                }
            }
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Gowa", logEx); }

        return "";
    }

    public bool IsChatArchived(string chatJid, string deviceId = "")
    {
        if (string.IsNullOrWhiteSpace(chatJid)) return false;
        lock (_archivedChatJids)
        {
            return _archivedChatJids.Contains(ArchivedKey(deviceId, chatJid));
        }
    }

    public void Start()
    {
        Stop();
        if (!Enabled) { Log("GowaService is off (WhatsApp is not enabled)."); return; }
        Log($"Starting GowaService (Mode: {(UseRemoteServer ? "Remote" : "Local")}, BaseUrl: {ActiveBaseUrl})...");
        _serviceStartedUtc = DateTime.UtcNow;
        _cts = new CancellationTokenSource();

        // Remote mode starts no local GOWA process. It still listens for the server's read receipts (message.ack),
        // which is the only way a message read on the phone or another PC clears its notification here.
        if (!UseRemoteServer) StartGowaProcess();
        StartWebhookServer(_cts.Token);

        _ = Task.Run(() => RemoteOrFallbackPollLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        Log("Stopping GowaService...");
        var cts = _cts;
        _cts = null;
        try { cts?.Cancel(); } catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Gowa", logEx); }
        finally { try { cts?.Dispose(); } catch { } }

        try
        {
            if (_httpListener != null)
            {
                if (_httpListener.IsListening)
                {
                    _httpListener.Stop();
                }
                _httpListener.Abort();
                _httpListener.Close();
                _httpListener = null;
            }
        }
        catch (Exception ex)
        {
            Log($"Error stopping HttpListener: {ex.Message}");
        }

        try
        {
            if (_gowaProcess != null)
            {
                if (!_gowaProcess.HasExited)
                {
                    _gowaProcess.Kill(true);
                }
                _gowaProcess.Dispose();
            }
            _gowaProcess = null;
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Gowa", logEx); }
    }

    private void StartGowaProcess()
    {
        string? exePath = GowaInstaller.ResolveLocalExecutable();
        if (exePath == null || !File.Exists(exePath))
        {
            ConnectionProblem = "Local WhatsApp engine not downloaded yet.";
            Log("GOWA binary not found locally; enable Local WhatsApp in Settings to download it.");
            return;
        }

        // Stop leftovers of THIS app's own GOWA (for example after a crash) so the configured port is free. GOWA processes
        // that run from another folder (a shared server on this PC, another install) are left alone.
        foreach (var p in Process.GetProcessesByName("windows-amd64"))
        {
            try
            {
                string? path = p.MainModule?.FileName;
                if (path != null && string.Equals(Path.GetFullPath(path), Path.GetFullPath(exePath), StringComparison.OrdinalIgnoreCase))
                    p.Kill(true);
            }
            catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Gowa", logEx); }
        }

        // Use persistent LocalAppData directory for GOWA session storage so rebuilding/cleaning Publish never logs out
        string persistentWorkDir = Path.Combine(AppDataPaths.Local, "GOWA");
        Directory.CreateDirectory(persistentWorkDir);

        // Migrate existing storages from Publish/GOWA/storages if persistent storages doesn't exist yet
        try
        {
            string oldStorages = Path.Combine(Path.GetDirectoryName(exePath)!, "storages");
            string newStorages = Path.Combine(persistentWorkDir, "storages");
            if (Directory.Exists(oldStorages) && !Directory.Exists(newStorages))
            {
                Directory.CreateDirectory(newStorages);
                foreach (var file in Directory.GetFiles(oldStorages))
                {
                    string dest = Path.Combine(newStorages, Path.GetFileName(file));
                    File.Copy(file, dest, true);
                }
                Log($"Migrated existing GOWA session storage to {newStorages}");
            }
        }
        catch (Exception ex)
        {
            Log($"Storage migration note: {ex.Message}");
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = $"rest --host=127.0.0.1 --port={GowaPort} --webhook=http://127.0.0.1:{WebhookPort}/webhook --webhook-events=\"message,message.ack\" --auto-download-media=false",
                WorkingDirectory = persistentWorkDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = false,
                RedirectStandardError = false
            };

            _gowaProcess = Process.Start(psi);
            Log($"GOWA process started successfully on port {GowaPort} (PID: {_gowaProcess?.Id}, WorkDir: {persistentWorkDir})");
        }
        catch (Exception ex)
        {
            Log($"Failed to start GOWA process: {ex.Message}");
        }
    }

    private async Task RemoteOrFallbackPollLoopAsync(CancellationToken token)
    {
        _pollStartedUtc = DateTime.UtcNow.AddSeconds(-5);
        lock (_lastSyncByDevice) { _lastSyncByDevice.Clear(); _seededDevices.Clear(); }

        // Initial wait for local process or network
        await Task.Delay(2500, token).ConfigureAwait(false);

        while (!token.IsCancellationRequested)
        {
            try
            {
                await CheckConnectionAsync().ConfigureAwait(false);
                _failedChecks = _isApiConnected ? 0 : _failedChecks + 1;

                if (_isApiConnected)
                {
                    // Every linked account is polled on its own, each with its own "since" time.
                    foreach (var account in _accounts)
                    {
                        try { await PollAccountAsync(account.DeviceId, token).ConfigureAwait(false); }
                        catch (System.Exception logEx) when (logEx is not OperationCanceledException) { MochiDock.Services.AppLog.Warn("Gowa", logEx); }
                    }
                }
            }
            catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Gowa", logEx); }

            try
            {
                // Back off while the server stays unreachable instead of hammering it every 2.5 seconds.
                int delayMs = _failedChecks >= 4 ? 15000 : (UseRemoteServer ? 2500 : 4000);
                await Task.Delay(delayMs, token).ConfigureAwait(false);
            }
            catch { break; }
        }
    }

    private async Task PollAccountAsync(string deviceId, CancellationToken token)
    {
        string device = ResolveDevice(deviceId);
        DateTime lastSync;
        bool seeded;
        lock (_lastSyncByDevice)
        {
            lastSync = _lastSyncByDevice.TryGetValue(device, out var stored) ? stored : _pollStartedUtc;
            seeded = _seededDevices.Contains(device);
        }

        using var req = DeviceRequest(HttpMethod.Get, "/chats?limit=12", device);
        using var res = await _httpClient.SendAsync(req, token).ConfigureAwait(false);
        if (!res.IsSuccessStatusCode)
        {
            Log($"[Poll] /chats for account '{device}' answered {(int)res.StatusCode}: {(await res.Content.ReadAsStringAsync(token).ConfigureAwait(false))}");
            return;
        }

        var json = await res.Content.ReadAsStringAsync(token).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("results", out var results) ||
            !results.TryGetProperty("data", out var dataArr) ||
            dataArr.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        DateTime maxSeenTime = lastSync;
        foreach (var chat in dataArr.EnumerateArray())
        {
            string chatJid = chat.TryGetProperty("jid", out var jProp) ? jProp.GetString() ?? "" : "";
            string chatName = chat.TryGetProperty("name", out var nProp) ? nProp.GetString() ?? "" : "";
            string lastTimeStr = chat.TryGetProperty("last_message_time", out var tProp) ? tProp.GetString() ?? "" : "";
            bool isArchived = chat.TryGetProperty("archived", out var aProp) && aProp.GetBoolean();
            LogChatFieldsOnce(chat, device);

            if (string.IsNullOrEmpty(chatJid) || chatJid.Equals("status@broadcast", StringComparison.OrdinalIgnoreCase))
                continue;

            lock (_archivedChatJids)
            {
                if (isArchived) _archivedChatJids.Add(ArchivedKey(device, chatJid));
                else _archivedChatJids.Remove(ArchivedKey(device, chatJid));
            }

            if (IgnoreArchivedChats && isArchived)
                continue;

            if (DateTime.TryParse(lastTimeStr, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var lastMsgUtc))
            {
                if (!seeded)
                {
                    if (lastMsgUtc > maxSeenTime) maxSeenTime = lastMsgUtc;
                    continue;
                }

                if (lastMsgUtc > lastSync)
                {
                    if (lastMsgUtc > maxSeenTime) maxSeenTime = lastMsgUtc;
                    await FetchAndProcessRecentChatMessagesAsync(chatJid, chatName, lastSync, device, token).ConfigureAwait(false);
                }
            }
        }

        lock (_lastSyncByDevice)
        {
            _lastSyncByDevice[device] = maxSeenTime;
            _seededDevices.Add(device);
        }
    }

    // Which fields does the server give for a chat? Logged once per account so we can tell whether WhatsApp's own mute state is available.
    private readonly HashSet<string> _chatFieldsLogged = new(StringComparer.OrdinalIgnoreCase);

    private void LogChatFieldsOnce(JsonElement chat, string device)
    {
        if (chat.ValueKind != JsonValueKind.Object) return;
        lock (_chatFieldsLogged) { if (!_chatFieldsLogged.Add(device ?? "")) return; }
        var names = chat.EnumerateObject().Select(p => p.Name).ToList();
        var muteFields = chat.EnumerateObject()
            .Where(p => p.Name.Contains("mute", StringComparison.OrdinalIgnoreCase))
            .Select(p => $"{p.Name}={p.Value}");
        Log($"[Chats] fields for account '{device}': {string.Join(", ", names)}; mute-related: {(muteFields.Any() ? string.Join(", ", muteFields) : "none")}");
    }

    private async Task FetchAndProcessRecentChatMessagesAsync(string chatJid, string chatName, DateTime sinceUtc, string device, CancellationToken token)
    {
        try
        {
            if (IgnoreArchivedChats && IsChatArchived(chatJid, device))
            {
                return;
            }

            bool isGroup = chatJid.EndsWith("@g.us", StringComparison.OrdinalIgnoreCase);
            if (IsChatMuted(chatJid, isGroup, device))
            {
                return;
            }

            using var req = DeviceRequest(HttpMethod.Get, $"/chat/{Uri.EscapeDataString(chatJid)}/messages?limit=5", device);

            using var res = await _httpClient.SendAsync(req, token).ConfigureAwait(false);
            if (!res.IsSuccessStatusCode) return;

            var json = await res.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("results", out var results) ||
                !results.TryGetProperty("data", out var dataArr) ||
                dataArr.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            // Collect messages in chronological order (API returns newest first)
            var items = new List<JsonElement>();
            foreach (var item in dataArr.EnumerateArray())
            {
                items.Add(item.Clone());
            }
            items.Reverse();

            string groupName = "";
            if (isGroup)
            {
                groupName = await ResolveGroupNameAsync(chatJid, device).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(groupName))
                {
                    groupName = !string.IsNullOrWhiteSpace(chatName) && !chatName.StartsWith("Group ", StringComparison.OrdinalIgnoreCase)
                        ? chatName
                        : "Group";
                }
            }

            foreach (var msg in items)
            {
                string id = msg.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
                string content = msg.TryGetProperty("content", out var cProp) ? cProp.GetString() ?? "" : "";
                string senderDisplay = msg.TryGetProperty("sender_display_name", out var sProp) ? sProp.GetString() ?? "" : "";
                string participantJid = msg.TryGetProperty("sender", out var sndProp) ? sndProp.GetString() ?? "" : "";
                string tsStr = msg.TryGetProperty("timestamp", out var tsProp) ? tsProp.GetString() ?? "" : "";
                bool isFromMe = msg.TryGetProperty("is_from_me", out var fmProp) && fmProp.GetBoolean();

                if (!DateTime.TryParse(tsStr, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out var msgUtc))
                {
                    continue;
                }

                if (msgUtc <= sinceUtc) continue;

                if (!string.IsNullOrEmpty(id) && !TryMarkSeen(device + "|" + id))
                {
                    // Already processed via webhook or previous poll
                    continue;
                }

                if (isFromMe)
                {
                    Log($"[Poll] Outgoing reply detected for {chatJid} - auto-dismissing chat");
                    MessageReadOrDismissed?.Invoke(chatJid, null, device);
                }
                else if (isGroup && IsSenderMuted(participantJid, device))
                {
                    // A person you silenced wrote in a group: Remote mode learns about messages only here, so the check has to be here too.
                    Log($"[Poll] Ignoring a message from a muted sender in {chatJid}");
                }
                else if (!string.IsNullOrWhiteSpace(content))
                {
                    RememberName(isGroup ? participantJid : chatJid, !string.IsNullOrWhiteSpace(senderDisplay) ? senderDisplay : (isGroup ? "" : chatName));
                    content = await NameMentionsAsync(content).ConfigureAwait(false);
                    string sender;
                    string senderName = "";

                    if (isGroup)
                    {
                        string participantPhone = FormatPhoneNumber(participantJid);
                        senderName = !string.IsNullOrWhiteSpace(senderDisplay) ? senderDisplay : participantPhone;
                        sender = $"{groupName}: {senderName}";
                    }
                    else
                    {
                        string displayName = !string.IsNullOrWhiteSpace(senderDisplay) ? senderDisplay : chatName;
                        if (!string.IsNullOrWhiteSpace(displayName) && !displayName.StartsWith("Group ", StringComparison.OrdinalIgnoreCase))
                        {
                            sender = displayName;
                            senderName = displayName;
                        }
                        else
                        {
                            string formattedNum = FormatPhoneNumber(chatJid);
                            sender = formattedNum;
                            senderName = formattedNum;
                        }
                    }

                    Log($"[Poll] Incoming WhatsApp message from {sender}: {content}");
                    MessageReceived?.Invoke(new WhatsAppNotification
                    {
                        RemoteId = id,
                        Sender = sender,
                        Message = content,
                        FromJid = chatJid,
                        DeviceId = device,
                    AccountName = AccountNameFor(device),
                        IsGroup = isGroup,
                        GroupName = groupName,
                        SenderName = senderName,
                        ParticipantJid = participantJid,
                        Timestamp = msgUtc.ToLocalTime()
                    });
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Error fetching messages for {chatJid}: {ex.Message}");
        }
    }

    // Read-sync diagnostics for the WhatsApp settings: when the last receipt arrived and how many webhook calls were refused for a bad token.
    private long _lastReadReceiptTicks, _lastReceiptTicks;
    private int _rejectedWebhookCount;
    public DateTime? LastReadReceiptAtUtc => _lastReadReceiptTicks == 0 ? null : new DateTime(Interlocked.Read(ref _lastReadReceiptTicks), DateTimeKind.Utc);
    public DateTime? LastReceiptAtUtc => _lastReceiptTicks == 0 ? null : new DateTime(Interlocked.Read(ref _lastReceiptTicks), DateTimeKind.Utc);
    public int RejectedWebhookCount => Volatile.Read(ref _rejectedWebhookCount);

    private bool TokenMatches(string? presented) =>
        !string.IsNullOrEmpty(RemoteWebhookToken) && presented != null &&
        System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(presented), Encoding.UTF8.GetBytes(RemoteWebhookToken));

    /// <summary>The webhook address to give the shared server (this PC's address on the route to it, plus the secret), or "" when unknown.</summary>
    public string BuildReadSyncUrl()
    {
        try
        {
            if (!Uri.TryCreate(RemoteServerUrl, UriKind.Absolute, out var server) || string.IsNullOrEmpty(RemoteWebhookToken)) return "";
            var address = Dns.GetHostAddresses(server.Host).FirstOrDefault(a => a.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
            if (address == null) return "";
            using var socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Dgram, System.Net.Sockets.ProtocolType.Udp);
            socket.Connect(address, server.Port > 0 ? server.Port : 80); // UDP connect sends nothing; it only selects the local route
            return socket.LocalEndPoint is IPEndPoint local ? $"http://{local.Address}:{WebhookPort}/webhook?token={RemoteWebhookToken}" : "";
        }
        catch { return ""; }
    }

    private void StartWebhookServer(CancellationToken token)
    {
        try
        {
            _httpListener = new HttpListener();
            // Local GOWA talks to loopback. A shared server is on another PC, so the listener must be reachable
            // from the network; requests are accepted only with this PC's secret token (see ProcessWebhookRequestAsync).
            string host = UseRemoteServer ? "+" : "127.0.0.1";
            _httpListener.Prefixes.Add($"http://{host}:{WebhookPort}/");
            _httpListener.Start();
            Log($"Webhook listener started successfully on http://{host}:{WebhookPort}/");

            Task.Run(async () =>
            {
                while (!token.IsCancellationRequested && _httpListener is { IsListening: true })
                {
                    try
                    {
                        var context = await _httpListener.GetContextAsync();
                        if (!await _webhookConcurrency.WaitAsync(0, token))
                        {
                            context.Response.StatusCode = 503;
                            context.Response.Close();
                            continue;
                        }
                        _ = ProcessWebhookRequestWithReleaseAsync(context, token);
                    }
                    catch
                    {
                        break;
                    }
                }
            }, token);
        }
        catch (Exception ex)
        {
            Log($"Failed to start webhook server: {ex.Message}");
        }
    }

    /// <summary>The send time of a webhook message: payload "timestamp" (ISO text or Unix number), else the envelope's.</summary>
    private static DateTimeOffset? ReadPayloadTime(JsonElement payload, JsonElement root)
    {
        foreach (var holder in new[] { payload, root })
        {
            if (!holder.TryGetProperty("timestamp", out var ts)) continue;
            string? text = ts.ValueKind switch
            {
                JsonValueKind.String => ts.GetString(),
                JsonValueKind.Number => ts.GetRawText(),
                _ => null
            };
            var parsed = MochiDock.Core.WhatsAppMessageAgePolicy.ParseMessageTime(text);
            if (parsed != null) return parsed;
        }
        return null;
    }

    private async Task ProcessWebhookRequestWithReleaseAsync(HttpListenerContext context, CancellationToken token)
    {
        try { await ProcessWebhookRequestAsync(context, token); }
        finally { _webhookConcurrency.Release(); }
    }

    private static async Task<string?> ReadBoundedBodyAsync(Stream stream, int maxBytes, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[4096];
        int total = 0;
        int read;
        while ((read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > maxBytes) return null;
            ms.Write(buffer, 0, read);
        }
        return Encoding.UTF8.GetString(ms.ToArray());
    }

    private async Task ProcessWebhookRequestAsync(HttpListenerContext context, CancellationToken token = default)
    {
        try
        {
            if (UseRemoteServer && !TokenMatches(context.Request.QueryString["token"]))
            {
                Interlocked.Increment(ref _rejectedWebhookCount);
                Log("[Webhook] Rejected a request without the correct token.");
                context.Response.StatusCode = 401;
                context.Response.Close();
                return;
            }

            if (context.Request.HttpMethod == "POST")
            {
                using var readCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                readCts.CancelAfter(TimeSpan.FromSeconds(5));
                var body = await ReadBoundedBodyAsync(context.Request.InputStream, MaxWebhookBodyBytes, readCts.Token);
                if (body is null)
                {
                    context.Response.StatusCode = 413;
                    context.Response.Close();
                    return;
                }

                using var doc = JsonDocument.Parse(body);
                var root = doc.RootElement;

                string eventType = root.TryGetProperty("event", out var ev) ? ev.GetString() ?? "" : "";
                // Each event names the account it belongs to; the shared "primary" device is never overwritten by it.
                string device = ResolveDevice(root.TryGetProperty("device_id", out var dev) ? dev.GetString() ?? "" : "");

                // Remote mode takes new messages from polling only; its webhook exists for read receipts.
                if (!UseRemoteServer && eventType.Equals("message", StringComparison.OrdinalIgnoreCase) && root.TryGetProperty("payload", out var payload))
                {
                    string from = payload.TryGetProperty("from", out var f) ? f.GetString() ?? "" : "";
                    string chatId = payload.TryGetProperty("chat_id", out var cidProp) ? cidProp.GetString() ?? "" : "";
                    if (string.IsNullOrEmpty(chatId))
                    {
                        chatId = from;
                    }

                    if (from.Equals("status@broadcast", StringComparison.OrdinalIgnoreCase) ||
                        chatId.Equals("status@broadcast", StringComparison.OrdinalIgnoreCase))
                    {
                        context.Response.StatusCode = 200;
                        context.Response.Close();
                        return;
                    }

                    if (IgnoreArchivedChats && (IsChatArchived(chatId, device) || IsChatArchived(from, device)))
                    {
                        Log($"[Webhook] Ignoring message from archived chat: {chatId}");
                        context.Response.StatusCode = 200;
                        context.Response.Close();
                        return;
                    }

                    bool isGroup = chatId.EndsWith("@g.us", StringComparison.OrdinalIgnoreCase) || from.EndsWith("@g.us", StringComparison.OrdinalIgnoreCase);
                    string targetChatId = isGroup && chatId.EndsWith("@g.us", StringComparison.OrdinalIgnoreCase) ? chatId : (from.EndsWith("@g.us", StringComparison.OrdinalIgnoreCase) ? from : chatId);

                    if (IsChatMuted(targetChatId, isGroup, device) || (isGroup ? IsSenderMuted(from, device) : IsChatMuted(from, false, device)))
                    {
                        Log($"[Webhook] Ignoring message from muted chat/sender: {targetChatId}");
                        context.Response.StatusCode = 200;
                        context.Response.Close();
                        return;
                    }

                    string contactName = "";
                    if (payload.TryGetProperty("sender_display_name", out var sdn) && !string.IsNullOrWhiteSpace(sdn.GetString()))
                    {
                        contactName = sdn.GetString()!.Trim();
                    }
                    else if (payload.TryGetProperty("from_name", out var fn) && !string.IsNullOrWhiteSpace(fn.GetString()))
                    {
                        contactName = fn.GetString()!.Trim();
                    }
                    else if (payload.TryGetProperty("push_name", out var pn) && !string.IsNullOrWhiteSpace(pn.GetString()))
                    {
                        contactName = pn.GetString()!.Trim();
                    }
                    else if (payload.TryGetProperty("name", out var n) && !string.IsNullOrWhiteSpace(n.GetString()))
                    {
                        contactName = n.GetString()!.Trim();
                    }

                    string messageText = "";

                    if (payload.TryGetProperty("body", out var b) && !string.IsNullOrEmpty(b.GetString()))
                    {
                        messageText = b.GetString()!;
                    }
                    else if (payload.TryGetProperty("message", out var msgObj))
                    {
                        if (msgObj.TryGetProperty("conversation", out var conv) && !string.IsNullOrEmpty(conv.GetString()))
                        {
                            messageText = conv.GetString()!;
                        }
                        else if (msgObj.TryGetProperty("extendedTextMessage", out var ext) && ext.TryGetProperty("text", out var extText))
                        {
                            messageText = extText.GetString() ?? "";
                        }
                        else if (msgObj.TryGetProperty("imageMessage", out var img))
                        {
                            string capText = img.TryGetProperty("caption", out var cap) ? (cap.GetString() ?? "") : "";
                            messageText = string.IsNullOrEmpty(capText) ? "📷 [Photo]" : $"📷 {capText}";
                        }
                        else if (msgObj.TryGetProperty("audioMessage", out _))
                        {
                            messageText = "🎤 [Voice Note]";
                        }
                        else if (msgObj.TryGetProperty("videoMessage", out var vid))
                        {
                            string capText = vid.TryGetProperty("caption", out var cap) ? (cap.GetString() ?? "") : "";
                            messageText = string.IsNullOrEmpty(capText) ? "🎥 [Video]" : $"🎥 {capText}";
                        }
                        else if (msgObj.TryGetProperty("stickerMessage", out _))
                        {
                            messageText = "🖼️ [Sticker]";
                        }
                        else if (msgObj.TryGetProperty("documentMessage", out var docMsg))
                        {
                            string fileName = docMsg.TryGetProperty("fileName", out var fnProp) ? (fnProp.GetString() ?? "file") : "file";
                            messageText = $"📄 [{fileName}]";
                        }
                    }

                    string remoteId = "";
                    if (payload.TryGetProperty("id", out var idProp) && !string.IsNullOrEmpty(idProp.GetString()))
                    {
                        remoteId = idProp.GetString()!;
                    }
                    else if (payload.TryGetProperty("key", out var keyObj) && keyObj.TryGetProperty("id", out var keyId))
                    {
                        remoteId = keyId.GetString() ?? "";
                    }

                    if (!string.IsNullOrEmpty(remoteId) && !TryMarkSeen(device + "|" + remoteId))
                    {
                        // Already processed
                        context.Response.StatusCode = 200;
                        context.Response.Close();
                        return;
                    }

                    bool fromMe = payload.TryGetProperty("is_from_me", out var ifm) ? ifm.GetBoolean() : (payload.TryGetProperty("from_me", out var fm) && fm.GetBoolean());
                    if (!fromMe && !string.IsNullOrWhiteSpace(messageText))
                    {
                        RememberName(from, contactName);
                        messageText = await NameMentionsAsync(messageText).ConfigureAwait(false);
                        string sender;
                        string groupName = "";
                        string senderName = "";

                        if (isGroup)
                        {
                            groupName = await ResolveGroupNameAsync(targetChatId, device).ConfigureAwait(false);
                            if (string.IsNullOrWhiteSpace(groupName))
                            {
                                groupName = "Group";
                            }

                            string participantPhone = FormatPhoneNumber(from);
                            senderName = !string.IsNullOrWhiteSpace(contactName) ? contactName : participantPhone;
                            sender = $"{groupName}: {senderName}";
                        }
                        else
                        {
                            string formattedNumber = FormatPhoneNumber(from);
                            sender = !string.IsNullOrWhiteSpace(contactName) ? contactName : formattedNumber;
                            senderName = sender;
                        }

                        // A re-linked or re-synced session replays history as webhook events: only fresh messages may notify.
                        var messageTime = ReadPayloadTime(payload, root);
                        if (!MochiDock.Core.WhatsAppMessageAgePolicy.ShouldNotify(messageTime, DateTimeOffset.Now, DateTime.UtcNow - _serviceStartedUtc))
                        {
                            Log($"[Webhook] Ignoring an old or replayed message from {sender}");
                            context.Response.StatusCode = 200;
                            context.Response.Close();
                            return;
                        }

                        Log($"Incoming WhatsApp message from {sender}: {messageText}");

                        MessageReceived?.Invoke(new WhatsAppNotification
                        {
                            RemoteId = remoteId,
                            Sender = sender,
                            Message = messageText,
                            FromJid = targetChatId,
                            DeviceId = device,
                            AccountName = AccountNameFor(device),
                            IsGroup = isGroup,
                            GroupName = groupName,
                            SenderName = senderName,
                            ParticipantJid = from,
                            Timestamp = messageTime?.LocalDateTime ?? DateTime.Now
                        });
                    }
                    else if (fromMe)
                    {
                        string targetChat = payload.TryGetProperty("to", out var toProp) ? toProp.GetString() ?? "" : "";
                        if (string.IsNullOrEmpty(targetChat))
                        {
                            targetChat = payload.TryGetProperty("chat_id", out var cProp) ? cProp.GetString() ?? "" : "";
                        }
                        if (string.IsNullOrEmpty(targetChat))
                        {
                            targetChat = from;
                        }

                        if (!string.IsNullOrEmpty(targetChat))
                        {
                            Log($"Outgoing reply detected for {targetChat} (fromMe=true) - auto-dismissing chat");
                            MessageReadOrDismissed?.Invoke(targetChat, null, device);
                        }
                    }
                }
                else if (eventType.Equals("message.ack", StringComparison.OrdinalIgnoreCase) && root.TryGetProperty("payload", out var ackPayload))
                {
                    string receiptType = ackPayload.TryGetProperty("receipt_type", out var rt) ? rt.GetString() ?? "" : "";
                    string chatId = ackPayload.TryGetProperty("chat_id", out var cid) ? cid.GetString() ?? "" : "";
                    if (string.IsNullOrEmpty(chatId) && ackPayload.TryGetProperty("from", out var ackFrom))
                    {
                        chatId = ackFrom.GetString() ?? "";
                    }

                    Log($"WhatsApp ACK event: receipt_type='{receiptType}', chat='{chatId}'");

                    long nowTicks = DateTime.UtcNow.Ticks;
                    Interlocked.Exchange(ref _lastReceiptTicks, nowTicks);
                    if (MochiDock.Core.AckReceiptPolicy.IsReadReceipt(receiptType)) Interlocked.Exchange(ref _lastReadReceiptTicks, nowTicks);

                    // "read-self" (you read it elsewhere) clears by ids, or the whole chat without ids. A plain "read" clears ONLY by ids (a contact reading
                    // your own message names ids that match no incoming notification), never a whole chat. See AckReceiptPolicy.Decide.
                    bool? ackFromMe = ackPayload.TryGetProperty("is_from_me", out var ackMine) && ackMine.ValueKind is JsonValueKind.True or JsonValueKind.False ? ackMine.GetBoolean()
                        : ackPayload.TryGetProperty("from_me", out var ackMine2) && ackMine2.ValueKind is JsonValueKind.True or JsonValueKind.False ? ackMine2.GetBoolean() : null;
                    {
                        List<string> readIds = new();
                        if (ackPayload.TryGetProperty("ids", out var idsArray) && idsArray.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var elem in idsArray.EnumerateArray())
                            {
                                string val = elem.GetString() ?? "";
                                if (!string.IsNullOrEmpty(val)) readIds.Add(val);
                            }
                        }
                        else if (ackPayload.TryGetProperty("id", out var singleId) && !string.IsNullOrEmpty(singleId.GetString()))
                        {
                            readIds.Add(singleId.GetString()!);
                        }

                        var dismissal = MochiDock.Core.AckReceiptPolicy.Decide(receiptType, ackFromMe, readIds.Count > 0);
                        if (dismissal == MochiDock.Core.ReceiptDismissal.None)
                        {
                            Log($"Receipt '{receiptType}' for {chatId} with {readIds.Count} message IDs does not dismiss anything");
                        }
                        else
                        {
                            Log($"Chat read detected for {chatId} with {readIds.Count} message IDs ({dismissal}) - triggering auto-dismissal");
                            MessageReadOrDismissed?.Invoke(chatId, dismissal == MochiDock.Core.ReceiptDismissal.ByIds ? readIds : null, device);
                        }
                    }
                }
            }

            context.Response.StatusCode = 200;
            var responseBytes = Encoding.UTF8.GetBytes("{\"status\":\"ok\"}");
            context.Response.ContentType = "application/json";
            context.Response.OutputStream.Write(responseBytes, 0, responseBytes.Length);
            context.Response.Close();
        }
        catch (Exception ex)
        {
            Log($"Error processing webhook: {ex.Message}");
            try
            {
                context.Response.StatusCode = 500;
                context.Response.Close();
            }
            catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Gowa", logEx); }
        }
    }

    public async Task<bool> SendReplyAsync(string toJid, string message, string deviceId = "")
    {
        try
        {
            string activeDevice = !string.IsNullOrEmpty(deviceId) ? deviceId : ConnectedDeviceId;
            string url = $"{ActiveBaseUrl}/send/message";
            if (!string.IsNullOrEmpty(activeDevice))
            {
                url += $"?device_id={Uri.EscapeDataString(activeDevice)}";
            }

            string phone = toJid.Replace("@s.whatsapp.net", "").Replace("@c.us", "");

            var payload = new
            {
                phone = phone,
                message = message
            };

            var json = JsonSerializer.Serialize(payload);
            using var req = CreateRequest(HttpMethod.Post, url);
            req.Content = new StringContent(json, Encoding.UTF8, "application/json");
            if (!string.IsNullOrEmpty(activeDevice))
            {
                req.Headers.TryAddWithoutValidation("X-Device-Id", activeDevice);
            }

            using var response = await _httpClient.SendAsync(req);
            Log($"Sent reply to {phone} via {ActiveBaseUrl}, status: {response.StatusCode}");
            return response.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Log($"Error sending WhatsApp reply: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Marks messages of a chat as read on the server (GOWA <c>POST /message/:id/read</c>). WhatsApp then sends the read receipt to every
    /// linked device and the server forwards it to every MochiDock, so the notification clears on the other PCs too.
    /// Best effort: a failure is logged and never reported to the caller, because the reply that triggered it already went out.
    /// </summary>
    public async Task MarkChatReadAsync(string chatJid, IEnumerable<string> remoteIds, string deviceId = "")
    {
        string activeDevice = !string.IsNullOrEmpty(deviceId) ? deviceId : ConnectedDeviceId;
        foreach (string id in remoteIds)
        {
            try
            {
                string url = $"{ActiveBaseUrl}/message/{Uri.EscapeDataString(id)}/read";
                if (!string.IsNullOrEmpty(activeDevice)) url += $"?device_id={Uri.EscapeDataString(activeDevice)}";
                var json = JsonSerializer.Serialize(new { phone = chatJid });
                using var req = CreateRequest(HttpMethod.Post, url);
                req.Content = new StringContent(json, Encoding.UTF8, "application/json");
                if (!string.IsNullOrEmpty(activeDevice)) req.Headers.TryAddWithoutValidation("X-Device-Id", activeDevice);
                using var response = await _httpClient.SendAsync(req);
                Log($"Marked {id} read in {chatJid}, status: {response.StatusCode}");
                if (!response.IsSuccessStatusCode) break;   // the server does not take the call: do not repeat it for every message
            }
            catch (Exception ex)
            {
                Log($"Error marking a WhatsApp message read: {ex.Message}");
                break;
            }
        }
    }

    public void OpenDashboard()
    {
        try
        {
            using var _ = Process.Start(new ProcessStartInfo
            {
                FileName = DashboardUrl,
                UseShellExecute = true
            });
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Gowa", logEx); }
    }
}
