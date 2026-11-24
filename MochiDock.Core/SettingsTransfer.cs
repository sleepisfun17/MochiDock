using System.Text.Json;
using System.Text.Json.Nodes;

namespace MochiDock.Core;

/// <summary>
/// The portable export file (Settings &gt; Data &amp; diagnostics): settings without secrets or values that only make sense on
/// the PC that wrote them, plus the task list. Pure JSON logic; reading and writing files is done by the app.
/// </summary>
public static class SettingsTransfer
{
    /// <summary>Format marker of the export file. A plain settings.json (exports before this format) has no marker.</summary>
    public const string Format = "MochiDock-export/1";
    const string FormatPrefix = "MochiDock-export/";

    /// <summary>
    /// Settings keys (as serialized in settings.json) that are never exported and that an import never overwrites: the webhook
    /// secret, and values tied to this PC's monitors, files, ports, devices, startup registration or local timers.
    /// </summary>
    public static IReadOnlyList<string> LocalOnlyKeys { get; } = new[]
    {
        "RemoteWebhookToken", "TargetMonitor", "TopMargin", "GamingTopOffset", "IslandHorizontalRatio",
        "SettingsWindowWidth", "SettingsWindowHeight", "PrayerAudioWavPath", "StartWithWindows", "LastUpdateCheckUtc", "LastAnnouncedUpdate",
        "SnoozeUntilTicks", "GowaPort", "WebhookPort", "PowerComponents", "HiddenDeviceBatteryKeys", "PreferredGamingResolution",
        "TaskbarItemsMigrated", "TaskbarShowCpuLoad", "TaskbarShowCpuTemp", "TaskbarShowGpuLoad", "TaskbarShowGpuTemp",
        "TaskbarShowMemory", "TaskbarShowDisk", "TaskbarShowUpload", "TaskbarShowDownload",
        "TaskbarShowPrayer", "TaskbarShowWeatherTemp", "TaskbarShowWeatherWind",
        "TaskbarBlockOrder", "TaskbarBlockZones", "TaskbarMonitor", "TaskbarPlacement", "TaskbarOffsetPx",
        "TaskbarLeftOffsetPx", "TaskbarVerticalOffsetPx",
        "TaskbarFontFamily", "TaskbarFontSize", "TaskbarFontBold", "TaskbarBlockSpacing", "TaskbarStableWidth",
        "TaskbarLabelColor", "TaskbarValueColor", "TaskbarNetworkArrows", "TaskbarNetworkUnit",
        "TaskbarSwapNetworkLines", "TaskbarCompactLabels", "TaskbarRefreshSeconds", "TaskbarBatteryLayout",
        "TaskbarPrayerDisplay", "TaskbarPrayerApproachMinutes", "TaskbarPrayerElapsedMinutes", "TaskbarWeatherIcon",
        "ShowDeviceBatteriesOnTaskbar", "ShowDeviceBatteryBackground",
    };

    private static readonly string[] LegacyLocalOnlyKeys =
    {
        "TaskbarShowSystem", "TaskbarShowMemoryDisk", "TaskbarShowNetwork", "TaskbarShowWeather",
    };

    /// <summary>Removes the local-only keys from <paramref name="settings"/> and returns it.</summary>
    public static JsonObject Strip(JsonObject settings)
    {
        foreach (var key in LocalOnlyKeys) settings.Remove(key);
        foreach (var key in LegacyLocalOnlyKeys) settings.Remove(key);
        return settings;
    }

    /// <summary>
    /// Gives <paramref name="imported"/> this PC's value for every local-only key, taken from <paramref name="current"/>.
    /// A key this PC does not have is removed, so the setting falls back to its default. Returns <paramref name="imported"/>.
    /// </summary>
    public static JsonObject Merge(JsonObject imported, JsonObject? current)
    {
        foreach (var key in LocalOnlyKeys)
        {
            imported.Remove(key);
            if (current != null && current.TryGetPropertyValue(key, out var value))
                imported[key] = value?.DeepClone();
        }
        bool currentMigrated = current != null
            && current.TryGetPropertyValue("TaskbarItemsMigrated", out var migNode)
            && migNode is JsonValue migVal
            && migVal.TryGetValue<bool>(out var mig) && mig;
        foreach (var key in LegacyLocalOnlyKeys)
        {
            imported.Remove(key);
            if (!currentMigrated && current != null && current.TryGetPropertyValue(key, out var value))
                imported[key] = value?.DeepClone();
        }
        return imported;
    }

    /// <summary>Builds the export document from the current settings (a copy is stripped) and the task list.</summary>
    public static JsonObject BuildExport(JsonObject settings, JsonArray tasks) => new()
    {
        ["format"] = Format,
        ["settings"] = Strip((JsonObject)settings.DeepClone()),
        ["tasks"] = tasks.DeepClone(),
    };

    /// <summary>
    /// Reads an export file or an older plain settings.json. Returns the settings object and the tasks (null for a plain
    /// settings.json, which carries none). Throws <see cref="JsonException"/> for anything else.
    /// </summary>
    public static (JsonObject Settings, JsonArray? Tasks) Parse(string json)
    {
        var root = JsonNode.Parse(json, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip })
                   as JsonObject ?? throw new JsonException("Settings must be an object.");
        if (!root.TryGetPropertyValue("format", out var formatNode))
            return (root, null);

        string? format = formatNode is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;
        if (format == null || !format.StartsWith(FormatPrefix, StringComparison.Ordinal))
            throw new JsonException("This is not a MochiDock export file.");
        if (format != Format)
            throw new JsonException("This export was made by a newer MochiDock. Update MochiDock and try again.");
        if (root["settings"] is not JsonObject settings)
            throw new JsonException("The export file has no settings.");

        JsonArray? tasks = null;
        if (root.TryGetPropertyValue("tasks", out var tasksNode) && tasksNode != null)
        {
            tasks = tasksNode as JsonArray ?? throw new JsonException("Tasks in the export file must be a list.");
            if (tasks.Any(t => t is not JsonObject)) throw new JsonException("Every task in the export file must be an object.");
        }
        // Detach both so callers can modify them or attach them elsewhere.
        root.Remove("settings");
        if (tasks != null) root.Remove("tasks");
        return (settings, tasks);
    }

    /// <summary>
    /// Merges imported items into local ones by id: an imported item replaces the local item with the same id (in place),
    /// local items missing from the import are kept, and imported items new to this PC come first, in file order.
    /// Ids compare ordinally; when the import repeats an id, its last item wins.
    /// </summary>
    public static List<T> MergeById<T>(IEnumerable<T> local, IEnumerable<T> imported, Func<T, string> id)
    {
        var incoming = new Dictionary<string, T>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var item in imported)
        {
            string key = id(item);
            if (!incoming.ContainsKey(key)) order.Add(key);
            incoming[key] = item;
        }

        var localList = local.ToList();
        var localIds = new HashSet<string>(localList.Select(id), StringComparer.Ordinal);
        var result = order.Where(k => !localIds.Contains(k)).Select(k => incoming[k]).ToList();
        result.AddRange(localList.Select(item => incoming.TryGetValue(id(item), out var replacement) ? replacement : item));
        return result;
    }
}
