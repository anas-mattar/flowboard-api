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

    /// <summary>data-model.md's RealtimeTokenClaims — a short-lived, board-scoped token
    /// for the SignalR hub connection only (research.md R-3), never the caller's 14-day
    /// backend session JWT.</summary>
    IssuedToken IssueRealtimeToken(Guid userPublicId, Guid boardPublicId);
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
    // data-model.md / research.md R-3/R-7: fixed at 2 minutes, not configurable per
    // environment — bounds access-revocation staleness without needing a revocation list.
    private static readonly TimeSpan RealtimeTokenLifetime = TimeSpan.FromMinutes(2);

    public const string RealtimeTokenPurpose = "realtime";
    public const string BoardIdClaimType = "boardId";
    public const string PurposeClaimType = "purpose";

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

    public IssuedToken IssueRealtimeToken(Guid userPublicId, Guid boardPublicId)
    {
        var now = DateTime.UtcNow;
        var expiresAtUtc = now.Add(RealtimeTokenLifetime);

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
                [JwtRegisteredClaimNames.Sub] = userPublicId.ToString(),
                [BoardIdClaimType] = boardPublicId.ToString(),
                [PurposeClaimType] = RealtimeTokenPurpose,
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

    /// <summary>The realtime token's boardId claim (TokenService.IssueRealtimeToken) — null
    /// for a normal 14-day session JWT, which never carries this claim.</summary>
    public static Guid? GetRealtimeBoardId(this ClaimsPrincipal principal)
    {
        var boardId = principal.FindFirstValue(TokenService.BoardIdClaimType);
        return Guid.TryParse(boardId, out var parsed) ? parsed : null;
    }
}
