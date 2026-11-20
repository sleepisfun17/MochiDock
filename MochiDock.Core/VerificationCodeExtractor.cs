using System.Text.RegularExpressions;

namespace MochiDock.Core;

/// <summary>
/// Finds the one-time / verification code in a mail's subject and preview text, for the Email card's Copy Code button.
/// Every plausible token is scored (a token right after "code:" beats a hyphenated fragment elsewhere in the mail), and
/// links, addresses and domains are removed first so their fragments can never win. Returns null when nothing qualifies.
/// </summary>
public static class VerificationCodeExtractor
{
    // A mail must mention a code at all; otherwise no number in it is treated as one.
    static readonly Regex TriggerKeywords = new(
        @"\b(?:code|verification|verify|verifikasi|otp|passcode|security\s*code|pin|kode|one-time|auth|token)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // Words whose next few tokens are taken as "the code" ("code: 110445", "kode verifikasi Anda 5521", "OTP 849201").
    static readonly Regex LabelKeywords = new(@"\b(?:code|otp|passcode|pin|kode|token)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    static readonly Regex Urls = new(@"(?:https?://|www\.)\S+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    static readonly Regex EmailAddresses = new(@"\S+@\S+", RegexOptions.CultureInvariant);
    static readonly Regex Domains = new(@"\b(?:[\w-]+\.)+[A-Za-z]{2,}\b(?:/\S*)?", RegexOptions.CultureInvariant);

    static readonly Regex Tokens = new(@"[A-Za-z0-9]+(?:-[A-Za-z0-9]+)?", RegexOptions.CultureInvariant);
    static readonly Regex Digits = new(@"^\d{4,8}$", RegexOptions.CultureInvariant);
    static readonly Regex Formatted = new(@"^(?:[A-Z0-9]{2,4}-[A-Z0-9]{3,8}|G-\d{4,8})$", RegexOptions.CultureInvariant);
    static readonly Regex MixedUpper = new(@"^(?=[A-Z0-9]*\d)(?=[A-Z0-9]*[A-Z])[A-Z0-9]{5,8}$", RegexOptions.CultureInvariant);

    const int LabelledScore = 100, FormattedScore = 70, DigitsScore = 40;
    const int KeywordBeforeBonus = 30, KeywordAfterBonus = 10, RepeatedBonus = 15, SubjectBonus = 10;
    const int KeywordWindow = 40, LabelLookahead = 3;

    static readonly string[] CurrencyMarks = { "$", "€", "£", "¥", "rp", "idr", "usd", "jpy", "eur" };

    public static string? Extract(string? subject, string? body)
    {
        string cleanSubject = Clean(subject);
        string text = (cleanSubject + " \n " + Clean(body)).Trim();
        if (text.Length == 0 || !TriggerKeywords.IsMatch(text)) return null;
        int subjectEnd = cleanSubject.Length;

        var tokens = Tokens.Matches(text).Cast<Match>().ToList();
        var keywordSpans = TriggerKeywords.Matches(text).Cast<Match>().Select(m => (Start: m.Index, End: m.Index + m.Length)).ToList();
        var scores = new Dictionary<string, (int Score, int Index)>(StringComparer.Ordinal);

        void Offer(Match token, int score)
        {
            if (token.Index < subjectEnd) score += SubjectBonus;
            if (!scores.TryGetValue(token.Value, out var best) || score > best.Score)
                scores[token.Value] = (score, best.Score == 0 ? token.Index : Math.Min(best.Index, token.Index));
            else if (token.Index < best.Index)
                scores[token.Value] = (best.Score, token.Index);
        }

        // Labelled: the first code-like token within a few tokens after "code", "OTP", "kode", ...
        foreach (Match label in LabelKeywords.Matches(text))
        {
            int labelEnd = label.Index + label.Length;
            int seen = 0;
            foreach (var token in tokens.Where(t => t.Index >= labelEnd))
            {
                if (seen++ >= LabelLookahead) break;
                if (IsCode(text, token)) { Offer(token, LabelledScore - 5 * (seen - 1)); break; }
            }
        }

        // Everything else that looks like a code, weighted by how close it is to a keyword.
        foreach (var token in tokens)
        {
            if (!IsCode(text, token)) continue;
            if (Digits.IsMatch(token.Value))
            {
                int score = DigitsScore;
                if (keywordSpans.Any(k => k.End <= token.Index && token.Index - k.End <= KeywordWindow)) score += KeywordBeforeBonus;
                else if (keywordSpans.Any(k => k.Start >= token.Index + token.Length && k.Start - (token.Index + token.Length) <= KeywordWindow)) score += KeywordAfterBonus;
                Offer(token, score);
            }
            else Offer(token, FormattedScore);
        }

        if (scores.Count == 0) return null;
        foreach (var value in tokens.Where(t => scores.ContainsKey(t.Value)).GroupBy(t => t.Value).Where(g => g.Count() > 1).Select(g => g.Key))
            scores[value] = (scores[value].Score + RepeatedBonus, scores[value].Index);

        return scores.OrderByDescending(s => s.Value.Score).ThenBy(s => s.Value.Index).First().Key;
    }

    /// <summary>Links, email addresses and domains are blanked so fragments of them are never offered as a code.</summary>
    static string Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        text = Urls.Replace(text, " ");
        text = EmailAddresses.Replace(text, " ");
        return Domains.Replace(text, " ").Trim();
    }

    static bool IsCode(string text, Match token)
    {
        string value = token.Value;
        if (Digits.IsMatch(value))
            return !IsYear(value) && !IsPartOfLargerNumber(text, token) && !IsMoney(text, token);
        // Hyphenated or mixed codes must not contain lowercase letters: that rules out words ("one-time", "sign-up") and link fragments.
        return Formatted.IsMatch(value) && value.Any(char.IsDigit) || MixedUpper.IsMatch(value);
    }

    static bool IsYear(string digits) => digits.Length == 4 && int.TryParse(digits, out int year) && year is >= 1970 and <= 2050;

    // Times, dates, decimals, percentages and phone-number groups: "21:14", "2026/11/19", "1.50", "15%", "+62 812 3456".
    static bool IsPartOfLargerNumber(string text, Match token)
    {
        int before = token.Index - 1, after = token.Index + token.Length;
        char prev = before >= 0 ? text[before] : ' ';
        char next = after < text.Length ? text[after] : ' ';
        if (prev is ':' or '/' or '+' || next is ':' or '/' or '%') return true;
        if ((prev is '.' or ',') && before > 0 && char.IsDigit(text[before - 1])) return true;
        if ((next is '.' or ',') && after + 1 < text.Length && char.IsDigit(text[after + 1])) return true;
        // A digit group separated by a single space ("812 3456 7890") is a phone number, not a code.
        if (prev == ' ' && before > 0 && char.IsDigit(text[before - 1])) return true;
        if (next == ' ' && after + 1 < text.Length && char.IsDigit(text[after + 1])) return true;
        return false;
    }

    static bool IsMoney(string text, Match token)
    {
        int start = Math.Max(0, token.Index - 5);
        string lead = text.Substring(start, token.Index - start).ToLowerInvariant();
        return CurrencyMarks.Any(mark => lead.TrimEnd().EndsWith(mark, StringComparison.Ordinal));
    }
}
