using MochiDock.Core;
using MochiDock.Models;

namespace MochiDock.Services;

/// <summary>
/// Classic Outlook mail source. The COM work lives in <see cref="OutlookComReader"/>. When MochiDock is elevated
/// (the normal case) the reader runs in a non-elevated helper process, because Windows blocks COM between an
/// elevated client and a normal Outlook; otherwise it runs in this process.
/// </summary>
public class OutlookMailService : IMailService
{
    public string ServiceName => "Outlook";
    public bool IsAvailable => OutlookComReader.IsInstalled;

    public event Action<EmailNotification>? EmailReceived;

    public bool FilterAds { get; set; } = true; // set from AppSettings.FilterAds at startup

    /// <summary>Folder names (besides every Inbox) watched with their subfolders; set from AppSettings.OutlookTrackedFolders at startup.</summary>
    public IReadOnlyList<string> TrackedFolders { get; set; } = OutlookFolderPolicy.DefaultTrackedFolders;

    /// <summary>Watch only the listed folders and their subfolders (not the Inbox); set from AppSettings.OutlookOnlyListedFolders.</summary>
    public bool OnlyListedFolders { get; set; }

    OutlookComReader? _reader;
    OutlookHelperHost? _host;

    public void Start()
    {
        Stop();

        bool elevated = OutlookHelperHost.IsElevated();
        if (elevated && OutlookHelperHost.IsHelperAvailable)
        {
            _host = new OutlookHelperHost { TrackedFolders = TrackedFolders, OnlyListedFolders = OnlyListedFolders };
            _host.MailArrived += OnMail;
            _host.Start();
            return;
        }

        if (elevated)
            AppLog.Warn("Outlook", "MochiDock is elevated but the Outlook helper is missing; Outlook cannot be reached from an elevated process.");

        _reader = new OutlookComReader(message => AppLog.Warn("Outlook", message));
        _reader.MailArrived += OnMail;
        _reader.SetTrackedFolders(TrackedFolders, OnlyListedFolders);
        _reader.Start();
    }

    public void Stop()
    {
        _host?.Dispose();
        _host = null;
        _reader?.Stop();
        _reader = null;
    }

    /// <summary>Applies a new tracked-folder list to the running reader or helper.</summary>
    public void UpdateTrackedFolders(IReadOnlyList<string> names, bool onlyListed)
    {
        TrackedFolders = names;
        OnlyListedFolders = onlyListed;
        _host?.SendTrackedFolders(names, onlyListed);
        _reader?.SetTrackedFolders(names, onlyListed);
    }

    public void OpenEmail(EmailNotification email)
    {
        if (_host is not null) _host.Open(email.EntryId);
        else if (_reader is not null) _reader.Open(email.EntryId);
        else
        {
            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("outlook.exe") { UseShellExecute = true }); }
            catch (Exception ex) { AppLog.Warn("Outlook", ex); }
        }
    }

    public void MarkAsRead(EmailNotification email)
    {
        if (_host is not null) _host.MarkRead(email.EntryId);
        else _reader?.MarkRead(email.EntryId);
    }

    public bool CanMarkAsRead(EmailNotification email) => true;

    void OnMail(OutlookMailDto mail)
    {
        try
        {
            if (FilterAds && MailAdFilter.IsAdvertisement(mail.Sender, mail.Subject, mail.Body))
            {
                AppLog.Warn("Outlook", $"Skipped as advertisement: \"{mail.Subject}\" from {mail.Sender}");
                return;
            }

            AppLog.Warn("Outlook", $"Mail reached the dock: \"{mail.Subject}\" from {mail.Sender}");
            EmailReceived?.Invoke(new EmailNotification
            {
                EntryId = mail.EntryId,
                Sender = mail.Sender,
                Subject = mail.Subject,
                BodyPreview = CleanPreview(mail.Body),
                FolderName = mail.FolderName,
                ClientSource = "Outlook",
                ReceivedTime = mail.ReceivedTime,
                ExtractedVerificationCode = Helpers.CodeDetectionHelper.ExtractVerificationCode(mail.Subject, mail.Body)
            });
        }
        catch (Exception ex) { AppLog.Warn("Outlook", ex); }
    }

    static string CleanPreview(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "(No content)";
        var clean = System.Text.RegularExpressions.Regex.Replace(body, @"\s+", " ").Trim();
        if (clean.Length > 90) clean = clean[..90] + "...";
        return clean;
    }
}
