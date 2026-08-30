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

    private async Task<CardSummaryDto> CreateCardAsync(HttpClient client, string titlePrefix) =>
        await CreateCardAsync(client, titlePrefix, BacklogListPublicId);

    private async Task<CardSummaryDto> CreateCardAsync(HttpClient client, string titlePrefix, Guid listPublicId)
    {
        var response = await client.PostAsJsonAsync(
            $"/v1/lists/{listPublicId}/cards", new CreateCardRequestBody($"{titlePrefix}-{Guid.NewGuid():N}"));
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
    public async Task UpdateCard_StaleIfMatch_RejectedSaveNeverPersistsOrBroadcasts()
    {
        // specs/008-realtime-sync/tasks.md T020 (FR-004): the losing save must never reach
        // SaveChangesAsync, so it can never persist an ActivityEvent or reach the
        // PublishActivityEventAsync call that follows it in CardService.UpdateCardAsync —
        // proven here via the activity feed rather than a hub connection.
        using var ownerClient = await FixtureOwnerClientAsync();
        var card = await CreateCardAsync(ownerClient, "RealtimeConflictCard");

        var getResponse = await ownerClient.GetAsync($"/v1/cards/{card.PublicId}");
        var staleEtag = getResponse.Headers.ETag!.Tag;

        var winningPatch = new HttpRequestMessage(HttpMethod.Patch, $"/v1/cards/{card.PublicId}")
        {
            Content = JsonContent.Create(new { title = "Winning save" }),
        };
        winningPatch.Headers.TryAddWithoutValidation("If-Match", staleEtag);
        Assert.Equal(HttpStatusCode.OK, (await ownerClient.SendAsync(winningPatch)).StatusCode);

        var losingPatch = new HttpRequestMessage(HttpMethod.Patch, $"/v1/cards/{card.PublicId}")
        {
            Content = JsonContent.Create(new { title = "Losing save" }),
        };
        losingPatch.Headers.TryAddWithoutValidation("If-Match", staleEtag);
        var losingResponse = await ownerClient.SendAsync(losingPatch);
        Assert.Equal(HttpStatusCode.Conflict, losingResponse.StatusCode);

        // Member B is shown the current (winning) data, never the rejected write, and can
        // retry using the fresh ETag it carries (FR-004 acceptance scenario 1).
        var refetch = await ownerClient.GetAsync($"/v1/cards/{card.PublicId}");
        var refetched = await refetch.Content.ReadFromJsonAsync<CardDetailDto>();
        Assert.Equal("Winning save", refetched!.Title);
        var freshEtag = refetch.Headers.ETag!.Tag;

        var activity = await ownerClient.GetFromJsonAsync<CursorPage<ActivityEntryDto>>($"/v1/cards/{card.PublicId}/activity");
        var renamedEntries = activity!.Items.Where(e => e.Type == ActivityEventType.CardRenamed).ToList();
        var onlyRename = Assert.Single(renamedEntries);
        Assert.Equal("Winning save", onlyRename.Payload.GetProperty("title").GetString());

        var retryPatch = new HttpRequestMessage(HttpMethod.Patch, $"/v1/cards/{card.PublicId}")
        {
            Content = JsonContent.Create(new { title = "Retried after refetch" }),
        };
        retryPatch.Headers.TryAddWithoutValidation("If-Match", freshEtag);
        Assert.Equal(HttpStatusCode.OK, (await ownerClient.SendAsync(retryPatch)).StatusCode);
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

    // ── Move ────────────────────────────────────────────────────────────────
    // contracts/move-api.md — POST /v1/cards/{cardPublicId}/move.

    [Fact]
    public async Task MoveCard_SameList_ReordersAndWritesNoActivityEntry()
    {
        using var ownerClient = await FixtureOwnerClientAsync();
        var first = await CreateCardAsync(ownerClient, "SameListFirst");
        var second = await CreateCardAsync(ownerClient, "SameListSecond");

        var response = await ownerClient.PostAsJsonAsync(
            $"/v1/cards/{second.PublicId}/move", new MoveCardRequestBody(BacklogListPublicId, first.PublicId));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var backlog = (await ownerClient.GetFromJsonAsync<BoardContentDto>($"/v1/boards/{ProductRoadmapBoardPublicId}"))!
            .Lists.Single(l => l.PublicId == BacklogListPublicId).Cards;
        var firstIndex = backlog.ToList().FindIndex(c => c.PublicId == first.PublicId);
        var secondIndex = backlog.ToList().FindIndex(c => c.PublicId == second.PublicId);
        Assert.True(secondIndex < firstIndex);

        var activity = await ownerClient.GetFromJsonAsync<CursorPage<ActivityEntryDto>>($"/v1/cards/{second.PublicId}/activity");
        Assert.DoesNotContain(activity!.Items, e => e.Type == ActivityEventType.CardMoved);
    }

    [Fact]
    public async Task MoveCard_CrossList_MovesAndWritesOneActivityEntry()
    {
        using var ownerClient = await FixtureOwnerClientAsync();
        var card = await CreateCardAsync(ownerClient, "CrossListCard");

        var response = await ownerClient.PostAsJsonAsync(
            $"/v1/cards/{card.PublicId}/move", new MoveCardRequestBody(ListConfiguration.DesignPublicId, null));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var detail = await ownerClient.GetFromJsonAsync<CardDetailDto>($"/v1/cards/{card.PublicId}");
        Assert.Equal(ListConfiguration.DesignPublicId, detail!.ListPublicId);

        var activity = await ownerClient.GetFromJsonAsync<CursorPage<ActivityEntryDto>>($"/v1/cards/{card.PublicId}/activity");
        var movedEntries = activity!.Items.Where(e => e.Type == ActivityEventType.CardMoved).ToList();
        Assert.Single(movedEntries);
    }

    [Fact]
    public async Task MoveCard_DestinationOverWipLimit_StillSucceeds()
    {
        using var ownerClient = await FixtureOwnerClientAsync();
        var card = await CreateCardAsync(ownerClient, "OverWipCard");

        // Review is seeded at its WipLimit (2/2) already — the move must not be blocked
        // (invariant 3, research.md R-6).
        var response = await ownerClient.PostAsJsonAsync(
            $"/v1/cards/{card.PublicId}/move", new MoveCardRequestBody(ListConfiguration.ReviewPublicId, null));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var board = await ownerClient.GetFromJsonAsync<BoardContentDto>($"/v1/boards/{ProductRoadmapBoardPublicId}");
        var review = board!.Lists.Single(l => l.PublicId == ListConfiguration.ReviewPublicId);
        Assert.True(review.CardCount > review.WipLimit);
    }

    [Fact]
    public async Task MoveCard_TwoSuccessiveMoves_NeitherIsRejected_LastWriteWins()
    {
        using var ownerClient = await FixtureOwnerClientAsync();
        var card = await CreateCardAsync(ownerClient, "ConcurrentMoveCard");

        var firstMove = await ownerClient.PostAsJsonAsync(
            $"/v1/cards/{card.PublicId}/move", new MoveCardRequestBody(ListConfiguration.DesignPublicId, null));
        Assert.Equal(HttpStatusCode.NoContent, firstMove.StatusCode);

        // No If-Match is ever sent for a move (ADR-21) — a second caller unaware of the
        // first move must still succeed, never 409.
        var secondMove = await ownerClient.PostAsJsonAsync(
            $"/v1/cards/{card.PublicId}/move", new MoveCardRequestBody(ListConfiguration.ReviewPublicId, null));
        Assert.Equal(HttpStatusCode.NoContent, secondMove.StatusCode);

        var detail = await ownerClient.GetFromJsonAsync<CardDetailDto>($"/v1/cards/{card.PublicId}");
        Assert.Equal(ListConfiguration.ReviewPublicId, detail!.ListPublicId);
    }

    [Fact]
    public async Task MoveCard_ThenFieldEdit_BothPersist_NeitherErasesTheOther()
    {
        // specs/008-realtime-sync/tasks.md T022 (FR-006): a card move (list/position only,
        // ADR-21's precondition-free ExecuteUpdateAsync) and a field edit (Title/
        // Description, RowVersion-guarded) touch disjoint columns — confirm persisting one
        // never reverts the other's already-committed value.
        using var ownerClient = await FixtureOwnerClientAsync();
        var card = await CreateCardAsync(ownerClient, "MoveThenEditCard");

        var moveResponse = await ownerClient.PostAsJsonAsync(
            $"/v1/cards/{card.PublicId}/move", new MoveCardRequestBody(ListConfiguration.DesignPublicId, null));
        Assert.Equal(HttpStatusCode.NoContent, moveResponse.StatusCode);

        // The field edit re-fetches its precondition after the move, exactly as a real
        // client would before typing into an already-open field — RowVersion is a
        // whole-row SQL Server rowversion (CardConfiguration.cs), so it advances on the
        // move even though the move never touches Title/Description.
        var postMoveGet = await ownerClient.GetAsync($"/v1/cards/{card.PublicId}");
        var postMoveEtag = postMoveGet.Headers.ETag!.Tag;

        var editRequest = new HttpRequestMessage(HttpMethod.Patch, $"/v1/cards/{card.PublicId}")
        {
            Content = JsonContent.Create(new { title = "Edited after move", description = "Edited description" }),
        };
        editRequest.Headers.TryAddWithoutValidation("If-Match", postMoveEtag);
        Assert.Equal(HttpStatusCode.OK, (await ownerClient.SendAsync(editRequest)).StatusCode);

        var finalDetail = await ownerClient.GetFromJsonAsync<CardDetailDto>($"/v1/cards/{card.PublicId}");
        Assert.Equal(ListConfiguration.DesignPublicId, finalDetail!.ListPublicId);
        Assert.Equal("Edited after move", finalDetail.Title);
        Assert.Equal("Edited description", finalDetail.Description);
    }

    [Fact]
    public async Task MoveCard_CrossBoardListPublicId_Returns400()
    {
        using var ownerClient = await FixtureOwnerClientAsync();
        var card = await CreateCardAsync(ownerClient, "CrossBoardMoveCard");

        var response = await ownerClient.PostAsJsonAsync(
            $"/v1/cards/{card.PublicId}/move", new MoveCardRequestBody(ListConfiguration.MarketingToDoPublicId, null));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MoveCard_BeforeCardFromDifferentList_Returns400()
    {
        using var ownerClient = await FixtureOwnerClientAsync();
        var card = await CreateCardAsync(ownerClient, "BeforeCardMismatchCard");
        var foreignSibling = await CreateCardAsync(ownerClient, "ForeignSibling", ListConfiguration.MarketingToDoPublicId);

        var response = await ownerClient.PostAsJsonAsync(
            $"/v1/cards/{card.PublicId}/move", new MoveCardRequestBody(ListConfiguration.DesignPublicId, foreignSibling.PublicId));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task MoveCard_NonMember_Returns404()
    {
        using var ownerClient = await FixtureOwnerClientAsync();
        var card = await CreateCardAsync(ownerClient, "NonMemberMoveCard");

        using var client = _factory.CreateClient();
        var caller = await SignUpAsync(client, "NonMemberMove");
        Authorize(client, caller.Token);

        var response = await client.PostAsJsonAsync(
            $"/v1/cards/{card.PublicId}/move", new MoveCardRequestBody(ListConfiguration.DesignPublicId, null));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task MoveCard_Observer_Returns403()
    {
        using var ownerClient = await FixtureOwnerClientAsync();
        var card = await CreateCardAsync(ownerClient, "ObserverMoveCard");
        using var observerClient = await InvitedClientAsync(ownerClient, "MoveObserver", "Observer");

        var response = await observerClient.PostAsJsonAsync(
            $"/v1/cards/{card.PublicId}/move", new MoveCardRequestBody(ListConfiguration.DesignPublicId, null));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
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
