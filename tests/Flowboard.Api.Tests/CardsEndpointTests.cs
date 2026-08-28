// Verifies the provider side of specs/004-card-crud/contracts/card-crud-api.md.
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Flowboard.Api.Data;
using Flowboard.Api.Data.Configurations;
using Flowboard.Api.Domain;
using Flowboard.Api.Domain.Entities;
using Flowboard.Api.Endpoints;
using Flowboard.Api.Services;
using Flowboard.Api.Tests.TestFixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Flowboard.Api.Tests;

[Collection(FlowboardApiCollection.Name)]
public sealed class CardsEndpointTests : IAsyncLifetime
{
    private const string FixtureOwnerEmail = UserConfiguration.FixtureOwnerEmail;
    private const string FixtureOwnerPassword = "FixtureOwner!2026";
    private const string DefaultPassword = "correct horse battery staple";
    private static readonly Guid ProductRoadmapBoardPublicId = BoardConfiguration.ProductRoadmapBoardPublicId;
    private static readonly Guid BacklogListPublicId = ListConfiguration.BacklogPublicId;

    private readonly FlowboardApiFactory _factory;
    private readonly List<string> _emails = [];
    private readonly List<Guid> _cardPublicIds = [];

    public CardsEndpointTests(FlowboardApiFactory factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<FlowboardDbContext>();

        var cardIds = await db.Cards.IgnoreQueryFilters()
            .Where(c => _cardPublicIds.Contains(c.PublicId))
            .Select(c => c.Id)
            .ToListAsync();

        if (cardIds.Count > 0)
        {
            db.ActivityEvents.RemoveRange(await db.ActivityEvents.IgnoreQueryFilters().Where(e => cardIds.Contains(e.CardId)).ToListAsync());
            db.Comments.RemoveRange(await db.Comments.IgnoreQueryFilters().Where(c => cardIds.Contains(c.CardId)).ToListAsync());
            db.ChecklistItems.RemoveRange(await db.ChecklistItems.IgnoreQueryFilters().Where(ci => cardIds.Contains(ci.CardId)).ToListAsync());
            db.CardLabels.RemoveRange(await db.CardLabels.IgnoreQueryFilters().Where(cl => cardIds.Contains(cl.CardId)).ToListAsync());
            db.CardMembers.RemoveRange(await db.CardMembers.IgnoreQueryFilters().Where(cm => cardIds.Contains(cm.CardId)).ToListAsync());
            db.Cards.RemoveRange(await db.Cards.IgnoreQueryFilters().Where(c => cardIds.Contains(c.Id)).ToListAsync());
            await db.SaveChangesAsync();
        }

        var users = await db.Users.Where(u => _emails.Contains(u.Email)).ToListAsync();
        var userIds = users.Select(u => u.Id).ToList();
        if (userIds.Count > 0)
        {
            db.CardMembers.RemoveRange(await db.CardMembers.IgnoreQueryFilters().Where(m => userIds.Contains(m.UserId)).ToListAsync());
            db.BoardMembers.RemoveRange(await db.BoardMembers.Where(m => userIds.Contains(m.UserId)).ToListAsync());
            db.Workspaces.RemoveRange(await db.Workspaces.Where(w => userIds.Contains(w.OwnerUserId)).ToListAsync());
            await db.SaveChangesAsync();
            db.Users.RemoveRange(users);
            await db.SaveChangesAsync();
        }
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

    private async Task<CardSummaryDto> CreateCardAsync(HttpClient client, string titlePrefix)
    {
        var response = await client.PostAsJsonAsync(
            $"/v1/lists/{BacklogListPublicId}/cards", new CreateCardRequestBody($"{titlePrefix}-{Guid.NewGuid():N}"));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var card = (await response.Content.ReadFromJsonAsync<CardSummaryDto>())!;
        _cardPublicIds.Add(card.PublicId);
        return card;
    }

    // ── Create ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateCard_ValidTitle_AppendsAndWritesCreatedEvent()
    {
        using var ownerClient = await FixtureOwnerClientAsync();
        var card = await CreateCardAsync(ownerClient, "NewCard");

        Assert.False(card.HasDescription);
        Assert.Null(card.DueAt);
        Assert.Empty(card.Labels);
        Assert.Empty(card.Members);

        var activity = await ownerClient.GetFromJsonAsync<CursorPage<ActivityEntryDto>>(
            $"/v1/cards/{card.PublicId}/activity");
        var entry = Assert.Single(activity!.Items);
        Assert.Equal(ActivityEventType.CardCreated, entry.Type);
    }

    [Fact]
    public async Task CreateCard_EmptyTitle_Returns400()
    {
        using var ownerClient = await FixtureOwnerClientAsync();
        var response = await ownerClient.PostAsJsonAsync(
            $"/v1/lists/{BacklogListPublicId}/cards", new CreateCardRequestBody("   "));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateCard_NonMember_Returns404()
    {
        using var client = _factory.CreateClient();
        var caller = await SignUpAsync(client, "NonMemberCreate");
        Authorize(client, caller.Token);

        var response = await client.PostAsJsonAsync(
            $"/v1/lists/{BacklogListPublicId}/cards", new CreateCardRequestBody("Should not be created"));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateCard_Observer_Returns403()
    {
        using var ownerClient = await FixtureOwnerClientAsync();
        using var observerClient = await InvitedClientAsync(ownerClient, "CreateObserver", "Observer");

        var response = await observerClient.PostAsJsonAsync(
            $"/v1/lists/{BacklogListPublicId}/cards", new CreateCardRequestBody("Observer cannot create"));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ── Get detail ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetCardDetail_ReturnsFullDetailWithETag()
    {
        using var ownerClient = await FixtureOwnerClientAsync();
        var card = await CreateCardAsync(ownerClient, "DetailCard");

        var response = await ownerClient.GetAsync($"/v1/cards/{card.PublicId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(response.Headers.ETag);

        var detail = await response.Content.ReadFromJsonAsync<CardDetailDto>();
        Assert.Equal(card.PublicId, detail!.PublicId);
        Assert.Equal(BacklogListPublicId, detail.ListPublicId);
        Assert.Equal(ProductRoadmapBoardPublicId, detail.BoardPublicId);
    }

    [Fact]
    public async Task GetCardDetail_NonMember_Returns404()
    {
        using var ownerClient = await FixtureOwnerClientAsync();
        var card = await CreateCardAsync(ownerClient, "HiddenCard");

        using var client = _factory.CreateClient();
        var caller = await SignUpAsync(client, "NonMemberGet");
        Authorize(client, caller.Token);

        var response = await client.GetAsync($"/v1/cards/{card.PublicId}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Update / concurrency ────────────────────────────────────────────────

    [Fact]
    public async Task UpdateCard_TitleAndDescription_UpdatesAndReturnsNewETag()
    {
        using var ownerClient = await FixtureOwnerClientAsync();
        var card = await CreateCardAsync(ownerClient, "UpdateCard");

        var getResponse = await ownerClient.GetAsync($"/v1/cards/{card.PublicId}");
        var etag = getResponse.Headers.ETag!.Tag;

        var patchRequest = new HttpRequestMessage(HttpMethod.Patch, $"/v1/cards/{card.PublicId}")
        {
            Content = JsonContent.Create(new { title = "Renamed title", description = "A new description" }),
        };
        patchRequest.Headers.TryAddWithoutValidation("If-Match", etag);
        var patchResponse = await ownerClient.SendAsync(patchRequest);

        Assert.Equal(HttpStatusCode.OK, patchResponse.StatusCode);
        Assert.NotEqual(etag, patchResponse.Headers.ETag!.Tag);

        var updated = await patchResponse.Content.ReadFromJsonAsync<CardDetailDto>();
        Assert.Equal("Renamed title", updated!.Title);
        Assert.Equal("A new description", updated.Description);
    }

    [Fact]
    public async Task UpdateCard_StaleIfMatch_Returns409()
    {
        using var ownerClient = await FixtureOwnerClientAsync();
        var card = await CreateCardAsync(ownerClient, "ConflictCard");

        var getResponse = await ownerClient.GetAsync($"/v1/cards/{card.PublicId}");
        var staleEtag = getResponse.Headers.ETag!.Tag;

        // First write moves the RowVersion forward.
        var firstPatch = new HttpRequestMessage(HttpMethod.Patch, $"/v1/cards/{card.PublicId}")
        {
            Content = JsonContent.Create(new { title = "First writer wins" }),
        };
        firstPatch.Headers.TryAddWithoutValidation("If-Match", staleEtag);
        Assert.Equal(HttpStatusCode.OK, (await ownerClient.SendAsync(firstPatch)).StatusCode);

        // Second write still carries the now-stale ETag.
        var secondPatch = new HttpRequestMessage(HttpMethod.Patch, $"/v1/cards/{card.PublicId}")
        {
            Content = JsonContent.Create(new { title = "Second writer loses" }),
        };
        secondPatch.Headers.TryAddWithoutValidation("If-Match", staleEtag);
        var secondResponse = await ownerClient.SendAsync(secondPatch);

        Assert.Equal(HttpStatusCode.Conflict, secondResponse.StatusCode);
    }

    [Fact]
    public async Task UpdateCard_MissingIfMatch_Returns400()
    {
        using var ownerClient = await FixtureOwnerClientAsync();
        var card = await CreateCardAsync(ownerClient, "NoIfMatchCard");

        var response = await ownerClient.PatchAsJsonAsync($"/v1/cards/{card.PublicId}", new { title = "No If-Match" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateCard_RejectsListIdAndPosition_Returns400()
    {
        using var ownerClient = await FixtureOwnerClientAsync();
        var card = await CreateCardAsync(ownerClient, "RejectMoveCard");

        var getResponse = await ownerClient.GetAsync($"/v1/cards/{card.PublicId}");
        var etag = getResponse.Headers.ETag!.Tag;

        var patchRequest = new HttpRequestMessage(HttpMethod.Patch, $"/v1/cards/{card.PublicId}")
        {
            Content = JsonContent.Create(new { position = 5000 }),
        };
        patchRequest.Headers.TryAddWithoutValidation("If-Match", etag);
        var response = await ownerClient.SendAsync(patchRequest);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateCard_ClearDescription_SetsNull()
    {
        using var ownerClient = await FixtureOwnerClientAsync();
        var card = await CreateCardAsync(ownerClient, "ClearDescriptionCard");

        var firstGet = await ownerClient.GetAsync($"/v1/cards/{card.PublicId}");
        var firstEtag = firstGet.Headers.ETag!.Tag;
        var setRequest = new HttpRequestMessage(HttpMethod.Patch, $"/v1/cards/{card.PublicId}")
        {
            Content = JsonContent.Create(new { description = "Has a description" }),
        };
        setRequest.Headers.TryAddWithoutValidation("If-Match", firstEtag);
        var setResponse = await ownerClient.SendAsync(setRequest);
        var setEtag = setResponse.Headers.ETag!.Tag;

        var clearRequest = new HttpRequestMessage(HttpMethod.Patch, $"/v1/cards/{card.PublicId}")
        {
            Content = JsonContent.Create(new { description = (string?)null }),
        };
        clearRequest.Headers.TryAddWithoutValidation("If-Match", setEtag);
        var clearResponse = await ownerClient.SendAsync(clearRequest);

        var cleared = await clearResponse.Content.ReadFromJsonAsync<CardDetailDto>();
        Assert.Null(cleared!.Description);
    }

    // ── Labels ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddLabel_SameBoardLabel_AssignsAndIsIdempotent()
    {
        using var ownerClient = await FixtureOwnerClientAsync();
        var card = await CreateCardAsync(ownerClient, "LabelCard");

        var first = await ownerClient.PostAsJsonAsync(
            $"/v1/cards/{card.PublicId}/labels", new AddLabelRequestBody(LabelConfiguration.FeaturePublicId));
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        var second = await ownerClient.PostAsJsonAsync(
            $"/v1/cards/{card.PublicId}/labels", new AddLabelRequestBody(LabelConfiguration.FeaturePublicId));
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);

        var detail = await ownerClient.GetFromJsonAsync<CardDetailDto>($"/v1/cards/{card.PublicId}");
        Assert.Single(detail!.Labels);
    }

    [Fact]
    public async Task AddLabel_CrossBoardLabel_Returns400()
    {
        using var ownerClient = await FixtureOwnerClientAsync();
        var card = await CreateCardAsync(ownerClient, "CrossBoardLabelCard");

        // Invariant 7: a label from a different board must be rejected. Marketing Launch
        // (board 3) has no labels seeded, so insert one directly for this boundary check.
        var foreignLabelPublicId = Guid.NewGuid();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FlowboardDbContext>();
            db.Labels.Add(new Label
            {
                PublicId = foreignLabelPublicId,
                BoardId = BoardConfiguration.MarketingLaunchBoardId,
                Name = "Foreign",
                Color = "#000000",
                CreatedDate = DateTime.UtcNow,
                CreatedBy = "TEST",
            });
            await db.SaveChangesAsync();
        }

        try
        {
            var response = await ownerClient.PostAsJsonAsync(
                $"/v1/cards/{card.PublicId}/labels", new AddLabelRequestBody(foreignLabelPublicId));
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
        finally
        {
            using var scope = _factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FlowboardDbContext>();
            var label = await db.Labels.FirstAsync(l => l.PublicId == foreignLabelPublicId);
            db.Labels.Remove(label);
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task RemoveLabel_Unassigned_IsIdempotent()
    {
        using var ownerClient = await FixtureOwnerClientAsync();
        var card = await CreateCardAsync(ownerClient, "RemoveUnassignedLabelCard");

        var response = await ownerClient.DeleteAsync($"/v1/cards/{card.PublicId}/labels/{LabelConfiguration.FeaturePublicId}");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    // ── Members ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddMember_NotYetBoardMember_AddsToBoardToo()
    {
        using var ownerClient = await FixtureOwnerClientAsync();
        var card = await CreateCardAsync(ownerClient, "AssignNonMemberCard");

        using var signupClient = _factory.CreateClient();
        var newUser = await SignUpAsync(signupClient, "AssignNonMember");

        var response = await ownerClient.PostAsJsonAsync(
            $"/v1/cards/{card.PublicId}/members", new AddMemberRequestBody(newUser.User.PublicId));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var members = await ownerClient.GetFromJsonAsync<MembersResponse>($"/v1/boards/{ProductRoadmapBoardPublicId}/members");
        Assert.Contains(members!.Members, m => m.User.PublicId == newUser.User.PublicId && m.Role == "BoardMember");

        var detail = await ownerClient.GetFromJsonAsync<CardDetailDto>($"/v1/cards/{card.PublicId}");
        Assert.Contains(detail!.Members, m => m.PublicId == newUser.User.PublicId);
    }

    [Fact]
    public async Task RemoveMember_RemovesCardOnly_NotBoardMembership()
    {
        using var ownerClient = await FixtureOwnerClientAsync();
        var card = await CreateCardAsync(ownerClient, "RemoveMemberCard");

        var akPublicId = UserConfiguration.FixtureMemberAkPublicId;
        var add = await ownerClient.PostAsJsonAsync($"/v1/cards/{card.PublicId}/members", new AddMemberRequestBody(akPublicId));
        Assert.Equal(HttpStatusCode.NoContent, add.StatusCode);

        var remove = await ownerClient.DeleteAsync($"/v1/cards/{card.PublicId}/members/{akPublicId}");
        Assert.Equal(HttpStatusCode.NoContent, remove.StatusCode);

        var detail = await ownerClient.GetFromJsonAsync<CardDetailDto>($"/v1/cards/{card.PublicId}");
        Assert.DoesNotContain(detail!.Members, m => m.PublicId == akPublicId);

        var members = await ownerClient.GetFromJsonAsync<MembersResponse>($"/v1/boards/{ProductRoadmapBoardPublicId}/members");
        Assert.Contains(members!.Members, m => m.User.PublicId == akPublicId);
    }

    // ── Checklist ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ChecklistItem_AddCheckDelete_Flow()
    {
        using var ownerClient = await FixtureOwnerClientAsync();
        var card = await CreateCardAsync(ownerClient, "ChecklistCard");

        var addResponse = await ownerClient.PostAsJsonAsync(
            $"/v1/cards/{card.PublicId}/checklist-items", new AddChecklistItemRequestBody("Write tests"));
        Assert.Equal(HttpStatusCode.Created, addResponse.StatusCode);
        var item = (await addResponse.Content.ReadFromJsonAsync<ChecklistItemDetailDto>())!;
        Assert.False(item.Done);

        var checkResponse = await ownerClient.PatchAsJsonAsync(
            $"/v1/checklist-items/{item.PublicId}", new UpdateChecklistItemRequestBody(true));
        Assert.Equal(HttpStatusCode.OK, checkResponse.StatusCode);
        var checked_ = (await checkResponse.Content.ReadFromJsonAsync<ChecklistItemDetailDto>())!;
        Assert.True(checked_.Done);

        // Re-sending the same value is idempotent and must not duplicate the event.
        await ownerClient.PatchAsJsonAsync($"/v1/checklist-items/{item.PublicId}", new UpdateChecklistItemRequestBody(true));

        var uncheckResponse = await ownerClient.PatchAsJsonAsync(
            $"/v1/checklist-items/{item.PublicId}", new UpdateChecklistItemRequestBody(false));
        Assert.False((await uncheckResponse.Content.ReadFromJsonAsync<ChecklistItemDetailDto>())!.Done);

        var activity = await ownerClient.GetFromJsonAsync<CursorPage<ActivityEntryDto>>($"/v1/cards/{card.PublicId}/activity");
        Assert.Equal(
            [ActivityEventType.ChecklistItemUnchecked, ActivityEventType.ChecklistItemChecked,
                ActivityEventType.ChecklistItemAdded, ActivityEventType.CardCreated],
            activity!.Items.Select(e => e.Type));

        var deleteResponse = await ownerClient.DeleteAsync($"/v1/checklist-items/{item.PublicId}");
        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var detail = await ownerClient.GetFromJsonAsync<CardDetailDto>($"/v1/cards/{card.PublicId}");
        Assert.Empty(detail!.ChecklistItems);
    }

    // ── Comments / activity ─────────────────────────────────────────────────

    [Fact]
    public async Task Comment_Observer_CanPost_ButCannotMutate()
    {
        using var ownerClient = await FixtureOwnerClientAsync();
        var card = await CreateCardAsync(ownerClient, "ObserverCommentCard");
        using var observerClient = await InvitedClientAsync(ownerClient, "CommentObserver", "Observer");

        var commentResponse = await observerClient.PostAsJsonAsync(
            $"/v1/cards/{card.PublicId}/comments", new AddCommentRequestBody("Looks good to me."));
        Assert.Equal(HttpStatusCode.Created, commentResponse.StatusCode);

        var forbiddenLabel = await observerClient.PostAsJsonAsync(
            $"/v1/cards/{card.PublicId}/labels", new AddLabelRequestBody(LabelConfiguration.FeaturePublicId));
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenLabel.StatusCode);
    }

    [Fact]
    public async Task Activity_ReturnsNewestFirst_IncludingCreatedAndComment()
    {
        using var ownerClient = await FixtureOwnerClientAsync();
        var card = await CreateCardAsync(ownerClient, "ActivityOrderCard");

        var comment = await ownerClient.PostAsJsonAsync(
            $"/v1/cards/{card.PublicId}/comments", new AddCommentRequestBody("A follow-up note."));
        Assert.Equal(HttpStatusCode.Created, comment.StatusCode);

        var activity = await ownerClient.GetFromJsonAsync<CursorPage<ActivityEntryDto>>($"/v1/cards/{card.PublicId}/activity");
        Assert.Equal(2, activity!.Items.Count);
        Assert.Equal(ActivityEventType.CommentAdded, activity.Items[0].Type);
        Assert.Equal(ActivityEventType.CardCreated, activity.Items[1].Type);
    }

    // ── Copy / delete ───────────────────────────────────────────────────────

    [Fact]
    public async Task CopyCard_DuplicatesLabelsMembersChecklist_ResetsDoneAndActivity()
    {
        using var ownerClient = await FixtureOwnerClientAsync();
        var card = await CreateCardAsync(ownerClient, "CopySourceCard");

        await ownerClient.PostAsJsonAsync($"/v1/cards/{card.PublicId}/labels", new AddLabelRequestBody(LabelConfiguration.FeaturePublicId));
        await ownerClient.PostAsJsonAsync(
            $"/v1/cards/{card.PublicId}/members", new AddMemberRequestBody(UserConfiguration.FixtureMemberAkPublicId));
        var addItem = await ownerClient.PostAsJsonAsync(
            $"/v1/cards/{card.PublicId}/checklist-items", new AddChecklistItemRequestBody("Do the thing"));
        var item = (await addItem.Content.ReadFromJsonAsync<ChecklistItemDetailDto>())!;
        await ownerClient.PatchAsJsonAsync($"/v1/checklist-items/{item.PublicId}", new UpdateChecklistItemRequestBody(true));

        var copyResponse = await ownerClient.PostAsync($"/v1/cards/{card.PublicId}/copy", null);
        Assert.Equal(HttpStatusCode.Created, copyResponse.StatusCode);
        var copy = (await copyResponse.Content.ReadFromJsonAsync<CardSummaryDto>())!;
        _cardPublicIds.Add(copy.PublicId);

        Assert.EndsWith(" (copy)", copy.Title);
        Assert.Single(copy.Labels);
        Assert.Single(copy.Members);

        var copyDetail = await ownerClient.GetFromJsonAsync<CardDetailDto>($"/v1/cards/{copy.PublicId}");
        var copiedItem = Assert.Single(copyDetail!.ChecklistItems);
        Assert.Equal("Do the thing", copiedItem.Text);
        Assert.False(copiedItem.Done);

        var copyActivity = await ownerClient.GetFromJsonAsync<CursorPage<ActivityEntryDto>>($"/v1/cards/{copy.PublicId}/activity");
        var onlyEntry = Assert.Single(copyActivity!.Items);
        Assert.Equal(ActivityEventType.CardCreated, onlyEntry.Type);
    }

    [Fact]
    public async Task DeleteCard_SoftDeletes_ThenNotFoundOnSecondDeleteAndGet()
    {
        using var ownerClient = await FixtureOwnerClientAsync();
        var card = await CreateCardAsync(ownerClient, "DeleteCard");

        var first = await ownerClient.DeleteAsync($"/v1/cards/{card.PublicId}");
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        var second = await ownerClient.DeleteAsync($"/v1/cards/{card.PublicId}");
        Assert.Equal(HttpStatusCode.NotFound, second.StatusCode);

        var get = await ownerClient.GetAsync($"/v1/cards/{card.PublicId}");
        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
    }

    // ── Permission matrix sweeps ────────────────────────────────────────────

    [Fact]
    public async Task NonMember_Gets404OnEveryCardScopedEndpoint()
    {
        using var ownerClient = await FixtureOwnerClientAsync();
        var card = await CreateCardAsync(ownerClient, "NonMemberSweepCard");

        using var client = _factory.CreateClient();
        var caller = await SignUpAsync(client, "NonMemberSweep");
        Authorize(client, caller.Token);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/v1/cards/{card.PublicId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/v1/cards/{card.PublicId}/activity")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsJsonAsync(
            $"/v1/cards/{card.PublicId}/comments", new AddCommentRequestBody("Should not land"))).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PostAsync($"/v1/cards/{card.PublicId}/copy", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/v1/cards/{card.PublicId}")).StatusCode);
    }

    [Fact]
    public async Task ObserverRole_Gets403OnMutatingEndpoints()
    {
        using var ownerClient = await FixtureOwnerClientAsync();
        var card = await CreateCardAsync(ownerClient, "ObserverSweepCard");
        using var observerClient = await InvitedClientAsync(ownerClient, "MutationObserver", "Observer");

        var getResponse = await ownerClient.GetAsync($"/v1/cards/{card.PublicId}");
        var etag = getResponse.Headers.ETag!.Tag;
        var patchRequest = new HttpRequestMessage(HttpMethod.Patch, $"/v1/cards/{card.PublicId}")
        {
            Content = JsonContent.Create(new { title = "Observer cannot rename" }),
        };
        patchRequest.Headers.TryAddWithoutValidation("If-Match", etag);

        Assert.Equal(HttpStatusCode.Forbidden, (await observerClient.SendAsync(patchRequest)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await observerClient.PostAsJsonAsync(
            $"/v1/cards/{card.PublicId}/checklist-items", new AddChecklistItemRequestBody("nope"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await observerClient.PostAsync($"/v1/cards/{card.PublicId}/copy", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await observerClient.DeleteAsync($"/v1/cards/{card.PublicId}")).StatusCode);
    }
}
