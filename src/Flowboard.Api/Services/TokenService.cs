// plan.md ADR-7 / research.md R-3, R-6: identity-only claims (sub, email, iat, exp) — no
// workspace/board/role claims. Authorization is always re-derived per request
// (BoardAccessService), never trusted from a cached claim.
using System.Security.Claims;
using Flowboard.Api.Domain.Entities;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Flowboard.Api.Services;

public sealed record IssuedToken(string Token, DateTime ExpiresAtUtc);

public interface ITokenService
{
    IssuedToken IssueToken(User user);
}

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string SigningKey { get; set; } = string.Empty;

    public string Issuer { get; set; } = "flowboard-api";

    public string Audience { get; set; } = "flowboard-web";

    public TimeSpan Lifetime { get; set; } = TimeSpan.FromDays(14);
}

public sealed class TokenService(IOptions<JwtOptions> options) : ITokenService
{
    private readonly JwtOptions _options = options.Value;

    public IssuedToken IssueToken(User user)
    {
        var now = DateTime.UtcNow;
        var expiresAtUtc = now.Add(_options.Lifetime);

        var handler = new JsonWebTokenHandler();
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _options.Issuer,
            Audience = _options.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expiresAtUtc,
            SigningCredentials = new SigningCredentials(
                JwtSigningKey.From(_options.SigningKey), SecurityAlgorithms.HmacSha256),
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = user.PublicId.ToString(),
                [JwtRegisteredClaimNames.Email] = user.Email,
            },
        };

        var token = handler.CreateToken(descriptor);
        return new IssuedToken(token, expiresAtUtc);
    }
}

/// <summary>Builds the same signing key from config for both issuance (here) and the
/// JwtBearer validation middleware (Program.cs) — one source of truth.</summary>
public static class JwtSigningKey
{
    public static SymmetricSecurityKey From(string base64Key) =>
        new(Convert.FromBase64String(base64Key));
}

public static class ClaimsPrincipalExtensions
{
    public static Guid? GetUserPublicId(this ClaimsPrincipal principal)
    {
        var sub = principal.FindFirstValue(JwtRegisteredClaimNames.Sub);
        return Guid.TryParse(sub, out var publicId) ? publicId : null;
    }
}
