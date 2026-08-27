// research.md R-9: WebApplicationFactory against the disposable flowboard-db-test database.
// Migrated once per test collection (see FlowboardApiCollection below); individual test
// classes clean up the rows they created rather than truncating shared tables.
using Flowboard.Api.Data;
using Flowboard.Api.Data.Configurations;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flowboard.Api.Tests.TestFixtures;

public sealed class FlowboardApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string TestConnectionString =
        "Server=(localdb)\\MSSQLLocalDB;Database=flowboard-db-test;Trusted_Connection=True;" +
        "MultipleActiveResultSets=true;TrustServerCertificate=True";

    // Fixed test-only signing key — never used outside this test host.
    private const string TestSigningKey = "DueOBhs2xn65gKOw4kgongPx2W5T+tOrUUUGVGnrqnQ=";

    // Known test password: "FixtureOwner!2026" (BoardMembersEndpointTests.FixtureOwnerPassword,
    // AuthEndpointTests). Second-model review B1: the migration seeds an unverifiable
    // placeholder hash (UserConfiguration.FixtureOwnerPlaceholderPasswordHash) so no real
    // credential ships in production — this real hash is set here, only in the disposable
    // flowboard-db-test database, never in the production migration model.
    private const string TestFixtureOwnerPasswordHash =
        "$2a$12$QaQbHkSBz2bsEVMr5NPkfun1/.UNcfh5Hthg57fglJoMnzCrxhIJy";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Flowboard", TestConnectionString);
        builder.UseSetting("Jwt:SigningKey", TestSigningKey);
        // TestServer requests have no real per-connection IP, so every in-process test
        // caller would otherwise share the login/signup rate limiters' one "unknown" partition.
        builder.UseSetting("RateLimiting:Login:PermitLimit", "1000");
        builder.UseSetting("RateLimiting:Signup:PermitLimit", "1000");
    }

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowboardDbContext>();
        await db.Database.MigrateAsync();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE [User] SET PasswordHash = {TestFixtureOwnerPasswordHash} WHERE Id = {UserConfiguration.FixtureOwnerId}");
    }

    async Task IAsyncLifetime.DisposeAsync() => await base.DisposeAsync();
}

[CollectionDefinition(Name)]
public sealed class FlowboardApiCollection : ICollectionFixture<FlowboardApiFactory>
{
    public const string Name = "FlowboardApi";
}
