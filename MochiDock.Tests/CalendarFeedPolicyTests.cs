using MochiDock.Core;

public class CalendarFeedPolicyTests
{
    [Fact]
    public void ColorKeysNormalizeAndMapToThemeBrushes()
    {
        Assert.Equal("Green", CalendarFeedPolicy.NormalizeColor("  green "));
        Assert.Equal("Blue", CalendarFeedPolicy.NormalizeColor("unknown"));
        Assert.Equal("Blue", CalendarFeedPolicy.NormalizeColor(null));

        Assert.Equal("IslandBlueBrush", CalendarFeedPolicy.BrushKey("Blue"));
        Assert.Equal("IslandGreenBrush", CalendarFeedPolicy.BrushKey("green"));
        Assert.Equal("IslandOrangeBrush", CalendarFeedPolicy.BrushKey("Orange"));
        Assert.Equal("IslandPurpleBrush", CalendarFeedPolicy.BrushKey("Purple"));
        Assert.Equal("IslandRedBrush", CalendarFeedPolicy.BrushKey("Red"));
        Assert.Equal("IslandAccentBrush", CalendarFeedPolicy.BrushKey("Accent"));
    }

    [Fact]
    public void PickNextColorSkipsUsedAndWrapsWhenAllTaken()
    {
        Assert.Equal("Blue", CalendarFeedPolicy.PickNextColor(Array.Empty<string?>()));
        Assert.Equal("Green", CalendarFeedPolicy.PickNextColor(new[] { "Blue" }));
        Assert.Equal("Orange", CalendarFeedPolicy.PickNextColor(new[] { "blue", "Green" }));
        Assert.Equal("Blue", CalendarFeedPolicy.PickNextColor(CalendarFeedPolicy.ColorKeys));
        Assert.Equal("Green", CalendarFeedPolicy.PickNextColor(CalendarFeedPolicy.ColorKeys.Append("Blue")));
    }

    [Fact]
    public void NormalizeNameTrimsFallsBackAndCapsLength()
    {
        Assert.Equal("Calendar", CalendarFeedPolicy.NormalizeName("   ", 1));
        Assert.Equal("Calendar 2", CalendarFeedPolicy.NormalizeName(null, 2));
        Assert.Equal("Work Team", CalendarFeedPolicy.NormalizeName("  Work   Team "));
        Assert.Equal(new string('A', 24), CalendarFeedPolicy.NormalizeName(new string('A', 40)));
    }

    [Fact]
    public void FormatDetailRespectsCalendarVisibilityFocusAndPlace()
    {
        Assert.Equal("Room 3B", CalendarFeedPolicy.FormatDetail("Work", showCalendarName: false, focusText: null, place: "Room 3B"));
        Assert.Equal("Work · Room 3B", CalendarFeedPolicy.FormatDetail("Work", showCalendarName: true, focusText: null, place: "Room 3B"));
        Assert.Equal("in 10 min · Personal · Cafe", CalendarFeedPolicy.FormatDetail("Personal", showCalendarName: true, focusText: "in 10 min", place: "Cafe"));
        Assert.Equal("", CalendarFeedPolicy.FormatDetail("Work", showCalendarName: false, focusText: null, place: "   "));
    }

    [Fact]
    public void DayEventColorsCollectsDistinctColorsUpToMaxPerDayAcrossSpans()
    {
        var day1 = new DateTime(2026, 11, 10, 9, 0, 0);
        var events = new[]
        {
            new AgendaEvent("1", "Work Trip", day1.Date, day1.Date.AddDays(2), true, "", null, "Work", "Blue"),
            new AgendaEvent("2", "Work 1:1", day1.AddHours(1), day1.AddHours(2), false, "", null, "Work", "Blue"),
            new AgendaEvent("3", "Dentist", day1.AddHours(3), day1.AddHours(4), false, "", null, "Personal", "Green"),
            new AgendaEvent("4", "Gym", day1.AddHours(5), day1.AddHours(6), false, "", null, "Health", "Orange"),
            new AgendaEvent("5", "Extra", day1.AddHours(7), day1.AddHours(8), false, "", null, "Other", "Red"),
        };

        var map = AgendaLogic.DayEventColors(events, day1.Date, day1.Date.AddDays(3), maxDotsPerDay: 3);
        Assert.Equal(new[] { "Blue", "Green", "Orange" }, map[day1.Date]);
        Assert.Equal(new[] { "Blue" }, map[day1.Date.AddDays(1)]);
        Assert.False(map.ContainsKey(day1.Date.AddDays(2)));
    }
}
