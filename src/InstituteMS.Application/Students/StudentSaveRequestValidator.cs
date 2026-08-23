using FluentValidation;

namespace InstituteMS.Application.Students;

/// <summary>
/// Server-side port of the legacy client-side rules in
/// <c>Scripts/Student.js</c> (<c>validateForm</c> + <c>changeme2</c>). The old app
/// only validated in the browser, so anything hitting the controller directly was
/// unchecked; these now run on every request regardless of client.
/// </summary>
public class StudentSaveRequestValidator : AbstractValidator<StudentSaveRequest>
{
    public StudentSaveRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Student Name is required.")
            .MaximumLength(100);

        RuleFor(x => x.GuardianName)
            .NotEmpty().WithMessage("Guardian Name is required.")
            .MaximumLength(300);

        RuleFor(x => x.Phone)
            .NotEmpty().WithMessage("Phone Number is required.")
            .Matches(@"^[0-9]{10}$").WithMessage("Enter a valid 10-digit Phone Number.");

        RuleFor(x => x.Address)
            .NotEmpty().WithMessage("Student Address is required.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Valid Email is required.")
            .MaximumLength(50);

        RuleFor(x => x.PersonalEmail)
            .EmailAddress().WithMessage("Personal email is not valid.")
            .MaximumLength(150)
            .When(x => !string.IsNullOrWhiteSpace(x.PersonalEmail));

        RuleFor(x => x.DateOfBirth)
            .NotNull().WithMessage("Date of Birth is required.")
            .Must(BeInThePast).WithMessage("Date of Birth cannot be in the future.");

        RuleFor(x => x.DateOfJoining)
            .NotNull().WithMessage("Date of Joining is required.");

        RuleFor(x => x)
            .Must(x => x.DateOfBirth is null || x.DateOfJoining is null || x.DateOfBirth < x.DateOfJoining)
            .WithMessage("Date of Joining must be after the Date of Birth.")
            .WithName("DateOfJoining");

        RuleFor(x => x.MonthlyFees)
            .NotNull().WithMessage("Monthly Fees must be a valid number.")
            .GreaterThanOrEqualTo(0).WithMessage("Monthly Fees cannot be negative.");

        RuleFor(x => x.AdmissionFees)
            .NotNull().WithMessage("Admission Fees must be a valid number.")
            .GreaterThanOrEqualTo(0).WithMessage("Admission Fees cannot be negative.");

        RuleFor(x => x.CourseId)
            .NotNull().WithMessage("Please select a Course.")
            .GreaterThan(0).WithMessage("Please select a Course.");

        RuleFor(x => x.TeacherCount)
            .NotNull().WithMessage("Number of Teachers is required.")
            .InclusiveBetween(1, 3).WithMessage("Number of Teachers must be between 1 and 3.");

        RuleFor(x => x.PrimaryTeacherId)
            .NotNull().WithMessage("Please select the first Teacher.")
            .GreaterThan(0).WithMessage("Please select the first Teacher.");

        RuleFor(x => x.SecondTeacherId)
            .NotNull().WithMessage("Please select the second Teacher.")
            .GreaterThan(0).WithMessage("Please select the second Teacher.")
            .When(x => x.TeacherCount >= 2);

        RuleFor(x => x.ThirdTeacherId)
            .NotNull().WithMessage("Please select the third Teacher.")
            .GreaterThan(0).WithMessage("Please select the third Teacher.")
            .When(x => x.TeacherCount >= 3);

        // The legacy `checkselectvalue` tried to block this but compared the wrong
        // elements and never fired, so the same teacher could be picked twice.
        RuleFor(x => x)
            .Must(HaveDistinctTeachers)
            .WithMessage("The same teacher cannot be selected more than once.")
            .WithName("Teachers");

        // Branch is a lookup id now, not free text — the service rejects an id that
        // does not resolve, so only the obvious shape is checked here.
        RuleFor(x => x.BranchId)
            .GreaterThan(0).WithMessage("Please select a valid branch.")
            .When(x => x.BranchId.HasValue);
    }

    private static bool BeInThePast(DateOnly? date) =>
        date is null || date.Value <= DateOnly.FromDateTime(DateTime.Today);

    private static bool HaveDistinctTeachers(StudentSaveRequest x)
    {
        var picked = new[] { x.PrimaryTeacherId, x.SecondTeacherId, x.ThirdTeacherId }
            .Where(id => id is > 0)
            .Select(id => id!.Value)
            .ToList();

        return picked.Count == picked.Distinct().Count();
    }
}
