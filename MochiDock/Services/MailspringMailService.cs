using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using MochiDock.Models;
using Microsoft.Data.Sqlite;

namespace MochiDock.Services;

public class MailspringMailService : IMailService
{
    public string ServiceName => "Mailspring";
    public bool IsAvailable => File.Exists(_dbPath);

    public event Action<EmailNotification>? EmailReceived;

    private readonly string _appDataPath;
    private readonly string _dbPath;
    private readonly string _configPath;
    private readonly Dictionary<string, string> _accountMap = new(); // accountId -> emailAddress
    private readonly DispatcherTimer _pollTimer;
    private long _lastMaxDate;
    private bool _initialized;

    public MailspringMailService()
    {
        _appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Mailspring");
        _dbPath = Path.Combine(_appDataPath, "edgehill.db");
        _configPath = Path.Combine(_appDataPath, "config.json");

        _pollTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _pollTimer.Tick += (s, e) => CheckNewEmails();
    }

    public void Start()
    {
        if (!IsAvailable) return;

        LoadAccountsFromConfig();
        InitLastMaxDate();
        _pollTimer.Start();
    }

    public void Stop()
    {
        _pollTimer.Stop();
    }

    private void LoadAccountsFromConfig()
    {
        _accountMap.Clear();
        if (!File.Exists(_configPath)) return;

        try
        {
            using var stream = File.OpenRead(_configPath);
            using var doc = JsonDocument.Parse(stream);
            var root = doc.RootElement;

            if (root.TryGetProperty("*", out var starProp) && starProp.TryGetProperty("accounts", out var accsProp))
            {
                foreach (var acc in accsProp.EnumerateArray())
                {
                    string id = acc.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
                    string email = acc.TryGetProperty("emailAddress", out var emailProp) ? emailProp.GetString() ?? "" : "";
                    if (!string.IsNullOrEmpty(id) && !string.IsNullOrEmpty(email))
                    {
                        _accountMap[id] = email;
                    }
                }
            }
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Mailspring", logEx); }
    }

    private void InitLastMaxDate()
    {
        try
        {
            using var con = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly");
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = "SELECT MAX(date) FROM Message WHERE unread = 1 AND draft = 0;";
            var res = cmd.ExecuteScalar();
            if (res != null && res != DBNull.Value)
            {
                _lastMaxDate = Convert.ToInt64(res);
            }
            else
            {
                _lastMaxDate = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 60;
            }
            _initialized = true;
        }
        catch
        {
            _lastMaxDate = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 60;
        }
    }

    private void CheckNewEmails()
    {
        if (!_initialized)
        {
            InitLastMaxDate();
            return;
        }

        try
        {
            using var con = new SqliteConnection($"Data Source={_dbPath};Mode=ReadOnly");
            con.Open();
            using var cmd = con.CreateCommand();
            cmd.CommandText = @"
                SELECT id, accountId, subject, date, data 
                FROM Message 
                WHERE unread = 1 AND draft = 0 AND date > @lastDate 
                ORDER BY date ASC 
                LIMIT 5;";
            cmd.Parameters.AddWithValue("@lastDate", _lastMaxDate);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                string id = reader.GetString(0);
                string accountId = reader.IsDBNull(1) ? "" : reader.GetString(1);
                string subject = reader.IsDBNull(2) ? "(No Subject)" : reader.GetString(2);
                long dateUnix = reader.GetInt64(3);
                string dataJson = reader.IsDBNull(4) ? "{}" : reader.GetString(4);

                if (dateUnix > _lastMaxDate)
                {
                    _lastMaxDate = dateUnix;
                }

                string senderDisplay = ParseSender(dataJson);
                string snippet = ParseSnippet(dataJson);
                string accountEmail = _accountMap.TryGetValue(accountId, out var mappedEmail) ? mappedEmail : "Gmail";

                var notif = new EmailNotification
                {
                    MessageId = id,
                    Sender = senderDisplay,
                    Subject = string.IsNullOrWhiteSpace(subject) ? "(No Subject)" : subject,
                    BodyPreview = snippet,
                    AccountEmail = accountEmail,
                    FolderName = accountEmail,
                    ClientSource = "Mailspring",
                    ReceivedTime = DateTimeOffset.FromUnixTimeSeconds(dateUnix).LocalDateTime,
                    ExtractedVerificationCode = Helpers.CodeDetectionHelper.ExtractVerificationCode(subject, snippet)
                };

                EmailReceived?.Invoke(notif);
            }
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Mailspring", logEx); }
    }

    private static string ParseSender(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("from", out var fromArr) && fromArr.GetArrayLength() > 0)
            {
                var first = fromArr[0];
                string name = first.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                string email = first.TryGetProperty("email", out var e) ? e.GetString() ?? "" : "";

                if (!string.IsNullOrWhiteSpace(name)) return name;
                if (!string.IsNullOrWhiteSpace(email)) return email;
            }
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Mailspring", logEx); }
        return "Unknown Sender";
    }

    private static string ParseSnippet(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("snippet", out var s))
            {
                return s.GetString() ?? "";
            }
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Mailspring", logEx); }
        return "";
    }

    public void OpenEmail(EmailNotification email)
    {
        try
        {
            string localExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Mailspring", "mailspring.exe");
            if (File.Exists(localExe))
            {
                Process.Start(new ProcessStartInfo(localExe) { UseShellExecute = true });
            }
            else
            {
                Process.Start(new ProcessStartInfo("mailspring://") { UseShellExecute = true });
            }
        }
        catch (System.Exception logEx) { MochiDock.Services.AppLog.Warn("Mailspring", logEx); }
    }

    public void MarkAsRead(EmailNotification email)
    {
        // Marking read in Mailspring happens via its sync engine or when opening the client
    }

    // Mailspring's database is opened read only, so MochiDock cannot mark its mail read.
    public bool CanMarkAsRead(EmailNotification email) => false;
}