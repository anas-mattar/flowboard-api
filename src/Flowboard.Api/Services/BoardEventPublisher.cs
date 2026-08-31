// contracts/realtime-api.md, data-model.md's BoardRealtimeEvent. Called explicitly, inline,
// after each mutating service method's SaveChangesAsync() succeeds (ADR-34,
// backend-rules.md's Realtime section — never before the commit).
using Flowboard.Api.Domain;
using Flowboard.Api.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace Flowboard.Api.Services;

public sealed record BoardRealtimeEvent(
    string Type, Guid BoardPublicId, DateTime OccurredAt, Guid ActorPublicId, string ActorDisplayName, object Payload);

public interface IBoardEventPublisher
{
    Task PublishAsync(
        Guid boardPublicId, string type, DateTime occurredAt, Guid actorPublicId, string actorDisplayName,
        object payload, CancellationToken cancellationToken);

    /// <summary>research.md R-7, ADR-38: evicts one user's tracked connections to a board
    /// (BoardMembershipService.RemoveMemberAsync) — sends "access.revoked" to those
    /// connections only, then removes them from the board's group.</summary>
    Task EvictUserAsync(Guid boardPublicId, Guid userPublicId, CancellationToken cancellationToken);

    /// <summary>Same as <see cref="EvictUserAsync"/>, but for every connection currently
    /// tracked on the board — used when the whole board becomes inaccessible
    /// (BoardContentService's archive/delete path).</summary>
    Task EvictBoardAsync(Guid boardPublicId, CancellationToken cancellationToken);
}

public sealed class BoardEventPublisher(IHubContext<BoardHub> hub, BoardConnectionTracker connectionTracker)
    : IBoardEventPublisher
{
    private const string ClientMethod = "BoardEvent";

    public Task PublishAsync(
        Guid boardPublicId, string type, DateTime occurredAt, Guid actorPublicId, string actorDisplayName,
        object payload, CancellationToken cancellationToken)
    {
        var evt = new BoardRealtimeEvent(type, boardPublicId, occurredAt, actorPublicId, actorDisplayName, payload);
        return hub.Clients.Group(BoardHub.GroupName(boardPublicId)).SendAsync(ClientMethod, evt, cancellationToken);
    }

    public async Task EvictUserAsync(Guid boardPublicId, Guid userPublicId, CancellationToken cancellationToken)
    {
        var connectionIds = connectionTracker.GetConnectionIds(boardPublicId, userPublicId);
        if (connectionIds.Count == 0)
        {
            return;
        }

        await EvictConnectionsAsync(boardPublicId, userPublicId, connectionIds, cancellationToken);
        connectionTracker.RemoveAllForBoardUser(boardPublicId, userPublicId);
    }

    public async Task EvictBoardAsync(Guid boardPublicId, CancellationToken cancellationToken)
    {
        var connectionIds = connectionTracker.GetBoardConnectionIds(boardPublicId);
        if (connectionIds.Count == 0)
        {
            return;
        }

        await EvictConnectionsAsync(boardPublicId, actorPublicId: Guid.Empty, connectionIds, cancellationToken);
        connectionTracker.RemoveAllForBoard(boardPublicId);
    }

    private async Task EvictConnectionsAsync(
        Guid boardPublicId, Guid actorPublicId, IReadOnlyList<string> connectionIds, CancellationToken cancellationToken)
    {
        var evt = new BoardRealtimeEvent(
            RealtimeEventType.AccessRevoked, boardPublicId, DateTime.UtcNow, actorPublicId, string.Empty, new { });
        await hub.Clients.Clients(connectionIds).SendAsync(ClientMethod, evt, cancellationToken);

        foreach (var connectionId in connectionIds)
        {
            await hub.Groups.RemoveFromGroupAsync(connectionId, BoardHub.GroupName(boardPublicId), cancellationToken);
        }
    }
}
