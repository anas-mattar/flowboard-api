// plan.md ADR-9: per-request authorization, no cached role claims. Every board-scoped
// endpoint calls this before doing anything else (backend-rules.md — a valid token is not
// board access).
using Flowboard.Api.Data;
using Flowboard.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flowboard.Api.Services;

public sealed record BoardAccess(int BoardId, BoardRole Role, bool IsWorkspaceOwner);

public interface IBoardAccessService
{
    /// <summary>Resolves the caller's effective role on a board, or null if they have no
    /// access at all (workspace owner => implicit BoardAdmin; else an explicit BoardMember
    /// row; else null).</summary>
    Task<BoardAccess?> ResolveAsync(Guid boardPublicId, Guid callerUserPublicId, CancellationToken cancellationToken);
}

public sealed class BoardAccessService(FlowboardDbContext db) : IBoardAccessService
{
    public async Task<BoardAccess?> ResolveAsync(
        Guid boardPublicId, Guid callerUserPublicId, CancellationToken cancellationToken)
    {
        var board = await db.Boards
            .AsNoTracking()
            .Where(b => b.PublicId == boardPublicId)
            .Select(b => new { b.Id, WorkspaceOwnerPublicId = b.Workspace.Owner.PublicId })
            .FirstOrDefaultAsync(cancellationToken);

        if (board is null)
        {
            return null;
        }

        if (board.WorkspaceOwnerPublicId == callerUserPublicId)
        {
            return new BoardAccess(board.Id, BoardRole.BoardAdmin, IsWorkspaceOwner: true);
        }

        var membershipRole = await db.BoardMembers
            .AsNoTracking()
            .Where(m => m.BoardId == board.Id && m.User.PublicId == callerUserPublicId)
            .Select(m => (BoardRole?)m.Role)
            .FirstOrDefaultAsync(cancellationToken);

        return membershipRole is null
            ? null
            : new BoardAccess(board.Id, membershipRole.Value, IsWorkspaceOwner: false);
    }
}
