using FluentValidation;

namespace InstituteMS.Application.Academics;

public class CourseSaveRequestValidator : AbstractValidator<CourseSaveRequest>
{
    public CourseSaveRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Course name is required.")
            .MaximumLength(300);

        RuleFor(x => x.Duration)
            .NotNull().WithMessage("Duration is required.")
            .InclusiveBetween(1, 120).WithMessage("Duration must be between 1 and 120 months.");
    }
}

/// <summary>
/// The legacy marks form validated nothing at all, so a subject could be saved with
/// obtained marks higher than the full marks. Each of the four slots is optional,
/// but a slot that is used has to be internally consistent.
/// </summary>
public class MarkSaveRequestValidator : AbstractValidator<MarkSaveRequest>
{
    public MarkSaveRequestValidator()
    {
        RuleFor(x => x.RegistrationId)
            .NotEmpty().WithMessage("Registration id is required.")
            .MaximumLength(15);

        RuleFor(x => x.ExamDate)
            .NotNull().WithMessage("Date of examination is required.")
            .Must(d => d is null || d.Value <= DateOnly.FromDateTime(DateTime.Today))
            .WithMessage("Date of examination cannot be in the future.");

        // At least one subject has to be filled in, otherwise the row says nothing.
        RuleFor(x => x)
            .Must(x => !string.IsNullOrWhiteSpace(x.Subject1)
                    || !string.IsNullOrWhiteSpace(x.Subject2)
                    || !string.IsNullOrWhiteSpace(x.Subject3)
                    || !string.IsNullOrWhiteSpace(x.Subject4))
            .WithMessage("Enter at least one subject.")
            .WithName("Subjects");

        AddSubjectRules(1, x => x.Subject1, x => x.FullMarks1, x => x.ObtainedMarks1);
        AddSubjectRules(2, x => x.Subject2, x => x.FullMarks2, x => x.ObtainedMarks2);
        AddSubjectRules(3, x => x.Subject3, x => x.FullMarks3, x => x.ObtainedMarks3);
        AddSubjectRules(4, x => x.Subject4, x => x.FullMarks4, x => x.ObtainedMarks4);
    }

    private void AddSubjectRules(
        int slot,
        Func<MarkSaveRequest, string?> subject,
        Func<MarkSaveRequest, int?> full,
        Func<MarkSaveRequest, int?> obtained)
    {
        var used = (MarkSaveRequest x) => !string.IsNullOrWhiteSpace(subject(x));

        RuleFor(x => x)
            .Must(x => full(x) is not null)
            .WithMessage($"Subject {slot}: enter the full marks.")
            .WithName($"FullMarks{slot}")
            .When(used);

        RuleFor(x => x)
            .Must(x => obtained(x) is not null)
            .WithMessage($"Subject {slot}: enter the marks obtained.")
            .WithName($"ObtainedMarks{slot}")
            .When(used);

        RuleFor(x => x)
            .Must(x => full(x) is null || full(x) > 0)
            .WithMessage($"Subject {slot}: full marks must be greater than zero.")
            .WithName($"FullMarks{slot}")
            .When(used);

        RuleFor(x => x)
            .Must(x => obtained(x) is null || obtained(x) >= 0)
            .WithMessage($"Subject {slot}: marks obtained cannot be negative.")
            .WithName($"ObtainedMarks{slot}")
            .When(used);

        RuleFor(x => x)
            .Must(x => full(x) is null || obtained(x) is null || obtained(x) <= full(x))
            .WithMessage($"Subject {slot}: marks obtained cannot exceed the full marks.")
            .WithName($"ObtainedMarks{slot}")
            .When(used);
    }
}

public class NoticeSaveRequestValidator : AbstractValidator<NoticeSaveRequest>
{
    public NoticeSaveRequestValidator()
    {
        RuleFor(x => x.Text)
            .NotEmpty().WithMessage("Notice text is required.")
            .MaximumLength(4000).WithMessage("Notice must be 4000 characters or fewer.");
    }
}
