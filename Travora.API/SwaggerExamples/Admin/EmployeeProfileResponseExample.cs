using Swashbuckle.AspNetCore.Filters;
using Travora.Application.DTOs.Admin.Employees;

namespace Travora.API.SwaggerExamples.Admin;

public class EmployeeProfileResponseExample : IExamplesProvider<EmployeeProfileResponse>
{
    public EmployeeProfileResponse GetExamples()
    {
        return new EmployeeProfileResponse
        {
            EmployeeId = 1,
            Name = "Ahmed Ali",
            Code = "EMP-001",
            Status = "Active",
            JobRole = "Driver",
            ProfileImageUrl = "https://example.com/avatar.jpg",
            NationalIdImageUrl = "https://example.com/id.jpg",
            DriverLicenseUrl = "https://example.com/license.jpg",
            ContactInfo = new EmployeeContactInfo
            {
                Email = "ahmed.ali@travora.com",
                Mobile = "+201001234567"
            },
            AdditionalDetails = new EmployeeAdditionalDetails
            {
                DateOfBirth = "1990-05-15",
                ShiftType = "Morning",
                NationalId = "29005151234567"
            },
            VehicleId = 5,
            CheckpointId = null
        };
    }
}
