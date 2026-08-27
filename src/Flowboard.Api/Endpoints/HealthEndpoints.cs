// Provider side of specs/001-solution-scaffold/contracts/health-api.md (governance repo).
// Public liveness endpoint — anonymous by design (spec Assumptions, constitution VIII).
using Flowboard.Api.Services;

namespace Flowboard.Api.Endpoints;

public static class HealthEndpoints
{
    public static IEndpointRouteBuilder MapHealthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/v1/health")
            .WithTags("Health")
            .AllowAnonymous();

        group.MapGet("", GetStatus);

        return endpoints;
    }

    private static IResult GetStatus(IHealthService health) => Results.Ok(health.GetStatus());
}
