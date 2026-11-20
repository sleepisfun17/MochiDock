using MochiDock.Core;
namespace MochiDock.Tests;
public class PrayerTimelineTests
{
    private static DateTimeOffset At(int h,int m=0,int s=0)=>new(2026,11,6,h,m,s,TimeSpan.FromHours(9));
    private static DateTimeOffset[] Prayers => [At(4,50),At(11,33),At(14,27),At(16,48),At(18,10)];
    private static double Position(DateTimeOffset now)=>PrayerTimeline.Position(now,At(0),At(0).AddDays(1),Prayers)!.Value;
    [Theory][InlineData(10,50,.27866)][InlineData(10,58,.28263)][InlineData(11,33,.3)][InlineData(14,27,.5)]
    public void ScreenshotTimesMatchAnchors(int h,int m,double expected)=>Assert.InRange(Position(At(h,m)),expected-.00001,expected+.00001);
    [Fact] public void DoesNotArriveEarlyAndDescendsOnlyAfterDhuhr()
    {
        Assert.True(Position(At(11,32,59))<.3);
        Assert.Equal(.3,Position(At(11,33)));
        Assert.True(Position(At(11,33,1))>.3);
        Assert.True(PrayerTimeline.Height(Position(At(11,33,1)))>PrayerTimeline.Height(.3));
        Assert.True(PrayerTimeline.Height(.5)>PrayerTimeline.Height(.3));
        Assert.Equal(TimeSpan.FromMinutes(43),Prayers[1]-At(10,50));
    }
    [Fact] public void AllBoundariesAndMonotonicity()
    {
        for(int i=0;i<5;i++)Assert.Equal(PrayerTimeline.Slots[i],Position(Prayers[i]));
        double previous=-1;
        for(int minute=0;minute<1440;minute++){double p=Position(At(0).AddMinutes(minute));Assert.True(p>=previous);previous=p;}
        Assert.Equal(0,Position(At(0)));
        Assert.Null(PrayerTimeline.Position(At(0).AddDays(1),At(0),At(0).AddDays(1),Prayers));
        Assert.Null(PrayerTimeline.Position(At(0).AddSeconds(-1),At(0),At(0).AddDays(1),Prayers));
    }
    [Fact] public void InvalidAnchorsHaveNoMarker()
    {
        var prayers=Prayers;prayers[2]=prayers[1];
        Assert.Null(PrayerTimeline.Position(At(10),At(0),At(0).AddDays(1),prayers));
    }
    [Theory][InlineData(320)][InlineData(400)][InlineData(584)]
    public void ApproachingMarkerCannotOverlapLargeRing(int width)
    {
        double p=Position(At(10,50));
        double dx=(.3-p)*width,dy=PrayerTimeline.Height(.3)-PrayerTimeline.Height(p);
        Assert.NotEqual(19,PrayerTimeline.TargetSize(Math.Sqrt(dx*dx+dy*dy)));
        Assert.Equal(0,PrayerTimeline.TargetSize(0));
    }
    [Theory][InlineData(3,8,23)][InlineData(11,1,25)]
    public void DaylightSavingUsesAbsoluteIntervals(int month,int day,int hours)
    {
        var zone=TimeZoneInfo.FindSystemTimeZoneById("America/New_York");var date=new DateTime(2026,month,day);
        var start=PrayerTime.At(date,zone);var end=PrayerTime.At(date.AddDays(1),zone);
        var prayers=new[]{5,12,15,17,19}.Select(h=>PrayerTime.At(date.AddHours(h),zone)).ToArray();
        Assert.Equal(hours,(end-start).TotalHours);
        Assert.Equal(.2,PrayerTimeline.Position(prayers[0]+(prayers[1]-prayers[0])/2,start,end,prayers)!.Value,10);
    }
    [Fact]
    public void IsNextSlotHighlightsUpcomingPrayerAndRollsOverToFajrAfterIsha()
    {
        var prayers = Prayers;
        // At 10:00 (before Dhuhr 11:33), Next is Dhuhr (index 1)
        Assert.False(PrayerTimeline.IsNextSlot(0, prayers[1], At(10), prayers));
        Assert.True(PrayerTimeline.IsNextSlot(1, prayers[1], At(10), prayers));
        Assert.False(PrayerTimeline.IsNextSlot(2, prayers[1], At(10), prayers));

        // After Isha (18:10), e.g. at 21:00, next prayer instant is tomorrow's Fajr
        var tomorrowFajr = prayers[0].AddDays(1);
        Assert.True(PrayerTimeline.IsNextSlot(0, tomorrowFajr, At(21), prayers));
        Assert.False(PrayerTimeline.IsNextSlot(1, tomorrowFajr, At(21), prayers));
        Assert.False(PrayerTimeline.IsNextSlot(4, tomorrowFajr, At(21), prayers));
    }

    [Fact]
    public void GetCurrentSlotIndexAccuratelyTracksOngoingPrayerWindow()
    {
        var prayers = Prayers; // Fajr 04:51, Dhuhr 11:33, Asr 14:29, Maghrib 16:49, Isha 18:10
        // Early morning before Fajr: yesterday's Isha is ongoing
        Assert.Equal(4, PrayerTimeline.GetCurrentSlotIndex(At(3, 0), prayers));
        // Between Fajr and Dhuhr: Fajr is ongoing
        Assert.Equal(0, PrayerTimeline.GetCurrentSlotIndex(At(6, 0), prayers));
        // Between Dhuhr and Asr: Dhuhr is ongoing
        Assert.Equal(1, PrayerTimeline.GetCurrentSlotIndex(At(12, 0), prayers));
        // Between Asr and Maghrib: Asr is ongoing
        Assert.Equal(2, PrayerTimeline.GetCurrentSlotIndex(At(15, 0), prayers));
        // Between Maghrib and Isha: Maghrib is ongoing
        Assert.Equal(3, PrayerTimeline.GetCurrentSlotIndex(At(17, 30), prayers));
        // After Isha: Isha is ongoing
        Assert.Equal(4, PrayerTimeline.GetCurrentSlotIndex(At(19, 39), prayers));
    }
}
