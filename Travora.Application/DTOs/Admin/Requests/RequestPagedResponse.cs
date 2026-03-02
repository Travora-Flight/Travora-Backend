namespace Travora.Application.DTOs.Admin.Requests;

public class RequestPagedResponse
{
    public List<RequestListResponse> Requests { get; set; } = new();
    public int Total { get; set; }
}
