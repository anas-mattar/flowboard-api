// Provider side of specs/005-drag-drop-ordering/contracts/move-api.md.
using Flowboard.Api.Data;
using Flowboard.Api.Domain;
using Flowboard.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flowboard.Api.Services;

public sealed record MoveListCommand(Guid? BeforeListPublicId);

public interface IListService
{
    Task<Result<Unit>> MoveListAsync(
        Guid listPublicId, Guid callerPublicId, MoveListCommand command, CancellationToken cancellationToken);
}

public sealed class ListService(FlowboardDbContext db, IBoardAccessService boardAccess) : IListService
{
    public async Task<Result<Unit>> MoveListAsync(
        Guid listPublicId, Guid callerPublicId, MoveListCommand command, CancellationToken cancellationToken)
    {
        var list = await db.Lists
            .Include(l => l.Board)
            .FirstOrDefaultAsync(l => l.PublicId == listPublicId, cancellationToken);
        if (list is null)
        {
            return Failure.NotFound();
        }

        var access = await boardAccess.ResolveAsync(list.Board.PublicId, callerPublicId, cancellationToken);
        if (access is null)
        {
            return Failure.NotFound();
        }
        if (!CanMutate(access.Role))
        {
            return Failure.Forbidden();
        }

        double newPosition;
        if (command.BeforeListPublicId is { } beforeListPublicId)
        {
            var beforeSibling = await db.Lists
                .Where(l => l.PublicId == beforeListPublicId)
                .Select(l => new { l.Id, l.BoardId, l.Position })
                .FirstOrDefaultAsync(cancellationToken);
            if (beforeSibling is null)
            {
                return Failure.NotFound();
            }
            if (beforeSibling.BoardId != list.BoardId)
            {
                return Failure.Validation("Validation failed",
                    new Dictionary<string, string[]> { ["beforeListPublicId"] = ["This list belongs to a different board."] });
            }

            var precedingPosition = await db.Lists
                .Where(l => l.BoardId == list.BoardId && l.Id != list.Id && l.Position < beforeSibling.Position)
                .OrderByDescending(l => l.Position)
                .Select(l => (double?)l.Position)
                .FirstOrDefaultAsync(cancellationToken);
            newPosition = Ordering.InsertBetween(precedingPosition ?? 0, beforeSibling.Position);
        }
        else
        {
            var lastPosition = await db.Lists
                .Where(l => l.BoardId == list.BoardId && l.Id != list.Id)
                .OrderByDescending(l => l.Position)
                .Select(l => (double?)l.Position)
                .FirstOrDefaultAsync(cancellationToken);
            newPosition = Ordering.Append(lastPosition);
        }

        list.Position = newPosition;
        list.UpdatedDate = DateTime.UtcNow;
        list.UpdatedBy = callerPublicId.ToString();

        await db.SaveChangesAsync(cancellationToken);
        return Result<Unit>.Success(Unit.Value);
    }

    private static bool CanMutate(BoardRole role) => role is BoardRole.BoardAdmin or BoardRole.BoardMember;
}
