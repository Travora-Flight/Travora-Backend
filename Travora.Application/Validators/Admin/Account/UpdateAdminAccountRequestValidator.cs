using FluentValidation;
using Travora.Application.DTOs.Admin.Account;

namespace Travora.Application.Validators.Admin.Account;

public class UpdateAdminAccountRequestValidator : AbstractValidator<UpdateAdminAccountRequest>
{
    public UpdateAdminAccountRequestValidator()
    {
        RuleFor(x => x.Phone)
            .MaximumLength(11).WithMessage("Phone number cannot exceed 11 digits")
            .When(x => !string.IsNullOrEmpty(x.Phone));
    }
}
