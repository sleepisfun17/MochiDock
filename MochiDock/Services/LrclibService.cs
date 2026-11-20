using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace MochiDock.Services;

/// <summary>One lyric line. <see cref="Timestamp"/> is zero for unsynced text; the pronunciation fields are filled later by <see cref="LyricRomanizerService"/>.</summary>
public class LyricLine
{
    public TimeSpan Timestamp { get; set; }
    public string Text { get; set; } = "";
    public string RomajiText { get; set; } = "";
    public List<RubyPiece> RubyPieces { get; set; } = new();
    public bool HasPronunciation => !string.IsNullOrEmpty(RomajiText) || RubyPieces.Count > 0;
}

/// <summary>The lyrics chosen for one track: synced lines when a trustworthy LRC record exists, otherwise plain text or an instrumental marker.</summary>
public class SongLyrics
{
    public bool IsInstrumental { get; set; }
    public string TrackName { get; set; } = "";
    public string ArtistName { get; set; } = "";
    public bool IsSynced { get; set; }
    public List<LyricLine> Lines { get; set; } = new();
    public string PlainLyrics { get; set; } = "";
}

/// <summary>
/// Looks up lyrics on LRCLIB (https://lrclib.net, public API, no key). The lookup tries the exact-signature endpoint first
/// (<c>/api/get</c>, with and then without the album, only when the track duration is known) and then the search endpoint
/// (<c>/api/search</c>, by fields and then as free text). A synced record wins as soon as one passes the checks; plain text or an
/// instrumental record is kept as the fallback while the lookup goes on. A server or network failure stops the lookup and is not cached.
/// Results live in a small memory cache and as one JSON file per track under <c>%LocalAppData%\MochiDock\Lyrics</c>.
/// </summary>
public class LrclibService
{
    /// <summary>A synced record whose duration differs from the playing track by more than this is not trusted for timing.</summary>
    public const double MaxSyncDriftSeconds = 6.0;
    /// <summary>A synced record needs at least this many timed lines with text.</summary>
    public const int MinLines = 4;
    /// <summary>A search result below this <see cref="MatchScore"/> is ignored.</summary>
    public const int MinScore = 40;

    const string ApiBase = "https://lrclib.net/api/";
    // 4: romaji uploads lose to native-script records, so lookups cached by format 3 are refetched.
    const int CacheFormat = 4;
    const int MemoryCapacity = 48;
    const int DiskCapacity = 600;
    static readonly TimeSpan SyncedLifetime = TimeSpan.FromDays(30);
    static readonly TimeSpan PlainLifetime = TimeSpan.FromDays(7);
    static readonly TimeSpan MissLifetime = TimeSpan.FromDays(1);
    static readonly JsonSerializerOptions CacheJson = new() { WriteIndented = false };

    readonly HttpClient _httpClient;
    readonly string _cacheDirectory;
    readonly object _gate = new();
    readonly Dictionary<string, CacheEntry> _memory = new(StringComparer.Ordinal);
    readonly LinkedList<string> _memoryOrder = new();
    readonly Dictionary<string, Task<LookupResult>> _inFlight = new(StringComparer.Ordinal);

    public LrclibService(HttpClient? client = null, string? cacheDirectory = null)
    {
        if (client == null)
        {
            client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
            client.DefaultRequestHeaders.Add("User-Agent", AppUserAgent.Value);
        }
        _httpClient = client;
        _cacheDirectory = cacheDirectory ?? Path.Combine(AppDataPaths.Local, "Lyrics");
    }

    /// <summary>True when the most recent lookup ended on a server or network failure (the UI offers a retry).</summary>
    public bool LastLookupFailed { get; private set; }

    // ───────────── Query clean-up ─────────────

    static readonly Regex BracketSegment = new(@"\s*[\(\[\{（【［〔](?<inner>[^\)\]\}）】］〕]*)[\)\]\}）】］〕]", RegexOptions.Compiled);
    static readonly Regex DashSuffix = new(@"\s+[-–—]\s+(?<tail>[^-–—]+)$", RegexOptions.Compiled);
    static readonly Regex FeaturingTail = new(@"\s+(?:feat\.?|ft\.?|featuring)\s+.*$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex ArtistSeparators = new(@"\s*(?:,|;|/|&|＆|、|\s+x\s+|\s+×\s+|\s+(?:feat\.?|ft\.?|featuring|with|vs\.?)\s+)\s*", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static readonly Regex Spaces = new(@"\s{2,}", RegexOptions.Compiled);

    // Words that mark a segment as packaging rather than part of the song's name.
    static readonly string[] PackagingWords =
    {
        "official", "video", "audio", "lyric", "lyrics", "mv", "m/v", "pv", "music video", "visualizer", "remaster", "remastered",
        "explicit", "clean", "hd", "hq", "4k", "live", "version", "ver.", "edit", "radio", "mono", "stereo", "feat", "ft.", "featuring",
        "from", "op", "ed", "opening", "ending", "theme", "topic", "full", "color coded", "romanized", "歌詞", "公式",
    };

    static bool IsPackaging(string segment)
    {
        var lower = segment.Trim().ToLowerInvariant();
        if (lower.Length == 0) return true;
        if (Regex.IsMatch(lower, @"^\d{4}$")) return true; // a bare year
        foreach (var word in PackagingWords)
        {
            if (lower == word || lower.StartsWith(word + " ", StringComparison.Ordinal) || lower.EndsWith(" " + word, StringComparison.Ordinal) ||
                lower.Contains(" " + word + " ", StringComparison.Ordinal))
                return true;
        }
        return false;
    }

    /// <summary>
    /// Removes packaging from a title or artist: bracketed notes such as "(Official Video)", "[Remastered 2011]" or "(feat. X)", a trailing
    /// " - Remastered" style suffix, and a trailing "feat. X". Bracketed text that looks like part of the name, such as "(Reprise)", stays.
    /// </summary>
    public static string StripDecorations(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        string text = s.Trim();
        text = BracketSegment.Replace(text, m => IsPackaging(m.Groups["inner"].Value) ? " " : m.Value);
        var dash = DashSuffix.Match(text);
        if (dash.Success && IsPackaging(dash.Groups["tail"].Value)) text = text[..dash.Index];
        text = FeaturingTail.Replace(text, "");
        text = text.Trim().Trim('"', '“', '”', '「', '」', '『', '』').Trim();
        return Spaces.Replace(text, " ");
    }

    /// <summary>
    /// A comparison key: NFKC (full-width becomes ASCII), lower case, accents removed, and every run of characters that is not a letter or
    /// digit turned into a single space. Kana, kanji and Hangul are letters, so they are kept.
    /// </summary>
    public static string Fold(string s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var decomposed = s.Normalize(NormalizationForm.FormKC).Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        bool pendingSpace = false;
        foreach (char c in decomposed)
        {
            var category = char.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark) continue;
            if (char.IsLetterOrDigit(c))
            {
                if (pendingSpace && builder.Length > 0) builder.Append(' ');
                pendingSpace = false;
                builder.Append(char.ToLowerInvariant(c));
            }
            else pendingSpace = true;
        }
        return builder.ToString().Normalize(NormalizationForm.FormC);
    }

    /// <summary>The first credited artist: "A, B", "A & B", "A x B", "A feat. B" and similar all give "A".</summary>
    public static string PrimaryArtist(string artist)
    {
        if (string.IsNullOrWhiteSpace(artist)) return "";
        var first = ArtistSeparators.Split(artist.Trim()).FirstOrDefault(p => !string.IsNullOrWhiteSpace(p)) ?? artist;
        return first.Trim();
    }

    /// <summary>
    /// Turns what the media session reports into an LRCLIB query. Video-site uploads often report "Artist - Title" as the title with a
    /// channel name as the artist ("Artist - Topic", "ArtistVEVO", or nothing); in that case the title is split. Packaging is removed from both.
    /// </summary>
    public static (string Title, string Artist) NormalizeQuery(string title, string artist)
    {
        string t = (title ?? "").Trim();
        string a = (artist ?? "").Trim();

        bool channelArtist = a.Length == 0 || a.EndsWith(" - Topic", StringComparison.OrdinalIgnoreCase) ||
                             a.EndsWith("VEVO", StringComparison.OrdinalIgnoreCase) || a.EndsWith("Official", StringComparison.OrdinalIgnoreCase);
        if (a.EndsWith(" - Topic", StringComparison.OrdinalIgnoreCase)) a = a[..^" - Topic".Length].Trim();
        else if (a.EndsWith("VEVO", StringComparison.OrdinalIgnoreCase) && a.Length > 4) a = a[..^4].Trim();

        var cleanedTitle = StripDecorations(t);
        int split = IndexOfTitleDash(cleanedTitle);
        if (split > 0)
        {
            string left = cleanedTitle[..split].Trim(), right = cleanedTitle[(split + 1)..].Trim();
            bool leftIsArtist = channelArtist || Fold(left) == Fold(a) || Fold(left).Contains(Fold(PrimaryArtist(a))) && Fold(a).Length > 0;
            if (leftIsArtist && left.Length > 0 && right.Length > 0)
            {
                cleanedTitle = StripDecorations(right);
                if (a.Length == 0 || channelArtist) a = left;
            }
        }

        a = StripDecorations(a);
        return (cleanedTitle, PrimaryArtist(a));
    }

    // The dash separating "Artist - Title": a hyphen or dash with spaces on both sides.
    static int IndexOfTitleDash(string s)
    {
        for (int i = 1; i < s.Length - 1; i++)
            if ((s[i] == '-' || s[i] == '–' || s[i] == '—') && s[i - 1] == ' ' && s[i + 1] == ' ') return i;
        return -1;
    }

    /// <summary>
    /// How well a search result fits the playing track, or null when it clearly is another song. Title agreement counts most (up to 50),
    /// then the artist (up to 30), the duration (up to 15) and a small bonus for synced text. <see cref="MinScore"/> is the acceptance line.
    /// </summary>
    public static int? MatchScore(string wantTitle, string wantArtist, double wantDuration, string candidateTitle, string candidateArtist, double candidateDuration, bool candidateSynced)
    {
        string wt = Fold(StripDecorations(wantTitle)), ct = Fold(StripDecorations(candidateTitle));
        if (wt.Length == 0 || ct.Length == 0) return null;

        int score;
        if (wt == ct) score = 50;
        else if (Squash(wt) == Squash(ct)) score = 46;
        else if (ct.Contains(wt) || wt.Contains(ct)) score = 32;
        else
        {
            double overlap = TokenOverlap(wt, ct);
            if (overlap < 0.5) return null;
            score = (int)Math.Round(overlap * 30);
        }

        string wa = Fold(wantArtist), ca = Fold(candidateArtist);
        if (wa.Length > 0 && ca.Length > 0)
        {
            string wp = Fold(PrimaryArtist(wantArtist)), cp = Fold(PrimaryArtist(candidateArtist));
            if (wa == ca) score += 30;
            else if (wp == cp || ca.Contains(wp) || wa.Contains(cp)) score += 22;
            else if (TokenOverlap(wa, ca) >= 0.5) score += 12;
            else score -= 20;
        }
        else score += 10; // nothing to compare, neither confirmed nor contradicted

        if (wantDuration > 0 && candidateDuration > 0)
        {
            double drift = Math.Abs(wantDuration - candidateDuration);
            if (drift <= 2) score += 15;
            else if (drift <= MaxSyncDriftSeconds) score += 8;
            else if (drift > 30) score -= 25;
            else score -= 10;
        }

        if (candidateSynced) score += 5;
        return score;
    }

    static string Squash(string folded) => folded.Replace(" ", "", StringComparison.Ordinal);

    static double TokenOverlap(string a, string b)
    {
        var left = a.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        var right = b.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
        if (left.Count == 0 || right.Count == 0) return 0;
        int shared = left.Count(right.Contains);
        return (double)shared / Math.Max(left.Count, right.Count);
    }

    /// <summary>The track identity used by the UI (lyric offset, retry) and the caches: session, cleaned title and artist, album and rounded duration.</summary>
    public static MochiDock.Core.LyricTrackIdentity CreateIdentity(string session, string title, string artist, string album, double duration)
    {
        var (cleanTitle, cleanArtist) = NormalizeQuery(title, artist);
        return MochiDock.Core.LyricTrackIdentity.Create(session ?? "", cleanTitle, cleanArtist, album ?? "", duration);
    }

    // ───────────── Lookup ─────────────

    public async Task<SongLyrics?> FetchLyricsAsync(string rawTrack, string rawArtist, string album = "", double durationSeconds = 0, CancellationToken cancellationToken = default, string sessionIdentity = "", bool forceRefresh = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var (title, artist) = NormalizeQuery(rawTrack ?? "", rawArtist ?? "");
        if (title.Length == 0) { LastLookupFailed = false; return null; }

        double duration = double.IsFinite(durationSeconds) && durationSeconds > 0 ? durationSeconds : 0;
        string key = CacheKey(title, artist, album ?? "", duration);

        if (!forceRefresh && TryReadCache(key, out var cached))
        {
            LastLookupFailed = false;
            return cached;
        }

        Task<LookupResult> shared;
        lock (_gate)
        {
            // A finished task left behind by a cancelled caller is not reused: its outcome is already cached, or it failed and deserves a retry.
            if (forceRefresh || !_inFlight.TryGetValue(key, out shared!) || shared.IsCompleted)
            {
                shared = LookupAndStoreAsync(key, title, artist, album ?? "", duration, cancellationToken);
                _inFlight[key] = shared;
            }
        }

        LookupResult result;
        try { result = await shared.WaitAsync(cancellationToken).ConfigureAwait(false); }
        finally
        {
            if (shared.IsCompleted)
                lock (_gate) { if (_inFlight.TryGetValue(key, out var current) && current == shared) _inFlight.Remove(key); }
        }

        LastLookupFailed = result.Failed;
        return result.Lyrics;
    }

    sealed record LookupResult(SongLyrics? Lyrics, bool Failed);

    async Task<LookupResult> LookupAndStoreAsync(string key, string title, string artist, string album, double duration, CancellationToken token)
    {
        var lookup = new Lookup(title, artist, duration);
        bool failed = await RunLookupAsync(lookup, album, token).ConfigureAwait(false);
        var lyrics = lookup.Result();
        if (!failed) WriteCache(key, lyrics);
        return new LookupResult(lyrics, failed);
    }

    /// <summary>Runs the request ladder until a synced record is accepted. Returns true when it stopped on a server or network failure.</summary>
    async Task<bool> RunLookupAsync(Lookup lookup, string album, CancellationToken token)
    {
        var steps = new List<(string Path, bool IsSearch)>();
        string t = Uri.EscapeDataString(lookup.Title), a = Uri.EscapeDataString(lookup.Artist);
        if (lookup.Duration > 0)
        {
            string d = Math.Round(lookup.Duration).ToString(CultureInfo.InvariantCulture);
            if (!string.IsNullOrWhiteSpace(album))
                steps.Add(($"get?track_name={t}&artist_name={a}&album_name={Uri.EscapeDataString(album.Trim())}&duration={d}", false));
            steps.Add(($"get?track_name={t}&artist_name={a}&duration={d}", false));
        }
        steps.Add((lookup.Artist.Length > 0 ? $"search?track_name={t}&artist_name={a}" : $"search?track_name={t}", true));
        steps.Add(($"search?q={Uri.EscapeDataString((lookup.Title + " " + lookup.Artist).Trim())}", true));

        foreach (var (path, isSearch) in steps)
        {
            token.ThrowIfCancellationRequested();
            string? body;
            try
            {
                using var response = await _httpClient.GetAsync(ApiBase + path, token).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.NotFound) continue;
                if (!response.IsSuccessStatusCode)
                {
                    AppLog.Warn("Lyrics", $"LRCLIB answered {(int)response.StatusCode} for {(isSearch ? "search" : "get")}");
                    return true;
                }
                body = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
            {
                AppLog.Warn("Lyrics", "LRCLIB request failed: " + ex.Message);
                return true;
            }

            foreach (var record in ReadRecords(body))
                lookup.Consider(record, trustedMatch: !isSearch);
            if (lookup.HasSynced) return false;
        }
        return false;
    }

    sealed record Record(string TrackName, string ArtistName, double Duration, bool Instrumental, string Plain, string Synced);

    static IEnumerable<Record> ReadRecords(string? body)
    {
        if (string.IsNullOrWhiteSpace(body)) return Array.Empty<Record>();
        try
        {
            using var doc = JsonDocument.Parse(body);
            var list = new List<Record>();
            if (doc.RootElement.ValueKind == JsonValueKind.Object) AddRecord(doc.RootElement, list);
            else if (doc.RootElement.ValueKind == JsonValueKind.Array)
                foreach (var item in doc.RootElement.EnumerateArray())
                    if (item.ValueKind == JsonValueKind.Object) AddRecord(item, list);
            return list;
        }
        catch (JsonException) { return Array.Empty<Record>(); }
    }

    static void AddRecord(JsonElement e, List<Record> list)
    {
        static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
        double duration = e.TryGetProperty("duration", out var d) && d.ValueKind == JsonValueKind.Number ? d.GetDouble() : 0;
        bool instrumental = e.TryGetProperty("instrumental", out var i) && i.ValueKind == JsonValueKind.True;
        list.Add(new Record(Str(e, "trackName"), Str(e, "artistName"), duration, instrumental, Str(e, "plainLyrics"), Str(e, "syncedLyrics")));
    }

    /// <summary>
    /// Collects the best synced candidate and the best fallback (plain text or instrumental) seen during one lookup.
    /// LRCLIB often holds a user-uploaded romaji copy of a Japanese song next to the original; a romaji synced record is kept only
    /// as a last resort, and the lookup carries on searching for a native-script one (MochiDock.Core.LyricScript).
    /// </summary>
    sealed class Lookup(string title, string artist, double duration)
    {
        public string Title { get; } = title;
        public string Artist { get; } = artist;
        public double Duration { get; } = duration;

        SongLyrics? _synced;
        SongLyrics? _romajiSynced;
        SongLyrics? _fallback;
        int _fallbackScore = int.MinValue;

        /// <summary>True once a synced record that is not a romaji transliteration was accepted: the request ladder can stop.</summary>
        public bool HasSynced => _synced != null;

        public SongLyrics? Result() => _synced ?? _romajiSynced ?? _fallback;

        public void Consider(Record r, bool trustedMatch)
        {
            int score;
            if (trustedMatch) score = 100; // the exact-signature endpoint already matched title, artist and duration
            else
            {
                var s = MatchScore(Title, Artist, Duration, r.TrackName, r.ArtistName, r.Duration, r.Synced.Length > 0);
                if (s is null || s < MinScore) return;
                score = s.Value;
            }

            if (_synced == null && r.Synced.Length > 0 && DurationFits(r.Duration))
            {
                var lines = ParseLrc(r.Synced);
                if (lines.Count(l => !string.IsNullOrWhiteSpace(l.Text)) >= MinLines)
                {
                    var found = new SongLyrics
                    {
                        TrackName = Name(r.TrackName, Title), ArtistName = Name(r.ArtistName, Artist), IsSynced = true,
                        Lines = lines, PlainLyrics = r.Plain.Length > 0 ? r.Plain : string.Join("\n", lines.Select(l => l.Text)),
                    };
                    if (!MochiDock.Core.LyricScript.LooksLikeRomaji(found.PlainLyrics)) _synced = found;
                    else _romajiSynced ??= found;
                    return;
                }
            }

            string plain = r.Plain.Length > 0 ? r.Plain : StripTimestamps(r.Synced);
            bool usable = plain.Trim().Length > 0 || r.Instrumental;
            // Text beats an instrumental marker, and native script beats a romaji copy; among equals the higher score wins.
            int rank = score + (plain.Trim().Length > 0 ? 1000 : 0) - (MochiDock.Core.LyricScript.LooksLikeRomaji(plain) ? 500 : 0);
            if (usable && rank > _fallbackScore)
            {
                _fallbackScore = rank;
                _fallback = new SongLyrics
                {
                    TrackName = Name(r.TrackName, Title), ArtistName = Name(r.ArtistName, Artist),
                    IsInstrumental = plain.Trim().Length == 0 && r.Instrumental, PlainLyrics = plain.Trim(),
                };
            }
        }

        bool DurationFits(double candidate) => Duration <= 0 || candidate <= 0 || Math.Abs(Duration - candidate) <= MaxSyncDriftSeconds;

        static string Name(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;

        static string StripTimestamps(string lrc) =>
            lrc.Length == 0 ? "" : string.Join("\n", ParseLrc(lrc).Select(l => l.Text)).Trim();
    }

    // ───────────── LRC ─────────────

    static readonly Regex WordTimestamp = new(@"<\d{1,3}:\d{1,2}(?:[.:]\d{1,3})?>", RegexOptions.Compiled);

    /// <summary>
    /// Parses LRC text. A line may carry several leading time tags (<c>[mm:ss]</c>, <c>[mm:ss.xx]</c>, <c>[mm:ss.xxx]</c> or <c>[mm:ss:xx]</c>);
    /// it then appears once per tag. <c>[offset:±ms]</c> moves every line (positive = earlier). Other ID tags (<c>[ar:…]</c>, <c>[ti:…]</c>)
    /// and enhanced-LRC word tags (<c>&lt;mm:ss.xx&gt;</c>) are dropped. Lines with no time tag are ignored; empty timed lines are kept as pauses.
    /// The result is ordered by time, keeping file order for equal times.
    /// </summary>
    public static List<LyricLine> ParseLrc(string lrcContent)
    {
        var timed = new List<(double Seconds, int Order, string Text)>();
        if (string.IsNullOrEmpty(lrcContent)) return new List<LyricLine>();

        double offsetSeconds = 0;
        int order = 0;
        foreach (var raw in lrcContent.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = raw.TrimStart('﻿', ' ', '\t');
            var stamps = new List<double>();
            int pos = 0;
            while (pos < line.Length && line[pos] == '[')
            {
                int close = line.IndexOf(']', pos + 1);
                if (close < 0) break;
                string tag = line.Substring(pos + 1, close - pos - 1);
                if (TryParseTime(tag, out double seconds)) stamps.Add(seconds);
                else if (stamps.Count == 0 && TryParseOffset(tag, out double offset)) offsetSeconds = offset;
                else if (stamps.Count > 0) break; // a bracket after the times belongs to the text
                pos = close + 1;
                while (pos < line.Length && line[pos] == ' ' && stamps.Count == 0) pos++;
            }
            if (stamps.Count == 0) continue;

            string text = WordTimestamp.Replace(line[pos..], "").Trim();
            text = Spaces.Replace(text, " ");
            foreach (var s in stamps) timed.Add((s, order++, text));
        }

        return timed
            .OrderBy(t => t.Seconds).ThenBy(t => t.Order)
            .Select(t => new LyricLine { Timestamp = TimeSpan.FromSeconds(Math.Max(0, t.Seconds - offsetSeconds)), Text = t.Text })
            .ToList();
    }

    static bool TryParseTime(string tag, out double seconds)
    {
        seconds = 0;
        int colon = tag.IndexOf(':');
        if (colon <= 0) return false;
        string minutesPart = tag[..colon], rest = tag[(colon + 1)..];
        if (!minutesPart.All(char.IsAsciiDigit) || rest.Length < 1) return false;

        string secondsPart = rest, fractionPart = "";
        int sep = rest.IndexOfAny(new[] { '.', ':' });
        if (sep >= 0) { secondsPart = rest[..sep]; fractionPart = rest[(sep + 1)..]; }
        if (secondsPart.Length is < 1 or > 2 || !secondsPart.All(char.IsAsciiDigit)) return false;
        if (fractionPart.Length > 3 || !fractionPart.All(char.IsAsciiDigit)) return false;

        int minutes = int.Parse(minutesPart, CultureInfo.InvariantCulture);
        int secs = int.Parse(secondsPart, CultureInfo.InvariantCulture);
        if (secs >= 60) return false;
        double fraction = fractionPart.Length == 0 ? 0 : int.Parse(fractionPart, CultureInfo.InvariantCulture) / Math.Pow(10, fractionPart.Length);
        seconds = minutes * 60 + secs + fraction;
        return true;
    }

    static bool TryParseOffset(string tag, out double seconds)
    {
        seconds = 0;
        if (!tag.StartsWith("offset:", StringComparison.OrdinalIgnoreCase)) return false;
        if (!int.TryParse(tag["offset:".Length..].Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out int ms)) return false;
        seconds = ms / 1000.0;
        return true;
    }

    // ───────────── Cache ─────────────

    sealed class CacheEntry
    {
        public int Format { get; set; }
        public DateTimeOffset Saved { get; set; }
        public string Key { get; set; } = "";
        public SongLyrics? Lyrics { get; set; }
    }

    static string CacheKey(string title, string artist, string album, double duration) =>
        string.Join("\u001f", Fold(title), Fold(artist), Fold(album), Math.Round(duration).ToString(CultureInfo.InvariantCulture));

    string CachePath(string key)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Path.Combine(_cacheDirectory, Convert.ToHexString(hash, 0, 12).ToLowerInvariant() + ".json");
    }

    static TimeSpan Lifetime(SongLyrics? lyrics) =>
        lyrics == null ? MissLifetime : lyrics.IsSynced ? SyncedLifetime : PlainLifetime;

    static bool IsFresh(CacheEntry entry) =>
        entry.Format == CacheFormat && DateTimeOffset.UtcNow - entry.Saved < Lifetime(entry.Lyrics) && entry.Saved <= DateTimeOffset.UtcNow.AddMinutes(5);

    bool TryReadCache(string key, out SongLyrics? lyrics)
    {
        lyrics = null;
        lock (_gate)
        {
            if (_memory.TryGetValue(key, out var hit))
            {
                if (IsFresh(hit)) { Touch(key); lyrics = hit.Lyrics; return true; }
                ForgetMemory(key);
            }
        }

        try
        {
            string path = CachePath(key);
            if (!File.Exists(path)) return false;
            var entry = JsonSerializer.Deserialize<CacheEntry>(File.ReadAllText(path), CacheJson);
            if (entry == null || entry.Key != key || !IsFresh(entry)) return false;
            lock (_gate) Remember(key, entry);
            lyrics = entry.Lyrics;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return false;
        }
    }

    void WriteCache(string key, SongLyrics? lyrics)
    {
        var entry = new CacheEntry { Format = CacheFormat, Saved = DateTimeOffset.UtcNow, Key = key, Lyrics = lyrics };
        lock (_gate) Remember(key, entry);
        try
        {
            Directory.CreateDirectory(_cacheDirectory);
            string path = CachePath(key), temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(entry, CacheJson));
            File.Move(temp, path, overwrite: true);
            TrimDisk();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Warn("Lyrics", "Lyrics cache write failed: " + ex.Message);
        }
    }

    void TrimDisk()
    {
        var files = new DirectoryInfo(_cacheDirectory).GetFiles("*.json");
        if (files.Length <= DiskCapacity) return;
        foreach (var old in files.OrderBy(f => f.LastWriteTimeUtc).Take(files.Length - DiskCapacity))
        {
            try { old.Delete(); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    void Remember(string key, CacheEntry entry)
    {
        if (_memory.ContainsKey(key)) _memoryOrder.Remove(key);
        _memory[key] = entry;
        _memoryOrder.AddFirst(key);
        while (_memoryOrder.Count > MemoryCapacity)
        {
            var last = _memoryOrder.Last!.Value;
            _memoryOrder.RemoveLast();
            _memory.Remove(last);
        }
    }

    void Touch(string key)
    {
        _memoryOrder.Remove(key);
        _memoryOrder.AddFirst(key);
    }

    void ForgetMemory(string key)
    {
        _memory.Remove(key);
        _memoryOrder.Remove(key);
    }
}
