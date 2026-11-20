using MochiDock.Core;

public class SettingsValidationTests
{
    [Theory]
    [InlineData(1, false)]
    [InlineData(1023, false)]
    [InlineData(0, false)]
    [InlineData(1024, true)]
    [InlineData(65535, true)]
    [InlineData(65536, false)]
    [InlineData(-5, false)]
    public void PortRuleIsTheSameForUiAndImport(int port, bool valid) =>
        Assert.Equal(valid, SettingsValidation.IsValidPort(port));

    [Theory]
    [InlineData(" 3099 ", true, 3099)]
    [InlineData("80", false, 0)]
    [InlineData("abc", false, 0)]
    [InlineData("", false, 0)]
    [InlineData(null, false, 0)]
    public void TryParsePort(string? text, bool ok, int expected)
    {
        Assert.Equal(ok, SettingsValidation.TryParsePort(text, out int port));
        if (ok) Assert.Equal(expected, port);
    }

    [Theory]
    [InlineData("  http://100.64.0.1:3099/ ", "http://100.64.0.1:3099")]
    [InlineData("http://host///", "http://host")]
    [InlineData("   ", "")]
    [InlineData(null, "")]
    public void TrimRemoteUrl(string? input, string expected) =>
        Assert.Equal(expected, SettingsValidation.TrimRemoteUrl(input));

    [Theory]
    [InlineData("192.168.1.5:3099", "http://192.168.1.5:3099")]
    [InlineData("https://box.ts.net/", "https://box.ts.net")]
    [InlineData("", "")]
    public void NormalizeRemoteUrlAddsMissingScheme(string input, string expected) =>
        Assert.Equal(expected, SettingsValidation.NormalizeRemoteUrl(input));

    [Theory]
    [InlineData("http://192.168.1.5:3099", false)]
    [InlineData("http://100.64.0.1:3099", false)]
    [InlineData("http://example.com:3099", true)]
    [InlineData("http://8.8.8.8", true)]
    [InlineData("", false)]
    public void PublicAddressWarning(string url, bool warn) =>
        Assert.Equal(warn, SettingsValidation.ShouldWarnPublicAddress(url));

    [Theory]
    [InlineData("22:00", true)]
    [InlineData("07:30", true)]
    [InlineData("00:00", true)]
    [InlineData("24:00", false)]
    [InlineData("25:61", false)]
    [InlineData("", false)]
    [InlineData("noon", false)]
    [InlineData(null, false)]
    [InlineData("2200", true)]
    [InlineData("22", true)]
    [InlineData("1:00", true)]
    [InlineData("2400", false)]
    public void TimeOfDayParsing(string? text, bool ok) =>
        Assert.Equal(ok, SettingsValidation.TryParseTimeOfDay(text, out _));

    [Theory]
    [InlineData("23:00", "22:00", "07:00", true)]   // overnight, before midnight
    [InlineData("03:00", "22:00", "07:00", true)]   // overnight, after midnight
    [InlineData("07:00", "22:00", "07:00", false)]  // end is exclusive
    [InlineData("12:00", "22:00", "07:00", false)]
    [InlineData("13:00", "12:00", "14:00", true)]   // same-day window
    [InlineData("14:00", "12:00", "14:00", false)]
    [InlineData("12:00", "08:00", "08:00", false)]  // start == end: empty window
    [InlineData("23:00", "bad", "07:00", false)]    // invalid input is never quiet
    [InlineData("23:00", "22:00", null, false)]
    public void QuietTime(string now, string? start, string? end, bool quiet) =>
        Assert.Equal(quiet, SettingsValidation.IsQuietTime(TimeSpan.Parse(now), start, end));

    [Fact]
    public void ThresholdAndOffsetClamps()
    {
        Assert.Equal(50, SettingsValidation.ClampCpuGpuThreshold(10));
        Assert.Equal(100, SettingsValidation.ClampCpuGpuThreshold(500));
        Assert.Equal(75, SettingsValidation.ClampCpuGpuThreshold(75));
        Assert.Equal(90, SettingsValidation.ClampCpuGpuThreshold(double.NaN));
        Assert.Equal(60, SettingsValidation.ClampTemperatureThreshold(0));
        Assert.Equal(85, SettingsValidation.ClampTemperatureThreshold(double.PositiveInfinity));
        Assert.Equal(60, SettingsValidation.ClampGamingOffset(-1));
        Assert.Equal(260, SettingsValidation.ClampGamingOffset(1000));
        Assert.Equal(150, SettingsValidation.ClampGamingOffset(double.NaN));
    }

    [Fact]
    public void AddOutlookFoldersMergesTrimsAndDeduplicates()
    {
        Assert.True(SettingsValidation.TryAddOutlookFolders(new[] { "Clients" }, " Others , clients;Work、Ｗｏｒｋ2 ", out var result, out var error));
        Assert.Null(error);
        Assert.Equal(new[] { "Clients", "Others", "Work", "Ｗｏｒｋ2" }, result);
    }

    [Theory]
    [InlineData("", "Type a folder name.")]
    [InlineData("  , ; ", "Type a folder name.")]
    [InlineData("a|b", "A folder name cannot contain \"|\".")]
    [InlineData("clients", "Already watched.")]
    public void AddOutlookFoldersRejectsEmptyPipeAndDuplicates(string input, string message)
    {
        Assert.False(SettingsValidation.TryAddOutlookFolders(new[] { "Clients" }, input, out var result, out var error));
        Assert.Equal(message, error);
        Assert.Equal(new[] { "Clients" }, result);
    }

    [Theory]
    [InlineData("4", true, 4)]
    [InlineData(" 0 ", true, 0)]
    [InlineData("12.5", true, 12.5)]
    [InlineData("100", true, 100)]
    [InlineData("101", false, 0)]
    [InlineData("-1", false, 0)]
    [InlineData("NaN", false, 0)]
    [InlineData("abc", false, 0)]
    [InlineData("", false, 0)]
    public void TopMarginParsing(string text, bool ok, double expected)
    {
        Assert.Equal(ok, SettingsValidation.TryParseTopMargin(text, out double margin));
        if (ok) Assert.Equal(expected, margin);
    }

    [Theory]
    [InlineData("0900", 9, 0)]
    [InlineData("900", 9, 0)]
    [InlineData("930", 9, 30)]
    [InlineData("2145", 21, 45)]
    [InlineData("11", 11, 0)]
    [InlineData("9", 9, 0)]
    [InlineData("0", 0, 0)]
    [InlineData("09:00", 9, 0)]
    [InlineData("9:30", 9, 30)]
    [InlineData("9.30", 9, 30)]
    [InlineData(" 23:59 ", 23, 59)]
    public void TimeInputAcceptsEveryWayOfTypingATime(string text, int hour, int minute)
    {
        Assert.True(TimeInput.TryParse(text, out var time));
        Assert.Equal(new TimeSpan(hour, minute, 0), time);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("24")]
    [InlineData("2400")]
    [InlineData("1260")]
    [InlineData("12:75")]
    [InlineData("9:5")]
    [InlineData("123456")]
    [InlineData("9a")]
    [InlineData("-9")]
    [InlineData("now")]
    public void TimeInputRejectsWhatIsNotATime(string text) =>
        Assert.False(TimeInput.TryParse(text, out _));

    [Fact]
    public void TimeInputFormatsAsHoursAndMinutes() =>
        Assert.Equal("09:05", TimeInput.Format(new TimeSpan(9, 5, 0)));
}
