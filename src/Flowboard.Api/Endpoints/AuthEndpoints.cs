// Provider side of specs/002-auth-workspaces/contracts/auth-api.md.
using Flowboard.Api.Domain;
using Flowboard.Api.Services;

namespace Flowboard.Api.Endpoints;

public sealed record SignupRequestBody(string Email, string Password, string DisplayName);

public sealed record LoginRequestBody(string Email, string Password);

public static class AuthEndpoints
{
    private const int MinPasswordLength = 10;

    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/v1/auth")
            .WithTags("Auth")
            .AllowAnonymous();

        group.MapPost("/signup", Signup).RequireRateLimiting("auth-signup");
        group.MapPost("/login", Login).RequireRateLimiting("auth-login");

        return endpoints;
    }

    private static async Task<IResult> Signup(
        SignupRequestBody body, IAuthService authService, CancellationToken cancellationToken)
    {
        var validationFailure = ValidateSignup(body);
        if (validationFailure is not null)
        {
            return validationFailure.ToHttpResult();
        }

        var result = await authService.SignUpAsync(
            new SignupRequest(body.Email, body.Password, body.DisplayName), cancellationToken);

        return result.ToHttpResult(response => Results.Json(response, statusCode: StatusCodes.Status201Created));
    }

    private static async Task<IResult> Login(
        LoginRequestBody body, IAuthService authService, CancellationToken cancellationToken)
    {
        var result = await authService.LogInAsync(new LoginRequest(body.Email, body.Password), cancellationToken);
        return result.ToHttpResult(Results.Ok);
    }

    private static Failure? ValidateSignup(SignupRequestBody body)
    {
        var errors = new Dictionary<string, string[]>();

        if (string.IsNullOrWhiteSpace(body.Email) || !body.Email.Contains('@') || body.Email.Length > 320)
        {
            errors["email"] = ["A valid email address is required."];
        }

        if (string.IsNullOrEmpty(body.Password) || body.Password.Length < MinPasswordLength)
        {
            errors["password"] = [$"Password must be at least {MinPasswordLength} characters."];
        }

        if (string.IsNullOrWhiteSpace(body.DisplayName) || body.DisplayName.Length > 100)
        {
            errors["displayName"] = ["Display name is required and must be 100 characters or fewer."];
        }

        return errors.Count == 0 ? null : Failure.Validation("Validation failed", errors);
    }
}
