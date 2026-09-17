using FluentValidation;
using InstituteMS.Domain.Entities;

namespace InstituteMS.Application.Enquiries;

public class CourseEnquirySaveRequestValidator : AbstractValidator<CourseEnquirySaveRequest>
{
    public CourseEnquirySaveRequestValidator()
    {
        RuleFor(x => x.StudentName)
            .NotEmpty().WithMessage("Name is required.")
            .MaximumLength(200);

        /* Ten digits, the same rule the student form uses. The phone number is the
           whole point of the record - an enquiry without one cannot be followed
           up, which is what the register exists for. */
        RuleFor(x => x.Phone)
            .NotEmpty().WithMessage("Phone number is required.")
            .Matches("^[0-9]{10}$").WithMessage("Enter a 10-digit phone number.");

        RuleFor(x => x.Address).MaximumLength(500);
        RuleFor(x => x.Remarks).MaximumLength(1000);

        RuleFor(x => x.CourseId)
            .GreaterThan(0).WithMessage("Please select a valid course.")
            .When(x => x.CourseId.HasValue);

        RuleFor(x => x.BranchId)
            .GreaterThan(0).WithMessage("Please select a valid branch.")
            .When(x => x.BranchId.HasValue);

        RuleFor(x => x.EnquiryDate)
            .Must(d => d is null || d.Value <= DateOnly.FromDateTime(DateTime.Today))
            .WithMessage("The enquiry date cannot be in the future.");

        /* The other way round from the enquiry date: there is no point scheduling
           a call for a day that has already gone. */
        RuleFor(x => x.NextFollowUpOn)
            .Must(d => d is null || d.Value >= DateOnly.FromDateTime(DateTime.Today))
            .WithMessage("The follow-up date cannot be in the past.");
    }
}

public class CourseEnquiryFollowUpRequestValidator : AbstractValidator<CourseEnquiryFollowUpRequest>
{
    public CourseEnquiryFollowUpRequestValidator()
    {
        RuleFor(x => x.Status)
            .NotEmpty().WithMessage("A status is required.")
            .Must(CourseEnquiryStatus.IsKnown)
            .WithMessage($"Status must be one of: {string.Join(", ", CourseEnquiryStatus.All)}.");

        RuleFor(x => x.Remarks).MaximumLength(500);

        RuleFor(x => x.NextFollowUpOn)
            .Must(d => d is null || d.Value >= DateOnly.FromDateTime(DateTime.Today))
            .WithMessage("The follow-up date cannot be in the past.");

        /* A next date on a closed enquiry is a contradiction: nobody is going to
           ring someone who has already enrolled or already said no. */
        RuleFor(x => x.NextFollowUpOn)
            .Empty()
            .WithMessage("A closed enquiry cannot have a follow-up date.")
            .When(x => x.Status is CourseEnquiryStatus.Converted or CourseEnquiryStatus.NotInterested);

        RuleFor(x => x.ConvertedStudentRegistrationId)
            .MaximumLength(50);
    }
}
