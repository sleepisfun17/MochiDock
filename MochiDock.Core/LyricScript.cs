using System.Text.RegularExpressions;

namespace MochiDock.Core;

/// <summary>Tells native-script lyrics from Latin transliterations (romaji) of the same song.</summary>
public static class LyricScript
{
    // One Hepburn-style word: syllables of an optional consonant cluster and a vowel, a syllabic n, or a doubled consonant
    // ("kokoro", "shinjiteru", "kitto", "sekai"). Long-vowel marks are folded before matching.
    static readonly Regex RomajiWord = new(
        @"^(?:(?:kk|ss|tt|pp|cch|tch)?(?:ky|gy|sh|ch|ts|ny|hy|by|py|my|ry|j|[kgsztdnhbpmyrwf])?[aiueo]|n)+$",
        RegexOptions.CultureInvariant);
    static readonly Regex Words = new(@"[A-Za-zĀāĪīŪūĒēŌōÂâÎîÛûÊêÔô']+", RegexOptions.CultureInvariant);

    /// <summary>Minimum number of Latin words before the text is judged at all.</summary>
    public const int MinWords = 12;
    /// <summary>Share of words that must read as romaji.</summary>
    public const double RomajiShare = 0.6;

    /// <summary>True when the text has Japanese kana or kanji.</summary>
    public static bool HasJapanese(string? text) =>
        !string.IsNullOrEmpty(text) && text.Any(c => LyricPronunciationPolicy.IsKana(c) || LyricPronunciationPolicy.IsHan(c));

    /// <summary>
    /// True when the lyrics are written entirely in Latin letters and most words are romaji ("kimi no koe ga kikoeru").
    /// English lyrics stay false: most English words are not valid romaji syllable chains.
    /// </summary>
    public static bool LooksLikeRomaji(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || HasJapanese(text)) return false;
        var words = Words.Matches(text).Select(m => Fold(m.Value)).Where(w => w.Length > 0).ToList();
        if (words.Count < MinWords) return false;
        int romaji = words.Count(w => RomajiWord.IsMatch(w));
        return romaji >= words.Count * RomajiShare;
    }

    static string Fold(string word)
    {
        var chars = word.ToLowerInvariant().Replace("'", "", StringComparison.Ordinal).Select(c => c switch
        {
            'ā' or 'â' => 'a', 'ī' or 'î' => 'i', 'ū' or 'û' => 'u', 'ē' or 'ê' => 'e', 'ō' or 'ô' => 'o',
            _ => c,
        });
        return new string(chars.ToArray());
    }
}
