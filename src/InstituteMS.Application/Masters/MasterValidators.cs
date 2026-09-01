using FluentValidation;

namespace InstituteMS.Application.Masters;

public class TeacherSaveRequestValidator : AbstractValidator<TeacherSaveRequest>
{
    public TeacherSaveRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Teacher name is required.")
            .MaximumLength(50);

        // This is the teacher's sign-in identity: the teacher account report matches
        // it against the signed-in email, the way the legacy
        // "select id from Teacher where LoginId = Session[UserID]" did. Most live
        // rows hold an email, so '@' has to be allowed - an earlier pattern without
        // it rejected those rows on edit. The limit is 50, matching production's
        // varchar(50); the older restored backup said 20, which was too narrow for
        // the 27-character logins production actually holds.
        RuleFor(x => x.LoginId)
            .NotEmpty().WithMessage("Teacher's user id is required.")
            .MaximumLength(50)
            .WithMessage("User id must be 50 characters or fewer.")
            .Matches("^[A-Za-z0-9@._-]+$")
            .WithMessage("User id may contain only letters, digits, at-sign, dot, underscore or hyphen.");

        // The legacy column is varchar(10) and the form accepted anything.
        RuleFor(x => x.Phone)
            .Matches("^[0-9]{10}$").WithMessage("Enter a valid 10-digit phone number.")
            .When(x => !string.IsNullOrWhiteSpace(x.Phone));
    }
}

public class BranchSaveRequestValidator : AbstractValidator<BranchSaveRequest>
{
    public BranchSaveRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Branch name is required.")
            .MaximumLength(50);

        // The code becomes a segment of every registration id issued for the branch,
        // so it must stay short and free of the '/' that separates those segments.
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Branch code is required.")
            .MaximumLength(10).WithMessage("Branch code must be 10 characters or fewer.")
            .Matches("^[A-Za-z0-9]+$")
            .WithMessage("Branch code may contain only letters and digits.");
    }
}
