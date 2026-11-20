using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using MochiDock.Core;
using Windows.Globalization;

namespace MochiDock.Services;

/// <summary>A lyric line with its one-line reading and its word-over-reading pieces.</summary>
public class PronouncedLine
{
    public string OriginalText { get; set; } = "";
    public string FullRomaji { get; set; } = "";
    public List<RubyPiece> Pieces { get; set; } = new();
    public bool HasPronunciation => !string.IsNullOrEmpty(FullRomaji) || Pieces.Count > 0;
}

/// <summary>The script a song is mostly written in; it decides how lines with only kanji/hanzi are read.</summary>
public enum ScriptKind
{
    Japanese,
    Korean,
    Chinese,
    None
}

/// <summary>
/// Readings for lyric lines.
/// Japanese: words and kana readings come from Windows' <see cref="JapanesePhoneticAnalyzer"/>; a word it cannot read (or every word, when
/// the analyzer is unavailable) is looked up in <c>Assets/ja_readings.tsv.gz</c>, surface/reading pairs derived from mecab-ipadic (NAIST
/// licence, see THIRD-PARTY-NOTICES.txt), by longest match. Kana become modified Hepburn romaji (<see cref="KanaRomanizer"/>).
/// Korean: the Revised Romanization (<see cref="HangulRomanizer"/>).
/// Chinese: Hanyu Pinyin with tone marks, one reading per character from the Unihan kMandarin field (<c>Assets/zh_pinyin.tsv.gz</c>,
/// Unicode License; built by <c>tools/data/build-pinyin-table.ps1</c>). Characters with several readings get the most customary one.
/// Only lines that <see cref="LyricPronunciationPolicy.NeedsReading"/> are pronounced, and a reading that only repeats its line is dropped.
/// The analyzer is a WinRT component, so whole-song work runs on a dedicated STA thread.
/// </summary>
public static class LyricRomanizerService
{
    // The analyzer accepts at most 100 characters per call.
    const int AnalyzerLimit = 100;
    static int _analyzerFailureLogged;

    static readonly Lazy<(Dictionary<string, string> Map, int LongestKey)> JapaneseDictionary = new(LoadJapaneseDictionary, LazyThreadSafetyMode.ExecutionAndPublication);
    static readonly Lazy<Dictionary<char, string>> PinyinTable = new(LoadPinyinTable, LazyThreadSafetyMode.ExecutionAndPublication);

    static Stream? OpenAsset(string name) =>
        typeof(LyricRomanizerService).Assembly.GetManifestResourceStream("MochiDock.Assets." + name);

    static IEnumerable<string[]> ReadTsv(string name)
    {
        using var stream = OpenAsset(name);
        if (stream == null) yield break;
        using var gzip = new GZipStream(stream, CompressionMode.Decompress);
        using var reader = new StreamReader(gzip, Encoding.UTF8);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            int tab = line.IndexOf('\t');
            if (tab > 0 && tab < line.Length - 1) yield return new[] { line[..tab], line[(tab + 1)..] };
        }
    }

    static (Dictionary<string, string>, int) LoadJapaneseDictionary()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        int longest = 0;
        try
        {
            foreach (var pair in ReadTsv("ja_readings.tsv.gz"))
            {
                map.TryAdd(pair[0], pair[1]);
                longest = Math.Max(longest, pair[0].Length);
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            AppLog.Warn("Lyrics", "Japanese reading dictionary unavailable: " + ex.Message);
        }
        return (map, longest);
    }

    static Dictionary<char, string> LoadPinyinTable()
    {
        var map = new Dictionary<char, string>();
        try
        {
            foreach (var pair in ReadTsv("zh_pinyin.tsv.gz"))
                if (pair[0].Length == 1) map.TryAdd(pair[0][0], pair[1]);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException)
        {
            AppLog.Warn("Lyrics", "Pinyin table unavailable: " + ex.Message);
        }
        return map;
    }

    // ───────────── Script detection ─────────────

    public static bool IsHan(char c) => LyricPronunciationPolicy.IsHan(c);

    public static bool HasKanji(string text) => !string.IsNullOrEmpty(text) && text.Any(LyricPronunciationPolicy.IsHan);

    /// <summary>True when the text contains kana, which only Japanese uses.</summary>
    public static bool ContainsJapanese(string text) => !string.IsNullOrEmpty(text) && text.Any(LyricPronunciationPolicy.IsKana);

    public static bool ContainsHangul(string text) => !string.IsNullOrEmpty(text) && text.Any(LyricPronunciationPolicy.IsHangul);

    /// <summary>
    /// The song's script from all of its lines: kana anywhere makes it Japanese unless Hangul lines outnumber kana lines; Hangul makes it
    /// Korean; Han characters alone make it Chinese.
    /// </summary>
    public static ScriptKind DetectSongScript(IEnumerable<string> lines)
    {
        int kana = 0, hangul = 0, han = 0;
        foreach (var line in lines ?? Enumerable.Empty<string>())
        {
            if (string.IsNullOrEmpty(line)) continue;
            if (ContainsJapanese(line)) kana++;
            if (ContainsHangul(line)) hangul++;
            if (HasKanji(line)) han++;
        }
        if (kana > 0 && kana >= hangul) return ScriptKind.Japanese;
        if (hangul > 0) return ScriptKind.Korean;
        if (han > 0) return ScriptKind.Chinese;
        return ScriptKind.None;
    }

    // ───────────── Single-script helpers ─────────────

    public static string KatakanaToHiragana(string s) => KanaRomanizer.ToHiragana(s);

    public static string KanaToRomaji(string kana) => KanaRomanizer.ToRomaji(kana);

    public static string RomanizeHangul(string hangul) => HangulRomanizer.Romanize(hangul);

    /// <summary>The one-line reading of pieces: each piece's romaji, or its text when it has none.</summary>
    public static string Reading(IEnumerable<RubyPiece> pieces) =>
        LyricReading.Line(pieces.Select(p => new ReadingSegment(p.Text, p.Furigana, p.Romaji, string.IsNullOrEmpty(p.Romaji) ? p.Text : p.Romaji)));

    /// <summary>Pieces for analyzer words (display text, kana reading, phrase start).</summary>
    public static List<RubyPiece> JapaneseRuby(List<(string Display, string Yomi, bool PhraseStart)> words) =>
        ToPieces(LyricReading.Japanese(words.Select(w => new JapaneseWord(w.Display, w.Yomi, w.PhraseStart)).ToList()));

    public static List<RubyPiece> KoreanRuby(string line) => ToPieces(LyricReading.Korean(line ?? ""));

    public static List<RubyPiece> ChineseRuby(string line) => ToPieces(LyricReading.Chinese(line ?? "", Pinyin));

    static string? Pinyin(char c) => PinyinTable.Value.TryGetValue(c, out var reading) ? reading : null;

    static List<RubyPiece> ToPieces(IEnumerable<ReadingSegment> segments) =>
        segments.Select(s => new RubyPiece { Text = s.Text, Furigana = s.Kana, Romaji = s.Romaji }).ToList();

    // ───────────── Japanese words ─────────────

    static List<JapaneseWord> JapaneseWords(string line)
    {
        var analyzed = AnalyzerWords(line);
        if (analyzed == null) return DictionaryWords(line, phraseStart: true);

        var words = new List<JapaneseWord>(analyzed.Count);
        foreach (var word in analyzed)
        {
            bool readable = word.Reading.Length > 0 && !word.Reading.Any(LyricPronunciationPolicy.IsHan);
            if (readable || !word.Display.Any(LyricPronunciationPolicy.IsHan)) words.Add(word);
            else words.AddRange(DictionaryWords(word.Display, word.PhraseStart));
        }
        return words;
    }

    static List<JapaneseWord>? AnalyzerWords(string line)
    {
        try
        {
            var words = new List<JapaneseWord>();
            foreach (var chunk in Chunks(line, AnalyzerLimit))
            {
                int position = 0;
                foreach (var phoneme in JapanesePhoneticAnalyzer.GetWords(chunk, false))
                {
                    string display = phoneme.DisplayText ?? "";
                    // The analyzer reports ASCII as full-width and may skip spaces: take the text back from the line where it lines up.
                    int start = position;
                    while (start < chunk.Length && char.IsWhiteSpace(chunk[start]) && !(display.Length > 0 && char.IsWhiteSpace(display[0]))) start++;
                    if (start > position) words.Add(new JapaneseWord(chunk[position..start], "", true));
                    if (start + display.Length <= chunk.Length &&
                        string.Equals(chunk.Substring(start, display.Length).Normalize(NormalizationForm.FormKC), display.Normalize(NormalizationForm.FormKC), StringComparison.Ordinal))
                    {
                        display = chunk.Substring(start, display.Length);
                        position = start + display.Length;
                    }
                    words.Add(new JapaneseWord(display, phoneme.YomiText ?? "", phoneme.IsPhraseStart));
                }
                if (position < chunk.Length && chunk[position..].All(char.IsWhiteSpace)) words.Add(new JapaneseWord(chunk[position..], "", true));
            }
            return words;
        }
        catch (Exception ex)
        {
            if (Interlocked.Exchange(ref _analyzerFailureLogged, 1) == 0)
                AppLog.Warn("Lyrics", "Japanese analyzer unavailable, using the reading dictionary: " + ex.Message);
            return null;
        }
    }

    // Splits at spaces or punctuation near the limit so a word is not cut in half when it can be avoided.
    static IEnumerable<string> Chunks(string text, int limit)
    {
        int start = 0;
        while (text.Length - start > limit)
        {
            int cut = start + limit;
            for (int i = cut - 1; i > start + limit / 2; i--)
                if (char.IsWhiteSpace(text[i]) || char.IsPunctuation(text[i])) { cut = i + 1; break; }
            yield return text[start..cut];
            start = cut;
        }
        if (start < text.Length) yield return text[start..];
    }

    /// <summary>
    /// Longest-match segmentation over the reading dictionary. Only entries that contain a kanji are used; kana runs are their own reading,
    /// and a kanji the dictionary does not know is left without a reading.
    /// </summary>
    static List<JapaneseWord> DictionaryWords(string text, bool phraseStart)
    {
        var (map, longest) = JapaneseDictionary.Value;
        var words = new List<JapaneseWord>();
        int i = 0;
        bool first = true;
        while (i < text.Length)
        {
            char c = text[i];
            if (LyricPronunciationPolicy.IsHan(c))
            {
                int length = 0;
                string reading = "";
                for (int l = Math.Min(longest, text.Length - i); l >= 1; l--)
                {
                    string candidate = text.Substring(i, l);
                    if (map.TryGetValue(candidate, out var r) && candidate.Any(LyricPronunciationPolicy.IsHan)) { length = l; reading = r; break; }
                }
                if (length == 0) { length = 1; reading = ""; }
                words.Add(new JapaneseWord(text.Substring(i, length), reading, first ? phraseStart : true));
                i += length;
            }
            else
            {
                int start = i;
                bool kana = LyricPronunciationPolicy.IsKana(c);
                while (i < text.Length && !LyricPronunciationPolicy.IsHan(text[i]) && LyricPronunciationPolicy.IsKana(text[i]) == kana) i++;
                string run = text[start..i];
                words.Add(new JapaneseWord(run, kana ? run : "", first ? phraseStart : !kana));
            }
            first = false;
        }
        return words;
    }

    // ───────────── Lines and songs ─────────────

    static PronouncedLine Pronounce(string text, ScriptKind script)
    {
        var result = new PronouncedLine { OriginalText = text ?? "" };
        if (!LyricPronunciationPolicy.NeedsReading(text)) return result;

        List<ReadingSegment> segments;
        if (ContainsHangul(text!) && !ContainsJapanese(text!)) segments = LyricReading.Korean(text!);
        else if (ContainsJapanese(text!) || script == ScriptKind.Japanese) segments = LyricReading.Japanese(JapaneseWords(text!));
        else segments = LyricReading.Chinese(text!, Pinyin);

        string full = LyricReading.Line(segments);
        if (full.Length == 0 || LyricPronunciationPolicy.IsRedundant(text, full)) return result;
        result.FullRomaji = full;
        result.Pieces = ToPieces(segments);
        return result;
    }

    /// <summary>
    /// Pronounces one line, choosing the script from the line itself. Runs on the calling thread when it is STA (the analyzer's class
    /// factory fails on MTA threads), otherwise blocks on a short-lived STA thread.
    /// </summary>
    public static PronouncedLine PronounceLine(string rawLine)
    {
        string text = rawLine ?? "";
        if (Thread.CurrentThread.GetApartmentState() != ApartmentState.STA)
            return RunOnSta(() => Pronounce(text, DetectSongScript(new[] { text })), CancellationToken.None).GetAwaiter().GetResult();
        return Pronounce(text, DetectSongScript(new[] { text }));
    }

    /// <summary>Pronounces one line on a background STA thread.</summary>
    public static Task<PronouncedLine> PronounceLineAsync(string rawLine) => RunOnSta(() => PronounceLine(rawLine), CancellationToken.None);

    /// <summary>
    /// Fills <see cref="LyricLine.RomajiText"/> and <see cref="LyricLine.RubyPieces"/> for every line of the song on a background STA
    /// thread and returns the same object. Repeated lines (choruses) are pronounced once. Cancellation stops between lines.
    /// </summary>
    public static Task<SongLyrics> PronounceSongAsync(SongLyrics lyrics, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lyrics);
        return RunOnSta(() =>
        {
            var script = DetectSongScript(lyrics.Lines.Select(l => l.Text));
            var done = new Dictionary<string, PronouncedLine>(StringComparer.Ordinal);
            foreach (var line in lyrics.Lines)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string text = line.Text ?? "";
                if (!done.TryGetValue(text, out var pronounced))
                {
                    pronounced = Pronounce(text, script);
                    done[text] = pronounced;
                }
                line.RomajiText = pronounced.FullRomaji;
                line.RubyPieces = pronounced.Pieces.Select(p => new RubyPiece { Text = p.Text, Furigana = p.Furigana, Romaji = p.Romaji }).ToList();
            }
            return lyrics;
        }, cancellationToken);
    }

    static Task<T> RunOnSta<T>(Func<T> work, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<T>(cancellationToken);
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try { completion.TrySetResult(work()); }
            catch (OperationCanceledException) { completion.TrySetCanceled(cancellationToken); }
            catch (Exception ex) { completion.TrySetException(ex); }
        })
        {
            IsBackground = true,
            Name = "Lyric pronunciation",
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }
}
