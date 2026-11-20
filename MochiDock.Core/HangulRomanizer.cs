using System.Text;

namespace MochiDock.Core;

/// <summary>
/// Hangul to Latin letters by the Revised Romanization of Korean (Ministry of Culture and Tourism, 2000). Syllables are split into lead,
/// vowel and tail with the Unicode arithmetic (syllable = 0xAC00 + (lead * 21 + vowel) * 28 + tail), and the sound changes the system
/// writes are applied inside each space-separated word: linking a final consonant to a following vowel, palatalisation (굳이 = guji),
/// aspiration with ㅎ (좋다 = jota), nasalisation (국물 = gungmul) and the ㄹ/ㄴ assimilations (신라 = silla, 종로 = jongno). Tensing is
/// not written, as the system prescribes. Standalone jamo (ㅋㅋ) get the letter of their sound; other characters pass through.
/// </summary>
public static class HangulRomanizer
{
    const int SyllableBase = 0xAC00, SyllableLast = 0xD7A3, VowelCount = 21, TailCount = 28;

    // The consonants in one order for leads and tails.
    enum C { None, G, KK, N, D, TT, R, M, B, PP, S, SS, Ng, J, JJ, Ch, K, T, P, H }

    static readonly C[] Leads = { C.G, C.KK, C.N, C.D, C.TT, C.R, C.M, C.B, C.PP, C.S, C.SS, C.Ng, C.J, C.JJ, C.Ch, C.K, C.T, C.P, C.H };

    // Tail index 1..27 as one or two consonants (index 0 = no tail).
    static readonly C[][] Tails =
    {
        Array.Empty<C>(), new[] { C.G }, new[] { C.KK }, new[] { C.G, C.S }, new[] { C.N }, new[] { C.N, C.J }, new[] { C.N, C.H },
        new[] { C.D }, new[] { C.R }, new[] { C.R, C.G }, new[] { C.R, C.M }, new[] { C.R, C.B }, new[] { C.R, C.S }, new[] { C.R, C.T },
        new[] { C.R, C.P }, new[] { C.R, C.H }, new[] { C.M }, new[] { C.B }, new[] { C.B, C.S }, new[] { C.S }, new[] { C.SS },
        new[] { C.Ng }, new[] { C.J }, new[] { C.Ch }, new[] { C.K }, new[] { C.T }, new[] { C.P }, new[] { C.H },
    };

    static readonly string[] Vowels =
    {
        "a", "ae", "ya", "yae", "eo", "e", "yeo", "ye", "o", "wa", "wae", "oe", "yo", "u", "wo", "we", "wi", "yu", "eu", "ui", "i",
    };
    const int VowelI = 20;

    static string Onset(C c) => c switch
    {
        C.G => "g", C.KK => "kk", C.N => "n", C.D => "d", C.TT => "tt", C.R => "r", C.M => "m", C.B => "b", C.PP => "pp",
        C.S => "s", C.SS => "ss", C.J => "j", C.JJ => "jj", C.Ch => "ch", C.K => "k", C.T => "t", C.P => "p", C.H => "h",
        _ => "",
    };

    // A tail before a consonant or at the end of a word is pronounced as one of seven sounds.
    static C Neutral(C c) => c switch
    {
        C.G or C.KK or C.K => C.K,
        C.D or C.S or C.SS or C.J or C.Ch or C.T or C.H => C.T,
        C.B or C.P => C.P,
        _ => c,
    };

    static string Coda(C c) => c switch
    {
        C.K => "k", C.T => "t", C.P => "p", C.N => "n", C.R => "l", C.M => "m", C.Ng => "ng", _ => "",
    };

    sealed class Syllable
    {
        public C Lead;
        public int Vowel;
        public List<C> Tail = new();
    }

    public static bool IsSyllable(char c) => c >= SyllableBase && c <= SyllableLast;

    /// <summary>Romanizes a text; each run of Hangul syllables is treated as one word for the sound changes.</summary>
    public static string Romanize(string text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var output = new StringBuilder(text.Length * 3);
        int i = 0;
        while (i < text.Length)
        {
            if (IsSyllable(text[i]))
            {
                int start = i;
                while (i < text.Length && IsSyllable(text[i])) i++;
                output.Append(RomanizeWord(text.AsSpan(start, i - start)));
            }
            else
            {
                output.Append(Jamo(text[i]) ?? text[i].ToString());
                i++;
            }
        }
        return output.ToString();
    }

    static string RomanizeWord(ReadOnlySpan<char> word)
    {
        var syllables = new List<Syllable>(word.Length);
        foreach (char ch in word)
        {
            int index = ch - SyllableBase;
            syllables.Add(new Syllable
            {
                Lead = Leads[index / (VowelCount * TailCount)],
                Vowel = index % (VowelCount * TailCount) / TailCount,
                Tail = new List<C>(Tails[index % TailCount]),
            });
        }

        for (int k = 0; k < syllables.Count; k++)
        {
            var current = syllables[k];
            var next = k + 1 < syllables.Count ? syllables[k + 1] : null;
            if (current.Tail.Count == 0) continue;
            if (next == null) { Settle(current); continue; }

            if (next.Lead == C.Ng)
            {
                Link(current, next);
                continue;
            }

            // ㅎ before ㄱ, ㄷ, ㅈ makes them aspirated; before ㄴ it sounds as ㄴ; elsewhere it is silent or a plain t.
            if (current.Tail[^1] == C.H)
            {
                if (next.Lead is C.G or C.D or C.J)
                {
                    next.Lead = next.Lead == C.G ? C.K : next.Lead == C.D ? C.T : C.Ch;
                    current.Tail.RemoveAt(current.Tail.Count - 1);
                }
                else if (next.Lead == C.N && current.Tail.Count == 1) current.Tail[0] = C.N;
                else if (current.Tail.Count == 2 || next.Lead == C.S) current.Tail.RemoveAt(current.Tail.Count - 1);
            }

            Settle(current);
            if (current.Tail.Count == 0) continue;
            C tail = current.Tail[0];

            // ㄱ, ㄷ, ㅂ (and what sounds like them) before ㅎ: the two merge into an aspirate.
            if (next.Lead == C.H && tail is C.K or C.T or C.P)
            {
                next.Lead = tail == C.K ? C.K : tail == C.T ? C.T : C.P;
                current.Tail.Clear();
                continue;
            }

            // ㄹ after anything but ㄴ or ㄹ is read ㄴ; ㄴ next to ㄹ becomes ㄹ.
            if (next.Lead == C.R)
            {
                if (tail == C.N || tail == C.R) { current.Tail[0] = C.R; }
                else next.Lead = C.N;
            }
            else if (next.Lead == C.N && tail == C.R)
            {
                next.Lead = C.R;
            }

            // Stops before a nasal become nasals.
            if (next.Lead is C.N or C.M)
                current.Tail[0] = current.Tail[0] switch { C.K => C.Ng, C.T => C.N, C.P => C.M, var same => same };
        }

        var output = new StringBuilder(syllables.Count * 4);
        C previousTail = C.None;
        foreach (var s in syllables)
        {
            // ㄹ written as onset after a ㄹ tail is "l" (ll), otherwise "r".
            output.Append(s.Lead == C.R && previousTail == C.R ? "l" : Onset(s.Lead));
            output.Append(Vowels[s.Vowel]);
            previousTail = s.Tail.Count > 0 ? s.Tail[0] : C.None;
            output.Append(previousTail == C.None ? "" : Coda(previousTail));
        }
        return output.ToString();
    }

    // A tail followed by a vowel moves to the next syllable (the last consonant of a cluster moves; ㅎ goes silent).
    static void Link(Syllable current, Syllable next)
    {
        C moving = current.Tail[^1];
        current.Tail.RemoveAt(current.Tail.Count - 1);
        if (moving == C.H)
        {
            // 많아 = mana, 좋아 = joa: the ㅎ disappears and a remaining ㄴ or ㄹ moves instead.
            if (current.Tail.Count == 1) { moving = current.Tail[0]; current.Tail.Clear(); }
            else return;
        }
        if (moving == C.Ng)
        {
            current.Tail.Add(C.Ng); // ㅇ stays where it is: 영어 = yeongeo
            return;
        }
        // 굳이, 같이: ㄷ and ㅌ before 이 become ㅈ and ㅊ.
        if (next.Vowel == VowelI && current.Tail.Count == 0)
            moving = moving == C.D ? C.J : moving == C.T ? C.Ch : moving;
        next.Lead = moving;
        Settle(current);
    }

    // Reduces a cluster to the consonant that is pronounced and neutralises the tail.
    static void Settle(Syllable s)
    {
        if (s.Tail.Count == 2)
        {
            var (first, second) = (s.Tail[0], s.Tail[1]);
            C kept = (first, second) switch
            {
                (C.R, C.G) => C.G,   // ㄺ: 닭 = dak
                (C.R, C.M) => C.M,   // ㄻ: 삶 = sam
                (C.R, C.P) => C.P,   // ㄿ
                (C.B, C.S) => C.B,   // ㅄ: 없다 = eopda
                _ => first,          // ㄳ ㄵ ㄶ ㄼ ㄽ ㄾ ㅀ keep the first
            };
            s.Tail.Clear();
            s.Tail.Add(kept);
        }
        if (s.Tail.Count == 1) s.Tail[0] = Neutral(s.Tail[0]);
    }

    // Compatibility jamo typed on their own: consonants by their sound, vowels by their letters.
    static string? Jamo(char c)
    {
        const string consonants = "ㄱㄲㄳㄴㄵㄶㄷㄸㄹㄺㄻㄼㄽㄾㄿㅀㅁㅂㅃㅄㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ";
        string[] consonantSounds =
        {
            "g", "kk", "gs", "n", "nj", "nh", "d", "tt", "r", "lg", "lm", "lb", "ls", "lt", "lp", "lh", "m", "b", "pp", "bs",
            "s", "ss", "ng", "j", "jj", "ch", "k", "t", "p", "h",
        };
        int ci = consonants.IndexOf(c);
        if (ci >= 0) return consonantSounds[ci];
        if (c >= 'ㅏ' && c <= 'ㅣ') return Vowels[c - 'ㅏ'];
        return null;
    }
}
