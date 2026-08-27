// Verifies the provider side of specs/002-auth-workspaces/contracts/auth-api.md.
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Flowboard.Api.Data;
using Flowboard.Api.Data.Configurations;
using Flowboard.Api.Domain.Entities;
using Flowboard.Api.Endpoints;
using Flowboard.Api.Services;
using Flowboard.Api.Tests.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flowboard.Api.Tests;

[Collection(FlowboardApiCollection.Name)]
public sealed class AuthEndpointTests : IAsyncLifetime
{
    private readonly FlowboardApiFactory _factory;
    private readonly List<string> _emails = [];

    public AuthEndpointTests(FlowboardApiFactory factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowboardDbContext>();

        var users = await db.Users.Where(u => _emails.Contains(u.Email)).ToListAsync();
        if (users.Count == 0)
        {
            return;
        }

        var userIds = users.Select(u => u.Id).ToList();
        db.BoardMembers.RemoveRange(await db.BoardMembers.Where(m => userIds.Contains(m.UserId)).ToListAsync());
        db.Invitations.RemoveRange(await db.Invitations.Where(i => _emails.Contains(i.Email)).ToListAsync());
        db.Workspaces.RemoveRange(await db.Workspaces.Where(w => userIds.Contains(w.OwnerUserId)).ToListAsync());
        db.Users.RemoveRange(users);
        await db.SaveChangesAsync();
    }

    private string NewEmail(string label)
    {
        var email = $"{label}-{Guid.NewGuid():N}@test.local";
        _emails.Add(email);
        return email;
    }

    [Fact]
    public async Task Signup_WithNewEmail_Returns201WithContractShape()
    {
        using var client = _factory.CreateClient();
        var email = NewEmail("signup-success");

        var response = await client.PostAsJsonAsync(
            "/v1/auth/signup", new SignupRequestBody(email, "correct horse battery staple", "Signup Success"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(body);
        Assert.Equal(email, body!.User.Email);
        Assert.Equal("Signup Success", body.User.DisplayName);
        Assert.NotEqual(Guid.Empty, body.User.PublicId);
        Assert.Equal(WorkspaceDto.WorkspaceAdminRole, body.Workspace.Role);
        Assert.False(string.IsNullOrWhiteSpace(body.Token));
        Assert.True(body.ExpiresAtUtc > DateTime.UtcNow);
    }

    [Fact]
    public async Task Signup_WithEmailAlreadyRegistered_Returns409()
    {
        using var client = _factory.CreateClient();
        var email = NewEmail("signup-duplicate");

        var first = await client.PostAsJsonAsync(
            "/v1/auth/signup", new SignupRequestBody(email, "correct horse battery staple", "First"));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await client.PostAsJsonAsync(
            "/v1/auth/signup", new SignupRequestBody(email, "another password entirely", "Second"));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Theory]
    [InlineData("not-an-email", "correct horse battery staple", "Valid Name")]
    [InlineData("valid@example.com", "short", "Valid Name")]
    [InlineData("valid@example.com", "correct horse battery staple", "")]
    public async Task Signup_WithInvalidInput_Returns400(string email, string password, string displayName)
    {
        using var client = _factory.CreateClient();
        if (email.Contains('@'))
        {
            // Defensive cleanup registration in case a validation bug lets this through.
            _emails.Add(email);
        }

        var response = await client.PostAsJsonAsync(
            "/v1/auth/signup", new SignupRequestBody(email, password, displayName));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithCorrectCredentials_Returns200()
    {
        using var client = _factory.CreateClient();
        var email = NewEmail("login-success");
        const string password = "correct horse battery staple";

        await client.PostAsJsonAsync("/v1/auth/signup", new SignupRequestBody(email, password, "Login Success"));

        var response = await client.PostAsJsonAsync("/v1/auth/login", new LoginRequestBody(email, password));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.Equal(email, body!.User.Email);
    }

    [Fact]
    public async Task Login_WithWrongPasswordOrUnknownEmail_ReturnsIdentical401()
    {
        using var client = _factory.CreateClient();
        var email = NewEmail("login-wrong-password");
        const string password = "correct horse battery staple";

        await client.PostAsJsonAsync(
            "/v1/auth/signup", new SignupRequestBody(email, password, "Login Wrong Password"));

        var wrongPassword = await client.PostAsJsonAsync(
            "/v1/auth/login", new LoginRequestBody(email, "not the right password"));
        var unknownEmail = await client.PostAsJsonAsync(
            "/v1/auth/login", new LoginRequestBody($"nobody-{Guid.NewGuid():N}@test.local", password));

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmail.StatusCode);

        using var wrongDoc = JsonDocument.Parse(await wrongPassword.Content.ReadAsStringAsync());
        using var unknownDoc = JsonDocument.Parse(await unknownEmail.Content.ReadAsStringAsync());
        Assert.Equal(
            wrongDoc.RootElement.GetProperty("title").GetString(),
            unknownDoc.RootElement.GetProperty("title").GetString());
    }

    [Fact]
    public async Task Signup_CreatesExactlyOneWorkspaceOwnedByCaller_WithNoExtraBoards()
    {
        using var client = _factory.CreateClient();
        var email = NewEmail("workspace-provisioning");

        var response = await client.PostAsJsonAsync(
            "/v1/auth/signup", new SignupRequestBody(email, "correct horse battery staple", "Workspace Owner"));
        var body = await response.Content.ReadFromJsonAsync<AuthResponse>();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowboardDbContext>();

        var user = await db.Users.SingleAsync(u => u.Email == email);
        var workspaces = await db.Workspaces.Where(w => w.OwnerUserId == user.Id).ToListAsync();

        Assert.Single(workspaces);
        Assert.Equal(body!.Workspace.PublicId, workspaces[0].PublicId);
        Assert.Equal(WorkspaceDto.WorkspaceAdminRole, body.Workspace.Role);

        // Zero boards in the new workspace — the fixture seed board lives in its own
        // separate seeded workspace (ADR-11), not the caller's.
        var boards = await db.Boards.Where(b => b.WorkspaceId == workspaces[0].Id).ToListAsync();
        Assert.Empty(boards);
    }

    [Fact]
    public async Task Signup_WithPendingInvitationForEmail_ClaimsItAsBoardMember()
    {
        var email = NewEmail("claim-invitation");

        using var ownerClient = _factory.CreateClient();
        var ownerLogin = await ownerClient.PostAsJsonAsync(
            "/v1/auth/login", new LoginRequestBody(UserConfiguration.FixtureOwnerEmail, "FixtureOwner!2026"));
        var ownerToken = (await ownerLogin.Content.ReadFromJsonAsync<AuthResponse>())!.Token;
        ownerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", ownerToken);

        var invite = await ownerClient.PostAsJsonAsync(
            $"/v1/boards/{BoardConfiguration.FixtureBoardPublicId}/invitations",
            new InviteRequestBody(email, "BoardMember"));
        Assert.Equal(HttpStatusCode.Created, invite.StatusCode);

        using var signupClient = _factory.CreateClient();
        var signup = await signupClient.PostAsJsonAsync(
            "/v1/auth/signup", new SignupRequestBody(email, "correct horse battery staple", "Claim Invitation"));
        Assert.Equal(HttpStatusCode.Created, signup.StatusCode);
        var signedUp = await signup.Content.ReadFromJsonAsync<AuthResponse>();

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowboardDbContext>();

        var user = await db.Users.SingleAsync(u => u.Email == email);
        var member = await db.BoardMembers.SingleOrDefaultAsync(
            m => m.BoardId == BoardConfiguration.FixtureBoardId && m.UserId == user.Id);
        Assert.NotNull(member);
        Assert.Equal(BoardRole.BoardMember, member!.Role);

        var invitation = await db.Invitations.SingleAsync(
            i => i.BoardId == BoardConfiguration.FixtureBoardId && i.Email == email);
        Assert.Equal(InvitationStatus.Accepted, invitation.Status);
        Assert.Equal(signedUp!.User.PublicId, user.PublicId);
    }
}
