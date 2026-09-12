using FluentValidation;

namespace InstituteMS.Application.Fees;

/// <summary>
/// Guards the fee ledger. Recording a payment writes four tables in one
/// transaction — <c>monthlyFees</c>, <c>Fees</c>, <c>LateFees</c> and one
/// <c>TecFees</c> row per assigned teacher — so a wrong number here does not
/// stop at the receipt: it moves straight into what a teacher is owed.
/// </summary>
/// <remarks>
/// <para>There was no validator on this request at all. Twelve other request
/// types in the application have one; the single path that moves money did not,
/// which is how a malformed payload wrote a &#8377;0 payment against a real
/// student without complaint.</para>
///
/// <para>The rules are deliberately about what is <b>impossible</b>, not about
/// what looks unusual. A fee ledger carries genuinely odd but legitimate rows —
/// part payments, credits carried forward, waived months — and a rule that
/// second-guesses the office would be worse than no rule at all. Negative money,
/// a percentage above 100 and a payment dated years ahead are not judgement
/// calls; everything else is left alone.</para>
/// </remarks>
public class FeeSaveRequestValidator : AbstractValidator<FeeSaveRequest>
{
    public FeeSaveRequestValidator()
    {
        RuleFor(x => x.RegistrationId)
            .NotEmpty().WithMessage("Choose the student this payment is for.")
            .MaximumLength(50);

        RuleFor(x => x.FeeDate)
            .NotEmpty().WithMessage("Choose the month this payment covers.");

        /* A fee month far in the future is a mistyped year, not a payment. A
           month or two ahead is normal - fees are often taken in advance - so the
           line is drawn generously. */
        RuleFor(x => x.FeeDate)
            .LessThanOrEqualTo(_ => DateOnly.FromDateTime(DateTime.Now.AddYears(1)))
            .WithMessage("That fee month is more than a year away — check the date.");

        RuleFor(x => x.PreviousPaymentDate)
            .LessThanOrEqualTo(_ => DateOnly.FromDateTime(DateTime.Now.AddDays(1)))
            .WithMessage("The previous payment date cannot be in the future.")
            .When(x => x.PreviousPaymentDate.HasValue);

        /* Amounts. Zero is allowed on the individual parts - a month with no fine,
           or a payment that clears only the old due, are both real - but the row
           as a whole has to move some money, which is checked below. */
        RuleFor(x => x.InstallmentAmount)
            .GreaterThanOrEqualTo(0m).WithMessage("The installment cannot be negative.")
            .LessThan(1_000_000m).WithMessage("The installment must be less than 10,00,000.");

        RuleFor(x => x.LateFine)
            .GreaterThanOrEqualTo(0m).WithMessage("The late fine cannot be negative.")
            .LessThan(1_000_000m).WithMessage("The late fine must be less than 10,00,000.");

        RuleFor(x => x.AmountPaid)
            .GreaterThanOrEqualTo(0m).WithMessage("The amount paid cannot be negative.")
            .LessThan(1_000_000m).WithMessage("The amount paid must be less than 10,00,000.");

        RuleFor(x => x.TotalForMonth)
            .GreaterThanOrEqualTo(0m).WithMessage("The total for the month cannot be negative.")
            .LessThan(1_000_000m).WithMessage("The total for the month must be less than 10,00,000.");

        /* OldDue and NewDue may legitimately be negative - the legacy ledger
           carries a credit forward that way - so only the magnitude is capped. */
        RuleFor(x => x.OldDue)
            .InclusiveBetween(-1_000_000m, 1_000_000m)
            .WithMessage("The old due is out of range.");

        RuleFor(x => x.NewDue)
            .InclusiveBetween(-1_000_000m, 1_000_000m)
            .WithMessage("The new due is out of range.");

        /* The teacher share is a percentage. Above 100 would pay a teacher more
           than the student handed over, which is the error worth catching: it
           reaches TecFees in the same transaction and shows up as money owed. */
        RuleFor(x => x.TeacherPercentage)
            .InclusiveBetween(0m, 100m)
            .WithMessage("The teacher percentage must be between 0 and 100.");

        RuleFor(x => x.PaymentMode)
            .Must(m => m is "Cash" or "Bank")
            .WithMessage("Payment mode must be Cash or Bank.");

        /* Somewhere in the row there has to be money, or the receipt records
           nothing. This is what a zero-everything payload trips on. */
        RuleFor(x => x)
            .Must(HasSomeMoney)
            .WithMessage("This payment records no money at all — enter an installment, a fine or an amount paid.")
            .OverridePropertyName(nameof(FeeSaveRequest.AmountPaid));
    }

    private static bool HasSomeMoney(FeeSaveRequest r)
        => r.InstallmentAmount > 0m || r.LateFine > 0m || r.AmountPaid > 0m || r.TotalForMonth > 0m;
}
