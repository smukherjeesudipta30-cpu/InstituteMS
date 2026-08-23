using FluentValidation;

namespace InstituteMS.Application.About;

public class ContactRequestValidator : AbstractValidator<ContactRequest>
{
    public ContactRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Please tell us your name.")
            .MaximumLength(200);

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email is required so we can reply.")
            .EmailAddress().WithMessage("Enter a valid email address.")
            .MaximumLength(200);

        RuleFor(x => x.Phone)
            .Matches(@"^[0-9+\-\s()]{6,20}$").WithMessage("Enter a valid phone number.")
            .When(x => !string.IsNullOrWhiteSpace(x.Phone));

        RuleFor(x => x.Message)
            .NotEmpty().WithMessage("Please write your message.")
            .MinimumLength(10).WithMessage("Message must be at least 10 characters.")
            .MaximumLength(2000);
    }
}
