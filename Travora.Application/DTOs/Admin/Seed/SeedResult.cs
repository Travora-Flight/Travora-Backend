namespace Travora.Application.DTOs.Admin.Seed;

public class SeedResult
{
    public bool Success { get; set; }
    public int Inserted { get; set; }
    public int Updated { get; set; }
    public int Skipped { get; set; }
    public int Total { get; set; }
    public string? Error { get; set; }
}
