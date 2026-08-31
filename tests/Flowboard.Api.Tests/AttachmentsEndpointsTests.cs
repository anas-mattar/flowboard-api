// Verifies the provider side of specs/009-card-attachments/contracts/attachments-api.md (US1).
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
public sealed class AttachmentsEndpointsTests : IAsyncLifetime
{
    private const string FixtureOwnerEmail = UserConfiguration.FixtureOwnerEmail;
    private const string FixtureOwnerPassword = "FixtureOwner!2026";
    private const string DefaultPassword = "correct horse battery staple";
    private static readonly Guid BacklogListPublicId = ListConfiguration.BacklogPublicId;

    private readonly FlowboardApiFactory _factory;
    private readonly List<string> _emails = [];
    private readonly List<Guid> _cardPublicIds = [];

    public AttachmentsEndpointsTests(FlowboardApiFactory factory) => _factory = factory;

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
            db.Attachments.RemoveRange(await db.Attachments.IgnoreQueryFilters().Where(a => cardIds.Contains(a.CardId)).ToListAsync());
            db.ActivityEvents.RemoveRange(await db.ActivityEvents.IgnoreQueryFilters().Where(e => cardIds.Contains(e.CardId)).ToListAsync());
            db.Cards.RemoveRange(await db.Cards.IgnoreQueryFilters().Where(c => cardIds.Contains(c.Id)).ToListAsync());
            await db.SaveChangesAsync();
        }

        var users = await db.Users.Where(u => _emails.Contains(u.Email)).ToListAsync();
        var userIds = users.Select(u => u.Id).ToList();
        if (userIds.Count > 0)
        {
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
            $"/v1/boards/{BoardConfiguration.ProductRoadmapBoardPublicId}/invitations", new InviteRequestBody(invitee.User.Email, role));
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

    private static async Task<HttpResponseMessage> UploadFileAsync(
        HttpClient client, Guid cardPublicId, string fileName, string contentType, byte[] bytes)
    {
        using var content = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(fileContent, "file", fileName);
        return await client.PostAsync($"/v1/cards/{cardPublicId}/attachments", content);
    }

    // ── Upload (T014) ───────────────────────────────────────────────────────

    [Fact]
    public async Task Upload_BoardAdmin_Succeeds_AndAppearsInCardDetail()
    {
        using var ownerClient = await FixtureOwnerClientAsync();
        var card = await CreateCardAsync(ownerClient, "UploadAdminCard");

        var response = await UploadFileAsync(ownerClient, card.PublicId, "wireframe-v3.pdf", "application/pdf", "%PDF-1.4 fake"u8.ToArray());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var uploaded = (await response.Content.ReadFromJsonAsync<AttachmentDetailDto>())!;
        Assert.Equal("wireframe-v3.pdf", uploaded.FileName);

        var detail = await ownerClient.GetFromJsonAsync<CardDetailDto>($"/v1/cards/{card.PublicId}");
        var entry = Assert.Single(detail!.Attachments);
        Assert.Equal(uploaded.PublicId, entry.PublicId);
        Assert.Equal("wireframe-v3.pdf", entry.FileName);

        var activity = await ownerClient.GetFromJsonAsync<CursorPage<ActivityEntryDto>>($"/v1/cards/{card.PublicId}/activity");
        Assert.Contains(activity!.Items, e => e.Type == ActivityEventType.AttachmentAdded);
    }

    [Fact]
    public async Task Upload_BoardMember_Succeeds()
    {
        using var ownerClient = await FixtureOwnerClientAsync();
        var card = await CreateCardAsync(ownerClient, "UploadMemberCard");
        using var memberClient = await InvitedClientAsync(ownerClient, "UploadMember", "BoardMember");

        var response = await UploadFileAsync(memberClient, card.PublicId, "notes.txt", "text/plain", "some notes"u8.ToArray());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Upload_Observer_Returns403()
    {
        using var ownerClient = await FixtureOwnerClientAsync();
        var card = await CreateCardAsync(ownerClient, "UploadObserverCard");
        using var observerClient = await InvitedClientAsync(ownerClient, "UploadObserver", "Observer");

        var response = await UploadFileAsync(observerClient, card.PublicId, "notes.txt", "text/plain", "nope"u8.ToArray());
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Upload_NonMember_Returns404()
    {
        using var ownerClient = await FixtureOwnerClientAsync();
        var card = await CreateCardAsync(ownerClient, "UploadNonMemberCard");

        using var client = _factory.CreateClient();
        var caller = await SignUpAsync(client, "UploadNonMember");
        Authorize(client, caller.Token);

        var response = await UploadFileAsync(client, card.PublicId, "notes.txt", "text/plain", "nope"u8.ToArray());
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Upload_BlockedExtension_Returns400()
    {
        using var ownerClient = await FixtureOwnerClientAsync();
        var card = await CreateCardAsync(ownerClient, "UploadBlockedExtensionCard");

        var response = await UploadFileAsync(ownerClient, card.PublicId, "installer.exe", "application/octet-stream", "MZ"u8.ToArray());
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Upload_OverSizeLimit_Returns400()
    {
        using var ownerClient = await FixtureOwnerClientAsync();
        var card = await CreateCardAsync(ownerClient, "UploadOverSizeCard");

        var oversized = new byte[(25 * 1024 * 1024) + 1];
        var response = await UploadFileAsync(ownerClient, card.PublicId, "huge.bin", "application/octet-stream", oversized);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ── Download (T015) ─────────────────────────────────────────────────────

    [Fact]
    public async Task Download_EveryRole_Succeeds_WithCorrectContentTypeAndFilename()
    {
        using var ownerClient = await FixtureOwnerClientAsync();
        var card = await CreateCardAsync(ownerClient, "DownloadCard");
        var uploadResponse = await UploadFileAsync(ownerClient, card.PublicId, "report.csv", "text/csv", "a,b,c"u8.ToArray());
        var uploaded = (await uploadResponse.Content.ReadFromJsonAsync<AttachmentDetailDto>())!;

        using var memberClient = await InvitedClientAsync(ownerClient, "DownloadMember", "BoardMember");
        using var observerClient = await InvitedClientAsync(ownerClient, "DownloadObserver", "Observer");

        foreach (var client in new[] { ownerClient, memberClient, observerClient })
        {
            var response = await client.GetAsync($"/v1/attachments/{uploaded.PublicId}/content");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal("report.csv", response.Content.Headers.ContentDisposition?.FileName?.Trim('"'));
            Assert.Equal("a,b,c", await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task Download_NonMember_Returns404()
    {
        using var ownerClient = await FixtureOwnerClientAsync();
        var card = await CreateCardAsync(ownerClient, "DownloadNonMemberCard");
        var uploadResponse = await UploadFileAsync(ownerClient, card.PublicId, "secret.txt", "text/plain", "shh"u8.ToArray());
        var uploaded = (await uploadResponse.Content.ReadFromJsonAsync<AttachmentDetailDto>())!;

        using var client = _factory.CreateClient();
        var caller = await SignUpAsync(client, "DownloadNonMember");
        Authorize(client, caller.Token);

        var response = await client.GetAsync($"/v1/attachments/{uploaded.PublicId}/content");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Download_UnknownAttachment_Returns404()
    {
        using var ownerClient = await FixtureOwnerClientAsync();
        var response = await ownerClient.GetAsync($"/v1/attachments/{Guid.NewGuid()}/content");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── Remove (T018) ───────────────────────────────────────────────────────

    [Fact]
    public async Task Remove_ByUploader_Succeeds_WritesActivity_AndDownloadThen404()
    {
        using var ownerClient = await FixtureOwnerClientAsync();
        var card = await CreateCardAsync(ownerClient, "RemoveByUploaderCard");
        using var memberClient = await InvitedClientAsync(ownerClient, "RemoveUploaderMember", "BoardMember");

        var uploadResponse = await UploadFileAsync(memberClient, card.PublicId, "mine.txt", "text/plain", "mine"u8.ToArray());
        var uploaded = (await uploadResponse.Content.ReadFromJsonAsync<AttachmentDetailDto>())!;

        var removeResponse = await memberClient.DeleteAsync($"/v1/attachments/{uploaded.PublicId}");
        Assert.Equal(HttpStatusCode.NoContent, removeResponse.StatusCode);

        var downloadResponse = await ownerClient.GetAsync($"/v1/attachments/{uploaded.PublicId}/content");
        Assert.Equal(HttpStatusCode.NotFound, downloadResponse.StatusCode);

        var detail = await ownerClient.GetFromJsonAsync<CardDetailDto>($"/v1/cards/{card.PublicId}");
        Assert.Empty(detail!.Attachments);

        var activity = await ownerClient.GetFromJsonAsync<CursorPage<ActivityEntryDto>>($"/v1/cards/{card.PublicId}/activity");
        Assert.Contains(activity!.Items, e => e.Type == ActivityEventType.AttachmentRemoved);
    }

    [Fact]
    public async Task Remove_ByBoardAdmin_RemovingSomeoneElsesAttachment_Succeeds()
    {
        using var ownerClient = await FixtureOwnerClientAsync();
        var card = await CreateCardAsync(ownerClient, "RemoveByAdminCard");
        using var memberClient = await InvitedClientAsync(ownerClient, "RemoveAdminMember", "BoardMember");

        var uploadResponse = await UploadFileAsync(memberClient, card.PublicId, "theirs.txt", "text/plain", "theirs"u8.ToArray());
        var uploaded = (await uploadResponse.Content.ReadFromJsonAsync<AttachmentDetailDto>())!;

        var removeResponse = await ownerClient.DeleteAsync($"/v1/attachments/{uploaded.PublicId}");
        Assert.Equal(HttpStatusCode.NoContent, removeResponse.StatusCode);
    }

    [Fact]
    public async Task Remove_ByNonUploadingBoardMember_Returns403()
    {
        using var ownerClient = await FixtureOwnerClientAsync();
        var card = await CreateCardAsync(ownerClient, "RemoveForbiddenMemberCard");
        var uploadResponse = await UploadFileAsync(ownerClient, card.PublicId, "ownerfile.txt", "text/plain", "owner"u8.ToArray());
        var uploaded = (await uploadResponse.Content.ReadFromJsonAsync<AttachmentDetailDto>())!;

        using var otherMemberClient = await InvitedClientAsync(ownerClient, "RemoveForbiddenMember", "BoardMember");
        var removeResponse = await otherMemberClient.DeleteAsync($"/v1/attachments/{uploaded.PublicId}");
        Assert.Equal(HttpStatusCode.Forbidden, removeResponse.StatusCode);
    }

    [Fact]
    public async Task Remove_ByObserver_Returns403()
    {
        using var ownerClient = await FixtureOwnerClientAsync();
        var card = await CreateCardAsync(ownerClient, "RemoveObserverCard");
        var uploadResponse = await UploadFileAsync(ownerClient, card.PublicId, "ownerfile2.txt", "text/plain", "owner"u8.ToArray());
        var uploaded = (await uploadResponse.Content.ReadFromJsonAsync<AttachmentDetailDto>())!;

        using var observerClient = await InvitedClientAsync(ownerClient, "RemoveObserver", "Observer");
        var removeResponse = await observerClient.DeleteAsync($"/v1/attachments/{uploaded.PublicId}");
        Assert.Equal(HttpStatusCode.Forbidden, removeResponse.StatusCode);
    }

    [Fact]
    public async Task Remove_NonMember_Returns404()
    {
        using var ownerClient = await FixtureOwnerClientAsync();
        var card = await CreateCardAsync(ownerClient, "RemoveNonMemberCard");
        var uploadResponse = await UploadFileAsync(ownerClient, card.PublicId, "ownerfile3.txt", "text/plain", "owner"u8.ToArray());
        var uploaded = (await uploadResponse.Content.ReadFromJsonAsync<AttachmentDetailDto>())!;

        using var client = _factory.CreateClient();
        var caller = await SignUpAsync(client, "RemoveNonMember");
        Authorize(client, caller.Token);

        var removeResponse = await client.DeleteAsync($"/v1/attachments/{uploaded.PublicId}");
        Assert.Equal(HttpStatusCode.NotFound, removeResponse.StatusCode);
    }

    [Fact]
    public async Task Remove_UnknownAttachment_Returns404()
    {
        using var ownerClient = await FixtureOwnerClientAsync();
        var response = await ownerClient.DeleteAsync($"/v1/attachments/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
