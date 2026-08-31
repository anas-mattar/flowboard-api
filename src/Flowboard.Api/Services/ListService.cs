// Provider side of specs/005-drag-drop-ordering/contracts/move-api.md and
// specs/006-board-list-management/contracts/board-list-management-api.md.
using Flowboard.Api.Data;
using Flowboard.Api.Domain;
using Flowboard.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flowboard.Api.Services;

public sealed record MoveListCommand(Guid? BeforeListPublicId);

public sealed record ListCreatedDto(Guid PublicId, string Name, int? WipLimit, int CardCount);

public sealed record ListUpdateResult(string Name, int? WipLimit, byte[] RowVersion);

public sealed record UpdateListCommand(bool HasName, string? Name, bool HasWipLimit, int? WipLimit);

public interface IListService
{
    Task<Result<Unit>> MoveListAsync(
        Guid listPublicId, Guid callerPublicId, MoveListCommand command, CancellationToken cancellationToken);

    /// <summary>research.md R-4, ADR-28: appends via Ordering.Append, mirrors card creation.</summary>
    Task<Result<ListCreatedDto>> CreateListAsync(
        Guid boardPublicId, Guid callerPublicId, string name, CancellationToken cancellationToken);

    /// <summary>Combined rename + WIP-limit endpoint (US4 + US6) — at least one field
    /// required; a negative wipLimit or empty/whitespace name is rejected.</summary>
    Task<Result<ListUpdateResult>> UpdateListAsync(
        Guid listPublicId, Guid callerPublicId, UpdateListCommand command, byte[] ifMatchRowVersion,
        CancellationToken cancellationToken);

    /// <summary>Soft-deletes every non-deleted card in the list; a no-op (still success)
    /// on an already-empty list. The list itself is untouched.</summary>
    Task<Result<Unit>> ArchiveAllCardsAsync(Guid listPublicId, Guid callerPublicId, CancellationToken cancellationToken);

    /// <summary>research.md R-5: soft-deletes the list and, in the same SaveChangesAsync,
    /// every non-deleted card it currently holds.</summary>
    Task<Result<Unit>> DeleteListAsync(Guid listPublicId, Guid callerPublicId, CancellationToken cancellationToken);

    /// <summary>research.md R-1: ascending by DueAt, undated last, ties broken by current
    /// Position. ADR-21-style: rewrites Position via ExecuteUpdateAsync per card, bypassing
    /// the RowVersion concurrency check — last-write-wins, like card move.</summary>
    Task<Result<Unit>> SortByDueDateAsync(Guid listPublicId, Guid callerPublicId, CancellationToken cancellationToken);
}

public sealed class ListService(FlowboardDbContext db, IBoardAccessService boardAccess, IBoardEventPublisher realtime)
    : IListService
{
    private async Task<string> GetCallerDisplayNameAsync(Guid callerPublicId, CancellationToken cancellationToken) =>
        await db.Users.Where(u => u.PublicId == callerPublicId).Select(u => u.DisplayName).FirstAsync(cancellationToken);

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

        var now = DateTime.UtcNow;
        list.Position = newPosition;
        list.UpdatedDate = now;
        list.UpdatedBy = callerPublicId.ToString();

        await db.SaveChangesAsync(cancellationToken);

        var actorDisplayName = await GetCallerDisplayNameAsync(callerPublicId, cancellationToken);
        await realtime.PublishAsync(
            list.Board.PublicId, RealtimeEventType.ListMoved, now, callerPublicId, actorDisplayName,
            new { listPublicId }, cancellationToken);

        return Result<Unit>.Success(Unit.Value);
    }

    public async Task<Result<ListCreatedDto>> CreateListAsync(
        Guid boardPublicId, Guid callerPublicId, string name, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
        {
            return Failure.Validation("Validation failed",
                new Dictionary<string, string[]> { ["name"] = ["Name must be 1-200 characters."] });
        }

        var access = await boardAccess.ResolveAsync(boardPublicId, callerPublicId, cancellationToken);
        if (access is null)
        {
            return Failure.NotFound();
        }
        if (!CanMutate(access.Role))
        {
            return Failure.Forbidden();
        }

        var lastPosition = await db.Lists
            .Where(l => l.BoardId == access.BoardId)
            .OrderByDescending(l => l.Position)
            .Select(l => (double?)l.Position)
            .FirstOrDefaultAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var list = new List
        {
            PublicId = Guid.NewGuid(),
            BoardId = access.BoardId,
            Name = name.Trim(),
            Position = Ordering.Append(lastPosition),
            CreatedDate = now,
            CreatedBy = callerPublicId.ToString(),
        };
        db.Lists.Add(list);
        await db.SaveChangesAsync(cancellationToken);

        var actorDisplayName = await GetCallerDisplayNameAsync(callerPublicId, cancellationToken);
        await realtime.PublishAsync(
            boardPublicId, RealtimeEventType.ListCreated, now, callerPublicId, actorDisplayName,
            new { listPublicId = list.PublicId }, cancellationToken);

        return Result<ListCreatedDto>.Success(new ListCreatedDto(list.PublicId, list.Name, list.WipLimit, CardCount: 0));
    }

    public async Task<Result<ListUpdateResult>> UpdateListAsync(
        Guid listPublicId, Guid callerPublicId, UpdateListCommand command, byte[] ifMatchRowVersion,
        CancellationToken cancellationToken)
    {
        if (!command.HasName && !command.HasWipLimit)
        {
            return Failure.Validation("Validation failed",
                new Dictionary<string, string[]> { ["body"] = ["At least one of name or wipLimit must be supplied."] });
        }
        if (command.HasName && string.IsNullOrWhiteSpace(command.Name))
        {
            return Failure.Validation("Validation failed",
                new Dictionary<string, string[]> { ["name"] = ["Name must be 1-200 characters."] });
        }
        if (command.HasWipLimit && command.WipLimit is < 0)
        {
            return Failure.Validation("Validation failed",
                new Dictionary<string, string[]> { ["wipLimit"] = ["wipLimit must not be negative."] });
        }

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

        if (command.HasName)
        {
            list.Name = command.Name!.Trim();
        }
        if (command.HasWipLimit)
        {
            // invariant 3: never touches any card, regardless of how far this puts the
            // list over capacity.
            list.WipLimit = command.WipLimit;
        }
        var now = DateTime.UtcNow;
        list.UpdatedDate = now;
        list.UpdatedBy = callerPublicId.ToString();

        db.Entry(list).Property(x => x.RowVersion).OriginalValue = ifMatchRowVersion;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Failure.Conflict("This list was changed by someone else.");
        }

        var actorDisplayName = await GetCallerDisplayNameAsync(callerPublicId, cancellationToken);
        if (command.HasName)
        {
            await realtime.PublishAsync(
                list.Board.PublicId, RealtimeEventType.ListRenamed, now, callerPublicId, actorDisplayName,
                new { listPublicId }, cancellationToken);
        }
        if (command.HasWipLimit)
        {
            await realtime.PublishAsync(
                list.Board.PublicId, RealtimeEventType.ListWipLimitChanged, now, callerPublicId, actorDisplayName,
                new { listPublicId }, cancellationToken);
        }

        return Result<ListUpdateResult>.Success(new ListUpdateResult(list.Name, list.WipLimit, list.RowVersion));
    }

    public async Task<Result<Unit>> ArchiveAllCardsAsync(
        Guid listPublicId, Guid callerPublicId, CancellationToken cancellationToken)
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

        var now = DateTime.UtcNow;
        var cards = await db.Cards.Where(c => c.ListId == list.Id).ToListAsync(cancellationToken);
        foreach (var card in cards)
        {
            card.IsDeleted = true;
            card.DeletedDate = now;
            card.DeletedBy = callerPublicId.ToString();
        }

        await db.SaveChangesAsync(cancellationToken);

        // research.md R-5/ADR-35: no ActivityEvent counterpart, matching CardService's own
        // single-card archive (RealtimeEventType.CardArchived) — one live-only event per
        // archived card so every viewer's board content re-fetches (FR-013 accepts a burst
        // of invalidations converging via the existing query client de-duplication).
        if (cards.Count > 0)
        {
            var actorDisplayName = await GetCallerDisplayNameAsync(callerPublicId, cancellationToken);
            foreach (var card in cards)
            {
                await realtime.PublishAsync(
                    list.Board.PublicId, RealtimeEventType.CardArchived, now, callerPublicId, actorDisplayName,
                    new { cardPublicId = card.PublicId }, cancellationToken);
            }
        }

        return Result<Unit>.Success(Unit.Value);
    }

    public async Task<Result<Unit>> DeleteListAsync(
        Guid listPublicId, Guid callerPublicId, CancellationToken cancellationToken)
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

        var now = DateTime.UtcNow;

        // research.md R-5: cascade-archive every card the list currently holds, in the
        // same SaveChangesAsync as the list's own soft delete — never a physical delete.
        var cards = await db.Cards.Where(c => c.ListId == list.Id).ToListAsync(cancellationToken);
        foreach (var card in cards)
        {
            card.IsDeleted = true;
            card.DeletedDate = now;
            card.DeletedBy = callerPublicId.ToString();
        }

        list.IsDeleted = true;
        list.DeletedDate = now;
        list.DeletedBy = callerPublicId.ToString();

        await db.SaveChangesAsync(cancellationToken);

        // research.md R-5/ADR-35: no ActivityEvent counterpart for either the list's own
        // archive or its cascade-archived cards.
        var actorDisplayName = await GetCallerDisplayNameAsync(callerPublicId, cancellationToken);
        await realtime.PublishAsync(
            list.Board.PublicId, RealtimeEventType.ListArchived, now, callerPublicId, actorDisplayName,
            new { listPublicId }, cancellationToken);
        foreach (var card in cards)
        {
            await realtime.PublishAsync(
                list.Board.PublicId, RealtimeEventType.CardArchived, now, callerPublicId, actorDisplayName,
                new { cardPublicId = card.PublicId }, cancellationToken);
        }

        return Result<Unit>.Success(Unit.Value);
    }

    public async Task<Result<Unit>> SortByDueDateAsync(
        Guid listPublicId, Guid callerPublicId, CancellationToken cancellationToken)
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

        // research.md R-1: SQL Server's own NULL-first default must be overridden
        // explicitly; ties broken by each card's current Position for a stable re-sort.
        var cardIds = await db.Cards
            .Where(c => c.ListId == list.Id)
            .OrderBy(c => c.DueAt == null ? 1 : 0)
            .ThenBy(c => c.DueAt)
            .ThenBy(c => c.Position)
            .Select(c => c.Id)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var callerIdString = callerPublicId.ToString();
        double? lastPosition = null;

        // ADR-21-style: ExecuteUpdateAsync bypasses the change tracker's RowVersion
        // concurrency check (invariant 6 — last-write-wins for position rewrites, distinct
        // from If-Match field edits), matching CardService.MoveCardAsync exactly.
        foreach (var cardId in cardIds)
        {
            var position = Ordering.Append(lastPosition);
            lastPosition = position;

            await db.Cards
                .Where(c => c.Id == cardId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(c => c.Position, position)
                    .SetProperty(c => c.UpdatedDate, now)
                    .SetProperty(c => c.UpdatedBy, callerIdString),
                    cancellationToken);
        }

        return Result<Unit>.Success(Unit.Value);
    }

    private static bool CanMutate(BoardRole role) => role is BoardRole.BoardAdmin or BoardRole.BoardMember;
}
