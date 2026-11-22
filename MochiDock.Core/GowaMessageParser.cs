using System;
using System.Text.Json;

namespace MochiDock.Core;

/// <summary>
/// Pure parsing and formatting helpers for incoming GOWA webhook and polling message payloads.
/// </summary>
public static class GowaMessageParser
{
    public static string ExtractContactName(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object) return "";

        foreach (string key in new[] { "sender_display_name", "from_name", "push_name", "name" })
        {
            if (payload.TryGetProperty(key, out var prop) &&
                prop.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(prop.GetString()))
            {
                return prop.GetString()!.Trim();
            }
        }

        return "";
    }

    public static string ExtractMessageText(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object) return "";

        if (payload.TryGetProperty("body", out var bodyProp) &&
            bodyProp.ValueKind == JsonValueKind.String &&
            !string.IsNullOrEmpty(bodyProp.GetString()))
        {
            return bodyProp.GetString()!;
        }

        if (!payload.TryGetProperty("message", out var msgObj) || msgObj.ValueKind != JsonValueKind.Object)
        {
            return "";
        }

        if (msgObj.TryGetProperty("conversation", out var conv) &&
            conv.ValueKind == JsonValueKind.String &&
            !string.IsNullOrEmpty(conv.GetString()))
        {
            return conv.GetString()!;
        }

        if (msgObj.TryGetProperty("extendedTextMessage", out var ext) &&
            ext.ValueKind == JsonValueKind.Object &&
            ext.TryGetProperty("text", out var extText) &&
            extText.ValueKind == JsonValueKind.String)
        {
            return extText.GetString() ?? "";
        }

        if (msgObj.TryGetProperty("imageMessage", out var img) && img.ValueKind == JsonValueKind.Object)
        {
            string capText = img.TryGetProperty("caption", out var cap) && cap.ValueKind == JsonValueKind.String
                ? (cap.GetString() ?? "")
                : "";
            return string.IsNullOrEmpty(capText) ? "📷 [Photo]" : $"📷 {capText}";
        }

        if (msgObj.TryGetProperty("audioMessage", out _))
        {
            return "🎤 [Voice Note]";
        }

        if (msgObj.TryGetProperty("videoMessage", out var vid) && vid.ValueKind == JsonValueKind.Object)
        {
            string capText = vid.TryGetProperty("caption", out var cap) && cap.ValueKind == JsonValueKind.String
                ? (cap.GetString() ?? "")
                : "";
            return string.IsNullOrEmpty(capText) ? "🎥 [Video]" : $"🎥 {capText}";
        }

        if (msgObj.TryGetProperty("stickerMessage", out _))
        {
            return "🖼️ [Sticker]";
        }

        if (msgObj.TryGetProperty("documentMessage", out var docMsg) && docMsg.ValueKind == JsonValueKind.Object)
        {
            string fileName = docMsg.TryGetProperty("fileName", out var fnProp) && fnProp.ValueKind == JsonValueKind.String
                ? (fnProp.GetString() ?? "file")
                : "file";
            return $"📄 [{fileName}]";
        }

        return "";
    }

    public static string ExtractRemoteId(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object) return "";

        if (payload.TryGetProperty("id", out var idProp) &&
            idProp.ValueKind == JsonValueKind.String &&
            !string.IsNullOrEmpty(idProp.GetString()))
        {
            return idProp.GetString()!;
        }

        if (payload.TryGetProperty("key", out var keyObj) &&
            keyObj.ValueKind == JsonValueKind.Object &&
            keyObj.TryGetProperty("id", out var keyId) &&
            keyId.ValueKind == JsonValueKind.String)
        {
            return keyId.GetString() ?? "";
        }

        return "";
    }

    public static bool IsFromMe(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object) return false;

        if (payload.TryGetProperty("is_from_me", out var ifm) && ifm.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return ifm.GetBoolean();
        }

        if (payload.TryGetProperty("from_me", out var fm) && fm.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return fm.GetBoolean();
        }

        return false;
    }

    public static (bool IsGroup, string TargetChatId) ResolveWebhookChatTarget(string from, string chatId)
    {
        string normalizedChatId = string.IsNullOrEmpty(chatId) ? from : chatId;
        bool isGroup = normalizedChatId.EndsWith("@g.us", StringComparison.OrdinalIgnoreCase) ||
                       from.EndsWith("@g.us", StringComparison.OrdinalIgnoreCase);

        string targetChatId = isGroup && normalizedChatId.EndsWith("@g.us", StringComparison.OrdinalIgnoreCase)
            ? normalizedChatId
            : (from.EndsWith("@g.us", StringComparison.OrdinalIgnoreCase) ? from : normalizedChatId);

        return (isGroup, targetChatId);
    }

    public static string FormatPhoneNumber(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "Unknown";
        string clean = raw.Replace("@s.whatsapp.net", "").Replace("@c.us", "").Replace("@g.us", "").Replace("@lid", "").Trim();

        // Indonesian numbers (628000000001 -> +62 800-0000-0001)
        if (clean.StartsWith("62", StringComparison.Ordinal) && clean.Length >= 10)
        {
            string prefix = "+62";
            string rest = clean.Substring(2);
            if (rest.Length <= 4) return $"{prefix} {rest}";
            if (rest.Length <= 8) return $"{prefix} {rest[..3]}-{rest[3..]}";
            return $"{prefix} {rest[..3]}-{rest.Substring(3, 4)}-{rest[7..]}";
        }

        if (clean.StartsWith("+", StringComparison.Ordinal)) return clean;
        return "+" + clean;
    }

    public static (string GroupName, string Sender, string SenderName) FormatSender(
        bool isGroup,
        string? groupName,
        string? displayName,
        string? fallbackChatName,
        string? phoneFallbackJid)
    {
        if (isGroup)
        {
            string resolvedGroup = !string.IsNullOrWhiteSpace(groupName)
                ? groupName
                : (!string.IsNullOrWhiteSpace(fallbackChatName) && !fallbackChatName.StartsWith("Group ", StringComparison.OrdinalIgnoreCase)
                    ? fallbackChatName
                    : "Group");
            string participantPhone = FormatPhoneNumber(phoneFallbackJid ?? "");
            string senderName = !string.IsNullOrWhiteSpace(displayName) ? displayName : participantPhone;
            return (resolvedGroup, $"{resolvedGroup}: {senderName}", senderName);
        }

        string? directCandidate = !string.IsNullOrWhiteSpace(displayName) ? displayName : fallbackChatName;
        if (!string.IsNullOrWhiteSpace(directCandidate) && !directCandidate.StartsWith("Group ", StringComparison.OrdinalIgnoreCase))
        {
            return ("", directCandidate, directCandidate);
        }

        string formattedNumber = FormatPhoneNumber(phoneFallbackJid ?? "");
        return ("", formattedNumber, formattedNumber);
    }
}
