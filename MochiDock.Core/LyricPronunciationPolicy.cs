using System;
using System.Globalization;
using System.Text;

namespace MochiDock.Core;

/// <summary>
/// Decides which lyric lines get a reading (romaji and furigana). A line with nothing to transliterate, such as an English or romaji line inside
/// a Japanese or Korean song, gets none, and a reading that only repeats the line is dropped, so the same text is never shown twice.
/// </summary>
public static class LyricPronunciationPolicy
{
    /// <summary>True only when the line contains kana, Han (kanji) or Hangul. Latin, full-width ASCII, digits, punctuation, symbols and emoji do not.</summary>
    public static bool NeedsReading(string? line)
    {
        if (string.IsNullOrEmpty(line)) return false;
        foreach (char c in line)
            if (IsKana(c) || IsHan(c) || IsHangul(c)) return true;
        return false;
    }

    /// <summary>
    /// True when the reading adds nothing to the text: equal after NFKC (full-width becomes ASCII), case folding, removing diacritics
    /// (ō to o) and dropping whitespace and punctuation.
    /// </summary>
    public static bool IsRedundant(string? text, string? reading) =>
        string.Equals(Normalize(text), Normalize(reading), StringComparison.Ordinal);

    internal static string Normalize(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var decomposed = value.Normalize(NormalizationForm.FormKD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (char c in decomposed)
        {
            if (char.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue; // diacritics left by the decomposition
            if (char.IsLetterOrDigit(c)) builder.Append(char.ToLowerInvariant(c));
        }
        return builder.ToString();
    }

    // The pronouncers (LyricRomanizerService, LyricReading) use these same ranges, so the policy and the readings agree.
    public static bool IsKana(char c) =>
        (c >= 'ぁ' && c <= 'ゖ') || (c >= '゙' && c <= 'ゟ') || (c >= 'ァ' && c <= 'ヺ') ||
        (c >= 'ー' && c <= 'ヿ') || (c >= 'ㇰ' && c <= 'ㇿ') || (c >= 'ｦ' && c <= 'ﾟ');

    public static bool IsHangul(char c) =>
        (c >= '가' && c <= '힣') || (c >= 'ᄀ' && c <= 'ᇿ') || (c >= 'ㄱ' && c <= 'ㆎ') ||
        (c >= 'ꥠ' && c <= '꥿') || (c >= 'ힰ' && c <= '퟿');

    public static bool IsHan(char c) =>
        (c >= '々' && c <= '〇') || (c >= '㐀' && c <= '䶿') || (c >= '一' && c <= '鿿') ||
        (c >= '豈' && c <= '﫿') || c == '々' || c == '〆' || c == '〇';
}
