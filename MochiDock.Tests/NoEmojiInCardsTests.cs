using System.Text.RegularExpressions;

public class NoEmojiInCardsTests
{
    static string FindCards()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "MochiDock", "Views", "Cards");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("MochiDock/Views/Cards was not found above " + AppContext.BaseDirectory);
    }

    [Fact]
    public void CardXamlHasNoEmojiText()
    {
        // WPF text cannot draw colour emoji, so an emoji in a card shows black and white. Use the flat icons in
        // Views/ColorIconResources.xaml (Image Source="{StaticResource IconBell}") instead. A Tag="..." value is data (the emoji sent as a
        // WhatsApp reaction), not drawn text, and the check mark and cross glyphs are plain symbols that follow the theme colour.
        var emoji = new Regex("[\u2600-\u26FF\u2700-\u27BF\u2B00-\u2BFF]|[\uD83C-\uD83E][\uDC00-\uDFFF]", RegexOptions.Compiled);
        var allowed = new[] { '\u2713', '\u2715', '\u2714', '\u2716' };   // check mark and cross glyphs
        var offenders = Directory.GetFiles(FindCards(), "*.xaml")
            .SelectMany(file => File.ReadLines(file).Select((line, i) => (file, line: Regex.Replace(line, "Tag=\"[^\"]*\"", ""), n: i + 1)))
            .Where(x => emoji.Matches(x.line).Any(m => !(m.Length == 1 && allowed.Contains(m.Value[0]))))
            .Select(x => $"{Path.GetFileName(x.file)}:{x.n}")
            .ToList();
        Assert.True(offenders.Count == 0, "Emoji text in card XAML (shows black and white): " + string.Join(", ", offenders));
    }

    [Fact]
    public void EveryIconUsedByTheCardsExists()
    {
        var views = Path.GetFullPath(Path.Combine(FindCards(), ".."));
        var defined = Regex.Matches(File.ReadAllText(Path.Combine(views, "ColorIconResources.xaml")), @"x:Key=""(Icon\w+)""").Select(m => m.Groups[1].Value).ToHashSet();
        var used = Directory.GetFiles(FindCards(), "*.xaml").SelectMany(f => Regex.Matches(File.ReadAllText(f), @"StaticResource (Icon\w+)\}").Select(m => m.Groups[1].Value)).ToHashSet();
        Assert.NotEmpty(used);
        Assert.Empty(used.Except(defined));
    }
}
