using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
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
        // Also persist immediately when recovered from .bak so a subsequent Load() does not see a missing file and reset to defaults.
        bool needsSave = result.Status == JsonLoadStatus.Recovered;
        if (string.IsNullOrWhiteSpace(loaded.RemoteWebhookToken))
        {
            loaded.RemoteWebhookToken = GowaService.NewWebhookToken();
            needsSave = true;
        }
        if (needsSave && result.CanWrite) { Save(loaded); FlushAsync().GetAwaiter().GetResult(); }
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
    /// <summary>
    /// Writes a portable export (<see cref="MochiDock.Core.SettingsTransfer.Format"/>): the saved settings without secrets or
    /// this PC's own values, plus <paramref name="tasks"/>. Mail and WhatsApp passwords live in DPAPI stores and never leave the PC.
    /// </summary>
    public static void Export(string path, JsonArray tasks)
    {
        FlushAsync().GetAwaiter().GetResult();
        var settings = JsonNode.Parse(File.ReadAllText(SettingsPath)) as JsonObject ?? throw new JsonException("Settings must be an object.");
        var export = MochiDock.Core.SettingsTransfer.BuildExport(settings, tasks);
        File.WriteAllText(path, export.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>A validated import that has not been saved yet. <see cref="Tasks"/> is null for a plain settings.json.</summary>
    public sealed record ImportedSettings(AppSettings Settings, JsonArray? Tasks);

    /// <summary>
    /// Reads an export file or an older plain settings.json, keeps this PC's values for the local-only keys, then validates
    /// and migrates the result. Nothing is written; call <see cref="CommitImport"/> once everything in the file is accepted.
    /// </summary>
    public static ImportedSettings ReadImport(string path)
    {
        var (imported, tasks) = MochiDock.Core.SettingsTransfer.Parse(File.ReadAllText(path));
        FlushAsync().GetAwaiter().GetResult();
        MochiDock.Core.SettingsTransfer.Merge(imported, ReadCurrentJson());
        var settings = imported.Deserialize<AppSettings>() ?? throw new JsonException("Settings must be an object.");
        if (!MochiDock.Core.SettingsValidation.IsValidPort(settings.GowaPort) || !MochiDock.Core.SettingsValidation.IsValidPort(settings.WebhookPort) ||
            !double.IsFinite(settings.TopMargin) || !double.IsFinite(settings.CapsuleScrollSpeed))
            throw new JsonException("Invalid port or display value in settings.");
        if(settings.PrayerAudioVersion==0){settings.PrayerSoundMode=MochiDock.Core.PrayerTime.MigrateSound(settings.EnableAdhanAudio,settings.PrayerSoundMode);settings.PrayerAudioVersion=1;}
        if(settings.PrayerSoundMode is not ("Silent" or "Tone" or "Adzan"))throw new JsonException("Invalid prayer sound mode.");
        settings.PrayerSoundMode=MochiDock.Core.PrayerTime.NormalizeSound(settings.PrayerSoundMode);settings.PrayerAudioVersion=2;
        settings.LyricsOffsetMs=MochiDock.Core.LyricOffset.Clamp(settings.LyricsOffsetMs);
        settings.MigrateTasksTab();
        settings.MigrateWhatsAppOptIn(HasGowaSession());
        // The webhook token is never exported; a PC without one gets a fresh token, as on a normal start.
        if (string.IsNullOrWhiteSpace(settings.RemoteWebhookToken)) settings.RemoteWebhookToken = GowaService.NewWebhookToken();
        return new ImportedSettings(settings, tasks);
    }

    /// <summary>Saves imported settings, even over a settings file that failed to load (an explicit user action).</summary>
    public static void CommitImport(AppSettings settings)
    {
        FlushAsync().GetAwaiter().GetResult();
        AtomicJsonStore.SaveJson(SettingsPath, JsonSerializer.Serialize(settings), explicitImport: true);
    }

    /// <summary>Reads and saves an import in one step. Returns the saved settings and the file's tasks (not applied here).</summary>
    public static ImportedSettings Import(string path)
    {
        var import = ReadImport(path);
        CommitImport(import.Settings);
        return import;
    }

    /// <summary>The saved settings.json as JSON, or null when it is missing or unreadable (local-only keys then fall back to defaults).</summary>
    private static JsonObject? ReadCurrentJson()
    {
        try { return File.Exists(SettingsPath) ? JsonNode.Parse(File.ReadAllText(SettingsPath)) as JsonObject : null; }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException) { AppLog.Warn("Settings", ex); return null; }
    }
}
