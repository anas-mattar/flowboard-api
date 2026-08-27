// Verifies the provider side of specs/003-board-view-readonly/contracts/board-content-api.md.
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

        Assert.Equal("future", allCards["Define SSO requirements for Enterprise"].DueStatus);
        Assert.Equal("future", allCards["Card detail redesign"].DueStatus);
        Assert.Equal("soon", allCards["Drag & drop performance on large boards"].DueStatus);
        Assert.Equal("future", allCards["Board member invitations"].DueStatus);
        Assert.Equal("overdue", allCards["Keyboard shortcuts pass"].DueStatus);
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
}
