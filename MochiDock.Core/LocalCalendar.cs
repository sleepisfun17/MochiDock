namespace MochiDock.Core;
public static class LocalCalendar {
 public static DateTime[] Days(DateTime month, DayOfWeek firstDay = DayOfWeek.Sunday) { var first=new DateTime(month.Year,month.Month,1); var start=first.AddDays(-(((int)first.DayOfWeek-(int)firstDay+7)%7));return Enumerable.Range(0,42).Select(i=>start.AddDays(i)).ToArray(); }
}