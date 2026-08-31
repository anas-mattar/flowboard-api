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
    public async Task MoveCard_TwoConcurrentMoves_ConvergeToOnePosition_NoDuplicateOrLostBroadcast()
    {
        // specs/008-realtime-sync/tasks.md T021 (FR-005, SC-003): two concurrent
        // MoveCardAsync calls on the same card (no If-Match precondition on move, ADR-21 —
        // last write wins) must converge to exactly one final list, and the hub must
        // deliver exactly one "card.moved" BoardEvent per accepted move — never a
        // duplicated or silently dropped broadcast.
        using var ownerClient = _factory.CreateClient();
        var owner = await SignUpAsync(ownerClient, "HubConcurrentMoveOwner");
        Authorize(ownerClient, owner.Token);
        var board = (await (await ownerClient.PostAsJsonAsync(
            "/v1/boards", new CreateBoardRequestBody("Hub Concurrent Move Board"))).Content.ReadFromJsonAsync<BoardCreatedDto>())!;
        var sourceListPublicId = board.Lists[0].PublicId;
        var destinationAPublicId = board.Lists[1].PublicId;
        var destinationBPublicId = board.Lists[2].PublicId;
        var card = (await (await ownerClient.PostAsJsonAsync(
            $"/v1/lists/{sourceListPublicId}/cards", new CreateCardRequestBody("Concurrent move card")))
            .Content.ReadFromJsonAsync<CardSummaryDto>())!;

        var token = await GetRealtimeTokenAsync(ownerClient, board.PublicId);
        var connection = BuildConnection(token);
        var movedEvents = new List<JsonElement>();
        connection.On<JsonElement>("BoardEvent", evt =>
        {
            if (evt.GetProperty("type").GetString() == "card.moved")
            {
                lock (movedEvents)
                {
                    movedEvents.Add(evt);
                }
            }
        });
        await connection.StartAsync();
        await connection.InvokeAsync("JoinBoard", board.PublicId.ToString());

        // Two callers move the same card to different lists at nearly the same time.
        var moveA = ownerClient.PostAsJsonAsync(
            $"/v1/cards/{card.PublicId}/move", new MoveCardRequestBody(destinationAPublicId, null));
        var moveB = ownerClient.PostAsJsonAsync(
            $"/v1/cards/{card.PublicId}/move", new MoveCardRequestBody(destinationBPublicId, null));
        var responses = await Task.WhenAll(moveA, moveB);
        Assert.All(responses, r => Assert.Equal(HttpStatusCode.NoContent, r.StatusCode));

        // Give the slower broadcast a moment to arrive.
        await Task.Delay(TimeSpan.FromSeconds(1));

        var boardContent = await ownerClient.GetFromJsonAsync<BoardContentDto>($"/v1/boards/{board.PublicId}");
        var listsWithCard = boardContent!.Lists.Where(l => l.Cards.Any(c => c.PublicId == card.PublicId)).ToList();
        var containingList = Assert.Single(listsWithCard);
        Assert.True(containingList.PublicId == destinationAPublicId || containingList.PublicId == destinationBPublicId);

        // Exactly two broadcasts were sent — one per accepted move — never lost, never
        // duplicated. Comparing the exact sorted multiset (not just "each name is one of
        // the two") also catches a bug that double-broadcasts one destination while
        // dropping the other, which a plain per-element containment check would miss.
        var destinationAName = boardContent.Lists.Single(l => l.PublicId == destinationAPublicId).Name;
        var destinationBName = boardContent.Lists.Single(l => l.PublicId == destinationBPublicId).Name;
        var receivedDestinations = movedEvents
            .Select(evt => evt.GetProperty("payload").GetProperty("toListName").GetString()!)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        var expectedDestinations = new[] { destinationAName, destinationBName }
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToList();
        Assert.Equal(expectedDestinations, receivedDestinations);
    }

    [Fact]
    public async Task UpdateCard_StaleIfMatch_RejectedSaveNeverBroadcasts()
    {
        // specs/008-realtime-sync/tasks.md T020 (FR-004): empirically confirms — via an
        // actual hub connection, not just the activity feed
        // (CardsEndpointTests.cs's UpdateCard_StaleIfMatch_RejectedSaveNeverPersistsOrBroadcasts
        // covers that half) — that a rejected concurrent field-edit save produces no
        // "card.renamed" broadcast at all, only the winning save's.
        using var ownerClient = _factory.CreateClient();
        var owner = await SignUpAsync(ownerClient, "HubStaleConflictOwner");
        Authorize(ownerClient, owner.Token);
        var board = (await (await ownerClient.PostAsJsonAsync(
            "/v1/boards", new CreateBoardRequestBody("Hub Stale Conflict Board"))).Content.ReadFromJsonAsync<BoardCreatedDto>())!;
        var listPublicId = board.Lists[0].PublicId;
        var card = (await (await ownerClient.PostAsJsonAsync(
            $"/v1/lists/{listPublicId}/cards", new CreateCardRequestBody("Hub stale conflict card")))
            .Content.ReadFromJsonAsync<CardSummaryDto>())!;

        var token = await GetRealtimeTokenAsync(ownerClient, board.PublicId);
        var connection = BuildConnection(token);
        var renamedEvents = new List<JsonElement>();
        connection.On<JsonElement>("BoardEvent", evt =>
        {
            if (evt.GetProperty("type").GetString() == "card.renamed")
            {
                lock (renamedEvents)
                {
                    renamedEvents.Add(evt);
                }
            }
        });
        await connection.StartAsync();
        await connection.InvokeAsync("JoinBoard", board.PublicId.ToString());

        var getResponse = await ownerClient.GetAsync($"/v1/cards/{card.PublicId}");
        var staleEtag = getResponse.Headers.ETag!.Tag;

        var winningPatch = new HttpRequestMessage(HttpMethod.Patch, $"/v1/cards/{card.PublicId}")
        {
            Content = JsonContent.Create(new { title = "Hub winning save" }),
        };
        winningPatch.Headers.TryAddWithoutValidation("If-Match", staleEtag);
        Assert.Equal(HttpStatusCode.OK, (await ownerClient.SendAsync(winningPatch)).StatusCode);

        var losingPatch = new HttpRequestMessage(HttpMethod.Patch, $"/v1/cards/{card.PublicId}")
        {
            Content = JsonContent.Create(new { title = "Hub losing save" }),
        };
        losingPatch.Headers.TryAddWithoutValidation("If-Match", staleEtag);
        Assert.Equal(HttpStatusCode.Conflict, (await ownerClient.SendAsync(losingPatch)).StatusCode);

        // Give any (incorrect) broadcast for the rejected save a moment to arrive.
        await Task.Delay(TimeSpan.FromSeconds(1));

        var onlyEvent = Assert.Single(renamedEvents);
        Assert.Equal("Hub winning save", onlyEvent.GetProperty("payload").GetProperty("title").GetString());
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

    [Fact]
    public async Task RemoveMember_ConcurrentDisconnectAtEvictionBoundary_ConvergesToNoAccessRegardlessOfRaceOutcome()
    {
        // human-pr-review.md (second-model adversarial review, 2026-08-31) disputed the
        // §7 investigation's conclusion, citing a plausible race between
        // BoardConnectionTracker.GetConnectionIds's snapshot (BoardEventPublisher.cs:45),
        // BoardHub.OnDisconnectedAsync's concurrent RemoveConnectionEverywhere cleanup, and
        // EvictConnectionsAsync's SendAsync/RemoveFromGroupAsync calls running against a
        // connection id that may already be gone by the time they execute — noting that a
        // completed SendAsync never proves client receipt. This test forces that exact race
        // deliberately (rather than relying on manual reproduction) and asserts the outcome
        // is safe under either interleaving: the DELETE that triggers eviction never
        // throws/500s even if the connection is dying at the same instant, and — this is
        // the actual FR-007 guarantee — a reconnect afterwards is always rejected, because
        // JoinBoard re-resolves access independently of whichever path removed the old
        // connection (invariant 5, "a valid token is not board access"). So even if the
        // eviction snapshot goes stale and the access.revoked push never reaches a dying
        // connection, the member can never end up with working board access again.
        using var ownerClient = _factory.CreateClient();
        var owner = await SignUpAsync(ownerClient, "HubRaceOwner");
        Authorize(ownerClient, owner.Token);
        var board = (await (await ownerClient.PostAsJsonAsync(
            "/v1/boards", new CreateBoardRequestBody("Hub Race Board"))).Content.ReadFromJsonAsync<BoardCreatedDto>())!;

        var (memberClient, member) = await InvitedClientAsync(ownerClient, board.PublicId, "HubRaceMember", "BoardMember");
        var memberToken = await GetRealtimeTokenAsync(memberClient, board.PublicId);

        var connection = BuildConnection(memberToken);
        await connection.StartAsync();
        await connection.InvokeAsync("JoinBoard", board.PublicId.ToString());

        // Race the member's own disconnect against the server-side eviction triggered by
        // removal — whichever ordering the runtime happens to pick, both operations must
        // complete cleanly.
        var removeTask = ownerClient.DeleteAsync($"/v1/boards/{board.PublicId}/members/{member.User.PublicId}");
        var stopTask = connection.StopAsync();
        await Task.WhenAll(removeTask, stopTask);

        Assert.Equal(HttpStatusCode.NoContent, (await removeTask).StatusCode);

        // Regardless of which side of the race won, reconnecting with the same
        // still-unexpired realtime token must be rejected — JoinBoard never trusts the
        // token's board scoping alone.
        var reconnected = BuildConnection(memberToken);
        await reconnected.StartAsync();
        try
        {
            await reconnected.InvokeAsync("JoinBoard", board.PublicId.ToString());
        }
        catch
        {
            // See JoinBoard_TokenBoardMismatch_ClosesConnection.
        }

        Assert.True(await WaitForCloseAsync(reconnected, TimeSpan.FromSeconds(10)));
    }

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(50);
        }

        return condition();
    }

    [Fact]
    public async Task Reconnect_RejoinsGroupAndReceivesSubsequentEvents_NoDuplicateDelivery()
    {
        // specs/008-realtime-sync/tasks.md T027 (FR-008, SC-004): a client that disconnects
        // and reconnects must explicitly re-JoinBoard (contracts/realtime-api.md's
        // onreconnected contract — a new connection does not implicitly restore group
        // membership) to keep receiving BoardEvent messages, and no event may be delivered
        // more than once, replayed, or dropped across the disconnect/reconnect boundary.
        //
        // Uses comment.added (payload carries a distinct `body` per call — unlike
        // card.created's empty payload) so delivery is asserted by identity and exact
        // ordered sequence, not just by count: a count-only assertion would pass even if an
        // event were duplicated while another was silently dropped.
        using var ownerClient = _factory.CreateClient();
        var owner = await SignUpAsync(ownerClient, "HubReconnectOwner");
        Authorize(ownerClient, owner.Token);
        var board = (await (await ownerClient.PostAsJsonAsync(
            "/v1/boards", new CreateBoardRequestBody("Hub Reconnect Board"))).Content.ReadFromJsonAsync<BoardCreatedDto>())!;
        var listPublicId = board.Lists[0].PublicId;
        var card = (await (await ownerClient.PostAsJsonAsync(
            $"/v1/lists/{listPublicId}/cards", new CreateCardRequestBody("Hub reconnect card")))
            .Content.ReadFromJsonAsync<CardSummaryDto>())!;

        var token = await GetRealtimeTokenAsync(ownerClient, board.PublicId);
        var connection = BuildConnection(token);
        var receivedBodies = new List<string>();
        connection.On<JsonElement>("BoardEvent", evt =>
        {
            if (evt.GetProperty("type").GetString() == "comment.added")
            {
                lock (receivedBodies)
                {
                    receivedBodies.Add(evt.GetProperty("payload").GetProperty("body").GetString()!);
                }
            }
        });

        await connection.StartAsync();
        var firstConnectionId = connection.ConnectionId;
        await connection.InvokeAsync("JoinBoard", board.PublicId.ToString());

        await ownerClient.PostAsJsonAsync(
            $"/v1/cards/{card.PublicId}/comments", new AddCommentRequestBody("before drop"));
        Assert.True(await WaitUntilAsync(
            () => { lock (receivedBodies) { return receivedBodies.Count >= 1; } },
            TimeSpan.FromSeconds(10)));
        // Exactly one identified pre-drop event arrived before the drop — an early duplicate
        // could otherwise silently satisfy a later ">= N" wait without being noticed.
        lock (receivedBodies)
        {
            Assert.Equal(new[] { "before drop" }, receivedBodies);
        }

        // Simulate a dropped connection.
        await connection.StopAsync();

        // A change made by another session while this client is disconnected must not be
        // queued for later replay — it simply cannot reach a connection that isn't there.
        await ownerClient.PostAsJsonAsync(
            $"/v1/cards/{card.PublicId}/comments", new AddCommentRequestBody("while offline"));
        await Task.Delay(TimeSpan.FromMilliseconds(500));

        // Reconnect and explicitly re-join, exactly as the frontend's onreconnected handler
        // does (contracts/realtime-api.md). Assert SignalR actually assigned a new
        // connection id — the premise this test relies on (old group membership belonged to
        // a connection that no longer exists) — rather than assuming it.
        await connection.StartAsync();
        var secondConnectionId = connection.ConnectionId;
        Assert.NotNull(firstConnectionId);
        Assert.NotNull(secondConnectionId);
        Assert.NotEqual(firstConnectionId, secondConnectionId);
        await connection.InvokeAsync("JoinBoard", board.PublicId.ToString());

        await ownerClient.PostAsJsonAsync(
            $"/v1/cards/{card.PublicId}/comments", new AddCommentRequestBody("after reconnect"));
        Assert.True(await WaitUntilAsync(
            () => { lock (receivedBodies) { return receivedBodies.Count >= 2; } },
            TimeSpan.FromSeconds(10)));

        // Exactly the pre-drop and post-reconnect comments were delivered, in order, each
        // exactly once; the while-offline comment was never delivered (no replay/queueing)
        // and nothing was duplicated.
        await Task.Delay(TimeSpan.FromMilliseconds(500));
        lock (receivedBodies)
        {
            Assert.Equal(new[] { "before drop", "after reconnect" }, receivedBodies);
        }
    }
}
