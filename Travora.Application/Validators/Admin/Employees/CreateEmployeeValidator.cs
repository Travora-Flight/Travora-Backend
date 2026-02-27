using FluentValidation;
using Travora.Application.DTOs.Admin.Employees;
using Travora.Domain.Enums;

namespace Travora.Application.Validators.Admin.Employees;

public class CreateEmployeeValidator : AbstractValidator<CreateEmployeeRequest>
{
    public CreateEmployeeValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().WithMessage("First name is required");
        RuleFor(x => x.LastName).NotEmpty().WithMessage("Last name is required");
        RuleFor(x => x.MobileNumber).NotEmpty().WithMessage("Mobile number is required");
        RuleFor(x => x.NationalId).NotEmpty().WithMessage("National ID is required");
        RuleFor(x => x.DateOfBirth).NotEmpty().WithMessage("Date of birth is required");
        RuleFor(x => x.JobRole).IsInEnum().WithMessage("Invalid job role");
        RuleFor(x => x.ShiftType).IsInEnum().WithMessage("Invalid shift type");
        RuleFor(x => x.ProfilePhoto).NotNull().WithMessage("Profile photo is required");
        RuleFor(x => x.NationalIdPhoto).NotNull().WithMessage("National ID photo is required");

        When(x => x.JobRole == JobRole.Driver, () =>
        {
            RuleFor(x => x.VehicleId).NotNull().WithMessage("Vehicle ID is required for drivers");
            RuleFor(x => x.DriverLicense).NotNull().WithMessage("Driver license is required for drivers");
        });

        When(x => x.JobRole == JobRole.BaggageHandler, () =>
        {
            RuleFor(x => x.CheckpointId).NotNull().WithMessage("Checkpoint ID is required for baggage handlers");
        });
    }
}
