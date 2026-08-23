using FluentValidation;

namespace InstituteMS.Application.Users;

/// <summary>
/// The legacy user form validated nothing beyond an email-exists check, and stored
/// whatever password was typed in plain text. These rules run on every request.
/// </summary>
public class UserSaveRequestValidator : AbstractValidator<UserSaveRequest>
{
    public UserSaveRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("User name is required.")
            .MaximumLength(250);

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required.")
            .EmailAddress().WithMessage("Enter a valid email address.")
            .MaximumLength(50);

        RuleFor(x => x.Roles)
            .NotEmpty().WithMessage("Please select a role.")
            .MaximumLength(150);

        RuleFor(x => x.Phone)
            .Matches(@"^[0-9]{10}$").WithMessage("Enter a valid 10-digit phone number.")
            .When(x => !string.IsNullOrWhiteSpace(x.Phone));

        RuleFor(x => x.DateOfBirth)
            .Must(d => d is null || d.Value <= DateOnly.FromDateTime(DateTime.Today))
            .WithMessage("Date of birth cannot be in the future.");

        // Optional on update (null = keep current), but must be strong when supplied.
        RuleFor(x => x.Password)
            .MinimumLength(8).WithMessage("Password must be at least 8 characters.")
            .Matches("[A-Z]").WithMessage("Password must contain an upper-case letter.")
            .Matches("[a-z]").WithMessage("Password must contain a lower-case letter.")
            .Matches("[0-9]").WithMessage("Password must contain a digit.")
            .MaximumLength(128)
            .When(x => !string.IsNullOrWhiteSpace(x.Password));
    }
}

public class SetPasswordRequestValidator : AbstractValidator<SetPasswordRequest>
{
    public SetPasswordRequestValidator()
    {
        // Same rules as the create form, so a password set this way is never weaker
        // than one the administrator types into the full user form.
        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("A password is required.")
            .MinimumLength(8).WithMessage("Password must be at least 8 characters.")
            .Matches("[A-Z]").WithMessage("Password must contain an upper-case letter.")
            .Matches("[a-z]").WithMessage("Password must contain a lower-case letter.")
            .Matches("[0-9]").WithMessage("Password must contain a digit.");
    }
}
