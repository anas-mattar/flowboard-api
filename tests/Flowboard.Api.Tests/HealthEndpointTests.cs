// Verifies the provider side of specs/001-solution-scaffold/contracts/health-api.md
// (governance repo): FR-006 — automated proof the health check responds successfully.
using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Flowboard.Api.Tests;

public sealed class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task GetHealth_Returns200WithContractPayload()
    {
        using var client = _factory.CreateClient();

        var response = await client.GetAsync("/v1/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;

        Assert.Equal("ok", root.GetProperty("status").GetString());
        Assert.Equal("flowboard-api", root.GetProperty("service").GetString());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("version").GetString()));

        var timestamp = root.GetProperty("timestampUtc").GetDateTime();
        Assert.Equal(DateTimeKind.Utc, timestamp.Kind);
    }
}
