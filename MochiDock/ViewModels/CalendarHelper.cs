using System;
using System.Collections.ObjectModel;
using System.Linq;

namespace MochiDock.ViewModels;

public class CalendarDayItem
{
    public int DayNumber { get; set; }
    public bool IsToday { get; set; }
    public bool IsCurrentMonth { get; set; }
}

public static class CalendarHelper
{
    public static ObservableCollection<CalendarDayItem> GenerateMonthDays(DateTime referenceDate)
    {
        var list = new ObservableCollection<CalendarDayItem>();
        var firstDayOfMonth = new DateTime(referenceDate.Year, referenceDate.Month, 1);
        int daysInMonth = DateTime.DaysInMonth(referenceDate.Year, referenceDate.Month);

        // Day of week where Monday is 0
        int startDayOfWeek = ((int)firstDayOfMonth.DayOfWeek + 6) % 7;

        // Previous month filler
        var prevMonth = firstDayOfMonth.AddMonths(-1);
        int daysInPrevMonth = DateTime.DaysInMonth(prevMonth.Year, prevMonth.Month);
        for (int i = startDayOfWeek - 1; i >= 0; i--)
        {
            list.Add(new CalendarDayItem
            {
                DayNumber = daysInPrevMonth - i,
                IsToday = false,
                IsCurrentMonth = false
            });
        }

        // Current month days
        var today = DateTime.Today;
        for (int day = 1; day <= daysInMonth; day++)
        {
            bool isToday = (today.Year == referenceDate.Year && today.Month == referenceDate.Month && today.Day == day);
            list.Add(new CalendarDayItem
            {
                DayNumber = day,
                IsToday = isToday,
                IsCurrentMonth = true
            });
        }

        // Next month filler ensuring full 7-day rows and standard 35 or 42 grid cells
        int nextDay = 1;
        while (list.Count < 35 || (list.Count % 7 != 0))
        {
            list.Add(new CalendarDayItem
            {
                DayNumber = nextDay++,
                IsToday = false,
                IsCurrentMonth = false
            });
        }

        return list;
    }
}

