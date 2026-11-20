using System.IO;
using System.Text.Json;
using MochiDock.Models;

namespace MochiDock.Services;

public static class SettingsService
{
    private static string SettingsPath => Path.Combine(AppDataPaths.Roaming, "settings.json");
    public static string SettingsFilePath => SettingsPath;
    private static readonly object QueueGate = new();
    private static Task Pending = Task.CompletedTask;
    public static event Action<string>? PersistenceError;

    public static AppSettings Load()
    {
        string legacy = Path.Combine(AppContext.BaseDirectory, "settings.json");
        if (!File.Exists(SettingsPath) && File.Exists(legacy))
        {
            var old = AtomicJsonStore.Load(legacy, () => new AppSettings());
            if (old.Status == JsonLoadStatus.Loaded)
                try { AtomicJsonStore.Save(SettingsPath, old.Value); } catch (Exception ex) { PersistenceError?.Invoke(ex.Message); }
        }
        // A fresh install has no Tasks tab to migrate, so Today stays off like the other optional tabs.
        bool fresh = !File.Exists(SettingsPath);
        var result=AtomicJsonStore.Load(SettingsPath, () => new AppSettings { TasksTabMerged = fresh, WhatsAppOptInMigrated = fresh });
        if(result.Status==JsonLoadStatus.Loaded) BackUpDaily();
        var loaded=result.Value;
        if(loaded.PrayerAudioVersion==0)
        {loaded.PrayerSoundMode=MochiDock.Core.PrayerTime.MigrateSound(loaded.EnableAdhanAudio,loaded.PrayerSoundMode);loaded.PrayerAudioVersion=1;}
        loaded.PrayerSoundMode=MochiDock.Core.PrayerTime.NormalizeSound(loaded.PrayerSoundMode);loaded.PrayerAudioVersion=2;
        loaded.LyricsOffsetMs=MochiDock.Core.LyricOffset.Clamp(loaded.LyricsOffsetMs);
        loaded.MigrateTasksTab();
        loaded.MigrateWhatsAppOptIn(HasGowaSession());
        // The WhatsApp webhook token is created once and kept, so a shared GOWA server's webhook URL stays valid across restarts.
        if (string.IsNullOrWhiteSpace(loaded.RemoteWebhookToken))
        {
            loaded.RemoteWebhookToken = GowaService.NewWebhookToken();
            if (result.CanWrite) { Save(loaded); FlushAsync().GetAwaiter().GetResult(); }
        }
        return loaded;
    }

    /// <summary>
    /// Copies a settings file that loaded cleanly to settings.backup-yyyyMMdd.json once a day and keeps the newest three, so a file
    /// that was overwritten by mistake can be restored by hand. Best effort: a failure is logged and never stops the app.
    /// </summary>
    private static void BackUpDaily()
    {
        try
        {
            string folder = Path.GetDirectoryName(SettingsPath)!;
            string today = Path.Combine(folder, MochiDock.Core.SettingsBackupPolicy.FileName(DateTime.Now));
            if (!File.Exists(today)) File.Copy(SettingsPath, today);
            var names = Directory.EnumerateFiles(folder, "settings.backup-*.json").Select(Path.GetFileName).OfType<string>();
            foreach (var old in MochiDock.Core.SettingsBackupPolicy.ToDelete(names)) File.Delete(Path.Combine(folder, old));
        }
        catch (Exception ex) { AppLog.Warn("Settings", ex); }
    }

    /// <summary>True when this PC holds local GOWA session data, i.e. a WhatsApp device was linked here before.</summary>
    private static bool HasGowaSession()
    {
        try
        {
            string dir = Path.Combine(AppDataPaths.Local, "GOWA", "storages");
            return Directory.Exists(dir) && Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Any();
        }
        catch { return false; }
    }

    public static void Save(AppSettings settings)
    {
        string snapshot = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
        lock (QueueGate)
            Pending = Pending.ContinueWith(_ =>
            {
                try { AtomicJsonStore.SaveJson(SettingsPath, snapshot); }
                catch (Exception ex) { PersistenceError?.Invoke(ex.Message); }
            }, TaskScheduler.Default);
    }

    public static Task FlushAsync() { lock (QueueGate) return Pending; }
    public static void Export(string path) { FlushAsync().GetAwaiter().GetResult(); File.Copy(SettingsPath, path, true); }
    public static AppSettings Import(string path)
    {
        string json = File.ReadAllText(path);
        var settings = JsonSerializer.Deserialize<AppSettings>(json) ?? throw new JsonException("Settings must be an object.");
        if (!MochiDock.Core.SettingsValidation.IsValidPort(settings.GowaPort) || !MochiDock.Core.SettingsValidation.IsValidPort(settings.WebhookPort) ||
            !double.IsFinite(settings.TopMargin) || !double.IsFinite(settings.CapsuleScrollSpeed))
            throw new JsonException("Invalid port or display value in settings.");
        FlushAsync().GetAwaiter().GetResult();
        if(settings.PrayerAudioVersion==0){settings.PrayerSoundMode=MochiDock.Core.PrayerTime.MigrateSound(settings.EnableAdhanAudio,settings.PrayerSoundMode);settings.PrayerAudioVersion=1;}
        if(settings.PrayerSoundMode is not ("Silent" or "Tone" or "Adzan"))throw new JsonException("Invalid prayer sound mode.");
        settings.PrayerSoundMode=MochiDock.Core.PrayerTime.NormalizeSound(settings.PrayerSoundMode);settings.PrayerAudioVersion=2;
        settings.LyricsOffsetMs=MochiDock.Core.LyricOffset.Clamp(settings.LyricsOffsetMs);
        settings.MigrateTasksTab();
        settings.MigrateWhatsAppOptIn(HasGowaSession());
        AtomicJsonStore.SaveJson(SettingsPath, JsonSerializer.Serialize(settings), explicitImport: true);
        return settings;
    }
}
