using MochiDock.Core;

public class SettingsSectionsTests
{
    [Fact]
    public void IdsAndTitlesAreUniqueAndNonEmpty()
    {
        var all = SettingsSections.All;
        Assert.Equal(all.Count, all.Select(s => s.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(all.Count, all.Select(s => s.Title).Distinct().Count());
        Assert.All(all, s => { Assert.False(string.IsNullOrWhiteSpace(s.Title)); Assert.False(string.IsNullOrWhiteSpace(s.Description)); });
    }

    [Fact]
    public void SidebarOrderStartsWithGeneralAndEndsWithData()
    {
        Assert.Equal(SettingsSections.General, SettingsSections.All[0].Id);
        Assert.Equal(SettingsSections.Data, SettingsSections.All[^1].Id);
    }

    [Theory]
    [InlineData("whatsapp", "whatsapp")]
    [InlineData(" Data ", "data")]
    [InlineData("prayer-place", "location")]
    [InlineData("calendar-connect", "calendar")]
    [InlineData("logs", "data")]
    [InlineData("unknown", "general")]
    [InlineData("", "general")]
    [InlineData(null, "general")]
    public void ResolveMapsIdsAliasesAndFallsBack(string? target, string expected) =>
        Assert.Equal(expected, SettingsSections.Resolve(target));

    [Fact]
    public void EveryAliasResolvesToAKnownSection()
    {
        foreach (var alias in new[] { "prayer-place", "weather-city", "calendar-connect", "gowa", "outlook", "logs", "import-export" })
            Assert.NotNull(SettingsSections.Find(SettingsSections.Resolve(alias)));
    }

    [Fact]
    public void EverySectionHasAGroupAndGroupsAreContiguousInSidebarOrder()
    {
        var all = SettingsSections.All;
        Assert.All(all, s => Assert.False(string.IsNullOrWhiteSpace(s.Group)));
        // A group heading must not appear twice, so the sections of one group sit next to each other.
        var runs = all.Select(s => s.Group).Aggregate(new List<string>(), (list, g) => { if (list.Count == 0 || list[^1] != g) list.Add(g); return list; });
        Assert.Equal(runs.Count, runs.Distinct().Count());
        Assert.Equal(new[] { "Dock", "Integrations", "Features", "Data" }, runs);
    }
}
