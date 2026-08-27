using System.Threading.RateLimiting;
using Flowboard.Api.Data;
using Flowboard.Api.Endpoints;
using Flowboard.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddSingleton<IHealthService, HealthService>();

// plan.md ADR-6: single DbContext in this project.
builder.Services.AddDbContext<FlowboardDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("Flowboard")));

// plan.md ADR-7: FlowBoard-issued JWT bearer tokens; ADR-9: per-request authorization.
builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection(JwtOptions.SectionName));
builder.Services.AddSingleton<IPasswordHasher, PasswordHasher>();
builder.Services.AddSingleton<ITokenService, TokenService>();
builder.Services.AddScoped<IBoardAccessService, BoardAccessService>();
builder.Services.AddScoped<IAuthService, AuthService>();
builder.Services.AddScoped<IBoardMembershipService, BoardMembershipService>();

var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
    ?? new JwtOptions();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        // Without this, the handler's legacy inbound claim mapping rewrites "sub"/"email"
        // into long XML-schema claim types, breaking ClaimsPrincipalExtensions.GetUserPublicId()
        // (TokenService issues short claim names; ADR-7).
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = JwtSigningKey.From(jwtOptions.SigningKey),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1),
        };
    });
builder.Services.AddAuthorization();

// backend-security.md §12: rate limit login AND signup, partitioned per client IP — a
// single fixed window with no partition key would let one caller's traffic lock every
// other caller out. Limits are configurable so the test host (no real per-connection IP
// under WebApplicationFactory) can raise them without touching production defaults.
// Second-model review H1: signup was originally unlimited despite contracts/auth-api.md
// requiring it — an unlimited signup is both an email-enumeration oracle (409 "already in
// use") and a BCrypt-work-factor-12 CPU-exhaustion vector.
var loginRateLimit = builder.Configuration.GetSection("RateLimiting:Login");
var loginPermitLimit = loginRateLimit.GetValue("PermitLimit", 10);
var loginWindowSeconds = loginRateLimit.GetValue("WindowSeconds", 60);

var signupRateLimit = builder.Configuration.GetSection("RateLimiting:Signup");
var signupPermitLimit = signupRateLimit.GetValue("PermitLimit", 10);
var signupWindowSeconds = signupRateLimit.GetValue("WindowSeconds", 60);

builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("auth-login", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromSeconds(loginWindowSeconds),
            PermitLimit = loginPermitLimit,
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        }));
    options.AddPolicy("auth-signup", httpContext => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            Window = TimeSpan.FromSeconds(signupWindowSeconds),
            PermitLimit = signupPermitLimit,
            QueueLimit = 0,
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
        }));
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapHealthEndpoints();
app.MapAuthEndpoints();
app.MapBoardMembersEndpoints();

app.Run();

// Exposes the entry point to WebApplicationFactory-based integration tests.
public partial class Program { }
