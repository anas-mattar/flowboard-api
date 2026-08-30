// Provider side of specs/003-board-view-readonly/contracts/board-content-api.md and
// specs/006-board-list-management/contracts/board-list-management-api.md.
using System.Text;
using Flowboard.Api.Data;
using Flowboard.Api.Domain;
using Flowboard.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flowboard.Api.Services;

public sealed record BoardSummaryDto(Guid PublicId, string Name, string Color, bool Starred, int CardCount);

public sealed record BoardListSummaryDto(Guid PublicId, string Name);

public sealed record BoardCreatedDto(
    Guid PublicId, string Name, string Color, bool Starred, int CardCount, IReadOnlyList<BoardListSummaryDto> Lists);

public sealed record BoardUpdateResult(string Name, byte[] RowVersion);

public sealed record LabelSummaryDto(Guid PublicId, string Name, string Color);

public sealed record CardSummaryDto(
    Guid PublicId,
    string Title,
    DateTime? DueAt,
    string? DueStatus,
    bool HasDescription,
    int? ChecklistDone,
    int? ChecklistTotal,
    int CommentCount,
    IReadOnlyList<LabelSummaryDto> Labels,
    IReadOnlyList<MemberUserDto> Members);

public sealed record ListContentDto(
    Guid PublicId, string Name, int? WipLimit, int CardCount, string RowVersion, IReadOnlyList<CardSummaryDto> Cards);

public sealed record BoardContentDto(
    Guid PublicId, string Name, string Color, bool Starred, string RowVersion, IReadOnlyList<ListContentDto> Lists);

public interface IBoardContentService
{
    /// <summary>ADR-12 cursor pagination: null/empty cursor starts at page 1.</summary>
    Task<Result<CursorPage<BoardSummaryDto>>> ListBoardsAsync(
        Guid callerPublicId, string? cursor, int limit, CancellationToken cancellationToken);

    Task<Result<BoardContentDto>> GetBoardContentAsync(
        Guid boardPublicId, Guid callerPublicId, CancellationToken cancellationToken);

    /// <summary>research.md R-3, ADR-26: always the caller's own workspace, no eligibility
    /// check — every registered user owns exactly one.</summary>
    Task<Result<BoardCreatedDto>> CreateBoardAsync(
        Guid callerPublicId, string name, CancellationToken cancellationToken);

    /// <summary>ADR-25: CanManageBoard (BoardAdmin only), stricter than CanMutate.</summary>
    Task<Result<BoardUpdateResult>> UpdateBoardAsync(
        Guid boardPublicId, Guid callerPublicId, string name, byte[] ifMatchRowVersion, CancellationToken cancellationToken);

    Task<Result<Unit>> StarBoardAsync(Guid boardPublicId, Guid callerPublicId, CancellationToken cancellationToken);

    Task<Result<Unit>> UnstarBoardAsync(Guid boardPublicId, Guid callerPublicId, CancellationToken cancellationToken);

    /// <summary>ADR-25: CanManageBoard only. Soft-delete; does not cascade to lists/cards
    /// (research R-5's cascade is list-delete only).</summary>
    Task<Result<Unit>> DeleteBoardAsync(Guid boardPublicId, Guid callerPublicId, CancellationToken cancellationToken);
}

public sealed class BoardContentService(FlowboardDbContext db, IBoardAccessService boardAccess) : IBoardContentService
{
    // Prototype's own rotating palette (flowboard-prototype.html's `#newBoard` handler);
    // data-model.md: Color stays auto-assigned, never user-editable in this feature.
    private static readonly string[] ColorPalette = ["#3d6df0", "#8f5bff", "#22a06b", "#e2703a", "#c9372c"];

    private readonly record struct BoardsCursorKey(bool Starred, DateTime CreatedDate, int Id);

    public async Task<Result<CursorPage<BoardSummaryDto>>> ListBoardsAsync(
        Guid callerPublicId, string? cursor, int limit, CancellationToken cancellationToken)
    {
        BoardsCursorKey? key = null;
        if (!string.IsNullOrEmpty(cursor))
        {
            if (!TryDecodeCursor(cursor, out var decoded))
            {
                return Failure.Validation("Invalid cursor");
            }

            key = decoded;
        }

        var query = db.Boards
            .AsNoTracking()
            .Where(b => b.Workspace.Owner.PublicId == callerPublicId
                || db.BoardMembers.Any(m => m.BoardId == b.Id && m.User.PublicId == callerPublicId));

        if (key is { } k)
        {
            query = query.Where(b =>
                (k.Starred && !b.Starred)
                || (b.Starred == k.Starred
                    && (b.CreatedDate > k.CreatedDate || (b.CreatedDate == k.CreatedDate && b.Id > k.Id))));
        }

        var page = await query
            .OrderByDescending(b => b.Starred)
            .ThenBy(b => b.CreatedDate)
            .ThenBy(b => b.Id)
            .Take(limit + 1)
            .Select(b => new
            {
                b.PublicId,
                b.Name,
                b.Color,
                b.Starred,
                b.CreatedDate,
                b.Id,
                CardCount = db.Cards.Count(c => c.List.BoardId == b.Id),
            })
            .ToListAsync(cancellationToken);

        var hasMore = page.Count > limit;
        var trimmed = hasMore ? page.Take(limit).ToList() : page;
        var items = trimmed
            .Select(b => new BoardSummaryDto(b.PublicId, b.Name, b.Color, b.Starred, b.CardCount))
            .ToList();
        var nextCursor = hasMore ? EncodeCursor(trimmed[^1].Starred, trimmed[^1].CreatedDate, trimmed[^1].Id) : null;

        return Result<CursorPage<BoardSummaryDto>>.Success(
            new CursorPage<BoardSummaryDto> { Items = items, NextCursor = nextCursor });
    }

    public async Task<Result<BoardContentDto>> GetBoardContentAsync(
        Guid boardPublicId, Guid callerPublicId, CancellationToken cancellationToken)
    {
        var access = await boardAccess.ResolveAsync(boardPublicId, callerPublicId, cancellationToken);
        if (access is null)
        {
            return Failure.NotFound();
        }

        var board = await db.Boards
            .AsNoTracking()
            .Where(b => b.Id == access.BoardId)
            .Select(b => new { b.PublicId, b.Name, b.Color, b.Starred, b.RowVersion })
            .FirstAsync(cancellationToken);

        var lists = await db.Lists
            .AsNoTracking()
            .Where(l => l.BoardId == access.BoardId)
            .OrderBy(l => l.Position)
            .Select(l => new { l.Id, l.PublicId, l.Name, l.WipLimit, l.RowVersion })
            .ToListAsync(cancellationToken);
        var listIds = lists.Select(l => l.Id).ToList();

        var cards = await db.Cards
            .AsNoTracking()
            .Where(c => listIds.Contains(c.ListId))
            .OrderBy(c => c.Position)
            .Select(c => new { c.Id, c.ListId, c.PublicId, c.Title, c.Description, c.DueAt, c.DueComplete })
            .ToListAsync(cancellationToken);
        var cardIds = cards.Select(c => c.Id).ToList();

        var checklistTotals = await db.ChecklistItems
            .AsNoTracking()
            .Where(ci => cardIds.Contains(ci.CardId))
            .GroupBy(ci => ci.CardId)
            .Select(g => new { CardId = g.Key, Total = g.Count(), Done = g.Count(ci => ci.Done) })
            .ToDictionaryAsync(g => g.CardId, cancellationToken);

        var commentCounts = await db.Comments
            .AsNoTracking()
            .Where(c => cardIds.Contains(c.CardId))
            .GroupBy(c => c.CardId)
            .Select(g => new { CardId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(g => g.CardId, g => g.Count, cancellationToken);

        var labelsByCard = (await db.CardLabels
                .AsNoTracking()
                .Where(cl => cardIds.Contains(cl.CardId))
                .Select(cl => new { cl.CardId, cl.Label.PublicId, cl.Label.Name, cl.Label.Color })
                .ToListAsync(cancellationToken))
            .GroupBy(x => x.CardId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<LabelSummaryDto>)g
                .Select(x => new LabelSummaryDto(x.PublicId, x.Name, x.Color))
                .ToList());

        var membersByCard = (await db.CardMembers
                .AsNoTracking()
                .Where(cm => cardIds.Contains(cm.CardId))
                .Select(cm => new { cm.CardId, cm.User.PublicId, cm.User.DisplayName, cm.User.Initials, cm.User.AvatarColor })
                .ToListAsync(cancellationToken))
            .GroupBy(x => x.CardId)
            .ToDictionary(g => g.Key, g => (IReadOnlyList<MemberUserDto>)g
                .Select(x => new MemberUserDto(x.PublicId, x.DisplayName, x.Initials, x.AvatarColor))
                .ToList());

        var now = DateTime.UtcNow;
        var cardsByList = cards.ToLookup(c => c.ListId);

        var listDtos = lists.Select(l =>
        {
            var listCards = cardsByList[l.Id]
                .Select(c =>
                {
                    var checklist = checklistTotals.GetValueOrDefault(c.Id);
                    return new CardSummaryDto(
                        c.PublicId,
                        c.Title,
                        c.DueAt,
                        CardDueStatus.Compute(c.DueAt, c.DueComplete, now),
                        HasDescription: !string.IsNullOrEmpty(c.Description),
                        ChecklistDone: checklist?.Done,
                        ChecklistTotal: checklist?.Total,
                        CommentCount: commentCounts.GetValueOrDefault(c.Id),
                        Labels: labelsByCard.GetValueOrDefault(c.Id, []),
                        Members: membersByCard.GetValueOrDefault(c.Id, []));
                })
                .ToList();

            return new ListContentDto(
                l.PublicId, l.Name, l.WipLimit, listCards.Count, Convert.ToBase64String(l.RowVersion), listCards);
        }).ToList();

        return Result<BoardContentDto>.Success(
            new BoardContentDto(
                board.PublicId, board.Name, board.Color, board.Starred, Convert.ToBase64String(board.RowVersion), listDtos));
    }

    public async Task<Result<BoardCreatedDto>> CreateBoardAsync(
        Guid callerPublicId, string name, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
        {
            return Failure.Validation("Validation failed",
                new Dictionary<string, string[]> { ["name"] = ["Name must be 1-200 characters."] });
        }

        // research.md R-3: every registered user owns exactly one workspace (002's
        // AuthService) — no eligibility check, no request field to target another one.
        var workspace = await db.Workspaces
            .Where(w => w.Owner.PublicId == callerPublicId)
            .Select(w => new { w.Id })
            .FirstAsync(cancellationToken);

        var existingBoardCount = await db.Boards.CountAsync(b => b.WorkspaceId == workspace.Id, cancellationToken);

        var now = DateTime.UtcNow;
        var board = new Board
        {
            PublicId = Guid.NewGuid(),
            WorkspaceId = workspace.Id,
            Name = name.Trim(),
            Color = ColorPalette[existingBoardCount % ColorPalette.Length],
            Starred = false,
            CreatedDate = now,
            CreatedBy = callerPublicId.ToString(),
        };
        db.Boards.Add(board);

        double? lastPosition = null;
        var lists = new List<List>();
        foreach (var listName in new[] { "To Do", "Doing", "Done" })
        {
            var position = Ordering.Append(lastPosition);
            lastPosition = position;
            lists.Add(new List
            {
                PublicId = Guid.NewGuid(),
                Board = board,
                Name = listName,
                Position = position,
                CreatedDate = now,
                CreatedBy = callerPublicId.ToString(),
            });
        }
        db.Lists.AddRange(lists);

        await db.SaveChangesAsync(cancellationToken);

        return Result<BoardCreatedDto>.Success(new BoardCreatedDto(
            board.PublicId, board.Name, board.Color, board.Starred, CardCount: 0,
            lists.Select(l => new BoardListSummaryDto(l.PublicId, l.Name)).ToList()));
    }

    public async Task<Result<BoardUpdateResult>> UpdateBoardAsync(
        Guid boardPublicId, Guid callerPublicId, string name, byte[] ifMatchRowVersion,
        CancellationToken cancellationToken)
    {
        var access = await boardAccess.ResolveAsync(boardPublicId, callerPublicId, cancellationToken);
        if (access is null)
        {
            return Failure.NotFound();
        }
        // research.md R-2: CanManageBoard — the exact check BoardMembershipService.InviteAsync
        // already uses, not a new concept.
        if (access.Role != BoardRole.BoardAdmin)
        {
            return Failure.Forbidden();
        }

        if (string.IsNullOrWhiteSpace(name) || name.Length > 200)
        {
            return Failure.Validation("Validation failed",
                new Dictionary<string, string[]> { ["name"] = ["Name must be 1-200 characters."] });
        }

        var board = await db.Boards.FirstAsync(b => b.Id == access.BoardId, cancellationToken);
        board.Name = name.Trim();
        board.UpdatedDate = DateTime.UtcNow;
        board.UpdatedBy = callerPublicId.ToString();

        db.Entry(board).Property(x => x.RowVersion).OriginalValue = ifMatchRowVersion;

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Failure.Conflict("This board was changed by someone else.");
        }

        return Result<BoardUpdateResult>.Success(new BoardUpdateResult(board.Name, board.RowVersion));
    }

    public Task<Result<Unit>> StarBoardAsync(Guid boardPublicId, Guid callerPublicId, CancellationToken cancellationToken) =>
        SetStarredAsync(boardPublicId, callerPublicId, starred: true, cancellationToken);

    public Task<Result<Unit>> UnstarBoardAsync(Guid boardPublicId, Guid callerPublicId, CancellationToken cancellationToken) =>
        SetStarredAsync(boardPublicId, callerPublicId, starred: false, cancellationToken);

    private async Task<Result<Unit>> SetStarredAsync(
        Guid boardPublicId, Guid callerPublicId, bool starred, CancellationToken cancellationToken)
    {
        var access = await boardAccess.ResolveAsync(boardPublicId, callerPublicId, cancellationToken);
        if (access is null)
        {
            return Failure.NotFound();
        }
        if (!CanMutate(access.Role))
        {
            return Failure.Forbidden();
        }

        var board = await db.Boards.FirstAsync(b => b.Id == access.BoardId, cancellationToken);
        board.Starred = starred;
        board.UpdatedDate = DateTime.UtcNow;
        board.UpdatedBy = callerPublicId.ToString();
        await db.SaveChangesAsync(cancellationToken);

        return Result<Unit>.Success(Unit.Value);
    }

    public async Task<Result<Unit>> DeleteBoardAsync(
        Guid boardPublicId, Guid callerPublicId, CancellationToken cancellationToken)
    {
        var access = await boardAccess.ResolveAsync(boardPublicId, callerPublicId, cancellationToken);
        if (access is null)
        {
            return Failure.NotFound();
        }
        if (access.Role != BoardRole.BoardAdmin)
        {
            return Failure.Forbidden();
        }

        var board = await db.Boards.FirstAsync(b => b.Id == access.BoardId, cancellationToken);
        var now = DateTime.UtcNow;
        board.IsDeleted = true;
        board.DeletedDate = now;
        board.DeletedBy = callerPublicId.ToString();
        await db.SaveChangesAsync(cancellationToken);

        return Result<Unit>.Success(Unit.Value);
    }

    private static bool CanMutate(BoardRole role) => role is BoardRole.BoardAdmin or BoardRole.BoardMember;

    private static string EncodeCursor(bool starred, DateTime createdDate, int id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{(starred ? 1 : 0)}|{createdDate:O}|{id}"));

    private static bool TryDecodeCursor(string cursor, out BoardsCursorKey key)
    {
        key = default;
        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|');
            if (parts.Length != 3)
            {
                return false;
            }

            var starred = parts[0] == "1";
            var createdDate = DateTime.Parse(parts[1], null, System.Globalization.DateTimeStyles.RoundtripKind);
            var id = int.Parse(parts[2]);
            key = new BoardsCursorKey(starred, createdDate, id);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or IndexOutOfRangeException or OverflowException)
        {
            return false;
        }
    }
}
