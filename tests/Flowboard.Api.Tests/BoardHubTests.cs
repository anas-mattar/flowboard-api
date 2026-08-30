// Verifies contracts/realtime-api.md — BoardHub's JoinBoard/LeaveBoard connection
// authorization, live BoardEvent broadcast shape (data-model.md's BoardRealtimeEvent),
// and the eviction path (research.md R-7, FR-007).
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Flowboard.Api.Data;
using Flowboard.Api.Domain;
using Flowboard.Api.Endpoints;
using Flowboard.Api.Services;
using Flowboard.Api.Tests.TestFixtures;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;

namespace Flowboard.Api.Tests;

[Collection(FlowboardApiCollection.Name)]
public sealed class BoardHubTests : IAsyncLifetime
{
    private const string DefaultPassword = "correct horse battery staple";

    private readonly FlowboardApiFactory _factory;
    private readonly List<string> _emails = [];
    private readonly List<HubConnection> _connections = [];

    public BoardHubTests(FlowboardApiFactory factory) => _factory = factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var connection in _connections)
        {
            await connection.DisposeAsync();
        }

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

    private async Task<(HttpClient Client, AuthResponse Invitee)> InvitedClientAsync(
        HttpClient ownerClient, Guid boardPublicId, string label, string role)
    {
        var signupClient = _factory.CreateClient();
        var invitee = await SignUpAsync(signupClient, label);
        var invite = await ownerClient.PostAsJsonAsync(
            $"/v1/boards/{boardPublicId}/invitations", new InviteRequestBody(invitee.User.Email, role));
        Assert.Equal(HttpStatusCode.Created, invite.StatusCode);

        var client = _factory.CreateClient();
        Authorize(client, invitee.Token);
        return (client, invitee);
    }

    private sealed record RealtimeTokenResponse(string Token, DateTime ExpiresAt);

    private static async Task<string> GetRealtimeTokenAsync(HttpClient client, Guid boardPublicId)
    {
        var response = await client.PostAsync($"/v1/boards/{boardPublicId}/realtime-token", content: null);
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<RealtimeTokenResponse>();
        return body!.Token;
    }

    // TestServer has no real socket, so the SignalR client must fall back to LongPolling
    // (plain HTTP) rather than WebSockets — routed through the same in-memory handler every
    // other test in this suite uses via _factory.CreateClient().
    private HubConnection BuildConnection(string token)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(_factory.Server.BaseAddress, "hubs/board"), options =>
            {
                options.HttpMessageHandlerFactory = _ => _factory.Server.CreateHandler();
                options.Transports = HttpTransportType.LongPolling;
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            })
            .Build();
        _connections.Add(connection);
        return connection;
    }

    private static async Task<bool> WaitForCloseAsync(HubConnection connection, TimeSpan timeout)
    {
        var tcs = new TaskCompletionSource();
        connection.Closed += _ =>
        {
            tcs.TrySetResult();
            return Task.CompletedTask;
        };

        if (connection.State == HubConnectionState.Disconnected)
        {
            return true;
        }

        var completed = await Task.WhenAny(tcs.Task, Task.Delay(timeout));
        return completed == tcs.Task || connection.State == HubConnectionState.Disconnected;
    }

    [Fact]
    public async Task JoinBoard_MemberAndObserverSucceed_ConnectionStaysOpen()
    {
        using var ownerClient = _factory.CreateClient();
        var owner = await SignUpAsync(ownerClient, "HubJoinOwner");
        Authorize(ownerClient, owner.Token);
        var board = (await (await ownerClient.PostAsJsonAsync(
            "/v1/boards", new CreateBoardRequestBody("Hub Join Board"))).Content.ReadFromJsonAsync<BoardCreatedDto>())!;

        var (memberClient, _) = await InvitedClientAsync(ownerClient, board.PublicId, "HubJoinMember", "BoardMember");
        var (observerClient, _) = await InvitedClientAsync(ownerClient, board.PublicId, "HubJoinObserver", "Observer");

        var memberToken = await GetRealtimeTokenAsync(memberClient, board.PublicId);
        var observerToken = await GetRealtimeTokenAsync(observerClient, board.PublicId);

        var memberConnection = BuildConnection(memberToken);
        var observerConnection = BuildConnection(observerToken);
        await memberConnection.StartAsync();
        await observerConnection.StartAsync();

        await memberConnection.InvokeAsync("JoinBoard", board.PublicId.ToString());
        await observerConnection.InvokeAsync("JoinBoard", board.PublicId.ToString());

        Assert.Equal(HubConnectionState.Connected, memberConnection.State);
        Assert.Equal(HubConnectionState.Connected, observerConnection.State);
    }

    [Fact]
    public async Task JoinBoard_TokenBoardMismatch_ClosesConnection()
    {
        using var ownerClient = _factory.CreateClient();
        var owner = await SignUpAsync(ownerClient, "HubMismatchOwner");
        Authorize(ownerClient, owner.Token);
        var boardA = (await (await ownerClient.PostAsJsonAsync(
            "/v1/boards", new CreateBoardRequestBody("Hub Mismatch Board A"))).Content.ReadFromJsonAsync<BoardCreatedDto>())!;
        var boardB = (await (await ownerClient.PostAsJsonAsync(
            "/v1/boards", new CreateBoardRequestBody("Hub Mismatch Board B"))).Content.ReadFromJsonAsync<BoardCreatedDto>())!;

        var tokenForBoardA = await GetRealtimeTokenAsync(ownerClient, boardA.PublicId);

        var connection = BuildConnection(tokenForBoardA);
        await connection.StartAsync();

        // The token's boardId claim is boardA — asking to join boardB must abort, never
        // silently join (contracts/realtime-api.md: "a token is only ever good for the one
        // board it was minted for").
        try
        {
            await connection.InvokeAsync("JoinBoard", boardB.PublicId.ToString());
        }
        catch
        {
            // Abort() during the invocation can surface as a connection error here instead
            // of the Closed event below — either outcome proves the join was rejected.
        }

        Assert.True(await WaitForCloseAsync(connection, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task JoinBoard_AfterAccessRevoked_ReResolvesRoleAndCloses()
    {
        using var ownerClient = _factory.CreateClient();
        var owner = await SignUpAsync(ownerClient, "HubRevokedOwner");
        Authorize(ownerClient, owner.Token);
        var board = (await (await ownerClient.PostAsJsonAsync(
            "/v1/boards", new CreateBoardRequestBody("Hub Revoked Board"))).Content.ReadFromJsonAsync<BoardCreatedDto>())!;

        var (memberClient, member) = await InvitedClientAsync(ownerClient, board.PublicId, "HubRevokedMember", "BoardMember");

        // A still-unexpired token minted while access existed.
        var token = await GetRealtimeTokenAsync(memberClient, board.PublicId);

        var removeResponse = await ownerClient.DeleteAsync($"/v1/boards/{board.PublicId}/members/{member.User.PublicId}");
        Assert.Equal(HttpStatusCode.NoContent, removeResponse.StatusCode);

        var connection = BuildConnection(token);
        await connection.StartAsync();

        // Invariant 5: JoinBoard re-resolves the caller's role via IBoardAccessService
        // rather than trusting the token's board scoping alone — the token is still
        // cryptographically valid, but the role behind it is gone.
        try
        {
            await connection.InvokeAsync("JoinBoard", board.PublicId.ToString());
        }
        catch
        {
            // See JoinBoard_TokenBoardMismatch_ClosesConnection.
        }

        Assert.True(await WaitForCloseAsync(connection, TimeSpan.FromSeconds(10)));
    }

    [Fact]
    public async Task BoardEvent_AfterCommentAdded_MatchesPersistedActivityEvent()
    {
        using var ownerClient = _factory.CreateClient();
        var owner = await SignUpAsync(ownerClient, "HubBroadcastOwner");
        Authorize(ownerClient, owner.Token);
        var board = (await (await ownerClient.PostAsJsonAsync(
            "/v1/boards", new CreateBoardRequestBody("Hub Broadcast Board"))).Content.ReadFromJsonAsync<BoardCreatedDto>())!;
        var listPublicId = board.Lists[0].PublicId;
        var card = (await (await ownerClient.PostAsJsonAsync(
            $"/v1/lists/{listPublicId}/cards", new CreateCardRequestBody("Hub broadcast card")))
            .Content.ReadFromJsonAsync<CardSummaryDto>())!;

        var (observerClient, _) = await InvitedClientAsync(ownerClient, board.PublicId, "HubBroadcastObserver", "Observer");
        var observerToken = await GetRealtimeTokenAsync(observerClient, board.PublicId);
        var connection = BuildConnection(observerToken);

        var receivedEvent = new TaskCompletionSource<JsonElement>();
        connection.On<JsonElement>("BoardEvent", evt => receivedEvent.TrySetResult(evt));

        await connection.StartAsync();
        await connection.InvokeAsync("JoinBoard", board.PublicId.ToString());

        var commentResponse = await ownerClient.PostAsJsonAsync(
            $"/v1/cards/{card.PublicId}/comments", new AddCommentRequestBody("Hub broadcast comment body"));
        Assert.Equal(HttpStatusCode.Created, commentResponse.StatusCode);

        var completed = await Task.WhenAny(receivedEvent.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(receivedEvent.Task, completed);

        var evtJson = await receivedEvent.Task;
        Assert.Equal("comment.added", evtJson.GetProperty("type").GetString());
        Assert.Equal(board.PublicId, evtJson.GetProperty("boardPublicId").GetGuid());
        Assert.Equal(owner.User.PublicId, evtJson.GetProperty("actorPublicId").GetGuid());
        Assert.Equal("Hub broadcast comment body", evtJson.GetProperty("payload").GetProperty("body").GetString());

        // Same shape as the persisted ActivityEvent (invariant 1, FR-003).
        var activity = await ownerClient.GetFromJsonAsync<CursorPage<ActivityEntryDto>>($"/v1/cards/{card.PublicId}/activity");
        var persisted = Assert.Single(activity!.Items, a => a.Type == "comment.added");
        Assert.Equal(persisted.Payload.GetProperty("body").GetString(), evtJson.GetProperty("payload").GetProperty("body").GetString());
    }

    [Fact]
    public async Task RemoveMember_EvictsConnectedConnection_NoFurtherBoardEvents()
    {
        using var ownerClient = _factory.CreateClient();
        var owner = await SignUpAsync(ownerClient, "HubEvictOwner");
        Authorize(ownerClient, owner.Token);
        var board = (await (await ownerClient.PostAsJsonAsync(
            "/v1/boards", new CreateBoardRequestBody("Hub Evict Board"))).Content.ReadFromJsonAsync<BoardCreatedDto>())!;
        var listPublicId = board.Lists[0].PublicId;

        var (memberClient, member) = await InvitedClientAsync(ownerClient, board.PublicId, "HubEvictMember", "BoardMember");
        var memberToken = await GetRealtimeTokenAsync(memberClient, board.PublicId);

        var connection = BuildConnection(memberToken);
        var events = new List<JsonElement>();
        var revokedReceived = new TaskCompletionSource();
        connection.On<JsonElement>("BoardEvent", evt =>
        {
            events.Add(evt);
            if (evt.GetProperty("type").GetString() == "access.revoked")
            {
                revokedReceived.TrySetResult();
            }
        });

        await connection.StartAsync();
        await connection.InvokeAsync("JoinBoard", board.PublicId.ToString());

        var removeResponse = await ownerClient.DeleteAsync($"/v1/boards/{board.PublicId}/members/{member.User.PublicId}");
        Assert.Equal(HttpStatusCode.NoContent, removeResponse.StatusCode);

        var completed = await Task.WhenAny(revokedReceived.Task, Task.Delay(TimeSpan.FromSeconds(10)));
        Assert.Same(revokedReceived.Task, completed);

        // A subsequent board mutation must not reach the evicted connection.
        await ownerClient.PostAsJsonAsync($"/v1/lists/{listPublicId}/cards", new CreateCardRequestBody("Post-eviction card"));
        await Task.Delay(TimeSpan.FromSeconds(1));

        Assert.DoesNotContain(events, e => e.GetProperty("type").GetString() == "card.created");
    }
}
