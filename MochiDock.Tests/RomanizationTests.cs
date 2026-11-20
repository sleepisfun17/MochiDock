using MochiDock.Core;

public class RomajiTests
{
    [Theory]
    [InlineData("きみ", "kimi")]
    [InlineData("とうきょう", "toukyou")]            // long vowels as written
    [InlineData("おかあさん", "okaasan")]
    [InlineData("コーヒー", "koohii")]               // ー repeats the vowel
    [InlineData("きって", "kitte")]                  // っ doubles the consonant
    [InlineData("まっちゃ", "matcha")]               // っ before ch is t
    [InlineData("いっしょ", "issho")]
    [InlineData("かんい", "kan'i")]                  // ん before a vowel
    [InlineData("ほんや", "hon'ya")]                 // ん before y
    [InlineData("しんぶん", "shinbun")]
    [InlineData("ちづ", "chizu")]
    [InlineData("ふじさん", "fujisan")]
    [InlineData("じゃあね", "jaane")]
    [InlineData("を", "o")]
    [InlineData("ティーシャツ", "tiishatsu")]
    [InlineData("ヴァイオリン", "vaiorin")]
    [InlineData("ﾊﾛｰ", "haroo")]                     // half-width katakana
    [InlineData("ガッ", "ga")]                       // a final っ has nothing to double
    [InlineData("いすゞ", "isuzu")]                  // iteration mark, voiced
    [InlineData("I love あなた", "I love anata")]    // non-kana passes through
    public void KanaFollowsModifiedHepburn(string kana, string romaji) => Assert.Equal(romaji, KanaRomanizer.ToRomaji(kana));

    [Fact]
    public void KatakanaBecomesHiragana() => Assert.Equal("ゔぁいおりん ー", KanaRomanizer.ToHiragana("ヴァイオリン ー"));

    [Fact]
    public void PhrasesSeparateParticlesAndReadWa()
    {
        var words = new[]
        {
            new JapaneseWord("君", "きみ", true), new JapaneseWord("は", "は", false),
            new JapaneseWord("今日", "きょう", true), new JapaneseWord("も", "も", false),
            new JapaneseWord("愛し", "あいし", true), new JapaneseWord("て", "て", false), new JapaneseWord("る", "る", false),
            new JapaneseWord("、", "、", false), new JapaneseWord("baby", "baby", true),
        };
        var segments = LyricReading.Japanese(words);
        Assert.Equal(new[] { "君は", "今日も", "愛してる、", "baby" }, segments.Select(s => s.Text));
        Assert.Equal(new[] { "kimi wa", "kyou mo", "aishiteru", "" }, segments.Select(s => s.Romaji));
        Assert.Equal("きみは", segments[0].Kana);
        Assert.Equal("kimi wa kyou mo aishiteru, baby", LyricReading.Line(segments));
    }

    [Fact]
    public void UnreadKanjiNeverLeaksIntoRomaji()
    {
        var segments = LyricReading.Japanese(new[] { new JapaneseWord("龘", "龘", true), new JapaneseWord("が", "が", false) });
        Assert.Equal("ga", segments.Single().Romaji);
    }

    [Theory]
    [InlineData("사랑해", "saranghae")]
    [InlineData("안녕하세요", "annyeonghaseyo")]
    [InlineData("한국어", "hangugeo")]        // linking
    [InlineData("읽어", "ilgeo")]             // cluster: second consonant moves
    [InlineData("없어", "eopseo")]
    [InlineData("많아", "mana")]              // ㅎ is silent before a vowel
    [InlineData("좋다", "jota")]              // aspiration
    [InlineData("축하", "chuka")]
    [InlineData("국물", "gungmul")]           // nasalisation
    [InlineData("종로", "jongno")]
    [InlineData("신라", "silla")]             // ㄴ + ㄹ
    [InlineData("설날", "seollal")]
    [InlineData("백마", "baengma")]
    [InlineData("같이", "gachi")]             // palatalisation
    [InlineData("굳이", "guji")]
    [InlineData("닭", "dak")]
    [InlineData("밖에", "bakke")]
    [InlineData("영어", "yeongeo")]
    [InlineData("라면", "ramyeon")]
    [InlineData("ㅋㅋ 좋아!", "kk joa!")]
    public void HangulFollowsRevisedRomanization(string hangul, string roman) => Assert.Equal(roman, HangulRomanizer.Romanize(hangul));

    [Fact]
    public void KoreanSegmentsAreWords()
    {
        var segments = LyricReading.Korean("너를 사랑해, baby");
        Assert.Equal(new[] { "neoreul", "saranghae", "" }, segments.Select(s => s.Romaji));
        Assert.Equal("neoreul saranghae, baby", LyricReading.Line(segments));
    }

    [Fact]
    public void ChineseSegmentsAreCharacters()
    {
        var table = new Dictionary<char, string> { ['我'] = "wǒ", ['爱'] = "ài", ['你'] = "nǐ" };
        var segments = LyricReading.Chinese("我爱你，baby", c => table.TryGetValue(c, out var p) ? p : null);
        Assert.Equal(new[] { "我", "爱", "你", "，", "baby" }, segments.Select(s => s.Text));
        Assert.Equal("wǒ ài nǐ, baby", LyricReading.Line(segments));
    }
}
