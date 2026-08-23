namespace InstituteMS.Infrastructure.Services;

/// <summary>
/// The institute's late-fine rule, ported from <c>$scope.days()</c> and
/// <c>$scope.Fine()</c> in <c>Scripts/MonthlyCourse.js</c>.
///
/// A fee month is treated as 32 days. Anything beyond that is fined at ₹2 per day.
/// Which date the clock runs from depends on when the student joined:
///
///   * Joined on or after 2019-11-01 — the "new scheme". The clock runs from the
///     joining date, and the elapsed days are spread across all the months still
///     outstanding, so a student paying three months at once is not fined as though
///     every one of them were late:
///         daysPerInstallment = daysElapsed / (installmentsPaid + 1)
///
///   * Joined before 2019-11-01 — the "old scheme". The clock runs from the previous
///     payment, and the elapsed days are used as they are.
///
/// In both cases:  fine = max(0, daysPerInstallment − 32) × 2
///
/// The cut-over date is hard-coded in the legacy JavaScript exactly as it is here.
/// It is the date the institute changed how it charged, so it is a business constant
/// rather than something to make configurable on a guess.
/// </summary>
public static class LateFineCalculator
{
    /// <summary>The date the institute switched schemes. From Scripts/MonthlyCourse.js.</summary>
    public static readonly DateOnly SchemeChangeDate = new(2019, 11, 1);

    /// <summary>A fee month, in days.</summary>
    public const int GraceDays = 32;

    /// <summary>Rupees charged per day beyond the grace period.</summary>
    public const decimal FinePerDay = 2m;

    public record Result(
        int DaysSincePrevious,
        decimal DaysPerInstallment,
        decimal LateFine,
        DateOnly? ReferenceDate,
        string Explanation);

    public static Result Calculate(
        DateOnly feeDate,
        DateOnly? dateOfJoining,
        DateOnly? lastPaymentDate,
        int installmentsPaid)
    {
        var newScheme = dateOfJoining is not null && dateOfJoining.Value >= SchemeChangeDate;

        // New scheme measures from joining; old scheme measures from the last payment
        // and falls back to joining when there has not been one.
        var reference = newScheme
            ? dateOfJoining
            : lastPaymentDate ?? dateOfJoining;

        if (reference is null)
        {
            return new Result(0, 0m, 0m, null,
                "No joining date or previous payment on record, so no fine was applied.");
        }

        // Absolute difference, matching the legacy Math.abs() on the millisecond delta.
        var days = Math.Abs(feeDate.DayNumber - reference.Value.DayNumber);

        decimal perInstallment;
        string basis;

        if (newScheme)
        {
            // Spread across every month still owed, including this one.
            var months = Math.Abs(installmentsPaid) + 1;
            perInstallment = decimal.Divide(days, months);
            basis =
                $"{days} day(s) since joining on {reference:dd MMM yyyy}, spread over {months} month(s) " +
                $"= {perInstallment:0.##} day(s) per month";
        }
        else
        {
            perInstallment = days;
            basis = $"{days} day(s) since {(lastPaymentDate is null ? "joining" : "the previous payment")} " +
                    $"on {reference:dd MMM yyyy}";
        }

        var overdue = perInstallment - GraceDays;
        var fine = overdue > 0m ? Math.Round(overdue * FinePerDay, 2, MidpointRounding.AwayFromZero) : 0m;

        var explanation = fine > 0m
            ? $"{basis}; {overdue:0.##} day(s) past the {GraceDays}-day allowance at ₹{FinePerDay:0} per day."
            : $"{basis}; within the {GraceDays}-day allowance, so no fine.";

        return new Result(days, decimal.Round(perInstallment, 2), fine, reference, explanation);
    }

    /// <summary>
    /// Teacher's share of a payment, split evenly between the teachers assigned.
    /// Legacy: <c>sal = ((installment * percentage) / 100) / teacherCount</c>.
    /// Note it is calculated on the installment, not the installment plus fine.
    /// </summary>
    public static decimal TeacherSalaryEach(decimal installment, decimal percentage, int teacherCount)
    {
        if (teacherCount <= 0) return 0m;

        return Math.Round(installment * percentage / 100m / teacherCount, 2, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// The receipt's "next payment due": one fee month after the payment being
    /// receipted.
    ///
    /// The legacy <c>convert3()</c> anchored this to the joining date for students on
    /// the new scheme — <c>joining + (monthsPaid + 1) × 32</c> — which printed a date
    /// in the past whenever a student was behind. A receipt for a payment taken on
    /// 22 Aug 2026 read "next due 3 Jul 2026". Anchoring to the payment date instead
    /// always reads forward, and unlike anchoring to the current date it does not
    /// change when the receipt is reprinted.
    /// </summary>
    public static DateOnly NextDueDate(DateOnly feeDate) => feeDate.AddDays(GraceDays);
}
