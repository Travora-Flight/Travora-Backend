namespace Travora.Application.DTOs.Admin.Employees;

public class EmployeeFormDataResponse
{
    public List<IdNamePair> AvailableVehicles { get; set; } = new();
    public List<IdNamePair> AvailableCheckpoints { get; set; } = new();
    public List<string> JobRoles { get; set; } = new();
    public List<string> ShiftTypes { get; set; } = new();
}

public class IdNamePair
{
    public int Id { get; set; }
    public string DisplayName { get; set; } = string.Empty;
}
