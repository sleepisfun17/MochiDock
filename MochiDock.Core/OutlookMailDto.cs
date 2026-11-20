using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MochiDock.Core;

/// <summary>A mail item read from Outlook, reduced to plain data so it can cross a process boundary.</summary>
public sealed record OutlookMailDto(string EntryId, string Sender, string Subject, string Body, string FolderName, DateTime ReceivedTime);

/// <summary>
/// One JSON line sent over the pipe between MochiDock (elevated) and the Outlook helper (not elevated).
/// T is the message type: hello, config, mail, log, open, read, stop.
/// </summary>
public sealed class OutlookPipeMessage
{
    [JsonPropertyName("t")] public string T { get; set; } = "";
    [JsonPropertyName("secret")] public string? Secret { get; set; }
    [JsonPropertyName("id")] public string? Id { get; set; }
    [JsonPropertyName("msg")] public string? Msg { get; set; }
    [JsonPropertyName("mail")] public OutlookMailDto? Mail { get; set; }
    /// <summary>On a config message: watch only the listed folders (and their subfolders). Absent means the default scope.</summary>
    [JsonPropertyName("only")] public bool? OnlyListed { get; set; }

    static readonly JsonSerializerOptions Options = new() { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    public string Encode() => JsonSerializer.Serialize(this, Options);

    public static OutlookPipeMessage? TryDecode(string? line)
    {
        if (string.IsNullOrWhiteSpace(line)) return null;
        try { return JsonSerializer.Deserialize<OutlookPipeMessage>(line, Options); }
        catch (JsonException) { return null; }
    }

    public static OutlookPipeMessage Hello(string secret) => new() { T = "hello", Secret = secret };
    public static OutlookPipeMessage MailMessage(OutlookMailDto mail) => new() { T = "mail", Mail = mail };
    public static OutlookPipeMessage Log(string message) => new() { T = "log", Msg = message };
    public static OutlookPipeMessage Open(string entryId) => new() { T = "open", Id = entryId };
    public static OutlookPipeMessage MarkRead(string entryId) => new() { T = "read", Id = entryId };
    /// <summary>Tracked folder names, joined with a bar (see OutlookFolderPolicy).</summary>
    public static OutlookPipeMessage Config(string trackedFolders, bool onlyListed = false) =>
        new() { T = "config", Msg = trackedFolders, OnlyListed = onlyListed ? true : null };
    public static OutlookPipeMessage Stop() => new() { T = "stop" };
}
