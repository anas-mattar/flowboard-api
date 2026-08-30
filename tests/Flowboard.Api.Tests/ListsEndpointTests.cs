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

        await db.SaveChangesAsync();

        await TestDataCleanup.RemoveUserOwnedDataAsync(db, _emails);
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

    private async Task<HttpClient> InvitedClientAsync(HttpClient ownerClient, Guid boardPublicId, string label, string role)
    {
        var signupClient = _factory.CreateClient();
        var invitee = await SignUpAsync(signupClient, label);
        var invite = await ownerClient.PostAsJsonAsync(
            $"/v1/boards/{boardPublicId}/invitations", new InviteRequestBody(invitee.User.Email, role));
        Assert.Equal(HttpStatusCode.Created, invite.StatusCode);

        var client = _factory.CreateClient();
        Authorize(client, await LogInAsync(client, invitee.User.Email, DefaultPassword));
        return client;
    }

    private static HttpRequestMessage PatchWithIfMatch(string url, object body, string rowVersionBase64)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, url) { Content = JsonContent.Create(body) };
        request.Headers.Add("If-Match", $"\"{rowVersionBase64}\"");
        return request;
    }

    private async Task<(HttpClient Client, BoardCreatedDto Board)> NewOwnerWithBoardAsync(string label)
    {
        var client = _factory.CreateClient();
        var owner = await SignUpAsync(client, label);
        Authorize(client, owner.Token);
        var response = await client.PostAsJsonAsync("/v1/boards", new CreateBoardRequestBody($"{label} Board"));
        var board = (await response.Content.ReadFromJsonAsync<BoardCreatedDto>())!;
        return (client, board);
    }

    private async Task<ListContentDto> GetListAsync(HttpClient client, Guid boardPublicId, Guid listPublicId) =>
        (await client.GetFromJsonAsync<BoardContentDto>($"/v1/boards/{boardPublicId}"))!
            .Lists.Single(l => l.PublicId == listPublicId);

    private async Task<Guid> CreateCardAsync(HttpClient client, Guid listPublicId, string title)
    {
        var response = await client.PostAsJsonAsync($"/v1/lists/{listPublicId}/cards", new CreateCardRequestBody(title));
        var created = await response.Content.ReadFromJsonAsync<CardSummaryDto>();
        return created!.PublicId;
    }

    private async Task SetDueAtAsync(HttpClient client, Guid cardPublicId, DateTime dueAt)
    {
        var getResponse = await client.GetAsync($"/v1/cards/{cardPublicId}");
        var rowVersion = getResponse.Headers.ETag!.Tag!.Trim('"');

        var request = new HttpRequestMessage(HttpMethod.Patch, $"/v1/cards/{cardPublicId}")
        {
            Content = JsonContent.Create(new { dueAt }),
        };
        request.Headers.Add("If-Match", $"\"{rowVersion}\"");
        var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

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

    // specs/006-board-list-management/contracts/board-list-management-api.md — POST /v1/boards/{id}/lists.

    [Fact]
    public async Task CreateList_LandsRightmost_Empty_NoWipLimit()
    {
        var (ownerClient, board) = await NewOwnerWithBoardAsync("CreateListOwner");
        using var client = ownerClient;

        var response = await client.PostAsJsonAsync($"/v1/boards/{board.PublicId}/lists", new CreateListRequestBody("Blocked"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<ListCreatedDto>();
        Assert.NotNull(created);
        Assert.Equal("Blocked", created!.Name);
        Assert.Null(created.WipLimit);
        Assert.Equal(0, created.CardCount);

        var content = await client.GetFromJsonAsync<BoardContentDto>($"/v1/boards/{board.PublicId}");
        Assert.Equal(["To Do", "Doing", "Done", "Blocked"], content!.Lists.Select(l => l.Name).ToArray());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateList_EmptyOrWhitespaceName_Returns400(string name)
    {
        var (ownerClient, board) = await NewOwnerWithBoardAsync("CreateListValidation");
        using var client = ownerClient;

        var response = await client.PostAsJsonAsync($"/v1/boards/{board.PublicId}/lists", new CreateListRequestBody(name));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateList_Observer_Returns403()
    {
        var (ownerClient, board) = await NewOwnerWithBoardAsync("CreateListOwnerObs");
        using var owner = ownerClient;
        using var observerClient = await InvitedClientAsync(owner, board.PublicId, "CreateListObserver", "Observer");

        var response = await observerClient.PostAsJsonAsync($"/v1/boards/{board.PublicId}/lists", new CreateListRequestBody("Blocked"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateList_NoBoardAccess_Returns404()
    {
        using var client = _factory.CreateClient();
        var caller = await SignUpAsync(client, "CreateListNoAccess");
        Authorize(client, caller.Token);

        var response = await client.PostAsJsonAsync($"/v1/boards/{Guid.NewGuid()}/lists", new CreateListRequestBody("Blocked"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // specs/006-board-list-management/contracts/board-list-management-api.md — PATCH /v1/lists/{id}.

    [Fact]
    public async Task UpdateList_RenameAndSetWipLimit_PersistsAndReturnsNewRowVersion()
    {
        var (ownerClient, board) = await NewOwnerWithBoardAsync("UpdateListOwner");
        using var client = ownerClient;
        var listId = board.Lists[0].PublicId;

        var before = await GetListAsync(client, board.PublicId, listId);
        var response = await client.SendAsync(PatchWithIfMatch(
            $"/v1/lists/{listId}", new { name = "In Progress", wipLimit = 3 }, before.RowVersion));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var after = await GetListAsync(client, board.PublicId, listId);
        Assert.Equal("In Progress", after.Name);
        Assert.Equal(3, after.WipLimit);
        Assert.NotEqual(before.RowVersion, after.RowVersion);

        // Clearing wipLimit to null persists and leaves the name untouched.
        var clearResponse = await client.SendAsync(PatchWithIfMatch(
            $"/v1/lists/{listId}", new { wipLimit = (int?)null }, after.RowVersion));
        Assert.Equal(HttpStatusCode.OK, clearResponse.StatusCode);

        var cleared = await GetListAsync(client, board.PublicId, listId);
        Assert.Null(cleared.WipLimit);
        Assert.Equal("In Progress", cleared.Name);
    }

    [Fact]
    public async Task UpdateList_WipLimitBelowCurrentCardCount_StillSucceeds_NeverTouchesCards()
    {
        var (ownerClient, board) = await NewOwnerWithBoardAsync("UpdateListWipBelowCount");
        using var client = ownerClient;
        var listId = board.Lists[0].PublicId;

        await CreateCardAsync(client, listId, "Card 1");
        await CreateCardAsync(client, listId, "Card 2");

        var before = await GetListAsync(client, board.PublicId, listId);
        var response = await client.SendAsync(PatchWithIfMatch($"/v1/lists/{listId}", new { wipLimit = 1 }, before.RowVersion));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var after = await GetListAsync(client, board.PublicId, listId);
        Assert.Equal(1, after.WipLimit);
        Assert.Equal(2, after.CardCount); // invariant 3: never blocks or removes a card
    }

    [Fact]
    public async Task UpdateList_NegativeWipLimit_Returns400()
    {
        var (ownerClient, board) = await NewOwnerWithBoardAsync("UpdateListNegativeWip");
        using var client = ownerClient;
        var listId = board.Lists[0].PublicId;
        var before = await GetListAsync(client, board.PublicId, listId);

        var response = await client.SendAsync(PatchWithIfMatch($"/v1/lists/{listId}", new { wipLimit = -1 }, before.RowVersion));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateList_NeitherFieldSupplied_Returns400()
    {
        var (ownerClient, board) = await NewOwnerWithBoardAsync("UpdateListNoFields");
        using var client = ownerClient;
        var listId = board.Lists[0].PublicId;
        var before = await GetListAsync(client, board.PublicId, listId);

        var response = await client.SendAsync(PatchWithIfMatch($"/v1/lists/{listId}", new { }, before.RowVersion));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateList_Observer_Returns403()
    {
        var (ownerClient, board) = await NewOwnerWithBoardAsync("UpdateListObserverOwner");
        using var owner = ownerClient;
        var listId = board.Lists[0].PublicId;
        var before = await GetListAsync(owner, board.PublicId, listId);

        using var observerClient = await InvitedClientAsync(owner, board.PublicId, "UpdateListObserver", "Observer");
        var response = await observerClient.SendAsync(
            PatchWithIfMatch($"/v1/lists/{listId}", new { name = "Renamed" }, before.RowVersion));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task UpdateList_StaleIfMatch_Returns409()
    {
        var (ownerClient, board) = await NewOwnerWithBoardAsync("UpdateListStale");
        using var client = ownerClient;
        var listId = board.Lists[0].PublicId;
        var before = await GetListAsync(client, board.PublicId, listId);

        var firstResponse = await client.SendAsync(PatchWithIfMatch($"/v1/lists/{listId}", new { name = "First Rename" }, before.RowVersion));
        Assert.Equal(HttpStatusCode.OK, firstResponse.StatusCode);

        var staleResponse = await client.SendAsync(
            PatchWithIfMatch($"/v1/lists/{listId}", new { name = "Second Rename" }, before.RowVersion));

        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);
    }

    // specs/006-board-list-management/contracts/board-list-management-api.md — archive-cards / DELETE.

    [Fact]
    public async Task ArchiveAllCards_EmptiesList_ListRemains_NoOpOnAlreadyEmptyList()
    {
        var (ownerClient, board) = await NewOwnerWithBoardAsync("ArchiveCardsOwner");
        using var client = ownerClient;
        var listId = board.Lists[0].PublicId;

        await CreateCardAsync(client, listId, "Card 1");
        await CreateCardAsync(client, listId, "Card 2");

        var response = await client.PostAsync($"/v1/lists/{listId}/archive-cards", content: null);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var afterArchive = await GetListAsync(client, board.PublicId, listId);
        Assert.Equal(0, afterArchive.CardCount);

        var content = await client.GetFromJsonAsync<BoardContentDto>($"/v1/boards/{board.PublicId}");
        Assert.Contains(content!.Lists, l => l.PublicId == listId);

        var secondResponse = await client.PostAsync($"/v1/lists/{listId}/archive-cards", content: null);
        Assert.Equal(HttpStatusCode.NoContent, secondResponse.StatusCode);
    }

    [Fact]
    public async Task DeleteList_WithCards_RemovesListAndCardsFromBoardContent()
    {
        var (ownerClient, board) = await NewOwnerWithBoardAsync("DeleteListOwner");
        using var client = ownerClient;
        var listId = board.Lists[0].PublicId;

        await CreateCardAsync(client, listId, "Card 1");

        var response = await client.DeleteAsync($"/v1/lists/{listId}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var content = await client.GetFromJsonAsync<BoardContentDto>($"/v1/boards/{board.PublicId}");
        Assert.DoesNotContain(content!.Lists, l => l.PublicId == listId);
        Assert.Equal(["Doing", "Done"], content.Lists.Select(l => l.Name).ToArray());
    }

    [Fact]
    public async Task ArchiveAllCardsAndDeleteList_Observer_Returns403()
    {
        var (ownerClient, board) = await NewOwnerWithBoardAsync("ListActionsObserverOwner");
        using var owner = ownerClient;
        var listId = board.Lists[0].PublicId;

        using var observerClient = await InvitedClientAsync(owner, board.PublicId, "ListActionsObserver", "Observer");

        var archiveResponse = await observerClient.PostAsync($"/v1/lists/{listId}/archive-cards", content: null);
        Assert.Equal(HttpStatusCode.Forbidden, archiveResponse.StatusCode);

        var deleteResponse = await observerClient.DeleteAsync($"/v1/lists/{listId}");
        Assert.Equal(HttpStatusCode.Forbidden, deleteResponse.StatusCode);
    }

    // specs/006-board-list-management/contracts/board-list-management-api.md — POST /v1/lists/{id}/sort.

    [Fact]
    public async Task SortByDueDate_AscendingUndatedLast_StableOnRepeat()
    {
        var (ownerClient, board) = await NewOwnerWithBoardAsync("SortOwner");
        using var client = ownerClient;
        var listId = board.Lists[0].PublicId;

        var cardC = await CreateCardAsync(client, listId, "No due date");
        var cardA = await CreateCardAsync(client, listId, "Due soonest");
        var cardB = await CreateCardAsync(client, listId, "Due later");

        await SetDueAtAsync(client, cardB, DateTime.UtcNow.AddDays(5));
        await SetDueAtAsync(client, cardA, DateTime.UtcNow.AddDays(1));

        var sortResponse = await client.PostAsync($"/v1/lists/{listId}/sort", content: null);
        Assert.Equal(HttpStatusCode.NoContent, sortResponse.StatusCode);

        var content = await client.GetFromJsonAsync<BoardContentDto>($"/v1/boards/{board.PublicId}");
        var order = content!.Lists.Single(l => l.PublicId == listId).Cards.Select(c => c.PublicId).ToArray();
        Assert.Equal([cardA, cardB, cardC], order);

        // Triggering the sort again with nothing changed produces an identical order.
        var secondSortResponse = await client.PostAsync($"/v1/lists/{listId}/sort", content: null);
        Assert.Equal(HttpStatusCode.NoContent, secondSortResponse.StatusCode);

        var contentAfterSecondSort = await client.GetFromJsonAsync<BoardContentDto>($"/v1/boards/{board.PublicId}");
        var orderAfterSecondSort = contentAfterSecondSort!.Lists.Single(l => l.PublicId == listId).Cards.Select(c => c.PublicId).ToArray();
        Assert.Equal(order, orderAfterSecondSort);
    }

    [Fact]
    public async Task SortByDueDate_Observer_Returns403()
    {
        var (ownerClient, board) = await NewOwnerWithBoardAsync("SortObserverOwner");
        using var owner = ownerClient;
        var listId = board.Lists[0].PublicId;

        using var observerClient = await InvitedClientAsync(owner, board.PublicId, "SortObserver", "Observer");
        var response = await observerClient.PostAsync($"/v1/lists/{listId}/sort", content: null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
