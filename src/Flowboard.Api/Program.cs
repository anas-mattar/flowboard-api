using System.Threading.RateLimiting;
using Flowboard.Api.Data;
using Flowboard.Api.Endpoints;
using Flowboard.Api.Hubs;
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
builder.Services.AddScoped<IBoardContentService, BoardContentService>();
builder.Services.AddScoped<ICardService, CardService>();
builder.Services.AddScoped<IListService, ListService>();

// plan.md ADR-40: local-disk-backed for now (research.md R-1) — no cloud object-store
// account/config exists anywhere in this project yet. Stateless, matching TokenService's
// singleton shape.
builder.Services.AddSingleton<IAttachmentStorage, LocalDiskAttachmentStorage>();

// plan.md ADR-32/ADR-33: this project's first realtime code — a per-board SignalR hub
// plus the singleton connection tracker and publish/evict surface every mutating service
// method calls after its own SaveChangesAsync() (research.md R-4/R-8: no backplane).
builder.Services.AddSignalR();
builder.Services.AddSingleton<BoardConnectionTracker>();
builder.Services.AddSingleton<IBoardEventPublisher, BoardEventPublisher>();

// ADR-33's browser-to-hub connection is cross-origin in every real deployment (the
// frontend's own host/port, distinct from this API) — unlike every other route, which the
// browser only ever reaches through the same-origin Next.js BFF. Scoped to the hub
// endpoint only (see app.MapHub below), not applied to any REST route.
var realtimeCorsOrigin = builder.Configuration["Cors:RealtimeOrigin"] ?? "http://localhost:3000";
builder.Services.AddCors(options =>
{
    options.AddPolicy("Realtime", policy => policy
        .WithOrigins(realtimeCorsOrigin)
        .AllowAnyHeader()
        .WithMethods("GET", "POST"));
});

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

        // contracts/realtime-api.md: browser WebSocket/SSE transports cannot carry an
        // Authorization header, so the SignalR JS client's accessTokenFactory instead
        // supplies the token as an access_token query-string value — the standard
        // mechanism for this, scoped to only the hub's own path.
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(accessToken)
                    && context.HttpContext.Request.Path.StartsWithSegments("/hubs/board"))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            },
        };
    });
builder.Services.AddAuthorization(options =>
{
    // BoardHub's connection policy: requires the realtime token's purpose claim, which a
    // normal 14-day session JWT never carries — rejected at the hub handshake, never
    // reaching a hub method (data-model.md's RealtimeTokenClaims, research.md R-3).
    options.AddPolicy("RealtimeOnly", policy => policy.RequireClaim(TokenService.PurposeClaimType, TokenService.RealtimeTokenPurpose));
});

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

app.UseCors();

app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

app.MapHealthEndpoints();
app.MapAuthEndpoints();
app.MapBoardMembersEndpoints();
app.MapBoardsEndpoints();
app.MapCardsEndpoints();
app.MapAttachmentsEndpoints();
app.MapListsEndpoints();
app.MapHub<BoardHub>("/hubs/board").RequireAuthorization("RealtimeOnly").RequireCors("Realtime");

app.Run();

// Exposes the entry point to WebApplicationFactory-based integration tests.
public partial class Program { }
