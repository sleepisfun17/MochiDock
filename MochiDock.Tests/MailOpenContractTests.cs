public class MailOpenContractTests
{
    static string FindApp()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "MochiDock", "Views", "Cards");
            if (Directory.Exists(candidate)) return Path.Combine(dir.FullName, "MochiDock");
        }
        throw new DirectoryNotFoundException("MochiDock/Views/Cards was not found above " + AppContext.BaseDirectory);
    }

    [Fact]
    public void NothingStartsMailto()
    {
        // mailto: means "write a new message" to every mail client (Mailspring opens an empty compose window).
        // Opening mail goes through CompositeMailService.OpenEmail, which opens the mail where it came from.
        var app = FindApp();
        var sep = Path.DirectorySeparatorChar;
        var offenders = Directory.GetFiles(app, "*.cs", SearchOption.AllDirectories)
            .Where(f => !f.Contains($"{sep}obj{sep}") && !f.Contains($"{sep}bin{sep}"))
            .Where(f => File.ReadAllText(f).Contains("\"mailto:", StringComparison.OrdinalIgnoreCase))
            .Select(Path.GetFileName)
            .ToList();
        Assert.True(offenders.Count == 0, "mailto: started in: " + string.Join(", ", offenders));
    }

    [Fact]
    public void SummaryOpenMailUsesTheMailSource()
    {
        var code = File.ReadAllText(Path.Combine(FindApp(), "Views", "Cards", "SummaryCardView.xaml.cs"));
        Assert.Contains("OpenSummaryEmail()", code);
    }
}
