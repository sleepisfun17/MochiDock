using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MochiDock.Core;

/// <summary>A piece of a message: plain text, or one emoji (Key is the name of its Twemoji picture, for example "1f600").</summary>
public sealed record EmojiRun(string Text, string? Key)
{
    public bool IsEmoji => Key != null;
}

/// <summary>
/// Splits message text into plain text and emoji so each emoji can be drawn as a coloured picture (WPF text draws emoji in black and white).
/// An emoji is one visible symbol: a pictograph, with an optional skin tone, a zero-width-joiner chain (a family, a couple), a flag
/// (two regional indicators, or a tag sequence such as England) or a keycap. Plain digits, "#", "(c)" and the like stay text unless
/// they are marked as emoji with U+FE0F. The text of all runs always adds up to the original string, even for broken surrogates.
/// </summary>
public static class EmojiText
{
    private const int Vs16 = 0xFE0F, Zwj = 0x200D, Keycap = 0x20E3;

    public static List<EmojiRun> Split(string? text)
    {
        var runs = new List<EmojiRun>();
        if (string.IsNullOrEmpty(text)) return runs;

        // Code points with the UTF-16 offset of each, so the runs can be cut out of the original string unchanged.
        var cps = new List<int>(text.Length);
        var offsets = new List<int>(text.Length + 1);
        int offset = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            cps.Add(rune.Value);
            offsets.Add(offset);
            offset += rune.Utf16SequenceLength;
        }
        offsets.Add(offset);

        int textStart = 0, i = 0;
        while (i < cps.Count)
        {
            int end = EmojiEnd(cps, i);
            if (end <= i) { i++; continue; }
            if (i > textStart) runs.Add(new EmojiRun(text.Substring(offsets[textStart], offsets[i] - offsets[textStart]), null));
            runs.Add(new EmojiRun(text.Substring(offsets[i], offsets[end] - offsets[i]), Key(cps, i, end)));
            i = textStart = end;
        }
        if (textStart < cps.Count) runs.Add(new EmojiRun(text.Substring(offsets[textStart]), null));
        return runs;
    }

    /// <summary>True when the text holds at least one emoji.</summary>
    public static bool HasEmoji(string? text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        foreach (var run in Split(text)) if (run.IsEmoji) return true;
        return false;
    }

    // The index after the emoji that starts at i, or i when no emoji starts there.
    private static int EmojiEnd(List<int> cps, int i)
    {
        int n = cps.Count, c = cps[i], j = i + 1;

        if (IsRegional(c)) return j < n && IsRegional(cps[j]) ? j + 1 : i;

        if (IsKeycapBase(c))
        {
            int k = j;
            if (k < n && cps[k] == Vs16) k++;
            return k < n && cps[k] == Keycap ? k + 1 : i;
        }

        bool presentation = IsEmojiPresentation(c);
        bool marked = j < n && cps[j] == Vs16;
        if (!presentation && !(marked && IsSymbolRange(c))) return i;

        while (j < n)
        {
            int d = cps[j];
            if (d == Vs16 || IsSkinTone(d)) { j++; continue; }
            if (c == 0x1F3F4 && d >= 0xE0020 && d <= 0xE007F) { j++; if (d == 0xE007F) break; continue; }   // a tag sequence ends with the cancel tag
            if (d == Zwj && j + 1 < n && (IsEmojiPresentation(cps[j + 1]) || IsSymbolRange(cps[j + 1]))) { j += 2; continue; }
            break;
        }
        return j;
    }

    // Twemoji names its pictures by code points in lower-case hex joined by "-", without U+FE0F unless the emoji has a joiner.
    private static string Key(List<int> cps, int start, int end)
    {
        bool joined = false;
        for (int k = start; k < end; k++) if (cps[k] == Zwj) { joined = true; break; }
        var key = new StringBuilder();
        for (int k = start; k < end; k++)
        {
            if (cps[k] == Vs16 && !joined) continue;
            if (key.Length > 0) key.Append('-');
            key.Append(cps[k].ToString("x", CultureInfo.InvariantCulture));
        }
        return key.ToString();
    }

    private static bool IsRegional(int c) => c >= 0x1F1E6 && c <= 0x1F1FF;
    private static bool IsSkinTone(int c) => c >= 0x1F3FB && c <= 0x1F3FF;
    private static bool IsKeycapBase(int c) => (c >= '0' && c <= '9') || c == '#' || c == '*';

    // Symbols that are text unless U+FE0F follows them (copyright, arrows, hearts, stars, ...).
    private static bool IsSymbolRange(int c) =>
        c == 0xA9 || c == 0xAE || (c >= 0x2000 && c <= 0x2BFF) || c == 0x3030 || c == 0x303D || c == 0x3297 || c == 0x3299;

    // Pictographs that are drawn as emoji without U+FE0F.
    private static bool IsEmojiPresentation(int c)
    {
        if (c >= 0x1F300 && c <= 0x1FAFF) return !IsRegional(c);
        if (c >= 0x1F000 && c <= 0x1F2FF)
            return c == 0x1F004 || c == 0x1F0CF || c == 0x1F170 || c == 0x1F171 || c == 0x1F17E || c == 0x1F17F || c == 0x1F18E ||
                   (c >= 0x1F191 && c <= 0x1F19A) || c == 0x1F201 || c == 0x1F202 || c == 0x1F21A || c == 0x1F22F ||
                   (c >= 0x1F232 && c <= 0x1F23A) || c == 0x1F250 || c == 0x1F251;
        return c switch
        {
            0x231A or 0x231B or 0x23E9 or 0x23EA or 0x23EB or 0x23EC or 0x23F0 or 0x23F3 or 0x25FD or 0x25FE => true,
            0x2614 or 0x2615 or 0x267F or 0x2693 or 0x26A1 or 0x26AA or 0x26AB or 0x26BD or 0x26BE or 0x26C4 or 0x26C5 or 0x26CE or 0x26D4 or 0x26EA => true,
            0x26F2 or 0x26F3 or 0x26F5 or 0x26FA or 0x26FD or 0x2705 or 0x270A or 0x270B or 0x2728 or 0x274C or 0x274E or 0x2753 or 0x2754 or 0x2755 => true,
            0x2757 or 0x2795 or 0x2796 or 0x2797 or 0x27B0 or 0x27BF or 0x2B1B or 0x2B1C or 0x2B50 or 0x2B55 => true,
            _ => (c >= 0x2648 && c <= 0x2653),
        };
    }
}
