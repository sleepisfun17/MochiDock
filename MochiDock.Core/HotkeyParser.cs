using System;
using System.Collections.Generic;

namespace MochiDock.Core;

/// <summary>A parsed global hotkey: Win32 modifier flags (Alt 1, Ctrl 2, Shift 4, Win 8), a virtual-key code and the canonical text.</summary>
public sealed record Hotkey(uint Modifiers, uint VirtualKey, string Text);

/// <summary>Parses and formats global hotkeys such as "Ctrl+Alt+R" or "Win+F9". Pure, so the rules are unit-tested.</summary>
public static class HotkeyParser
{
    public const string Default = "Ctrl+Alt+R";
    /// <summary>Alt+Shift+S opens the Script reader, so it cannot also be the resolution hotkey.</summary>
    public const string ScriptHotkey = "Alt+Shift+S";

    private const uint Alt = 1, Ctrl = 2, Shift = 4, Win = 8;

    /// <summary>Punctuation keys of a US layout by their unshifted character, with their Win32 OEM virtual-key codes. "+" is not allowed because it separates the parts.</summary>
    private static readonly Dictionary<char, uint> Punctuation = new()
    {
        ['`'] = 0xC0, ['-'] = 0xBD, ['='] = 0xBB, ['['] = 0xDB, [']'] = 0xDD, ['\\'] = 0xDC,
        [';'] = 0xBA, ['\''] = 0xDE, [','] = 0xBC, ['.'] = 0xBE, ['/'] = 0xBF,
    };

    /// <summary>The character name of the punctuation key at the given virtual-key code, or null.</summary>
    public static string? PunctuationName(uint virtualKey)
    {
        foreach (var pair in Punctuation) if (pair.Value == virtualKey) return pair.Key.ToString();
        return null;
    }

    public static bool TryParse(string? text, out Hotkey hotkey, out string? error)
    {
        hotkey = new Hotkey(0, 0, "");
        error = null;
        if (string.IsNullOrWhiteSpace(text)) { error = "Press the shortcut you want."; return false; }

        uint modifiers = 0;
        string? key = null;
        foreach (var raw in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": modifiers |= Ctrl; break;
                case "alt": modifiers |= Alt; break;
                case "shift": modifiers |= Shift; break;
                case "win" or "windows": modifiers |= Win; break;
                default:
                    if (key != null) { error = "Use one key plus modifiers, for example Ctrl+Alt+R."; return false; }
                    key = raw;
                    break;
            }
        }
        if (key == null || !TryKeyCode(key, out uint vk, out string keyName))
        {
            error = "The key must be a letter, a digit, F1-F24 or one of ` - = [ ] \\ ; ' , . /";
            return false;
        }
        if ((modifiers & (Ctrl | Alt | Win)) == 0)
        {
            error = "Include Ctrl, Alt or Win so the shortcut does not interfere with typing.";
            return false;
        }

        var parts = new List<string>();
        if ((modifiers & Ctrl) != 0) parts.Add("Ctrl");
        if ((modifiers & Alt) != 0) parts.Add("Alt");
        if ((modifiers & Shift) != 0) parts.Add("Shift");
        if ((modifiers & Win) != 0) parts.Add("Win");
        parts.Add(keyName);
        string canonical = string.Join("+", parts);
        if (string.Equals(canonical, ScriptHotkey, StringComparison.Ordinal))
        {
            error = "Alt+Shift+S is already used by the Script reader.";
            return false;
        }
        hotkey = new Hotkey(modifiers, vk, canonical);
        return true;
    }

    /// <summary>Canonical text for the given modifiers and key name, or null when it is not a valid hotkey.</summary>
    public static string? Format(bool ctrl, bool alt, bool shift, bool win, string keyName)
    {
        var parts = new List<string>();
        if (ctrl) parts.Add("Ctrl");
        if (alt) parts.Add("Alt");
        if (shift) parts.Add("Shift");
        if (win) parts.Add("Win");
        parts.Add(keyName);
        return TryParse(string.Join("+", parts), out var hotkey, out _) ? hotkey.Text : null;
    }

    private static bool TryKeyCode(string key, out uint vk, out string name)
    {
        vk = 0;
        name = "";
        string k = key.Trim().ToUpperInvariant();
        if (k.Length == 1 && k[0] is >= 'A' and <= 'Z') { vk = k[0]; name = k; return true; }
        if (k.Length == 1 && k[0] is >= '0' and <= '9') { vk = k[0]; name = k; return true; }
        if (k.Length == 1 && Punctuation.TryGetValue(k[0], out uint oem)) { vk = oem; name = k; return true; }
        if (k.Length is 2 or 3 && k[0] == 'F' && int.TryParse(k.AsSpan(1), out int f) && f is >= 1 and <= 24)
        {
            vk = (uint)(0x70 + f - 1);
            name = "F" + f;
            return true;
        }
        return false;
    }
}
