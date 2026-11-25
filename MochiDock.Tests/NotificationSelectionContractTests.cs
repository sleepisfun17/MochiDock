using System.Xml.Linq;

public class NotificationSelectionContractTests
{
    static readonly XNamespace W = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    static string FindMochiDockRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "MochiDock");
            if (Directory.Exists(candidate) && File.Exists(Path.Combine(candidate, "App.xaml")))
                return candidate;
        }
        throw new DirectoryNotFoundException("MochiDock directory was not found above " + AppContext.BaseDirectory);
    }

    [Fact]
    public void AppXamlRegistersReferenceEqualsConverter()
    {
        var appXaml = XDocument.Load(Path.Combine(FindMochiDockRoot(), "App.xaml"));
        Assert.Contains(
            appXaml.Descendants(),
            e => e.Name.LocalName == "ReferenceEqualsConverter" && (string?)e.Attribute(X + "Key") == "ReferenceEqualsConverter");
    }

    [Theory]
    [InlineData("WhatsAppCardView.xaml", "DataContext.CurrentWhatsApp")]
    [InlineData("EmailCardView.xaml", "DataContext.CurrentEmail")]
    [InlineData("DiscordCardView.xaml", "DataContext.CurrentDiscord")]
    [InlineData("AppsCardView.xaml", "DataContext.CurrentApp")]
    public void NotificationCardMarksSelectedItemWithVisualsAndAnimation(string fileName, string expectedCurrentPath)
    {
        var path = Path.Combine(FindMochiDockRoot(), "Views", "Cards", fileName);
        var doc = XDocument.Load(path);

        var itemBorder = doc.Descendants(W + "Border")
            .Single(e => (string?)e.Attribute(X + "Name") == "ItemBorder");

        // Local Background/BorderBrush attributes would override DataTemplate.Triggers in WPF.
        Assert.Null(itemBorder.Attribute("Background"));
        Assert.Null(itemBorder.Attribute("BorderBrush"));

        Assert.Single(doc.Descendants(W + "Border"), e => (string?)e.Attribute(X + "Name") == "SelectionBar");
        Assert.Single(doc.Descendants(W + "Border"), e => (string?)e.Attribute(X + "Name") == "SelectedBadge");

        var multiBindings = doc.Descendants(W + "MultiBinding")
            .Where(m => (string?)m.Attribute("Converter") == "{StaticResource ReferenceEqualsConverter}")
            .ToList();
        Assert.Single(multiBindings);

        var currentBinding = multiBindings[0].Elements(W + "Binding")
            .FirstOrDefault(b => (string?)b.Attribute("Path") == expectedCurrentPath);
        Assert.NotNull(currentBinding);
    }

    [Fact]
    public void WhatsAppReplyBoxUsesDynamicSenderHint()
    {
        var path = Path.Combine(FindMochiDockRoot(), "Views", "Cards", "WhatsAppCardView.xaml");
        var text = File.ReadAllText(path);
        Assert.Contains("TextBoxHint.Text=\"{Binding WhatsAppReplyHint}\"", text);
    }
}
