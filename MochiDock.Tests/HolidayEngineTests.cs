using System;
using MochiDock.Core;
using Xunit;

namespace MochiDock.Tests;

public class HolidayEngineTests
{
    [Fact]
    public void JapanHolidays_MatchKnownPublicHolidays()
    {
        var cultureDay = new DateTime(2026, 11, 3);
        var holidays = HolidayEngine.GetHolidays(cultureDay, enableJapan: true, enableIndonesia: false);
        
        Assert.Single(holidays);
        Assert.Equal("jp", holidays[0].Country);
        Assert.Equal("文化の日", holidays[0].NameLocal);
        Assert.Equal("Culture Day", holidays[0].NameEn);
        Assert.True(HolidayEngine.IsHoliday(cultureDay, enableJapan: true, enableIndonesia: false));
    }

    [Fact]
    public void IndonesiaHolidays_MatchKnownPublicHolidays()
    {
        var independenceDay = new DateTime(2026, 8, 17);
        var holidays = HolidayEngine.GetHolidays(independenceDay, enableJapan: false, enableIndonesia: true);

        Assert.Single(holidays);
        Assert.Equal("id", holidays[0].Country);
        Assert.Contains("Kemerdekaan", holidays[0].NameLocal);
        Assert.True(HolidayEngine.IsHoliday(independenceDay, enableJapan: false, enableIndonesia: true));
    }

    [Fact]
    public void HolidayFilters_CanBeToggledIndependently()
    {
        var date = new DateTime(2026, 11, 3);
        Assert.True(HolidayEngine.IsHoliday(date, enableJapan: true, enableIndonesia: false));
        Assert.False(HolidayEngine.IsHoliday(date, enableJapan: false, enableIndonesia: false));
        Assert.False(HolidayEngine.IsHoliday(date, enableJapan: false, enableIndonesia: true));
    }
}

