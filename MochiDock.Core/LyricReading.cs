using System.Text;

namespace MochiDock.Core;

/// <summary>One word as a Japanese morphological analyzer reports it: the text, its reading in kana, and whether a new phrase starts here.</summary>
public sealed record JapaneseWord(string Display, string Reading, bool PhraseStart);

/// <summary>
/// One display unit of a pronounced lyric line: <see cref="Text"/> is shown, <see cref="Romaji"/> under it (empty for text that needs no
/// reading, such as English words), <see cref="Kana"/> is the hiragana reading of segments that contain kanji, and <see cref="LineText"/> is
/// this segment's share of the one-line reading (romanization plus its punctuation, or the text itself for Latin words).
/// </summary>
public sealed record ReadingSegment(string Text, string Kana, string Romaji, string LineText);

/// <summary>Builds <see cref="ReadingSegment"/>s for Japanese, Chinese and Korean lines, and joins them into a one-line reading.</summary>
public static class LyricReading
{
    enum Kind { Cjk, Latin, Punctuation, Space }

    static Kind KindOf(string text)
    {
        if (text.Length == 0 || text.All(char.IsWhiteSpace)) return Kind.Space;
        bool latin = false;
        foreach (char c in text)
        {
            if (LyricPronunciationPolicy.IsKana(c) || LyricPronunciationPolicy.IsHan(c) || LyricPronunciationPolicy.IsHangul(c)) return Kind.Cjk;
            if (char.IsLetterOrDigit(c)) latin = true;
        }
        return latin ? Kind.Latin : Kind.Punctuation;
    }

    /// <summary>The ASCII form of East Asian punctuation for the one-line reading; brackets and quotes are dropped.</summary>
    public static string PunctuationForLine(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            switch (c)
            {
                case '、' or '，' or '､': builder.Append(','); break;
                case '。' or '｡' or '．': builder.Append('.'); break;
                case '！': builder.Append('!'); break;
                case '？': builder.Append('?'); break;
                case '～' or '〜': builder.Append('~'); break;
                case '…' or '‥': builder.Append("..."); break;
                case '：': builder.Append(':'); break;
                case '；': builder.Append(';'); break;
                case '・' or '　': break;
                case '「' or '」' or '『' or '』' or '（' or '）' or '【' or '】' or '〈' or '〉' or '《' or '》' or '“' or '”' or '"' or '(' or ')': break;
                default:
                    if (!char.IsWhiteSpace(c)) builder.Append(c);
                    break;
            }
        }
        return builder.ToString();
    }

    /// <summary>Joins the segments' line text with single spaces; punctuation sticks to the word before it.</summary>
    public static string Line(IEnumerable<ReadingSegment> segments)
    {
        var builder = new StringBuilder();
        foreach (var segment in segments)
        {
            string part = segment.LineText.Trim();
            if (part.Length == 0) continue;
            bool attaches = KindOf(segment.Text) == Kind.Punctuation && part.All(c => !char.IsLetterOrDigit(c));
            if (builder.Length > 0 && !attaches) builder.Append(' ');
            builder.Append(part);
        }
        return builder.ToString();
    }

    // Particles written as separate words in romaji (kimi ga, boku no). は and へ also change their reading.
    static readonly HashSet<string> Particles = new(StringComparer.Ordinal)
    {
        "は", "が", "を", "に", "へ", "と", "で", "も", "の", "や", "か", "ね", "よ", "な", "さ", "ぞ", "わ", "から", "まで", "より", "って", "けど", "だけ", "しか", "ほど", "など",
        "でも", "には", "では", "とは", "にも", "とも", "のに", "ので", "ながら", "けれど", "だって", "かな", "よね", "かも",
    };

    /// <summary>
    /// Groups analyzer words into phrases (one segment per phrase) and romanizes them. Inside a phrase the words run together except
    /// particles, which get a space (kimi ga, aishiteru). The particles は and へ are read "wa" and "e". A word whose reading is not kana
    /// (an analyzer gap) contributes no romaji rather than raw kanji.
    /// </summary>
    public static List<ReadingSegment> Japanese(IReadOnlyList<JapaneseWord> words)
    {
        var segments = new List<ReadingSegment>();
        var text = new StringBuilder();
        var kana = new StringBuilder();
        var romaji = new StringBuilder();
        var trailing = new StringBuilder();
        Kind current = Kind.Space;
        bool hasHan = false;

        void Flush()
        {
            if (text.Length > 0)
            {
                string roman = romaji.ToString();
                string line = current == Kind.Latin ? text.ToString() : roman + PunctuationForLine(trailing.ToString());
                if (current == Kind.Punctuation) line = PunctuationForLine(text.ToString());
                segments.Add(new ReadingSegment(text.ToString(), hasHan ? kana.ToString() : "", current == Kind.Cjk ? roman : "", line));
            }
            text.Clear(); kana.Clear(); romaji.Clear(); trailing.Clear();
            hasHan = false;
            current = Kind.Space;
        }

        foreach (var word in words)
        {
            string display = word.Display ?? "";
            var kind = KindOf(display);
            if (kind == Kind.Space) { Flush(); continue; }

            if (kind == Kind.Punctuation)
            {
                if (current == Kind.Space) { current = Kind.Punctuation; text.Append(display); continue; }
                text.Append(display);
                trailing.Append(display);
                continue;
            }

            bool startsSegment = current != kind || trailing.Length > 0 || (kind == Kind.Cjk && word.PhraseStart);
            if (startsSegment) { Flush(); current = kind; }

            text.Append(display);
            if (kind == Kind.Latin) continue;

            string reading = ReadingOf(display, word.Reading);
            kana.Append(reading);
            if (display.Any(LyricPronunciationPolicy.IsHan)) hasHan = true;

            string roman = romaji.Length > 0 && display == "は" ? "wa"
                         : romaji.Length > 0 && display == "へ" ? "e"
                         : KanaRomanizer.ToRomaji(reading);
            roman = new string(roman.Where(c => c < 0x2E80).ToArray()); // never leak unread kanji into the romaji
            if (roman.Length == 0) continue;
            if (romaji.Length > 0 && Particles.Contains(display)) romaji.Append(' ');
            romaji.Append(roman);
        }
        Flush();
        return segments;
    }

    static string ReadingOf(string display, string reading)
    {
        string r = KanaRomanizer.ToHiragana(reading ?? "");
        if (r.Length > 0 && r.All(c => LyricPronunciationPolicy.IsKana(c) || !LyricPronunciationPolicy.IsHan(c))) return r;
        return display.All(c => !LyricPronunciationPolicy.IsHan(c)) ? KanaRomanizer.ToHiragana(display) : "";
    }

    /// <summary>
    /// Chinese: one segment per Han character with its pinyin from <paramref name="pinyin"/> (null when unknown), Latin words as they are,
    /// punctuation attached to the line reading.
    /// </summary>
    public static List<ReadingSegment> Chinese(string line, Func<char, string?> pinyin)
    {
        var segments = new List<ReadingSegment>();
        int i = 0;
        while (i < line.Length)
        {
            char c = line[i];
            if (LyricPronunciationPolicy.IsHan(c))
            {
                string reading = pinyin(c) ?? "";
                segments.Add(new ReadingSegment(c.ToString(), "", reading, reading));
                i++;
                continue;
            }
            int start = i;
            var kind = KindOf(c.ToString());
            while (i < line.Length && !LyricPronunciationPolicy.IsHan(line[i]) && KindOf(line[i].ToString()) == kind) i++;
            if (i == start) i++;
            string run = line[start..i];
            if (kind == Kind.Space) continue;
            segments.Add(new ReadingSegment(run, "", "", kind == Kind.Punctuation ? PunctuationForLine(run) : run));
        }
        return segments;
    }

    /// <summary>Korean: one segment per space-separated word, romanized by the Revised Romanization.</summary>
    public static List<ReadingSegment> Korean(string line)
    {
        var segments = new List<ReadingSegment>();
        foreach (var word in line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            bool hangul = word.Any(LyricPronunciationPolicy.IsHangul);
            if (!hangul)
            {
                segments.Add(new ReadingSegment(word, "", "", KindOf(word) == Kind.Punctuation ? PunctuationForLine(word) : word));
                continue;
            }
            string roman = HangulRomanizer.Romanize(word);
            string lineText = PunctuationForLine(roman);
            segments.Add(new ReadingSegment(word, "", StripPunctuation(roman), lineText));
        }
        return segments;
    }

    static string StripPunctuation(string roman) => new(roman.Where(c => char.IsLetterOrDigit(c) || c == '\'' || c == '-').ToArray());
}
