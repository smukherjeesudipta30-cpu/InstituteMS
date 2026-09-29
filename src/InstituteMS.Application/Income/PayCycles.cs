namespace InstituteMS.Application.Income;

/// <summary>
/// The pay cycle arithmetic shared by the dashboard and the Income report. A
/// cycle runs from the start day (26 by default, set by Admin) to the day
/// before it next month, and is named by the month it ends in - 26 Aug to
/// 25 Sep is "September". With a start day of 1 a cycle is the calendar month.
/// </summary>
public static class PayCycles
{
    /// <summary>The month the cycle running on <paramref name="day"/> ends in.</summary>
    public static DateOnly LabelMonth(DateOnly day, int startDay)
    {
        var month = new DateOnly(day.Year, day.Month, 1);
        return startDay > 1 && day.Day >= startDay ? month.AddMonths(1) : month;
    }

    /// <summary>First and last day of the cycle named by <paramref name="labelMonth"/>.</summary>
    public static (DateOnly Start, DateOnly End) Range(DateOnly labelMonth, int startDay)
    {
        var month = new DateOnly(labelMonth.Year, labelMonth.Month, 1);
        if (startDay <= 1)
            return (month, month.AddMonths(1).AddDays(-1));

        // Short months: a start day past the month's end falls on its last day.
        var prev = month.AddMonths(-1);
        var start = new DateOnly(prev.Year, prev.Month, Math.Min(startDay, DateTime.DaysInMonth(prev.Year, prev.Month)));
        var end = new DateOnly(month.Year, month.Month, Math.Min(startDay - 1, DateTime.DaysInMonth(month.Year, month.Month)));
        return (start, end);
    }

    /// <summary>
    /// The latest cycle that has finished by <paramref name="today"/> - the newest
    /// one money is shown for. A cycle still running is not shown until it ends.
    /// </summary>
    public static DateOnly LastCompletedLabelMonth(DateOnly today, int startDay) =>
        LabelMonth(today, startDay).AddMonths(-1);

    /// <summary>The last day of the last completed cycle: no report reaches past it.</summary>
    public static DateOnly LastCompletedEnd(DateOnly today, int startDay) =>
        Range(LastCompletedLabelMonth(today, startDay), startDay).End;
}
