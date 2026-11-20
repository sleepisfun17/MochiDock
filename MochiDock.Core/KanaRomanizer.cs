using System.Text;

namespace MochiDock.Core;

/// <summary>
/// Kana to romaji in modified Hepburn, the style most lyric sites use:
/// long vowels are written as the kana spell them (とうきょう = toukyou, ああ = aa; the katakana mark ー repeats the vowel before it),
/// っ doubles the next consonant (っち = tchi), ん is "n", written "n'" before a vowel or y so that かんい (kan'i) differs from かに (kani).
/// Grammar is not known here: the particles は and へ are read "wa" and "e" by the caller that knows word boundaries; を is always "o".
/// Characters that are not kana pass through unchanged.
/// </summary>
public static class KanaRomanizer
{
    static readonly Dictionary<string, string> Syllables = Build();

    static Dictionary<string, string> Build()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        void Row(string kana, params string[] roman)
        {
            for (int i = 0; i < roman.Length; i++) map[kana[i].ToString()] = roman[i];
        }

        Row("あいうえお", "a", "i", "u", "e", "o");
        Row("かきくけこ", "ka", "ki", "ku", "ke", "ko");
        Row("がぎぐげご", "ga", "gi", "gu", "ge", "go");
        Row("さしすせそ", "sa", "shi", "su", "se", "so");
        Row("ざじずぜぞ", "za", "ji", "zu", "ze", "zo");
        Row("たちつてと", "ta", "chi", "tsu", "te", "to");
        Row("だぢづでど", "da", "ji", "zu", "de", "do");
        Row("なにぬねの", "na", "ni", "nu", "ne", "no");
        Row("はひふへほ", "ha", "hi", "fu", "he", "ho");
        Row("ばびぶべぼ", "ba", "bi", "bu", "be", "bo");
        Row("ぱぴぷぺぽ", "pa", "pi", "pu", "pe", "po");
        Row("まみむめも", "ma", "mi", "mu", "me", "mo");
        Row("やゆよ", "ya", "yu", "yo");
        Row("らりるれろ", "ra", "ri", "ru", "re", "ro");
        Row("わゐゑを", "wa", "i", "e", "o");
        Row("ゔ", "vu");
        // Small kana on their own (an exclamation like "あぁ", or a kana the analyzer split off).
        Row("ぁぃぅぇぉゃゅょゎゕゖ", "a", "i", "u", "e", "o", "ya", "yu", "yo", "wa", "ka", "ke");
        // Katakana-only letters with no hiragana form.
        Row("ヷヸヹヺ", "va", "vi", "ve", "vo");

        // Contracted sounds: consonant + small ya/yu/yo.
        void Contracted(char kana, string stem)
        {
            map[kana + "ゃ"] = stem + "a";
            map[kana + "ゅ"] = stem + "u";
            map[kana + "ょ"] = stem + "o";
        }
        Contracted('き', "ky"); Contracted('ぎ', "gy"); Contracted('し', "sh"); Contracted('じ', "j");
        Contracted('ち', "ch"); Contracted('ぢ', "j"); Contracted('に', "ny"); Contracted('ひ', "hy");
        Contracted('び', "by"); Contracted('ぴ', "py"); Contracted('み', "my"); Contracted('り', "ry");
        Contracted('ゔ', "vy"); Contracted('て', "ty"); Contracted('で', "dy"); Contracted('ふ', "fy");

        // Sounds used for loanwords, written with a small vowel.
        foreach (var (kana, roman) in new[]
        {
            ("しぇ", "she"), ("じぇ", "je"), ("ちぇ", "che"), ("いぇ", "ye"),
            ("つぁ", "tsa"), ("つぃ", "tsi"), ("つぇ", "tse"), ("つぉ", "tso"),
            ("てぃ", "ti"), ("でぃ", "di"), ("とぅ", "tu"), ("どぅ", "du"),
            ("ふぁ", "fa"), ("ふぃ", "fi"), ("ふぇ", "fe"), ("ふぉ", "fo"),
            ("うぃ", "wi"), ("うぇ", "we"), ("うぉ", "wo"),
            ("ゔぁ", "va"), ("ゔぃ", "vi"), ("ゔぇ", "ve"), ("ゔぉ", "vo"),
            ("くぁ", "kwa"), ("ぐぁ", "gwa"), ("すぃ", "si"), ("ずぃ", "zi"),
        })
            map[kana] = roman;
        return map;
    }

    /// <summary>
    /// Katakana to hiragana. Half-width katakana are widened first. ヴ becomes ゔ; ヷ to ヺ and the prolonged sound mark ー have no
    /// hiragana form and stay as they are.
    /// </summary>
    public static string ToHiragana(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        // NFKC widens half-width kana and merges a following half-width voiced mark into one character.
        if (text.Any(c => c >= '｡' && c <= 'ﾟ')) text = text.Normalize(NormalizationForm.FormKC);
        var builder = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (c >= 'ァ' && c <= 'ヶ') builder.Append((char)(c - 0x60));
            else if (c == 'ヽ') builder.Append('ゝ');
            else if (c == 'ヾ') builder.Append('ゞ');
            else builder.Append(c);
        }
        return builder.ToString();
    }

    public static bool IsKana(char c) =>
        (c >= 'ぁ' && c <= 'ゖ') || (c >= 'ゝ' && c <= 'ゞ') || (c >= 'ァ' && c <= 'ヺ') || c == 'ー' || (c >= 'ヽ' && c <= 'ヾ') ||
        (c >= 'ｦ' && c <= 'ﾟ');

    /// <summary>Romaji for a kana string (hiragana, katakana or both). See the class summary for the rules.</summary>
    public static string ToRomaji(string kana)
    {
        if (string.IsNullOrEmpty(kana)) return "";
        string text = ExpandIterationMarks(ToHiragana(kana));
        var output = new StringBuilder(text.Length * 2);
        bool doubleNext = false;

        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];

            if (c == 'っ')
            {
                doubleNext = true;
                i++;
                continue;
            }

            if (c == 'ー')
            {
                char vowel = LastVowel(output);
                if (vowel != '\0') output.Append(vowel);
                doubleNext = false;
                i++;
                continue;
            }

            if (c == 'ん')
            {
                string next = PeekSyllable(text, i + 1, out _);
                output.Append(next.Length > 0 && (IsVowel(next[0]) || next[0] == 'y') ? "n'" : "n");
                doubleNext = false;
                i++;
                continue;
            }

            string syllable = PeekSyllable(text, i, out int length);
            if (syllable.Length == 0)
            {
                output.Append(c);
                doubleNext = false;
                i++;
                continue;
            }

            if (doubleNext)
            {
                if (syllable.StartsWith("ch", StringComparison.Ordinal)) output.Append('t');
                else if (!IsVowel(syllable[0])) output.Append(syllable[0]);
                doubleNext = false;
            }
            output.Append(syllable);
            i += length;
        }
        return output.ToString();
    }

    static string PeekSyllable(string text, int index, out int length)
    {
        length = 0;
        if (index >= text.Length) return "";
        if (index + 1 < text.Length && Syllables.TryGetValue(text.Substring(index, 2), out var pair))
        {
            length = 2;
            return pair;
        }
        if (Syllables.TryGetValue(text[index].ToString(), out var single))
        {
            length = 1;
            return single;
        }
        return "";
    }

    static bool IsVowel(char c) => c is 'a' or 'i' or 'u' or 'e' or 'o';

    static char LastVowel(StringBuilder output)
    {
        for (int i = output.Length - 1; i >= 0; i--)
        {
            if (IsVowel(output[i])) return output[i];
            if (!char.IsAsciiLetter(output[i])) return '\0';
        }
        return '\0';
    }

    // ゝ repeats the kana before it, ゞ repeats it voiced (すゞ = すず).
    static string ExpandIterationMarks(string text)
    {
        if (text.IndexOf('ゝ') < 0 && text.IndexOf('ゞ') < 0) return text;
        var builder = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if ((c == 'ゝ' || c == 'ゞ') && builder.Length > 0)
            {
                char previous = builder[^1];
                char unvoiced = Unvoiced(previous);
                builder.Append(c == 'ゞ' ? Voiced(unvoiced) : unvoiced);
            }
            else builder.Append(c);
        }
        return builder.ToString();
    }

    const string VoicedPairs = "かがきぎくぐけげこごさざしじすずせぜそぞただちぢつづてでとどはばひびふぶへべほぼ";

    static char Voiced(char c)
    {
        int index = VoicedPairs.IndexOf(c);
        return index >= 0 && index % 2 == 0 ? VoicedPairs[index + 1] : c;
    }

    static char Unvoiced(char c)
    {
        int index = VoicedPairs.IndexOf(c);
        return index >= 0 && index % 2 == 1 ? VoicedPairs[index - 1] : c;
    }
}
