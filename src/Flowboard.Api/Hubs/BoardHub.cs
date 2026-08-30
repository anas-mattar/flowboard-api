// contracts/realtime-api.md — the project's first SignalR hub (backend-rules.md's
// pre-existing "Realtime (SignalR)" section). Connection auth requires the "RealtimeOnly"
// policy (Program.cs — purpose == "realtime"), so a normal 14-day session JWT never
// reaches a hub method. JoinBoard re-resolves the caller's current role via
// IBoardAccessService rather than trusting the token's board scoping alone — invariant 5,
// "a valid token is not board access."
using Flowboard.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace Flowboard.Api.Hubs;

[Authorize(Policy = "RealtimeOnly")]
public sealed class BoardHub(IBoardAccessService boardAccess, BoardConnectionTracker connectionTracker) : Hub
{
    public async Task JoinBoard(string boardPublicId)
    {
        var tokenBoardId = Context.User?.GetRealtimeBoardId();
        var callerPublicId = Context.User?.GetUserPublicId();
        if (tokenBoardId is null || callerPublicId is null
            || !Guid.TryParse(boardPublicId, out var requestedBoardId)
            || requestedBoardId != tokenBoardId)
        {
            Context.Abort();
            return;
        }

        var access = await boardAccess.ResolveAsync(requestedBoardId, callerPublicId.Value, Context.ConnectionAborted);
        if (access is null)
        {
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(requestedBoardId));
        connectionTracker.AddConnection(requestedBoardId, callerPublicId.Value, Context.ConnectionId);
    }

    public async Task LeaveBoard(string boardPublicId)
    {
        if (!Guid.TryParse(boardPublicId, out var parsedBoardId))
        {
            return;
        }

        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(parsedBoardId));

        var callerPublicId = Context.User?.GetUserPublicId();
        if (callerPublicId is { } userPublicId)
        {
            connectionTracker.RemoveConnection(parsedBoardId, userPublicId, Context.ConnectionId);
        }
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        connectionTracker.RemoveConnectionEverywhere(Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    internal static string GroupName(Guid boardPublicId) => $"board:{boardPublicId}";
}
