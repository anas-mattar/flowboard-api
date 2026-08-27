// Verifies the provider side of specs/002-auth-workspaces/contracts/board-membership-api.md.
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Flowboard.Api.Data;
using Flowboard.Api.Data.Configurations;
using Flowboard.Api.Endpoints;
using Flowboard.Api.Services;
using Flowboard.Api.Tests.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flowboard.Api.Tests;

[Collection(FlowboardApiCollection.Name)]
public sealed class BoardMembersEndpointTests : IAsyncLifetime
{
    private const string FixtureOwnerEmail = UserConfiguration.FixtureOwnerEmail;
    private const string FixtureOwnerPassword = "FixtureOwner!2026";
    private const string DefaultPassword = "correct horse battery staple";
    private static readonly Guid FixtureBoardPublicId = BoardConfiguration.FixtureBoardPublicId;

    private readonly FlowboardApiFactory _factory;
    private readonly List<string> _emails = [];

    public BoardMembersEndpointTests(FlowboardApiFactory factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowboardDbContext>();

        var users = await db.Users.Where(u => _emails.Contains(u.Email)).ToListAsync();
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

    private static void Authorize(HttpClient client, string token) =>
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

    private async Task<AuthResponse> SignUpAsync(HttpClient client, string label)
    {
        var email = NewEmail(label);
        var response = await client.PostAsJsonAsync(
            "/v1/auth/signup", new SignupRequestBody(email, DefaultPassword, label));
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    private async Task<string> LogInAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/v1/auth/login", new LoginRequestBody(email, password));
        return (await response.Content.ReadFromJsonAsync<AuthResponse>())!.Token;
    }

    private async Task<HttpClient> FixtureOwnerClientAsync()
    {
        var client = _factory.CreateClient();
        Authorize(client, await LogInAsync(client, FixtureOwnerEmail, FixtureOwnerPassword));
        return client;
    }

    [Fact]
    public async Task Invite_ExistingUser_CreatesImmediateMembership()
    {
        using var ownerClient = await FixtureOwnerClientAsync();
        using var inviteeSignupClient = _factory.CreateClient();
        var invitee = await SignUpAsync(inviteeSignupClient, "InviteExisting");

        var response = await ownerClient.PostAsJsonAsync(
            $"/v1/boards/{FixtureBoardPublicId}/invitations", new InviteRequestBody(invitee.User.Email, "BoardMember"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var member = await response.Content.ReadFromJsonAsync<MemberDto>();
        Assert.NotNull(member);
        Assert.Equal(invitee.User.PublicId, member!.User.PublicId);
        Assert.Equal("BoardMember", member.Role);
        Assert.False(member.IsWorkspaceOwner);
    }

    [Fact]
    public async Task Invite_UnregisteredEmail_CreatesPendingInvitation()
    {
        using var ownerClient = await FixtureOwnerClientAsync();
        var email = NewEmail("invite-pending");

        var response = await ownerClient.PostAsJsonAsync(
            $"/v1/boards/{FixtureBoardPublicId}/invitations", new InviteRequestBody(email, "Observer"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var pending = await response.Content.ReadFromJsonAsync<PendingInvitationDto>();
        Assert.NotNull(pending);
        Assert.Equal(email, pending!.Email);
        Assert.Equal("Observer", pending.Role);
    }

    [Fact]
    public async Task Invite_AlreadyBoardMember_Returns409()
    {
        using var ownerClient = await FixtureOwnerClientAsync();
        using var inviteeSignupClient = _factory.CreateClient();
        var invitee = await SignUpAsync(inviteeSignupClient, "InviteDuplicate");

        var first = await ownerClient.PostAsJsonAsync(
            $"/v1/boards/{FixtureBoardPublicId}/invitations", new InviteRequestBody(invitee.User.Email, "BoardMember"));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await ownerClient.PostAsJsonAsync(
            $"/v1/boards/{FixtureBoardPublicId}/invitations", new InviteRequestBody(invitee.User.Email, "Observer"));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Invite_SamePendingEmailTwice_UpdatesRoleInPlace()
    {
        using var ownerClient = await FixtureOwnerClientAsync();
        var email = NewEmail("invite-reinvite");

        var first = await ownerClient.PostAsJsonAsync(
            $"/v1/boards/{FixtureBoardPublicId}/invitations", new InviteRequestBody(email, "Observer"));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstBody = await first.Content.ReadFromJsonAsync<PendingInvitationDto>();

        var second = await ownerClient.PostAsJsonAsync(
            $"/v1/boards/{FixtureBoardPublicId}/invitations", new InviteRequestBody(email, "BoardMember"));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondBody = await second.Content.ReadFromJsonAsync<PendingInvitationDto>();

        Assert.Equal(firstBody!.PublicId, secondBody!.PublicId);
        Assert.Equal("BoardMember", secondBody.Role);
    }

    [Fact]
    public async Task NonMember_Gets404OnEveryBoardScopedEndpoint()
    {
        using var client = _factory.CreateClient();
        var caller = await SignUpAsync(client, "NonMember");
        Authorize(client, caller.Token);

        var membersResponse = await client.GetAsync($"/v1/boards/{FixtureBoardPublicId}/members");
        Assert.Equal(HttpStatusCode.NotFound, membersResponse.StatusCode);

        var inviteResponse = await client.PostAsJsonAsync(
            $"/v1/boards/{FixtureBoardPublicId}/invitations",
            new InviteRequestBody(NewEmail("non-member-invitee"), "Observer"));
        Assert.Equal(HttpStatusCode.NotFound, inviteResponse.StatusCode);

        var removeResponse = await client.DeleteAsync($"/v1/boards/{FixtureBoardPublicId}/members/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, removeResponse.StatusCode);
    }

    [Fact]
    public async Task ObserverRole_Gets403OnInviteAndRemove()
    {
        using var ownerClient = await FixtureOwnerClientAsync();
        using var observerSignupClient = _factory.CreateClient();
        var observer = await SignUpAsync(observerSignupClient, "ObserverRole");

        var invite = await ownerClient.PostAsJsonAsync(
            $"/v1/boards/{FixtureBoardPublicId}/invitations", new InviteRequestBody(observer.User.Email, "Observer"));
        Assert.Equal(HttpStatusCode.Created, invite.StatusCode);

        using var observerClient = _factory.CreateClient();
        Authorize(observerClient, await LogInAsync(observerClient, observer.User.Email, DefaultPassword));

        var observerInvite = await observerClient.PostAsJsonAsync(
            $"/v1/boards/{FixtureBoardPublicId}/invitations",
            new InviteRequestBody(NewEmail("observer-cant-invite"), "Observer"));
        Assert.Equal(HttpStatusCode.Forbidden, observerInvite.StatusCode);

        var observerRemove = await observerClient.DeleteAsync(
            $"/v1/boards/{FixtureBoardPublicId}/members/{observer.User.PublicId}");
        Assert.Equal(HttpStatusCode.Forbidden, observerRemove.StatusCode);
    }

    [Fact]
    public async Task WorkspaceOwner_HasImplicitBoardAdminAccessWithNoBoardMemberRow()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowboardDbContext>();
        var fixtureOwner = await db.Users.AsNoTracking().SingleAsync(u => u.Email == FixtureOwnerEmail);

        var hasMemberRow = await db.BoardMembers.AnyAsync(
            m => m.BoardId == BoardConfiguration.FixtureBoardId && m.UserId == fixtureOwner.Id);
        Assert.False(hasMemberRow);

        using var ownerClient = await FixtureOwnerClientAsync();
        var response = await ownerClient.GetAsync($"/v1/boards/{FixtureBoardPublicId}/members");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<MembersResponse>();
        Assert.Contains(body!.Members, m => m.User.PublicId == fixtureOwner.PublicId && m.IsWorkspaceOwner);
    }

    [Fact]
    public async Task RemovingMember_TakesEffectOnVeryNextRequest_NotJustAfterFreshLogin()
    {
        using var ownerClient = await FixtureOwnerClientAsync();
        using var memberSignupClient = _factory.CreateClient();
        var member = await SignUpAsync(memberSignupClient, "LiveRevoke");

        var invite = await ownerClient.PostAsJsonAsync(
            $"/v1/boards/{FixtureBoardPublicId}/invitations", new InviteRequestBody(member.User.Email, "BoardAdmin"));
        Assert.Equal(HttpStatusCode.Created, invite.StatusCode);

        using var memberClient = _factory.CreateClient();
        Authorize(memberClient, await LogInAsync(memberClient, member.User.Email, DefaultPassword));

        // Proves the elevated BoardAdmin role is live before removal.
        var canInvite = await memberClient.PostAsJsonAsync(
            $"/v1/boards/{FixtureBoardPublicId}/invitations",
            new InviteRequestBody(NewEmail("live-revoke-target"), "Observer"));
        Assert.Equal(HttpStatusCode.Created, canInvite.StatusCode);

        var remove = await ownerClient.DeleteAsync($"/v1/boards/{FixtureBoardPublicId}/members/{member.User.PublicId}");
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);

        // Same still-valid JWT, no fresh login: access must already be gone.
        var afterRemoval = await memberClient.GetAsync($"/v1/boards/{FixtureBoardPublicId}/members");
        Assert.Equal(HttpStatusCode.NotFound, afterRemoval.StatusCode);
    }
}
