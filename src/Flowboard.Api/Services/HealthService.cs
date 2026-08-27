// Provider side of specs/001-solution-scaffold/contracts/health-api.md (governance repo).
namespace Flowboard.Api.Services;

public sealed record HealthStatusDto(string Status, string Service, string Version, DateTime TimestampUtc);

public interface IHealthService
{
    HealthStatusDto GetStatus();
}

public sealed class HealthService : IHealthService
{
    private static readonly string Version =
        typeof(HealthService).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    public HealthStatusDto GetStatus() => new("ok", "flowboard-api", Version, DateTime.UtcNow);
}
