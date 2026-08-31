// Deterministic, unit-level coverage of the race the second-model adversarial review
// (specs/008-realtime-sync/human-pr-review.md, re-review 2026-08-31) disputed:
// BoardHubTests.RemoveMember_ConcurrentDisconnectAtEvictionBoundary_ConvergesToNoAccessRegardlessOfRaceOutcome
// races real async operations against a real (if in-memory) SignalR transport, so it can
// prove the outcome is *stable* but not that the disputed interleaving — a connection
// disappearing from BoardConnectionTracker between EvictUserAsync's snapshot and its
// SendAsync/RemoveFromGroupAsync calls — actually occurred. This file fakes
// IHubContext<BoardHub> so that interleaving can be forced exactly, on demand, instead of
// hoped for.
using Flowboard.Api.Domain;
using Flowboard.Api.Hubs;
using Flowboard.Api.Services;
using Microsoft.AspNetCore.SignalR;

namespace Flowboard.Api.Tests;

public sealed class BoardEventPublisherTests
{
    private sealed class FakeClientProxy : IClientProxy
    {
        public List<(string Method, object?[] Args)> Sent { get; } = [];

        public Func<string, object?[], Task>? OnSend { get; set; }

        public async Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default)
        {
            Sent.Add((method, args));
            if (OnSend is not null)
            {
                await OnSend(method, args);
            }
        }
    }

    private sealed class FakeHubClients : IHubClients
    {
        public FakeClientProxy ClientsProxy { get; } = new();

        public IReadOnlyList<string>? LastClientsIds { get; private set; }

        public IClientProxy All => throw new NotSupportedException();

        public IClientProxy AllExcept(IReadOnlyList<string> excludedConnectionIds) => throw new NotSupportedException();

        public IClientProxy Client(string connectionId) => throw new NotSupportedException();

        public IClientProxy Clients(IReadOnlyList<string> connectionIds)
        {
            LastClientsIds = connectionIds;
            return ClientsProxy;
        }

        public IClientProxy Group(string groupName) => throw new NotSupportedException();

        public IClientProxy GroupExcept(string groupName, IReadOnlyList<string> excludedConnectionIds) =>
            throw new NotSupportedException();

        public IClientProxy Groups(IReadOnlyList<string> groupNames) => throw new NotSupportedException();

        public IClientProxy OthersInGroup(string groupName) => throw new NotSupportedException();

        public IClientProxy User(string userId) => throw new NotSupportedException();

        public IClientProxy Users(IReadOnlyList<string> userIds) => throw new NotSupportedException();
    }

    private sealed class FakeGroupManager : IGroupManager
    {
        public List<(string ConnectionId, string GroupName)> Removed { get; } = [];

        public Task AddToGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task RemoveFromGroupAsync(string connectionId, string groupName, CancellationToken cancellationToken = default)
        {
            Removed.Add((connectionId, groupName));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeHubContext : IHubContext<BoardHub>
    {
        public FakeHubClients ClientsImpl { get; } = new();

        public FakeGroupManager GroupsImpl { get; } = new();

        public IHubClients Clients => ClientsImpl;

        public IGroupManager Groups => GroupsImpl;
    }

    [Fact]
    public async Task EvictUserAsync_ConnectionDisappearsFromTrackerDuringSend_StillAttemptsDeliveryAndCleansUpConsistently()
    {
        // The fake's SendAsync callback synchronously simulates BoardHub.OnDisconnectedAsync
        // firing on a different request context, exactly while EvictUserAsync's send for
        // this connection is in flight — the precise interleaving in dispute. This proves
        // BoardEventPublisher's own code (delivery attempt, group removal, tracker
        // bookkeeping) behaves correctly under that race. What a real SignalR transport
        // does with a connectionId that's mid-disconnect is an ASP.NET Core SignalR
        // framework guarantee, not application code, and is out of scope here.
        var boardPublicId = Guid.NewGuid();
        var userPublicId = Guid.NewGuid();
        const string connectionId = "conn-racing";

        var tracker = new BoardConnectionTracker();
        tracker.AddConnection(boardPublicId, userPublicId, connectionId);

        var hub = new FakeHubContext();
        hub.ClientsImpl.ClientsProxy.OnSend = (_, _) =>
        {
            tracker.RemoveConnectionEverywhere(connectionId);
            return Task.CompletedTask;
        };

        var publisher = new BoardEventPublisher(hub, tracker);

        await publisher.EvictUserAsync(boardPublicId, userPublicId, CancellationToken.None);

        // The delivery attempt still targeted the connection that was live at the moment
        // of the snapshot — eviction never silently skips a connection just because a
        // disconnect might be racing it.
        Assert.Equal([connectionId], hub.ClientsImpl.LastClientsIds);
        var sent = Assert.Single(hub.ClientsImpl.ClientsProxy.Sent);
        Assert.Equal("BoardEvent", sent.Method);
        var evt = Assert.IsType<BoardRealtimeEvent>(Assert.Single(sent.Args));
        Assert.Equal(RealtimeEventType.AccessRevoked, evt.Type);

        // Group cleanup still runs for the connection, even though it's already gone from
        // the tracker by the time this executes. BoardHub.GroupName is internal, so its
        // format ("board:{publicId}") is duplicated here rather than referenced directly.
        Assert.Equal([(connectionId, $"board:{boardPublicId}")], hub.GroupsImpl.Removed);

        // No stale tracker entry either way — RemoveConnectionEverywhere (mid-race) and
        // RemoveAllForBoardUser (post-eviction) must not leave anything inconsistent.
        Assert.Empty(tracker.GetConnectionIds(boardPublicId, userPublicId));
    }

    [Fact]
    public async Task EvictUserAsync_NoTrackedConnections_IsNoOp()
    {
        var hub = new FakeHubContext();
        var tracker = new BoardConnectionTracker();
        var publisher = new BoardEventPublisher(hub, tracker);

        await publisher.EvictUserAsync(Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(hub.ClientsImpl.ClientsProxy.Sent);
        Assert.Empty(hub.GroupsImpl.Removed);
    }
}
