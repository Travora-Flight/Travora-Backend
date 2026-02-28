using FluentValidation;
using Microsoft.AspNetCore.Http;
using Travora.Application.DTOs.Admin.Employees;
using Travora.Domain.Enums;
using System.IO;

namespace Travora.Application.Validators.Admin.Employees;

public class CreateEmployeeValidator : AbstractValidator<CreateEmployeeRequest>
{
    public CreateEmployeeValidator()
    {
        RuleFor(x => x.FirstName).NotEmpty().WithMessage("First name is required");
        RuleFor(x => x.LastName).NotEmpty().WithMessage("Last name is required");
        
        RuleFor(x => x.MobileNumber)
            .NotEmpty().WithMessage("Mobile number is required")
            .Length(11).WithMessage("Mobile number must be 11 digits")
            .Matches("^01").WithMessage("Mobile number must start with 01");

        RuleFor(x => x.NationalId)
            .NotEmpty().WithMessage("National ID is required")
            .Length(14).WithMessage("National ID must be 14 digits");

        RuleFor(x => x.DateOfBirth)
            .NotEmpty().WithMessage("Date of birth is required")
            .Must(BeAtLeast18).WithMessage("Employee must be at least 18 years old");

        RuleFor(x => x.JobRole)
            .IsInEnum().WithMessage("Invalid job role")
            .Must(x => x == JobRole.Driver || x == JobRole.BaggageHandler).WithMessage("Job role must be driver or baggage_handler");

        RuleFor(x => x.ShiftType).IsInEnum().WithMessage("Invalid shift type");

        RuleFor(x => x.ProfilePhoto)
            .NotNull().WithMessage("Profile photo is required")
            .Must(BeAValidImage).WithMessage("Profile photo must be an image (jpg, jpeg, png)");

        RuleFor(x => x.NationalIdPhoto)
            .NotNull().WithMessage("National ID photo is required")
            .Must(BeAValidImage).WithMessage("National ID photo must be an image (jpg, jpeg, png)");

        When(x => x.JobRole == JobRole.Driver, () =>
        {
            RuleFor(x => x.VehicleId)
                .NotNull().WithMessage("vehicleId مطلوب للـ Driver")
                .GreaterThan(0).WithMessage("vehicleId مطلوب للـ Driver");

            RuleFor(x => x.DriverLicense)
                .NotNull().WithMessage("driverLicense مطلوب للـ Driver")
                .Must(BeAValidImage).WithMessage("Driver license must be an image (jpg, jpeg, png)");
        });

        When(x => x.JobRole == JobRole.BaggageHandler, () =>
        {
            RuleFor(x => x.CheckpointId)
                .NotNull().WithMessage("checkpointId مطلوب للـ Baggage Handler")
                .GreaterThan(0).WithMessage("checkpointId مطلوب للـ Baggage Handler");
        });
    }

    private bool BeAtLeast18(DateTime dateOfBirth)
    {
        var age = DateTime.Today.Year - dateOfBirth.Year;
        if (dateOfBirth.Date > DateTime.Today.AddYears(-age)) age--;
        return age >= 18;
    }

    private bool BeAValidImage(IFormFile? file)
    {
        if (file == null) return false;
        var ext = Path.GetExtension(file.FileName).ToLower();
        return ext == ".jpg" || ext == ".jpeg" || ext == ".png";
    }
}
