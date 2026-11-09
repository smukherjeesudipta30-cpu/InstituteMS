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

        /* Required of every teacher except the technical ones, at the owner's
           instruction. The exemption is the IsTechnical tick rather than anything
           read out of the job title — see the note on the entity. */
        /* Not required here. The owner: "who told you to add qualification & computer
           training here, it will applicable for Staff hierarchy entry only" - a
           teacher login can be created before the teacher is ever added to the
           hierarchy chart, and qualification/training are entered there instead,
           not repeated on this form. */
        RuleFor(x => x.Qualification).MaximumLength(200);
        RuleFor(x => x.ComputerTraining).MaximumLength(200);
        RuleFor(x => x.Branch).MaximumLength(100);
    }
}

public class BranchSaveRequestValidator : AbstractValidator<BranchSaveRequest>
{
    public BranchSaveRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Branch name is required.")
            .MaximumLength(50);

        // name@bank, e.g. bom261001361082@mahb or paytm.s1ngj29@pty.
        RuleFor(x => x.UpiId)
            .Matches(@"^[A-Za-z0-9.\-_]{2,256}@[A-Za-z]{2,64}$")
            .WithMessage("A UPI ID looks like name@bank, e.g. bom261001361082@mahb.")
            .MaximumLength(100)
            .When(x => !string.IsNullOrWhiteSpace(x.UpiId));

        RuleFor(x => x.UpiPayeeName).MaximumLength(100);

        /* Latitude and longitude go together: one without the other is not a place,
           and would leave the fence unusable while looking as though it were set. */
        RuleFor(x => x.Longitude)
            .NotNull().WithMessage("Enter the longitude as well as the latitude.")
            .When(x => x.Latitude.HasValue);

        RuleFor(x => x.Latitude)
            .NotNull().WithMessage("Enter the latitude as well as the longitude.")
            .When(x => x.Longitude.HasValue);

        RuleFor(x => x.Latitude)
            .InclusiveBetween(-90m, 90m).WithMessage("Latitude must be between -90 and 90.")
            .When(x => x.Latitude.HasValue);

        RuleFor(x => x.Longitude)
            .InclusiveBetween(-180m, 180m).WithMessage("Longitude must be between -180 and 180.")
            .When(x => x.Longitude.HasValue);

        /* Below about 50 m an ordinary phone fix cannot reliably place someone
           inside, so staff at the door would be refused. Above a kilometre the
           fence stops meaning "at the centre" at all. */
        RuleFor(x => x.GeofenceMetres)
            .InclusiveBetween(50, 2000)
            .WithMessage("The allowed distance must be between 50 and 2000 metres.");

        // The code becomes a segment of every registration id issued for the branch,
        // so it must stay short and free of the '/' that separates those segments.
        RuleFor(x => x.Code)
            .NotEmpty().WithMessage("Branch code is required.")
            .MaximumLength(10).WithMessage("Branch code must be 10 characters or fewer.")
            .Matches("^[A-Za-z0-9]+$")
            .WithMessage("Branch code may contain only letters and digits.");
    }
}
