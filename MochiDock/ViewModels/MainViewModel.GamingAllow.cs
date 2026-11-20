using System;
using System.Linq;
using MochiDock.Core;
using MochiDock.Services;

namespace MochiDock.ViewModels;

public partial class MainViewModel
{
    // A message from someone on the "let through while gaming" list. The gaming capsule shows the usual readout, so the message is
    // shown there for a few seconds, with the notification sound; the dock is never expanded or taken over during a game.
    private string _gamingPassText = "";
    private DateTime _gamingPassUntilUtc;

    private void ShowGamingPassThrough(string text)
    {
        _gamingPassText = text;
        _gamingPassUntilUtc = DateTime.UtcNow.AddSeconds(8);
        SoundService.PlayNotificationSound();   // does nothing while notification sounds are off
        OnPropertyChanged(nameof(CompactCapsuleText));
    }

    // Called by the hardware poll timer so the text disappears on time.
    private void ExpireGamingPassThrough()
    {
        if (_gamingPassText.Length == 0 || DateTime.UtcNow < _gamingPassUntilUtc) return;
        _gamingPassText = "";
        OnPropertyChanged(nameof(CompactCapsuleText));
    }

    // The text shown in the gaming capsule instead of the readout: a pass-through message, else the break reminder.
    private string? GamingCapsuleNotice() =>
        _gamingPassText.Length > 0 && DateTime.UtcNow < _gamingPassUntilUtc ? _gamingPassText : BreakNoticeText();

    /// <summary>Adds the chat of the latest WhatsApp notification to the list. Returns a message to show, or "" when it worked.</summary>
    public string AllowLastWhatsAppWhileGaming()
    {
        var last = RecentWhatsAppMessages.FirstOrDefault();
        if (last == null || string.IsNullOrWhiteSpace(last.FromJid)) return "No WhatsApp notification yet. Add one after a message arrives.";
        string label = last.IsGroup && !string.IsNullOrWhiteSpace(last.GroupName) ? last.GroupName : last.Sender;
        AddGamingAllow(new GamingAllowEntry { Kind = GamingAllowList.WhatsApp, Key = _gowaService.MuteKeyFor(last.FromJid, last.DeviceId), Label = label });
        return "";
    }

    /// <summary>Adds a Discord sender (typed, or the latest notification's sender when the name is empty). Returns a message, or "" when it worked.</summary>
    public string AllowDiscordWhileGaming(string? sender)
    {
        string name = (sender ?? "").Trim();
        if (name.Length == 0) name = RecentDiscordMessages.FirstOrDefault()?.Sender?.Trim() ?? "";
        if (name.Length == 0) return "Type the sender's name, or add one after a Discord message arrives.";
        AddGamingAllow(new GamingAllowEntry { Kind = GamingAllowList.Discord, Key = name, Label = name });
        return "";
    }

    private void AddGamingAllow(GamingAllowEntry entry) =>
        UpdateSettings(x => x.GamingAllowList = GamingAllowList.Add(x.GamingAllowList, entry));

    public void RemoveGamingAllow(GamingAllowEntry entry) =>
        UpdateSettings(x => x.GamingAllowList.RemoveAll(e => ReferenceEquals(e, entry) ||
            (string.Equals(e.Kind, entry.Kind, StringComparison.OrdinalIgnoreCase) && string.Equals(e.Key, entry.Key, StringComparison.OrdinalIgnoreCase))));
}
