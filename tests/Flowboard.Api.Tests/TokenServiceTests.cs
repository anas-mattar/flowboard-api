using Flowboard.Api.Domain.Entities;
using Flowboard.Api.Services;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Flowboard.Api.Tests;

public sealed class TokenServiceTests
{
    // Fixed test-only keys — never used outside this test.
    private const string SigningKey = "DueOBhs2xn65gKOw4kgongPx2W5T+tOrUUUGVGnrqnQ=";
    private const string DifferentSigningKey = "Olols09EY8clhDSk3r92syj1xDOaChBbXNmTZI5HJo4=";

    private static JwtOptions MakeOptions(TimeSpan? lifetime = null) => new()
    {
        SigningKey = SigningKey,
        Issuer = "flowboard-api-tests",
        Audience = "flowboard-web-tests",
        Lifetime = lifetime ?? TimeSpan.FromMinutes(5),
    };

    private static User FixtureUser() => new()
    {
        Id = 1,
        PublicId = Guid.NewGuid(),
        Email = "tokentest@example.com",
        DisplayName = "Token Test",
    };

    private static TokenValidationParameters ValidationParameters(JwtOptions options, string? signingKeyOverride = null) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = options.Issuer,
        ValidateAudience = true,
        ValidAudience = options.Audience,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = JwtSigningKey.From(signingKeyOverride ?? options.SigningKey),
        ValidateLifetime = true,
        ClockSkew = TimeSpan.Zero,
    };

    [Fact]
    public async Task IssueToken_ThenValidate_Succeeds()
    {
        var options = MakeOptions();
        var service = new TokenService(Options.Create(options));
        var user = FixtureUser();

        var issued = service.IssueToken(user);

        var handler = new JsonWebTokenHandler();
        var result = await handler.ValidateTokenAsync(issued.Token, ValidationParameters(options));

        Assert.True(result.IsValid);
        Assert.Equal(user.PublicId.ToString(), result.ClaimsIdentity.FindFirst(JwtRegisteredClaimNames.Sub)?.Value);
        Assert.Equal(user.Email, result.ClaimsIdentity.FindFirst(JwtRegisteredClaimNames.Email)?.Value);
    }

    [Fact]
    public async Task IssueToken_Expired_FailsValidation()
    {
        var options = MakeOptions(TimeSpan.FromSeconds(-1));
        var service = new TokenService(Options.Create(options));

        var issued = service.IssueToken(FixtureUser());

        var handler = new JsonWebTokenHandler();
        var result = await handler.ValidateTokenAsync(issued.Token, ValidationParameters(options));

        Assert.False(result.IsValid);
    }

    [Fact]
    public async Task ValidateToken_WithTamperedSigningKey_Fails()
    {
        var options = MakeOptions();
        var service = new TokenService(Options.Create(options));

        var issued = service.IssueToken(FixtureUser());

        var handler = new JsonWebTokenHandler();
        var result = await handler.ValidateTokenAsync(
            issued.Token, ValidationParameters(options, signingKeyOverride: DifferentSigningKey));

        Assert.False(result.IsValid);
    }
}
