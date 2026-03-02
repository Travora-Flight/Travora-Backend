namespace Travora.Application.Interfaces;

public interface IReportGeneratorJob
{
    Task GeneratePdfReportAsync(int reportId);
}
