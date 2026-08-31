// Verifies the provider side of specs/003-board-view-readonly/contracts/board-content-api.md
// and specs/007-search-filter/contracts/search-filter-addendum.md.
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Flowboard.Api.Data;
using Flowboard.Api.Data.Configurations;
using Flowboard.Api.Domain;
using Flowboard.Api.Endpoints;
using Flowboard.Api.Services;
using Flowboard.Api.Tests.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flowboard.Api.Tests;

[Collection(FlowboardApiCollection.Name)]
public sealed class BoardsEndpointTests : IAsyncLifetime
{
    private const string FixtureOwnerEmail = UserConfiguration.FixtureOwnerEmail;
    private const string FixtureOwnerPassword = "FixtureOwner!2026";
    private const string DefaultPassword = "correct horse battery staple";
    private static readonly Guid FixtureBoardPublicId = BoardConfiguration.FixtureBoardPublicId;
    private static readonly Guid ProductRoadmapBoardPublicId = BoardConfiguration.ProductRoadmapBoardPublicId;
    private static readonly Guid MarketingLaunchBoardPublicId = BoardConfiguration.MarketingLaunchBoardPublicId;
    private static readonly Guid CustomerSupportBoardPublicId = BoardConfiguration.CustomerSupportBoardPublicId;

    private readonly FlowboardApiFactory _factory;
    private readonly List<string> _emails = [];

    public BoardsEndpointTests(FlowboardApiFactory factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowboardDbContext>();
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

    [Fact]
    public async Task ListBoards_ReturnsExactlyOwnedAndMemberBoards_EmptyForNoAccess()
    {
        using var ownerClient = await FixtureOwnerClientAsync();

        var ownerResponse = await ownerClient.GetFromJsonAsync<CursorPage<BoardSummaryDto>>("/v1/boards");
        Assert.NotNull(ownerResponse);
        var ownerBoardNames = ownerResponse!.Items.Select(b => b.Name).ToHashSet();
        Assert.Equal(
            new HashSet<string> { "Fixture Board", "Product Roadmap Q3", "Marketing Launch", "Customer Support" },
            ownerBoardNames);
        Assert.Null(ownerResponse.NextCursor);

        using var strangerClient = _factory.CreateClient();
        var stranger = await SignUpAsync(strangerClient, "NoAccess");
        Authorize(strangerClient, stranger.Token);

        var strangerResponse = await strangerClient.GetFromJsonAsync<CursorPage<BoardSummaryDto>>("/v1/boards");
        Assert.NotNull(strangerResponse);
        Assert.Empty(strangerResponse!.Items);
        Assert.Null(strangerResponse.NextCursor);

        var inviteResponse = await ownerClient.PostAsJsonAsync(
            $"/v1/boards/{ProductRoadmapBoardPublicId}/invitations",
            new InviteRequestBody(stranger.User.Email, "BoardMember"));
        Assert.Equal(HttpStatusCode.Created, inviteResponse.StatusCode);

        var memberResponse = await strangerClient.GetFromJsonAsync<CursorPage<BoardSummaryDto>>("/v1/boards");
        Assert.NotNull(memberResponse);
        var memberBoard = Assert.Single(memberResponse!.Items);
        Assert.Equal("Product Roadmap Q3", memberBoard.Name);
        Assert.Equal(11, memberBoard.CardCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public async Task ListBoards_LimitOutOfRange_ReturnsValidationProblem(int limit)
    {
        using var ownerClient = await FixtureOwnerClientAsync();

        var response = await ownerClient.GetAsync($"/v1/boards?limit={limit}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ListBoards_MalformedCursor_ReturnsValidationProblem()
    {
        using var ownerClient = await FixtureOwnerClientAsync();

        var response = await ownerClient.GetAsync("/v1/boards?cursor=not-a-valid-cursor");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetBoardContent_NoAccess_Returns404()
    {
        using var strangerClient = _factory.CreateClient();
        var stranger = await SignUpAsync(strangerClient, "Denied");
        Authorize(strangerClient, stranger.Token);

        var response = await strangerClient.GetAsync($"/v1/boards/{ProductRoadmapBoardPublicId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetBoardContent_UnknownBoard_Returns404()
    {
        using var ownerClient = await FixtureOwnerClientAsync();

        var response = await ownerClient.GetAsync($"/v1/boards/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetBoardContent_ReturnsListsAndCardsInStoredOrder_IndicatorsOnlyWhenApplicable()
    {
        using var ownerClient = await FixtureOwnerClientAsync();

        var board = await ownerClient.GetFromJsonAsync<BoardContentDto>($"/v1/boards/{ProductRoadmapBoardPublicId}");

        Assert.NotNull(board);
        Assert.Equal("Product Roadmap Q3", board!.Name);
        Assert.True(board.Starred);
        Assert.Equal(["Backlog", "Design", "In Progress", "Review"], board.Lists.Select(l => l.Name).ToArray());

        var backlog = board.Lists[0];
        Assert.Equal(
            ["Define SSO requirements for Enterprise", "Accessibility audit (WCAG 2.2 AA)", "Prototype smoke card"],
            backlog.Cards.Select(c => c.Title).ToArray());

        // VI-011: a card with no applicable indicators shows only its title.
        var smokeCard = backlog.Cards.Single(c => c.Title == "Prototype smoke card");
        Assert.Null(smokeCard.DueAt);
        Assert.Null(smokeCard.DueStatus);
        Assert.False(smokeCard.HasDescription);
        Assert.Null(smokeCard.Description);
        Assert.Null(smokeCard.ChecklistDone);
        Assert.Null(smokeCard.ChecklistTotal);
        Assert.Equal(0, smokeCard.CommentCount);
        Assert.Empty(smokeCard.Labels);
        Assert.Empty(smokeCard.Members);

        // A card with several applicable indicators shows all of them.
        var accessibilityCard = backlog.Cards.Single(c => c.Title == "Accessibility audit (WCAG 2.2 AA)");
        Assert.Equal(["Design", "Research"], accessibilityCard.Labels.Select(l => l.Name).ToArray());
        Assert.Equal(["Priya Nair"], accessibilityCard.Members.Select(m => m.DisplayName).ToArray());
        Assert.Null(accessibilityCard.DueAt);
        Assert.Null(accessibilityCard.ChecklistDone);
        Assert.Equal(0, accessibilityCard.CommentCount);
    }

    [Fact]
    public async Task GetBoardContent_CardWithDescription_ReturnsDescriptionText()
    {
        // Own board (not the shared fixture board) so this test cannot skew the
        // fixed card counts other tests in this class assert against.
        using var client = _factory.CreateClient();
        var user = await SignUpAsync(client, "DescriptionSearchOwner");
        Authorize(client, user.Token);

        var board = (await (await client.PostAsJsonAsync(
            "/v1/boards", new CreateBoardRequestBody("Description Search Fixture"))).Content
            .ReadFromJsonAsync<BoardCreatedDto>())!;
        var listPublicId = board.Lists[0].PublicId;

        var created = (await (await client.PostAsJsonAsync(
            $"/v1/lists/{listPublicId}/cards", new CreateCardRequestBody("Description search card")))
            .Content.ReadFromJsonAsync<CardSummaryDto>())!;

        var etag = (await client.GetAsync($"/v1/cards/{created.PublicId}")).Headers.ETag!.Tag;
        var patchRequest = new HttpRequestMessage(HttpMethod.Patch, $"/v1/cards/{created.PublicId}")
        {
            Content = JsonContent.Create(new { description = "Matches search text (007-search-filter)." }),
        };
        patchRequest.Headers.TryAddWithoutValidation("If-Match", etag);
        await client.SendAsync(patchRequest);

        var refetched = await client.GetFromJsonAsync<BoardContentDto>($"/v1/boards/{board.PublicId}");
        var card = refetched!.Lists[0].Cards.Single(c => c.PublicId == created.PublicId);

        Assert.True(card.HasDescription);
        Assert.Equal("Matches search text (007-search-filter).", card.Description);
    }

    [Fact]
    public async Task GetBoardContent_ProductRoadmapQ3_MatchesGoldenFixture()
    {
        using var ownerClient = await FixtureOwnerClientAsync();

        var board = await ownerClient.GetFromJsonAsync<BoardContentDto>($"/v1/boards/{ProductRoadmapBoardPublicId}");
        Assert.NotNull(board);

        var backlog = board!.Lists.Single(l => l.Name == "Backlog");
        Assert.Null(backlog.WipLimit);
        Assert.Equal(3, backlog.CardCount);

        var design = board.Lists.Single(l => l.Name == "Design");
        Assert.Equal(3, design.WipLimit);
        Assert.Equal(2, design.CardCount);

        var inProgress = board.Lists.Single(l => l.Name == "In Progress");
        Assert.Equal(3, inProgress.WipLimit);
        Assert.Equal(4, inProgress.CardCount);
        Assert.True(inProgress.CardCount > inProgress.WipLimit); // VI-007: this is the WIP-exceeded, red-pill list.

        var review = board.Lists.Single(l => l.Name == "Review");
        Assert.Equal(2, review.WipLimit);
        Assert.Equal(2, review.CardCount);

        var allCards = board.Lists.SelectMany(l => l.Cards).ToDictionary(c => c.Title);

        // The golden fixture's due dates are seeded relative to migration-apply time
        // (20260827165840_AddBoardContent.cs's seedNow), not test-run time, so which
        // bucket ("future"/"soon"/"overdue") a given card falls into drifts as real
        // wall-clock time passes — a card seeded "1 day from now" eventually becomes
        // "overdue" no matter how long ago that "now" was. Asserting a hardcoded bucket
        // name here was a time bomb (confirmed: it flipped on 2026-08-31, on an
        // otherwise-untouched DB, unrelated to any code change). Instead, recompute the
        // expected bucket via the production CardDueStatus.Compute domain function
        // (CardDueStatusTests.cs independently covers that function's own boundary
        // correctness with a fixed clock, so this integration check isn't the only thing
        // standing between a classifier regression and a green build), fed the
        // ground-truth persisted DueAt read directly from the database — and also assert
        // that DueAt itself round-trips through the API unchanged, so a bug that returned
        // a wrong/stale DueAt (while still computing *some* status from it) would still be
        // caught. There is a theoretical race: the service computes its own `now` when
        // building the response, and this test computes a separate, slightly later `now`
        // — if a seeded DueAt sits close enough to the "soon"/"overdue" or "soon"/"future"
        // boundary, the two computations could disagree. Note that a seeded DueAt *does*
        // eventually approach these boundaries as wall-clock time passes — that drift is
        // exactly the mechanism behind the original bug this fix addresses. What's narrow
        // here is different: for this specific race to flake, the boundary crossing would
        // have to land in the sub-millisecond gap between the service's `now` and this
        // test's `now`, not merely "on the same day" — a timing coincidence, not a
        // date coincidence, so this is not expected to flake in practice even though the
        // calendar boundary itself is reached routinely.
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowboardDbContext>();
            var persistedDueAt = await db.Cards
                .Where(c => allCards.Keys.Contains(c.Title))
                .ToDictionaryAsync(c => c.Title, c => c.DueAt);

            var now = DateTime.UtcNow;
            foreach (var title in new[]
                     {
                         "Define SSO requirements for Enterprise", "Card detail redesign",
                         "Drag & drop performance on large boards", "Board member invitations",
                         "Keyboard shortcuts pass",
                     })
            {
                Assert.Equal(persistedDueAt[title], allCards[title].DueAt);

                // dueComplete: false — the migration seed never sets DueComplete for these
                // fixture rows, so it keeps its schema default.
                Assert.Equal(CardDueStatus.Compute(persistedDueAt[title], dueComplete: false, now), allCards[title].DueStatus);
            }
        }
        foreach (var title in new[]
                 {
                     "Accessibility audit (WCAG 2.2 AA)", "Prototype smoke card", "Empty-state illustrations",
                     "Research competitor onboarding flows", "Label filter in the top bar", "Activity feed pagination",
                 })
        {
            Assert.Null(allCards[title].DueStatus);
        }

        var redesign = allCards["Card detail redesign"];
        Assert.Equal(2, redesign.ChecklistDone);
        Assert.Equal(3, redesign.ChecklistTotal);

        var dragDrop = allCards["Drag & drop performance on large boards"];
        Assert.Equal(1, dragDrop.ChecklistDone);
        Assert.Equal(2, dragDrop.ChecklistTotal);
        Assert.Equal(1, dragDrop.CommentCount);
        Assert.Equal(["Bug", "Urgent"], dragDrop.Labels.Select(l => l.Name).ToArray());

        var researchCard = allCards["Research competitor onboarding flows"];
        Assert.Equal(0, researchCard.ChecklistDone);
        Assert.Equal(1, researchCard.ChecklistTotal);
        Assert.Equal(1, researchCard.CommentCount);

        var invitationsCard = allCards["Board member invitations"];
        Assert.Equal(["Aiko Kimura", "Luca Ferrari"], invitationsCard.Members.Select(m => m.DisplayName).ToArray());

        Assert.Equal(["Tomás Bravo"], allCards["Keyboard shortcuts pass"].Members.Select(m => m.DisplayName).ToArray());
        Assert.Equal(["Omar Haddad"], allCards["Activity feed pagination"].Members.Select(m => m.DisplayName).ToArray());
    }

    [Fact]
    public async Task GetBoardContent_MarketingLaunchAndCustomerSupport_MatchSidebarCardCounts()
    {
        using var ownerClient = await FixtureOwnerClientAsync();

        var marketing = await ownerClient.GetFromJsonAsync<BoardContentDto>($"/v1/boards/{MarketingLaunchBoardPublicId}");
        Assert.NotNull(marketing);
        Assert.Equal(4, marketing!.Lists.Sum(l => l.CardCount));

        var support = await ownerClient.GetFromJsonAsync<BoardContentDto>($"/v1/boards/{CustomerSupportBoardPublicId}");
        Assert.NotNull(support);
        Assert.Equal(2, support!.Lists.Sum(l => l.CardCount));
    }

    // specs/006-board-list-management/contracts/board-list-management-api.md — POST /v1/boards.

    [Fact]
    public async Task CreateBoard_ReturnsThreeStarterLists_CreatorIsImplicitAdmin()
    {
        using var client = _factory.CreateClient();
        var user = await SignUpAsync(client, "CreateBoardOwner");
        Authorize(client, user.Token);

        var response = await client.PostAsJsonAsync("/v1/boards", new CreateBoardRequestBody("Q4 Planning"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await response.Content.ReadFromJsonAsync<BoardCreatedDto>();
        Assert.NotNull(created);
        Assert.Equal("Q4 Planning", created!.Name);
        Assert.False(created.Starred);
        Assert.Equal(0, created.CardCount);
        Assert.Equal(["To Do", "Doing", "Done"], created.Lists.Select(l => l.Name).ToArray());

        // No BoardMember row is created for the creator — BoardAccessService already
        // resolves them as this board's implicit BoardAdmin via workspace ownership.
        var content = await client.GetFromJsonAsync<BoardContentDto>($"/v1/boards/{created.PublicId}");
        Assert.NotNull(content);
        Assert.Equal(["To Do", "Doing", "Done"], content!.Lists.Select(l => l.Name).ToArray());
        Assert.All(content.Lists, l => Assert.Equal(0, l.CardCount));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateBoard_EmptyOrWhitespaceName_Returns400(string name)
    {
        using var ownerClient = await FixtureOwnerClientAsync();

        var response = await ownerClient.PostAsJsonAsync("/v1/boards", new CreateBoardRequestBody(name));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // specs/006-board-list-management/contracts/board-list-management-api.md — PATCH /v1/boards/{id}.

    [Fact]
    public async Task UpdateBoard_AsAdmin_Renames_MemberAndObserverForbidden_StaleIfMatchConflicts()
    {
        using var ownerClient = _factory.CreateClient();
        var owner = await SignUpAsync(ownerClient, "RenameBoardOwner");
        Authorize(ownerClient, owner.Token);

        var createResponse = await ownerClient.PostAsJsonAsync("/v1/boards", new CreateBoardRequestBody("Original Name"));
        var board = (await createResponse.Content.ReadFromJsonAsync<BoardCreatedDto>())!;

        var before = await ownerClient.GetFromJsonAsync<BoardContentDto>($"/v1/boards/{board.PublicId}");
        Assert.NotNull(before);

        var renameResponse = await ownerClient.SendAsync(PatchWithIfMatch(
            $"/v1/boards/{board.PublicId}", new UpdateBoardRequestBody("Renamed Board"), before!.RowVersion));
        Assert.Equal(HttpStatusCode.OK, renameResponse.StatusCode);

        var after = await ownerClient.GetFromJsonAsync<BoardContentDto>($"/v1/boards/{board.PublicId}");
        Assert.NotNull(after);
        Assert.Equal("Renamed Board", after!.Name);
        Assert.NotEqual(before.RowVersion, after.RowVersion);

        // Stale If-Match (the pre-rename RowVersion) -> 409.
        var staleResponse = await ownerClient.SendAsync(PatchWithIfMatch(
            $"/v1/boards/{board.PublicId}", new UpdateBoardRequestBody("Another Name"), before.RowVersion));
        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);

        // A plain BoardMember cannot rename, even with a fresh RowVersion.
        using var memberClient = await InvitedClientAsync(ownerClient, board.PublicId, "RenameBoardMember", "BoardMember");
        var memberResponse = await memberClient.SendAsync(PatchWithIfMatch(
            $"/v1/boards/{board.PublicId}", new UpdateBoardRequestBody("Member Attempt"), after.RowVersion));
        Assert.Equal(HttpStatusCode.Forbidden, memberResponse.StatusCode);

        // Neither can an Observer.
        using var observerClient = await InvitedClientAsync(ownerClient, board.PublicId, "RenameBoardObserver", "Observer");
        var observerResponse = await observerClient.SendAsync(PatchWithIfMatch(
            $"/v1/boards/{board.PublicId}", new UpdateBoardRequestBody("Observer Attempt"), after.RowVersion));
        Assert.Equal(HttpStatusCode.Forbidden, observerResponse.StatusCode);
    }

    [Fact]
    public async Task UpdateBoard_EmptyOrWhitespaceName_Returns400()
    {
        using var ownerClient = _factory.CreateClient();
        var owner = await SignUpAsync(ownerClient, "RenameBoardValidation");
        Authorize(ownerClient, owner.Token);

        var createResponse = await ownerClient.PostAsJsonAsync("/v1/boards", new CreateBoardRequestBody("Original Name"));
        var board = (await createResponse.Content.ReadFromJsonAsync<BoardCreatedDto>())!;
        var before = await ownerClient.GetFromJsonAsync<BoardContentDto>($"/v1/boards/{board.PublicId}");

        var response = await ownerClient.SendAsync(PatchWithIfMatch(
            $"/v1/boards/{board.PublicId}", new UpdateBoardRequestBody("   "), before!.RowVersion));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // specs/006-board-list-management/contracts/board-list-management-api.md — star/unstar.

    [Fact]
    public async Task StarBoard_MovesToFrontOfList_UnstarReturnsToNormalOrder_ObserverForbidden()
    {
        using var ownerClient = _factory.CreateClient();
        var owner = await SignUpAsync(ownerClient, "StarBoardOwner");
        Authorize(ownerClient, owner.Token);

        var first = (await (await ownerClient.PostAsJsonAsync(
            "/v1/boards", new CreateBoardRequestBody("First Board"))).Content.ReadFromJsonAsync<BoardCreatedDto>())!;
        var second = (await (await ownerClient.PostAsJsonAsync(
            "/v1/boards", new CreateBoardRequestBody("Second Board"))).Content.ReadFromJsonAsync<BoardCreatedDto>())!;

        var starResponse = await ownerClient.PostAsync($"/v1/boards/{second.PublicId}/star", content: null);
        Assert.Equal(HttpStatusCode.NoContent, starResponse.StatusCode);

        var afterStar = await ownerClient.GetFromJsonAsync<CursorPage<BoardSummaryDto>>("/v1/boards");
        var ownBoards = afterStar!.Items.Where(b => b.PublicId == first.PublicId || b.PublicId == second.PublicId).ToList();
        Assert.Equal(second.PublicId, ownBoards[0].PublicId);
        Assert.True(ownBoards[0].Starred);

        var unstarResponse = await ownerClient.PostAsync($"/v1/boards/{second.PublicId}/unstar", content: null);
        Assert.Equal(HttpStatusCode.NoContent, unstarResponse.StatusCode);

        var afterUnstar = await ownerClient.GetFromJsonAsync<CursorPage<BoardSummaryDto>>("/v1/boards");
        var secondAfterUnstar = afterUnstar!.Items.Single(b => b.PublicId == second.PublicId);
        Assert.False(secondAfterUnstar.Starred);

        using var observerClient = await InvitedClientAsync(ownerClient, first.PublicId, "StarBoardObserver", "Observer");
        var observerResponse = await observerClient.PostAsync($"/v1/boards/{first.PublicId}/star", content: null);
        Assert.Equal(HttpStatusCode.Forbidden, observerResponse.StatusCode);
    }

    // specs/006-board-list-management/contracts/board-list-management-api.md — DELETE /v1/boards/{id}.

    [Fact]
    public async Task DeleteBoard_AsAdmin_RemovesFromEveryMembersList_MemberForbidden_SecondDeleteIs404()
    {
        using var ownerClient = _factory.CreateClient();
        var owner = await SignUpAsync(ownerClient, "DeleteBoardOwner");
        Authorize(ownerClient, owner.Token);

        var board = (await (await ownerClient.PostAsJsonAsync(
            "/v1/boards", new CreateBoardRequestBody("Board To Delete"))).Content.ReadFromJsonAsync<BoardCreatedDto>())!;

        using var memberClient = await InvitedClientAsync(ownerClient, board.PublicId, "DeleteBoardMember", "BoardMember");
        var memberDeleteResponse = await memberClient.DeleteAsync($"/v1/boards/{board.PublicId}");
        Assert.Equal(HttpStatusCode.Forbidden, memberDeleteResponse.StatusCode);

        var memberListBefore = await memberClient.GetFromJsonAsync<CursorPage<BoardSummaryDto>>("/v1/boards");
        Assert.Contains(memberListBefore!.Items, b => b.PublicId == board.PublicId);

        var deleteResponse = await ownerClient.DeleteAsync($"/v1/boards/{board.PublicId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var ownerListAfter = await ownerClient.GetFromJsonAsync<CursorPage<BoardSummaryDto>>("/v1/boards");
        Assert.DoesNotContain(ownerListAfter!.Items, b => b.PublicId == board.PublicId);

        var memberListAfter = await memberClient.GetFromJsonAsync<CursorPage<BoardSummaryDto>>("/v1/boards");
        Assert.DoesNotContain(memberListAfter!.Items, b => b.PublicId == board.PublicId);

        var secondDeleteResponse = await ownerClient.DeleteAsync($"/v1/boards/{board.PublicId}");
        Assert.Equal(HttpStatusCode.NotFound, secondDeleteResponse.StatusCode);
    }
}
