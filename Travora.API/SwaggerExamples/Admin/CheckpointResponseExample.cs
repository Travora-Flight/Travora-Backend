using Swashbuckle.AspNetCore.Filters;
using Travora.Application.DTOs.Admin.Checkpoints;

namespace Travora.API.SwaggerExamples.Admin;

public class CheckpointResponseExample : IExamplesProvider<CheckpointResponse>
{
    public CheckpointResponse GetExamples()
    {
        return new CheckpointResponse
        {
            CheckpointId = 10,
            CheckpointName = "X-Ray Scanner A",
            Description = "Main security checkpoint at Terminal 1",
            GpsLatitude = 30.1234m,
            GpsLongitude = 31.5678m,
            IsAssigned = true
        };
    }
}
