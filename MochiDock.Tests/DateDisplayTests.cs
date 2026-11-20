using System.Globalization;
using MochiDock.Core;

public class DateDisplayTests
{
    static readonly DateTime Day = new(2026, 11, 19);   // a Thursday
    static CultureInfo C(string name) => CultureInfo.GetCultureInfo(name);

    [Theory]
    [InlineData(DateStyle.YearFirst, "2026/11/19 木", "2026年11月", "11/19")]
    [InlineData(DateStyle.DayFirst, "Thu 19 Nov 2026", "November 2026", "19 Nov")]
    [InlineData(DateStyle.MonthFirst, "Thu, Nov 19, 2026", "November 2026", "Nov 19")]
    [InlineData(DateStyle.Iso, "2026-11-19 Thu", "2026-11", "11-19")]
    public void PresetsLookTheSameOnAnyWindowsLanguage(DateStyle style, string full, string month, string shortDay)
    {
        foreach (var culture in new[] { C("en-US"), C("ja-JP"), C("id-ID"), C("de-DE") })
        {
            Assert.Equal(full, DateDisplay.Full(Day, style, culture));
            Assert.Equal(month, DateDisplay.Month(Day, style, culture));
            Assert.Equal(shortDay, DateDisplay.Short(Day, style, culture));
        }
    }

    [Fact]
    public void AutomaticOnJapaneseWindowsKeepsTheEarlierLook()
    {
        Assert.Equal("2026/11/19 木", DateDisplay.Full(Day, DateStyle.Automatic, C("ja-JP")));
        Assert.Equal("2026年11月", DateDisplay.Month(Day, DateStyle.Automatic, C("ja-JP")));
        Assert.Equal("11/19", DateDisplay.Short(Day, DateStyle.Automatic, C("ja-JP")));
    }

    [Fact]
    public void AutomaticFollowsTheRegionOrder()
    {
        Assert.Equal("Thu 11/19/2026", DateDisplay.Full(Day, DateStyle.Automatic, C("en-US")));
        Assert.Equal("11/19", DateDisplay.Short(Day, DateStyle.Automatic, C("en-US")));
        Assert.Equal("Thu 19/11/2026", DateDisplay.Full(Day, DateStyle.Automatic, C("en-GB")));
        Assert.Equal("19/11", DateDisplay.Short(Day, DateStyle.Automatic, C("en-GB")));
        Assert.Equal("19.11", DateDisplay.Short(Day, DateStyle.Automatic, C("de-DE")));
        Assert.Equal("November 2026", DateDisplay.Month(Day, DateStyle.Automatic, C("en-US")));
    }

    [Theory]
    [InlineData(null, DateStyle.Automatic)]
    [InlineData("", DateStyle.Automatic)]
    [InlineData("nonsense", DateStyle.Automatic)]
    [InlineData("YearFirst", DateStyle.YearFirst)]
    [InlineData(" iso ", DateStyle.Iso)]
    [InlineData("3", DateStyle.Automatic)]
    public void ParsesSavedValues(string? saved, DateStyle expected) => Assert.Equal(expected, DateDisplay.Parse(saved));

    [Fact]
    public void LabelsShowTodayAsTheExample() =>
        Assert.Equal("Day first · Thu 19 Nov 2026", DateDisplay.Label(DateStyle.DayFirst, Day, C("en-US")));
}
