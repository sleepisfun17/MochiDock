using System.Text.RegularExpressions;

public class SettingsXamlTokenTests
{
    static string FindSettingsViews()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "MochiDock", "Views", "Settings");
            if (Directory.Exists(candidate)) return candidate;
        }
        throw new DirectoryNotFoundException("MochiDock/Views/Settings was not found above " + AppContext.BaseDirectory);
    }

    [Fact]
    public void SettingsWindowXamlUsesThemeTokensNotHexColours()
    {
        // The Advanced Settings window is the design anchor: every colour comes from a theme token so both palettes recolour it.
        var hex = new Regex("\"#[0-9A-Fa-f]{6}", RegexOptions.Compiled);
        var offenders = Directory.GetFiles(FindSettingsViews(), "*.xaml", SearchOption.AllDirectories)
            .SelectMany(file => File.ReadLines(file).Select((line, i) => (file, line, n: i + 1)))
            .Where(x => hex.IsMatch(x.line))
            .Select(x => $"{Path.GetFileName(x.file)}:{x.n}")
            .ToList();
        Assert.True(offenders.Count == 0, "Hex colours in Settings XAML: " + string.Join(", ", offenders));
    }

    [Fact]
    public void NoSettingAppearsInTwoSections()
    {
        // One option, one place: a label repeated across section pages means the same setting is offered twice.
        var label = new Regex(@"<s:SettingRow[^>]*?\sLabel=""([^""]+)""", RegexOptions.Compiled | RegexOptions.Singleline);
        var duplicates = Directory.GetFiles(Path.Combine(FindSettingsViews(), "Sections"), "*.xaml")
            .SelectMany(file => label.Matches(File.ReadAllText(file)).Select(m => (Label: m.Groups[1].Value, File: Path.GetFileName(file))))
            .GroupBy(x => x.Label, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(x => x.File).Distinct().Count() > 1)
            .Select(g => $"\"{g.Key}\" in {string.Join(", ", g.Select(x => x.File).Distinct())}")
            .ToList();
        Assert.True(duplicates.Count == 0, "Duplicated setting labels: " + string.Join("; ", duplicates));
    }

    [Fact]
    public void TabToggleListCoversEveryHeaderTabExactlyOnce()
    {
        string views = FindSettingsViews();
        string app = Path.GetFullPath(Path.Combine(views, "..", ".."));
        var listed = Regex.Matches(File.ReadAllText(Path.Combine(views, "TabToggleList.xaml")), @"IsChecked=""\{Binding (\w+),").Select(m => m.Groups[1].Value).ToList();
        Assert.Equal(listed.Count, listed.Distinct().Count());

        // Every Show...Tab property of the view model, plus the prayer switch, is offered.
        var declared = Regex.Matches(File.ReadAllText(Path.Combine(app, "ViewModels", "MainViewModel.cs")), @"public bool (Show\w+Tab)")
            .Select(m => m.Groups[1].Value).Append("EnablePrayerTimes").Distinct().OrderBy(x => x).ToList();
        Assert.True(declared.SequenceEqual(listed.OrderBy(x => x)), $"declared [{string.Join(", ", declared)}] vs listed [{string.Join(", ", listed.OrderBy(x => x))}]");

        // Every listed switch really drives a header tab: the header's visibility binds to it.
        string header = File.ReadAllText(Path.Combine(app, "MainWindow.xaml"));
        Assert.All(listed, name => Assert.Contains("Binding " + name + ",", header));
    }
}
