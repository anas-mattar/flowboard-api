// Provider side of specs/003-board-view-readonly/contracts/board-content-api.md.
using System.Text;
using Flowboard.Api.Data;
using Flowboard.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace Flowboard.Api.Services;

public sealed record BoardSummaryDto(Guid PublicId, string Name, string Color, bool Starred, int CardCount);

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

public sealed record ListContentDto(Guid PublicId, string Name, int? WipLimit, int CardCount, IReadOnlyList<CardSummaryDto> Cards);

public sealed record BoardContentDto(Guid PublicId, string Name, string Color, bool Starred, IReadOnlyList<ListContentDto> Lists);

public interface IBoardContentService
{
    /// <summary>ADR-12 cursor pagination: null/empty cursor starts at page 1.</summary>
    Task<Result<CursorPage<BoardSummaryDto>>> ListBoardsAsync(
        Guid callerPublicId, string? cursor, int limit, CancellationToken cancellationToken);

    Task<Result<BoardContentDto>> GetBoardContentAsync(
        Guid boardPublicId, Guid callerPublicId, CancellationToken cancellationToken);
}

public sealed class BoardContentService(FlowboardDbContext db, IBoardAccessService boardAccess) : IBoardContentService
{
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
            .Select(b => new { b.PublicId, b.Name, b.Color, b.Starred })
            .FirstAsync(cancellationToken);

        var lists = await db.Lists
            .AsNoTracking()
            .Where(l => l.BoardId == access.BoardId)
            .OrderBy(l => l.Position)
            .Select(l => new { l.Id, l.PublicId, l.Name, l.WipLimit })
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
                        ComputeDueStatus(c.DueAt, c.DueComplete, now),
                        HasDescription: !string.IsNullOrEmpty(c.Description),
                        ChecklistDone: checklist?.Done,
                        ChecklistTotal: checklist?.Total,
                        CommentCount: commentCounts.GetValueOrDefault(c.Id),
                        Labels: labelsByCard.GetValueOrDefault(c.Id, []),
                        Members: membersByCard.GetValueOrDefault(c.Id, []));
                })
                .ToList();

            return new ListContentDto(l.PublicId, l.Name, l.WipLimit, listCards.Count, listCards);
        }).ToList();

        return Result<BoardContentDto>.Success(
            new BoardContentDto(board.PublicId, board.Name, board.Color, board.Starred, listDtos));
    }

    private static string? ComputeDueStatus(DateTime? dueAt, bool dueComplete, DateTime now)
    {
        if (dueAt is null)
        {
            return null;
        }

        if (dueComplete)
        {
            return "complete";
        }

        if (dueAt.Value < now)
        {
            return "overdue";
        }

        return dueAt.Value <= now.AddDays(2) ? "soon" : "future";
    }

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
