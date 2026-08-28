// Verifies the provider side of specs/005-drag-drop-ordering/contracts/move-api.md.
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
public sealed class ListsEndpointTests : IAsyncLifetime
{
    private const string FixtureOwnerEmail = UserConfiguration.FixtureOwnerEmail;
    private const string FixtureOwnerPassword = "FixtureOwner!2026";
    private const string DefaultPassword = "correct horse battery staple";
    private static readonly Guid ProductRoadmapBoardPublicId = BoardConfiguration.ProductRoadmapBoardPublicId;
    private static readonly Guid BacklogPublicId = ListConfiguration.BacklogPublicId;
    private static readonly Guid DesignPublicId = ListConfiguration.DesignPublicId;
    private static readonly Guid InProgressPublicId = ListConfiguration.InProgressPublicId;
    private static readonly Guid ReviewPublicId = ListConfiguration.ReviewPublicId;

    private readonly FlowboardApiFactory _factory;
    private readonly List<string> _emails = [];

    public ListsEndpointTests(FlowboardApiFactory factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowboardDbContext>();

        // This class is the only one that ever moves a seeded list's Position — restore the
        // fixed seed values (ListConfiguration's own HasData) so other test classes sharing
        // this database (e.g. BoardsEndpointTests's stored-order assertion) are unaffected.
        var lists = await db.Lists
            .Where(l => l.BoardId == BoardConfiguration.ProductRoadmapBoardId)
            .ToListAsync();
        foreach (var list in lists)
        {
            list.Position = list.Id switch
            {
                ListConfiguration.BacklogId => 1000,
                ListConfiguration.DesignId => 2000,
                ListConfiguration.InProgressId => 3000,
                ListConfiguration.ReviewId => 4000,
                _ => list.Position,
            };
        }

        var users = await db.Users.Where(u => _emails.Contains(u.Email)).ToListAsync();
        var userIds = users.Select(u => u.Id).ToList();
        db.BoardMembers.RemoveRange(await db.BoardMembers.Where(m => userIds.Contains(m.UserId)).ToListAsync());
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

    private async Task<HttpClient> InvitedClientAsync(HttpClient ownerClient, string label, string role)
    {
        var signupClient = _factory.CreateClient();
        var invitee = await SignUpAsync(signupClient, label);
        var invite = await ownerClient.PostAsJsonAsync(
            $"/v1/boards/{ProductRoadmapBoardPublicId}/invitations", new InviteRequestBody(invitee.User.Email, role));
        Assert.Equal(HttpStatusCode.Created, invite.StatusCode);

        var client = _factory.CreateClient();
        Authorize(client, await LogInAsync(client, invitee.User.Email, DefaultPassword));
        return client;
    }

    private async Task<IReadOnlyList<Guid>> GetListOrderAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<BoardContentDto>($"/v1/boards/{ProductRoadmapBoardPublicId}"))!
            .Lists.Select(l => l.PublicId).ToList();

    [Fact]
    public async Task MoveList_ToNewPosition_PersistsForSubsequentFetch()
    {
        using var ownerClient = await FixtureOwnerClientAsync();

        // Backlog, Design, InProgress, Review is the seeded order; move Review to the front.
        var response = await ownerClient.PostAsJsonAsync(
            $"/v1/lists/{ReviewPublicId}/move", new MoveListRequestBody(BacklogPublicId));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var order = await GetListOrderAsync(ownerClient);
        Assert.Equal([ReviewPublicId, BacklogPublicId, DesignPublicId, InProgressPublicId], order);
    }

    [Fact]
    public async Task MoveList_BackToOwnStartingPosition_IsANoOp()
    {
        using var ownerClient = await FixtureOwnerClientAsync();
        var before = await GetListOrderAsync(ownerClient);

        // Design is already immediately before InProgress — asking to land there again must
        // not change the observable order.
        var response = await ownerClient.PostAsJsonAsync(
            $"/v1/lists/{DesignPublicId}/move", new MoveListRequestBody(InProgressPublicId));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var after = await GetListOrderAsync(ownerClient);
        Assert.Equal(before, after);
    }

    [Fact]
    public async Task MoveList_BeforeListFromDifferentBoard_Returns400()
    {
        using var ownerClient = await FixtureOwnerClientAsync();

        var response = await ownerClient.PostAsJsonAsync(
            $"/v1/lists/{BacklogPublicId}/move", new MoveListRequestBody(ListConfiguration.MarketingToDoPublicId));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MoveList_NonMember_Returns404()
    {
        using var client = _factory.CreateClient();
        var caller = await SignUpAsync(client, "NonMemberMoveList");
        Authorize(client, caller.Token);

        var response = await client.PostAsJsonAsync(
            $"/v1/lists/{BacklogPublicId}/move", new MoveListRequestBody(null));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task MoveList_Observer_Returns403()
    {
        using var ownerClient = await FixtureOwnerClientAsync();
        using var observerClient = await InvitedClientAsync(ownerClient, "MoveListObserver", "Observer");

        var response = await observerClient.PostAsJsonAsync(
            $"/v1/lists/{BacklogPublicId}/move", new MoveListRequestBody(null));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
