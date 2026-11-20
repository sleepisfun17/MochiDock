using MochiDock.Core;

public class IcsParserTests
{
    // Display zone for tests: UTC+9, no DST, independent of the machine's zone.
    static readonly TimeZoneInfo Tokyo = TimeZoneInfo.CreateCustomTimeZone("test-jst", TimeSpan.FromHours(9), "JST", "JST");
    static readonly DateTime From = new(2026, 11, 1);
    static readonly DateTime To = new(2026, 12, 31);

    static string Feed(params string[] events) =>
        "BEGIN:VCALENDAR\r\nVERSION:2.0\r\nPRODID:-//Google Inc//Google Calendar 70.9054//EN\r\nX-WR-TIMEZONE:Asia/Tokyo\r\n" +
        "BEGIN:VTIMEZONE\r\nTZID:Asia/Tokyo\r\nBEGIN:STANDARD\r\nDTSTART:19700101T000000\r\nTZOFFSETFROM:+0900\r\nTZOFFSETTO:+0900\r\nEND:STANDARD\r\nEND:VTIMEZONE\r\n" +
        string.Concat(events) + "END:VCALENDAR\r\n";

    static string Event(string body) => "BEGIN:VEVENT\r\n" + body.Replace("\n", "\r\n") + "\r\nEND:VEVENT\r\n";

    static IReadOnlyList<AgendaEvent> Read(string feed, DateTime? from = null, DateTime? to = null) => IcsParser.Read(feed, from ?? From, to ?? To, Tokyo);

    [Fact]
    public void ReadsUtcAndTzidAndFloatingTimes()
    {
        var events = Read(Feed(
            Event("UID:a\nSUMMARY:Utc call\nDTSTART:20261111T010000Z\nDTEND:20261111T020000Z"),
            Event("UID:b\nSUMMARY:Local lunch\nDTSTART;TZID=Asia/Tokyo:20261111T120000\nDTEND;TZID=Asia/Tokyo:20261111T130000"),
            Event("UID:c\nSUMMARY:Floating\nDTSTART:20261111T150000\nDTEND:20261111T153000")));
        Assert.Equal(new DateTime(2026, 11, 11, 10, 0, 0), events.Single(e => e.Uid == "a").Start);   // 01:00Z = 10:00 JST
        Assert.Equal(new DateTime(2026, 11, 11, 12, 0, 0), events.Single(e => e.Uid == "b").Start);
        Assert.Equal(new DateTime(2026, 11, 11, 15, 0, 0), events.Single(e => e.Uid == "c").Start);
        Assert.Equal(new DateTime(2026, 11, 11, 11, 0, 0), events.Single(e => e.Uid == "a").End);
    }

    [Fact]
    public void TzidInAnotherZoneIsConvertedToTheDisplayZone()
    {
        var events = Read(Feed(Event("UID:ny\nSUMMARY:NY standup\nDTSTART;TZID=America/New_York:20261111T090000\nDTEND;TZID=America/New_York:20261111T093000")));
        Assert.Single(events);
        Assert.Equal(new DateTime(2026, 11, 11, 23, 0, 0), events[0].Start);   // 09:00 EST (UTC-5) = 14:00Z = 23:00 JST
    }

    [Fact]
    public void AllDayEventsUseExclusiveEndAndSpanDays()
    {
        var events = Read(Feed(Event("UID:trip\nSUMMARY:Trip\nDTSTART;VALUE=DATE:20261110\nDTEND;VALUE=DATE:20261113")));
        var e = Assert.Single(events);
        Assert.True(e.AllDay);
        Assert.Equal(new DateTime(2026, 11, 10), e.Start);
        Assert.Equal(new DateTime(2026, 11, 13), e.End);
        Assert.Single(AgendaLogic.ForDay(events, new DateTime(2026, 11, 12)));
        Assert.Empty(AgendaLogic.ForDay(events, new DateTime(2026, 11, 13)));   // end date is exclusive
        var dots = AgendaLogic.DaysWithEvents(events, From, To);
        Assert.Equal(3, dots.Count);
    }

    [Fact]
    public void SingleDayAllDayWithoutEndLasts24Hours()
    {
        var e = Assert.Single(Read(Feed(Event("UID:d\nSUMMARY:Holiday\nDTSTART;VALUE=DATE:20261123"))));
        Assert.Equal(new DateTime(2026, 11, 24), e.End);
    }

    [Fact]
    public void UnfoldsLongLinesAndUnescapesText()
    {
        var feed = Feed("BEGIN:VEVENT\r\nUID:f\r\nSUMMARY:Planning\\, part 1\\; very long\r\n  title continues\r\nDTSTART:20261111T000000Z\r\nDTEND:20261111T010000Z\r\nLOCATION:Room 4\\nFloor 2\r\nEND:VEVENT\r\n");
        var e = Assert.Single(Read(feed));
        Assert.Equal("Planning, part 1; very long title continues", e.Title);
        Assert.Equal("Room 4\nFloor 2", e.Place);
    }

    [Fact]
    public void IgnoresAlarmsAndTimezoneBlocks()
    {
        var feed = Feed(Event("UID:x\nSUMMARY:With alarm\nDTSTART:20261111T000000Z\nDTEND:20261111T010000Z\nBEGIN:VALARM\nACTION:DISPLAY\nDESCRIPTION:Reminder text\nTRIGGER:-PT10M\nEND:VALARM"));
        var e = Assert.Single(Read(feed));
        Assert.Equal("With alarm", e.Title);
    }

    [Fact]
    public void DurationIsUsedWhenThereIsNoEnd()
    {
        var e = Assert.Single(Read(Feed(Event("UID:g\nSUMMARY:Dur\nDTSTART:20261111T000000Z\nDURATION:PT1H30M"))));
        Assert.Equal(TimeSpan.FromMinutes(90), e.End - e.Start);
    }

    [Fact]
    public void WeeklyRecurrenceWithByDayAndInterval()
    {
        // Tue/Thu every 2 weeks from Tue 3 Nov.
        var events = Read(Feed(Event("UID:w\nSUMMARY:Gym\nDTSTART;TZID=Asia/Tokyo:20261103T180000\nDTEND;TZID=Asia/Tokyo:20261103T190000\nRRULE:FREQ=WEEKLY;INTERVAL=2;BYDAY=TU,TH")), new DateTime(2026, 11, 1), new DateTime(2026, 12, 1));
        Assert.Equal(new[] { 3, 5, 17, 19 }, events.Select(e => e.Start.Day).ToArray());
    }

    [Fact]
    public void CountAndUntilStopTheSeries()
    {
        var counted = Read(Feed(Event("UID:c\nSUMMARY:Daily\nDTSTART:20261105T000000Z\nDTEND:20261105T010000Z\nRRULE:FREQ=DAILY;COUNT=3")));
        Assert.Equal(3, counted.Count);
        var until = Read(Feed(Event("UID:u\nSUMMARY:Daily\nDTSTART:20261105T000000Z\nDTEND:20261105T010000Z\nRRULE:FREQ=DAILY;UNTIL=20261107T235959Z")));
        Assert.Equal(new[] { 5, 6, 7 }, until.Select(e => e.Start.Day).ToArray());
    }

    [Fact]
    public void MonthlyNthWeekdayAndLastWeekday()
    {
        // 2nd Tuesday and last Friday of each month.
        var second = Read(Feed(Event("UID:m\nSUMMARY:Board\nDTSTART:20261110T000000Z\nDTEND:20261110T010000Z\nRRULE:FREQ=MONTHLY;BYDAY=2TU")), From, new DateTime(2027, 1, 31));
        Assert.Equal(new[] { new DateTime(2026, 11, 10, 9, 0, 0), new DateTime(2026, 12, 8, 9, 0, 0), new DateTime(2027, 1, 12, 9, 0, 0) }, second.Select(e => e.Start).ToArray());
        var last = Read(Feed(Event("UID:l\nSUMMARY:Review\nDTSTART:20261127T000000Z\nDTEND:20261127T010000Z\nRRULE:FREQ=MONTHLY;BYDAY=-1FR")), From, new DateTime(2027, 1, 1));
        Assert.Equal(new[] { 27, 25 }, last.Select(e => e.Start.Day).ToArray());
    }

    [Fact]
    public void MonthlyByMonthDaySkipsMonthsWithoutThatDay()
    {
        var events = Read(Feed(Event("UID:d\nSUMMARY:Rent\nDTSTART;VALUE=DATE:20260131\nRRULE:FREQ=MONTHLY")), new DateTime(2026, 1, 1), new DateTime(2026, 6, 1));
        Assert.Equal(new[] { 1, 3, 5 }, events.Select(e => e.Start.Month).ToArray());   // Jan 31, Mar 31, May 31 (no 31st in Feb/Apr)
    }

    [Fact]
    public void YearlyBirthday()
    {
        var events = Read(Feed(Event("UID:b\nSUMMARY:Birthday\nDTSTART;VALUE=DATE:20200214\nRRULE:FREQ=YEARLY")), new DateTime(2026, 1, 1), new DateTime(2028, 12, 31));
        Assert.Equal(new[] { new DateTime(2026, 2, 14), new DateTime(2027, 2, 14), new DateTime(2028, 2, 14) }, events.Select(e => e.Start).ToArray());
    }

    [Fact]
    public void ExdateRemovesOneOccurrence()
    {
        var events = Read(Feed(Event("UID:e\nSUMMARY:Standup\nDTSTART;TZID=Asia/Tokyo:20261102T100000\nDTEND;TZID=Asia/Tokyo:20261102T101500\nRRULE:FREQ=DAILY;COUNT=4\nEXDATE;TZID=Asia/Tokyo:20261103T100000")));
        Assert.Equal(new[] { 2, 4, 5 }, events.Select(e => e.Start.Day).ToArray());
    }

    [Fact]
    public void RecurrenceIdOverrideReplacesOneOccurrenceAndCancellationRemovesIt()
    {
        var feed = Feed(
            Event("UID:s\nSUMMARY:Sync\nDTSTART;TZID=Asia/Tokyo:20261102T100000\nDTEND;TZID=Asia/Tokyo:20261102T110000\nRRULE:FREQ=DAILY;COUNT=4"),
            Event("UID:s\nSUMMARY:Sync (moved)\nRECURRENCE-ID;TZID=Asia/Tokyo:20261103T100000\nDTSTART;TZID=Asia/Tokyo:20261103T150000\nDTEND;TZID=Asia/Tokyo:20261103T160000"),
            Event("UID:s\nSTATUS:CANCELLED\nSUMMARY:Sync\nRECURRENCE-ID;TZID=Asia/Tokyo:20261104T100000\nDTSTART;TZID=Asia/Tokyo:20261104T100000\nDTEND;TZID=Asia/Tokyo:20261104T110000"));
        var events = Read(feed);
        Assert.Equal(3, events.Count);
        Assert.Contains(events, e => e.Title == "Sync (moved)" && e.Start == new DateTime(2026, 11, 3, 15, 0, 0));
        Assert.DoesNotContain(events, e => e.Start == new DateTime(2026, 11, 3, 10, 0, 0));
        Assert.DoesNotContain(events, e => e.Start.Day == 4);
    }

    [Fact]
    public void CancelledEventsAreDropped() =>
        Assert.Empty(Read(Feed(Event("UID:x\nSTATUS:CANCELLED\nSUMMARY:Gone\nDTSTART:20261111T000000Z\nDTEND:20261111T010000Z"))));

    [Fact]
    public void OnlyEventsOverlappingTheWindowAreReturned()
    {
        var feed = Feed(
            Event("UID:old\nSUMMARY:Old\nDTSTART:20250101T000000Z\nDTEND:20250101T010000Z"),
            Event("UID:in\nSUMMARY:In\nDTSTART:20261111T000000Z\nDTEND:20261111T010000Z"),
            Event("UID:far\nSUMMARY:Far\nDTSTART:20300101T000000Z\nDTEND:20300101T010000Z"));
        Assert.Equal("In", Assert.Single(Read(feed)).Title);
    }

    [Fact]
    public void UnsupportedRulesDegradeToTheFirstOccurrenceWithAWarning()
    {
        var calendar = IcsParser.Parse(Feed(Event("UID:q\nSUMMARY:Odd\nDTSTART:20261111T000000Z\nDTEND:20261111T010000Z\nRRULE:FREQ=SECONDLY;COUNT=5")));
        var events = IcsParser.Expand(calendar, From, To, Tokyo);
        Assert.Single(events);
        Assert.NotEmpty(calendar.Warnings);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a calendar")]
    [InlineData("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nDTSTART:garbage\r\nEND:VEVENT\r\nEND:VCALENDAR")]
    [InlineData("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nSUMMARY:No start\r\nEND:VEVENT\r\nEND:VCALENDAR")]
    public void GarbageNeverThrows(string text) => Assert.Empty(Read(text));

    [Fact]
    public void LooksLikeCalendarDetectsFeeds()
    {
        Assert.True(IcsParser.LooksLikeCalendar("\uFEFFBEGIN:VCALENDAR\r\n"));
        Assert.False(IcsParser.LooksLikeCalendar("<html>"));
        Assert.False(IcsParser.LooksLikeCalendar(null));
    }

    [Fact]
    public void FindsMeetLinkFromConferencePropertyOrDescription()
    {
        var events = Read(Feed(
            Event("UID:m1\nSUMMARY:Meet\nDTSTART:20261111T000000Z\nDTEND:20261111T010000Z\nX-GOOGLE-CONFERENCE:https://meet.google.com/abc-defg-hij"),
            Event("UID:m2\nSUMMARY:Zoom\nDTSTART:20261112T000000Z\nDTEND:20261112T010000Z\nDESCRIPTION:Join here: https://acme.zoom.us/j/12345?pwd=x.\\nThanks"),
            Event("UID:m3\nSUMMARY:Phish\nDTSTART:20261113T000000Z\nDTEND:20261113T010000Z\nDESCRIPTION:http://meet.google.com/evil https://evil.example/zoom.us")));
        Assert.Equal("https://meet.google.com/abc-defg-hij", events.Single(e => e.Uid == "m1").JoinUrl);
        Assert.Equal("https://acme.zoom.us/j/12345?pwd=x", events.Single(e => e.Uid == "m2").JoinUrl);
        Assert.Null(events.Single(e => e.Uid == "m3").JoinUrl);
    }
}

public class AgendaLogicTests
{
    static AgendaEvent E(string title, DateTime start, DateTime end, bool allDay = false) => new(title, title, start, end, allDay, "", null);
    static readonly DateTime Day = new(2026, 11, 11);

    [Theory]
    [InlineData("https://meet.google.com/abc-defg-hij", true)]
    [InlineData("https://us02web.zoom.us/j/123", true)]
    [InlineData("https://teams.microsoft.com/l/meetup-join/x", true)]
    [InlineData("http://meet.google.com/abc", false)]                       // not https
    [InlineData("https://meet.google.com:8443/abc", false)]                 // custom port
    [InlineData("https://user:pw@meet.google.com/abc", false)]              // user info
    [InlineData("https://notmeet.google.com.evil.example/abc", false)]      // look-alike host
    [InlineData("https://evilzoom.us/j/1", false)]                          // suffix without a dot boundary
    [InlineData("javascript:alert(1)", false)]
    [InlineData("file:///C:/Windows/System32/calc.exe", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void JoinUrlAllowList(string? url, bool allowed) => Assert.Equal(allowed, AgendaLogic.IsJoinUrlAllowed(url));

    [Fact]
    public void ForDayOrdersAllDayFirstThenByStartAndIncludesMidnightCrossers()
    {
        var events = new[]
        {
            E("Late", Day.AddHours(15), Day.AddHours(16)),
            E("Early", Day.AddHours(9), Day.AddHours(10)),
            E("Overnight", Day.AddHours(-2), Day.AddHours(2)),
            E("Holiday", Day, Day.AddDays(1), allDay: true),
            E("Tomorrow", Day.AddDays(1).AddHours(9), Day.AddDays(1).AddHours(10)),
        };
        Assert.Equal(new[] { "Holiday", "Overnight", "Early", "Late" }, AgendaLogic.ForDay(events, Day).Select(e => e.Title).ToArray());
    }

    [Fact]
    public void FocusPicksCurrentThenNextWithinAnHour()
    {
        var events = new[] { E("Now", Day.AddHours(10), Day.AddHours(11)), E("Next", Day.AddHours(11).AddMinutes(25), Day.AddHours(12)), E("Holiday", Day, Day.AddDays(1), true) };
        var now = AgendaLogic.Focus(events, Day.AddHours(10).AddMinutes(12));
        Assert.Equal((AgendaFocusKind.Now, "Now", "48 min left"), (now!.Kind, now.Event.Title, now.Text));
        var next = AgendaLogic.Focus(events, Day.AddHours(11).AddMinutes(1));
        Assert.Equal((AgendaFocusKind.Next, "Next", "in 24 min"), (next!.Kind, next.Event.Title, next.Text));
        Assert.Null(AgendaLogic.Focus(events, Day.AddHours(8)));                  // next event more than an hour away
        Assert.Null(AgendaLogic.Focus(new[] { events[2] }, Day.AddHours(9)));     // all-day events are never the focus
    }

    [Fact]
    public void FocusPrefersTheLatestStartedOfOverlappingEvents()
    {
        var events = new[] { E("Long", Day.AddHours(9), Day.AddHours(12)), E("Short", Day.AddHours(10), Day.AddHours(10).AddMinutes(30)) };
        Assert.Equal("Short", AgendaLogic.Focus(events, Day.AddHours(10).AddMinutes(10))!.Event.Title);
    }

    [Fact]
    public void TimeTextFormats()
    {
        Assert.Equal("All day", AgendaLogic.TimeText(E("a", Day, Day.AddDays(1), true)));
        Assert.Equal("10:00–11:30", AgendaLogic.TimeText(E("b", Day.AddHours(10), Day.AddHours(11).AddMinutes(30))));
        Assert.Equal("09:05", AgendaLogic.TimeText(E("c", Day.AddHours(9).AddMinutes(5), Day.AddHours(9).AddMinutes(5))));
    }

    [Theory]
    [InlineData("https://calendar.google.com/calendar/ical/x/private-abc/basic.ics", "https://calendar.google.com/calendar/ical/x/private-abc/basic.ics")]
    [InlineData("webcal://calendar.google.com/calendar/ical/x/basic.ics", "https://calendar.google.com/calendar/ical/x/basic.ics")]
    [InlineData("  https://example.com/a.ics  ", "https://example.com/a.ics")]
    [InlineData("http://example.com/a.ics", null)]
    [InlineData("https://user:pw@example.com/a.ics", null)]
    [InlineData("ftp://example.com/a.ics", null)]
    [InlineData("not a url", null)]
    [InlineData("", null)]
    public void FeedUrlNormalization(string input, string? expected) => Assert.Equal(expected, AgendaLogic.NormalizeFeedUrl(input)?.ToString());
}
