// Provider side of specs/004-card-crud/contracts/card-crud-api.md.
using System.Globalization;
using System.Text;
using System.Text.Json;
using Flowboard.Api.Data;
using Flowboard.Api.Domain;
using Flowboard.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Flowboard.Api.Services;

public sealed record CardDetailDto(
    Guid PublicId,
    string Title,
    string? Description,
    DateTime? DueAt,
    bool DueComplete,
    string? DueStatus,
    Guid ListPublicId,
    string ListName,
    Guid BoardPublicId,
    string BoardName,
    IReadOnlyList<LabelSummaryDto> Labels,
    IReadOnlyList<MemberUserDto> Members,
    IReadOnlyList<ChecklistItemDetailDto> ChecklistItems);

public sealed record ChecklistItemDetailDto(Guid PublicId, string Text, bool Done);

public sealed record ActivityEntryDto(
    string Type, JsonElement Payload, string ActorDisplayName, string ActorInitials, string ActorAvatarColor, DateTime CreatedAt);

public sealed record CardDetailResult(CardDetailDto Detail, byte[] RowVersion);

public sealed record UpdateCardCommand(
    bool HasTitle, string? Title,
    bool HasDescription, string? Description,
    bool HasDueAt, DateTime? DueAt,
    bool HasDueComplete, bool DueComplete);

public sealed record MoveCardCommand(Guid ListPublicId, Guid? BeforeCardPublicId);

public interface ICardService
{
    Task<Result<CardSummaryDto>> CreateCardAsync(
        Guid listPublicId, Guid callerPublicId, string title, CancellationToken cancellationToken);

    Task<Result<CardDetailResult>> GetCardDetailAsync(
        Guid cardPublicId, Guid callerPublicId, CancellationToken cancellationToken);

    Task<Result<CardDetailResult>> UpdateCardAsync(
        Guid cardPublicId, Guid callerPublicId, UpdateCardCommand command, byte[] ifMatchRowVersion,
        CancellationToken cancellationToken);

    Task<Result<Unit>> AddLabelAsync(
        Guid cardPublicId, Guid callerPublicId, Guid labelPublicId, CancellationToken cancellationToken);

    Task<Result<Unit>> RemoveLabelAsync(
        Guid cardPublicId, Guid callerPublicId, Guid labelPublicId, CancellationToken cancellationToken);

    Task<Result<Unit>> AddMemberAsync(
        Guid cardPublicId, Guid callerPublicId, Guid userPublicId, CancellationToken cancellationToken);

    Task<Result<Unit>> RemoveMemberAsync(
        Guid cardPublicId, Guid callerPublicId, Guid userPublicId, CancellationToken cancellationToken);

    Task<Result<ChecklistItemDetailDto>> AddChecklistItemAsync(
        Guid cardPublicId, Guid callerPublicId, string text, CancellationToken cancellationToken);

    Task<Result<ChecklistItemDetailDto>> UpdateChecklistItemAsync(
        Guid checklistItemPublicId, Guid callerPublicId, bool done, CancellationToken cancellationToken);

    Task<Result<Unit>> DeleteChecklistItemAsync(
        Guid checklistItemPublicId, Guid callerPublicId, CancellationToken cancellationToken);

    Task<Result<ActivityEntryDto>> AddCommentAsync(
        Guid cardPublicId, Guid callerPublicId, string body, CancellationToken cancellationToken);

    Task<Result<CursorPage<ActivityEntryDto>>> GetActivityAsync(
        Guid cardPublicId, Guid callerPublicId, string? cursor, int limit, CancellationToken cancellationToken);

    Task<Result<CardSummaryDto>> CopyCardAsync(
        Guid cardPublicId, Guid callerPublicId, CancellationToken cancellationToken);

    Task<Result<Unit>> DeleteCardAsync(
        Guid cardPublicId, Guid callerPublicId, CancellationToken cancellationToken);

    Task<Result<Unit>> MoveCardAsync(
        Guid cardPublicId, Guid callerPublicId, MoveCardCommand command, CancellationToken cancellationToken);
}

public sealed class CardService(FlowboardDbContext db, IBoardAccessService boardAccess) : ICardService
{
    private static readonly JsonSerializerOptions PayloadJsonOptions = new(JsonSerializerDefaults.Web);

    private sealed record ResolvedCard(Card Card, BoardAccess Access);

    private sealed record ResolvedChecklistItem(ChecklistItem Item, Card Card, BoardAccess Access);

    private readonly record struct ActivityCursorKey(DateTime CreatedDate, int Id);

    public async Task<Result<CardSummaryDto>> CreateCardAsync(
        Guid listPublicId, Guid callerPublicId, string title, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(title) || title.Length > 200)
        {
            return Failure.Validation("Validation failed",
                new Dictionary<string, string[]> { ["title"] = ["Title must be 1-200 characters."] });
        }

        var list = await db.Lists
            .Where(l => l.PublicId == listPublicId)
            .Select(l => new { l.Id, BoardPublicId = l.Board.PublicId })
            .FirstOrDefaultAsync(cancellationToken);
        if (list is null)
        {
            return Failure.NotFound();
        }

        var access = await boardAccess.ResolveAsync(list.BoardPublicId, callerPublicId, cancellationToken);
        if (access is null)
        {
            return Failure.NotFound();
        }
        if (!CanMutate(access.Role))
        {
            return Failure.Forbidden();
        }

        var lastPosition = await db.Cards
            .Where(c => c.ListId == list.Id)
            .OrderByDescending(c => c.Position)
            .Select(c => (double?)c.Position)
            .FirstOrDefaultAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var card = new Card
        {
            PublicId = Guid.NewGuid(),
            ListId = list.Id,
            Title = title.Trim(),
            Position = Ordering.Append(lastPosition),
            CreatedDate = now,
            CreatedBy = callerPublicId.ToString(),
        };
        db.Cards.Add(card);

        var actorId = await GetCallerIdAsync(callerPublicId, cancellationToken);
        db.ActivityEvents.Add(NewEvent(card, actorId, callerPublicId, ActivityEventType.CardCreated, new { }));

        await db.SaveChangesAsync(cancellationToken);

        return Result<CardSummaryDto>.Success(new CardSummaryDto(
            card.PublicId, card.Title, DueAt: null, DueStatus: null, HasDescription: false,
            Description: null,
            ChecklistDone: null, ChecklistTotal: null, CommentCount: 0, Labels: [], Members: []));
    }

    public async Task<Result<CardDetailResult>> GetCardDetailAsync(
        Guid cardPublicId, Guid callerPublicId, CancellationToken cancellationToken)
    {
        var resolved = await ResolveCardAsync(cardPublicId, callerPublicId, cancellationToken);
        if (resolved is null)
        {
            return Failure.NotFound();
        }

        var dto = await BuildDetailDtoAsync(resolved.Card, cancellationToken);
        return Result<CardDetailResult>.Success(new CardDetailResult(dto, resolved.Card.RowVersion));
    }

    public async Task<Result<CardDetailResult>> UpdateCardAsync(
        Guid cardPublicId, Guid callerPublicId, UpdateCardCommand command, byte[] ifMatchRowVersion,
        CancellationToken cancellationToken)
    {
        var resolved = await ResolveCardAsync(cardPublicId, callerPublicId, cancellationToken);
        if (resolved is null)
        {
            return Failure.NotFound();
        }
        if (!CanMutate(resolved.Access.Role))
        {
            return Failure.Forbidden();
        }

        if (command.HasTitle && (string.IsNullOrWhiteSpace(command.Title) || command.Title!.Length > 200))
        {
            return Failure.Validation("Validation failed",
                new Dictionary<string, string[]> { ["title"] = ["Title must be 1-200 characters."] });
        }

        var card = resolved.Card;
        var now = DateTime.UtcNow;
        var actorId = await GetCallerIdAsync(callerPublicId, cancellationToken);
        var events = new List<ActivityEvent>();

        if (command.HasTitle)
        {
            card.Title = command.Title!.Trim();
            events.Add(NewEvent(card, actorId, callerPublicId, ActivityEventType.CardRenamed, new { title = card.Title }));
        }

        if (command.HasDescription)
        {
            card.Description = string.IsNullOrEmpty(command.Description) ? null : command.Description;
            events.Add(NewEvent(card, actorId, callerPublicId, ActivityEventType.CardDescribed, new { }));
        }

        if (command.HasDueAt)
        {
            card.DueAt = command.DueAt;
            events.Add(NewEvent(
                card, actorId, callerPublicId,
                command.DueAt is null ? ActivityEventType.DueCleared : ActivityEventType.DueSet,
                command.DueAt is null ? new { } : new { dueAt = command.DueAt }));
        }

        if (command.HasDueComplete)
        {
            card.DueComplete = command.DueComplete;
            if (command.DueComplete)
            {
                events.Add(NewEvent(card, actorId, callerPublicId, ActivityEventType.DueCompleted, new { }));
            }
        }

        card.UpdatedDate = now;
        card.UpdatedBy = callerPublicId.ToString();

        db.Entry(card).Property(x => x.RowVersion).OriginalValue = ifMatchRowVersion;
        db.ActivityEvents.AddRange(events);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Failure.Conflict("This card was changed by someone else.");
        }

        var dto = await BuildDetailDtoAsync(card, cancellationToken);
        return Result<CardDetailResult>.Success(new CardDetailResult(dto, card.RowVersion));
    }

    public async Task<Result<Unit>> AddLabelAsync(
        Guid cardPublicId, Guid callerPublicId, Guid labelPublicId, CancellationToken cancellationToken)
    {
        var resolved = await ResolveCardAsync(cardPublicId, callerPublicId, cancellationToken);
        if (resolved is null)
        {
            return Failure.NotFound();
        }
        if (!CanMutate(resolved.Access.Role))
        {
            return Failure.Forbidden();
        }

        var card = resolved.Card;
        var label = await db.Labels.FirstOrDefaultAsync(l => l.PublicId == labelPublicId, cancellationToken);
        if (label is null)
        {
            return Failure.NotFound();
        }

        // Invariant 7 — a label belongs to exactly one board.
        if (label.BoardId != card.List.BoardId)
        {
            return Failure.Validation("Validation failed",
                new Dictionary<string, string[]> { ["labelPublicId"] = ["This label belongs to a different board."] });
        }

        var alreadyAssigned = await db.CardLabels.AnyAsync(
            cl => cl.CardId == card.Id && cl.LabelId == label.Id, cancellationToken);
        if (!alreadyAssigned)
        {
            db.CardLabels.Add(new CardLabel
            {
                Card = card, LabelId = label.Id, CreatedDate = DateTime.UtcNow, CreatedBy = callerPublicId.ToString(),
            });
            var actorId = await GetCallerIdAsync(callerPublicId, cancellationToken);
            db.ActivityEvents.Add(NewEvent(card, actorId, callerPublicId, ActivityEventType.LabelAdded, new { labelName = label.Name }));
            await db.SaveChangesAsync(cancellationToken);
        }

        return Result<Unit>.Success(Unit.Value);
    }

    public async Task<Result<Unit>> RemoveLabelAsync(
        Guid cardPublicId, Guid callerPublicId, Guid labelPublicId, CancellationToken cancellationToken)
    {
        var resolved = await ResolveCardAsync(cardPublicId, callerPublicId, cancellationToken);
        if (resolved is null)
        {
            return Failure.NotFound();
        }
        if (!CanMutate(resolved.Access.Role))
        {
            return Failure.Forbidden();
        }

        var cardLabel = await db.CardLabels
            .Include(cl => cl.Label)
            .FirstOrDefaultAsync(cl => cl.CardId == resolved.Card.Id && cl.Label.PublicId == labelPublicId, cancellationToken);
        if (cardLabel is not null)
        {
            var labelName = cardLabel.Label.Name;
            db.CardLabels.Remove(cardLabel);
            var actorId = await GetCallerIdAsync(callerPublicId, cancellationToken);
            db.ActivityEvents.Add(NewEvent(resolved.Card, actorId, callerPublicId, ActivityEventType.LabelRemoved, new { labelName }));
            await db.SaveChangesAsync(cancellationToken);
        }

        return Result<Unit>.Success(Unit.Value);
    }

    public async Task<Result<Unit>> AddMemberAsync(
        Guid cardPublicId, Guid callerPublicId, Guid userPublicId, CancellationToken cancellationToken)
    {
        var resolved = await ResolveCardAsync(cardPublicId, callerPublicId, cancellationToken);
        if (resolved is null)
        {
            return Failure.NotFound();
        }
        if (!CanMutate(resolved.Access.Role))
        {
            return Failure.Forbidden();
        }

        var card = resolved.Card;
        var user = await db.Users.FirstOrDefaultAsync(u => u.PublicId == userPublicId, cancellationToken);
        if (user is null)
        {
            return Failure.NotFound();
        }

        var boardId = card.List.BoardId;

        // FR-006 / invariant 5's sanctioned side effect: assigning a non-member adds them
        // to the board first. The workspace owner already has implicit access (ADR-9).
        var ownerPublicId = await db.Boards.AsNoTracking()
            .Where(b => b.Id == boardId)
            .Select(b => b.Workspace.Owner.PublicId)
            .FirstAsync(cancellationToken);

        if (ownerPublicId != userPublicId)
        {
            var isBoardMember = await db.BoardMembers.AnyAsync(
                m => m.BoardId == boardId && m.UserId == user.Id, cancellationToken);
            if (!isBoardMember)
            {
                db.BoardMembers.Add(new BoardMember
                {
                    BoardId = boardId,
                    UserId = user.Id,
                    Role = BoardRole.BoardMember,
                    CreatedDate = DateTime.UtcNow,
                    CreatedBy = callerPublicId.ToString(),
                });
            }
        }

        var alreadyCardMember = await db.CardMembers.AnyAsync(
            cm => cm.CardId == card.Id && cm.UserId == user.Id, cancellationToken);
        if (!alreadyCardMember)
        {
            db.CardMembers.Add(new CardMember
            {
                Card = card, UserId = user.Id, CreatedDate = DateTime.UtcNow, CreatedBy = callerPublicId.ToString(),
            });
            var actorId = await GetCallerIdAsync(callerPublicId, cancellationToken);
            db.ActivityEvents.Add(NewEvent(
                card, actorId, callerPublicId, ActivityEventType.MemberAssigned, new { memberDisplayName = user.DisplayName }));
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result<Unit>.Success(Unit.Value);
    }

    public async Task<Result<Unit>> RemoveMemberAsync(
        Guid cardPublicId, Guid callerPublicId, Guid userPublicId, CancellationToken cancellationToken)
    {
        var resolved = await ResolveCardAsync(cardPublicId, callerPublicId, cancellationToken);
        if (resolved is null)
        {
            return Failure.NotFound();
        }
        if (!CanMutate(resolved.Access.Role))
        {
            return Failure.Forbidden();
        }

        var cardMember = await db.CardMembers
            .Include(cm => cm.User)
            .FirstOrDefaultAsync(cm => cm.CardId == resolved.Card.Id && cm.User.PublicId == userPublicId, cancellationToken);
        if (cardMember is not null)
        {
            var displayName = cardMember.User.DisplayName;
            db.CardMembers.Remove(cardMember);
            var actorId = await GetCallerIdAsync(callerPublicId, cancellationToken);
            db.ActivityEvents.Add(NewEvent(
                resolved.Card, actorId, callerPublicId, ActivityEventType.MemberUnassigned, new { memberDisplayName = displayName }));
            await db.SaveChangesAsync(cancellationToken);
        }

        return Result<Unit>.Success(Unit.Value);
    }

    public async Task<Result<ChecklistItemDetailDto>> AddChecklistItemAsync(
        Guid cardPublicId, Guid callerPublicId, string text, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 500)
        {
            return Failure.Validation("Validation failed",
                new Dictionary<string, string[]> { ["text"] = ["Text must be 1-500 characters."] });
        }

        var resolved = await ResolveCardAsync(cardPublicId, callerPublicId, cancellationToken);
        if (resolved is null)
        {
            return Failure.NotFound();
        }
        if (!CanMutate(resolved.Access.Role))
        {
            return Failure.Forbidden();
        }

        var card = resolved.Card;
        var lastPosition = await db.ChecklistItems
            .Where(ci => ci.CardId == card.Id)
            .OrderByDescending(ci => ci.Position)
            .Select(ci => (double?)ci.Position)
            .FirstOrDefaultAsync(cancellationToken);

        var item = new ChecklistItem
        {
            PublicId = Guid.NewGuid(),
            Card = card,
            Text = text.Trim(),
            Done = false,
            Position = Ordering.Append(lastPosition),
            CreatedDate = DateTime.UtcNow,
            CreatedBy = callerPublicId.ToString(),
        };
        db.ChecklistItems.Add(item);

        var actorId = await GetCallerIdAsync(callerPublicId, cancellationToken);
        db.ActivityEvents.Add(NewEvent(card, actorId, callerPublicId, ActivityEventType.ChecklistItemAdded, new { text = item.Text }));

        await db.SaveChangesAsync(cancellationToken);
        return Result<ChecklistItemDetailDto>.Success(new ChecklistItemDetailDto(item.PublicId, item.Text, item.Done));
    }

    public async Task<Result<ChecklistItemDetailDto>> UpdateChecklistItemAsync(
        Guid checklistItemPublicId, Guid callerPublicId, bool done, CancellationToken cancellationToken)
    {
        var resolved = await ResolveChecklistItemAsync(checklistItemPublicId, callerPublicId, cancellationToken);
        if (resolved is null)
        {
            return Failure.NotFound();
        }
        if (!CanMutate(resolved.Access.Role))
        {
            return Failure.Forbidden();
        }

        var changed = resolved.Item.Done != done;
        resolved.Item.Done = done;
        if (changed)
        {
            var actorId = await GetCallerIdAsync(callerPublicId, cancellationToken);
            var type = done ? ActivityEventType.ChecklistItemChecked : ActivityEventType.ChecklistItemUnchecked;
            db.ActivityEvents.Add(NewEvent(resolved.Card, actorId, callerPublicId, type, new { text = resolved.Item.Text }));
        }

        await db.SaveChangesAsync(cancellationToken);
        return Result<ChecklistItemDetailDto>.Success(
            new ChecklistItemDetailDto(resolved.Item.PublicId, resolved.Item.Text, resolved.Item.Done));
    }

    public async Task<Result<Unit>> DeleteChecklistItemAsync(
        Guid checklistItemPublicId, Guid callerPublicId, CancellationToken cancellationToken)
    {
        var resolved = await ResolveChecklistItemAsync(checklistItemPublicId, callerPublicId, cancellationToken);
        if (resolved is null)
        {
            return Failure.NotFound();
        }
        if (!CanMutate(resolved.Access.Role))
        {
            return Failure.Forbidden();
        }

        var text = resolved.Item.Text;
        db.ChecklistItems.Remove(resolved.Item);
        var actorId = await GetCallerIdAsync(callerPublicId, cancellationToken);
        db.ActivityEvents.Add(NewEvent(resolved.Card, actorId, callerPublicId, ActivityEventType.ChecklistItemDeleted, new { text }));

        await db.SaveChangesAsync(cancellationToken);
        return Result<Unit>.Success(Unit.Value);
    }

    public async Task<Result<ActivityEntryDto>> AddCommentAsync(
        Guid cardPublicId, Guid callerPublicId, string body, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(body) || body.Length > 2000)
        {
            return Failure.Validation("Validation failed",
                new Dictionary<string, string[]> { ["body"] = ["Body must be 1-2000 characters."] });
        }

        var resolved = await ResolveCardAsync(cardPublicId, callerPublicId, cancellationToken);
        if (resolved is null)
        {
            return Failure.NotFound();
        }
        // R-2: every role, including Observer, may comment — no CanMutate check here.

        var caller = await db.Users.FirstAsync(u => u.PublicId == callerPublicId, cancellationToken);
        var trimmedBody = body.Trim();

        db.Comments.Add(new Comment
        {
            Card = resolved.Card,
            AuthorId = caller.Id,
            Body = trimmedBody,
            CreatedDate = DateTime.UtcNow,
            CreatedBy = callerPublicId.ToString(),
        });

        var activityEvent = NewEvent(
            resolved.Card, caller.Id, callerPublicId, ActivityEventType.CommentAdded, new { body = trimmedBody });
        db.ActivityEvents.Add(activityEvent);

        await db.SaveChangesAsync(cancellationToken);

        return Result<ActivityEntryDto>.Success(new ActivityEntryDto(
            activityEvent.Type,
            JsonSerializer.Deserialize<JsonElement>(activityEvent.Payload),
            caller.DisplayName, caller.Initials, caller.AvatarColor, activityEvent.CreatedDate));
    }

    public async Task<Result<CursorPage<ActivityEntryDto>>> GetActivityAsync(
        Guid cardPublicId, Guid callerPublicId, string? cursor, int limit, CancellationToken cancellationToken)
    {
        var resolved = await ResolveCardAsync(cardPublicId, callerPublicId, cancellationToken);
        if (resolved is null)
        {
            return Failure.NotFound();
        }

        ActivityCursorKey? key = null;
        if (!string.IsNullOrEmpty(cursor))
        {
            if (!TryDecodeCursor(cursor, out var decoded))
            {
                return Failure.Validation("Invalid cursor");
            }

            key = decoded;
        }

        var query = db.ActivityEvents.AsNoTracking().Where(e => e.CardId == resolved.Card.Id);
        if (key is { } k)
        {
            query = query.Where(e => e.CreatedDate < k.CreatedDate || (e.CreatedDate == k.CreatedDate && e.Id < k.Id));
        }

        var page = await query
            .OrderByDescending(e => e.CreatedDate)
            .ThenByDescending(e => e.Id)
            .Take(limit + 1)
            .Select(e => new
            {
                e.Id,
                e.Type,
                e.Payload,
                e.CreatedDate,
                e.Actor.DisplayName,
                e.Actor.Initials,
                e.Actor.AvatarColor,
            })
            .ToListAsync(cancellationToken);

        var hasMore = page.Count > limit;
        var trimmed = hasMore ? page.Take(limit).ToList() : page;
        var items = trimmed
            .Select(e => new ActivityEntryDto(
                e.Type, JsonSerializer.Deserialize<JsonElement>(e.Payload), e.DisplayName, e.Initials, e.AvatarColor, e.CreatedDate))
            .ToList();
        var nextCursor = hasMore ? EncodeCursor(trimmed[^1].CreatedDate, trimmed[^1].Id) : null;

        return Result<CursorPage<ActivityEntryDto>>.Success(
            new CursorPage<ActivityEntryDto> { Items = items, NextCursor = nextCursor });
    }

    public async Task<Result<CardSummaryDto>> CopyCardAsync(
        Guid cardPublicId, Guid callerPublicId, CancellationToken cancellationToken)
    {
        var resolved = await ResolveCardAsync(cardPublicId, callerPublicId, cancellationToken);
        if (resolved is null)
        {
            return Failure.NotFound();
        }
        if (!CanMutate(resolved.Access.Role))
        {
            return Failure.Forbidden();
        }

        var original = resolved.Card;

        var nextSiblingPosition = await db.Cards
            .Where(c => c.ListId == original.ListId && c.Position > original.Position)
            .OrderBy(c => c.Position)
            .Select(c => (double?)c.Position)
            .FirstOrDefaultAsync(cancellationToken);

        var now = DateTime.UtcNow;
        var copy = new Card
        {
            PublicId = Guid.NewGuid(),
            ListId = original.ListId,
            Title = original.Title + " (copy)",
            Description = original.Description,
            DueAt = original.DueAt,
            DueComplete = original.DueComplete,
            Position = Ordering.InsertBetween(original.Position, nextSiblingPosition),
            CreatedDate = now,
            CreatedBy = callerPublicId.ToString(),
        };
        db.Cards.Add(copy);

        var originalLabels = await db.CardLabels.AsNoTracking()
            .Where(cl => cl.CardId == original.Id)
            .Select(cl => new { cl.LabelId, cl.Label.PublicId, cl.Label.Name, cl.Label.Color })
            .ToListAsync(cancellationToken);
        foreach (var l in originalLabels)
        {
            db.CardLabels.Add(new CardLabel { Card = copy, LabelId = l.LabelId, CreatedDate = now, CreatedBy = callerPublicId.ToString() });
        }

        var originalMembers = await db.CardMembers.AsNoTracking()
            .Where(cm => cm.CardId == original.Id)
            .Select(cm => new { cm.UserId, cm.User.PublicId, cm.User.DisplayName, cm.User.Initials, cm.User.AvatarColor })
            .ToListAsync(cancellationToken);
        foreach (var m in originalMembers)
        {
            db.CardMembers.Add(new CardMember { Card = copy, UserId = m.UserId, CreatedDate = now, CreatedBy = callerPublicId.ToString() });
        }

        var originalChecklistItems = await db.ChecklistItems.AsNoTracking()
            .Where(ci => ci.CardId == original.Id)
            .OrderBy(ci => ci.Position)
            .Select(ci => new { ci.Text, ci.Position })
            .ToListAsync(cancellationToken);
        foreach (var item in originalChecklistItems)
        {
            db.ChecklistItems.Add(new ChecklistItem
            {
                PublicId = Guid.NewGuid(),
                Card = copy,
                Text = item.Text,
                Done = false,
                Position = item.Position,
                CreatedDate = now,
                CreatedBy = callerPublicId.ToString(),
            });
        }

        var actorId = await GetCallerIdAsync(callerPublicId, cancellationToken);
        db.ActivityEvents.Add(NewEvent(copy, actorId, callerPublicId, ActivityEventType.CardCreated, new { }));

        await db.SaveChangesAsync(cancellationToken);

        return Result<CardSummaryDto>.Success(new CardSummaryDto(
            copy.PublicId,
            copy.Title,
            copy.DueAt,
            CardDueStatus.Compute(copy.DueAt, copy.DueComplete, now),
            HasDescription: !string.IsNullOrEmpty(copy.Description),
            Description: copy.Description,
            ChecklistDone: originalChecklistItems.Count == 0 ? null : 0,
            ChecklistTotal: originalChecklistItems.Count == 0 ? null : originalChecklistItems.Count,
            CommentCount: 0,
            Labels: originalLabels.Select(l => new LabelSummaryDto(l.PublicId, l.Name, l.Color)).ToList(),
            Members: originalMembers.Select(m => new MemberUserDto(m.PublicId, m.DisplayName, m.Initials, m.AvatarColor)).ToList()));
    }

    public async Task<Result<Unit>> DeleteCardAsync(
        Guid cardPublicId, Guid callerPublicId, CancellationToken cancellationToken)
    {
        var resolved = await ResolveCardAsync(cardPublicId, callerPublicId, cancellationToken);
        if (resolved is null)
        {
            return Failure.NotFound();
        }
        if (!CanMutate(resolved.Access.Role))
        {
            return Failure.Forbidden();
        }

        var card = resolved.Card;
        var now = DateTime.UtcNow;
        card.IsDeleted = true;
        card.DeletedDate = now;
        card.DeletedBy = callerPublicId.ToString();

        // No activity event: the card (and its whole feed) is excluded by its own query
        // filter from the moment this saves, so an event here would never be readable.
        await db.SaveChangesAsync(cancellationToken);
        return Result<Unit>.Success(Unit.Value);
    }

    // contracts/move-api.md — POST /v1/cards/{cardPublicId}/move.
    public async Task<Result<Unit>> MoveCardAsync(
        Guid cardPublicId, Guid callerPublicId, MoveCardCommand command, CancellationToken cancellationToken)
    {
        var resolved = await ResolveCardAsync(cardPublicId, callerPublicId, cancellationToken);
        if (resolved is null)
        {
            return Failure.NotFound();
        }
        if (!CanMutate(resolved.Access.Role))
        {
            return Failure.Forbidden();
        }

        var card = resolved.Card;
        var currentBoardId = card.List.BoardId;

        var destinationList = await db.Lists
            .Where(l => l.PublicId == command.ListPublicId)
            .Select(l => new { l.Id, l.BoardId, l.Name })
            .FirstOrDefaultAsync(cancellationToken);
        if (destinationList is null)
        {
            return Failure.NotFound();
        }
        if (destinationList.BoardId != currentBoardId)
        {
            return Failure.Validation("Validation failed",
                new Dictionary<string, string[]> { ["listPublicId"] = ["This list belongs to a different board."] });
        }

        double newPosition;
        if (command.BeforeCardPublicId is { } beforeCardPublicId)
        {
            var beforeSibling = await db.Cards
                .Where(c => c.PublicId == beforeCardPublicId)
                .Select(c => new { c.Id, c.ListId, c.Position })
                .FirstOrDefaultAsync(cancellationToken);
            if (beforeSibling is null)
            {
                return Failure.NotFound();
            }
            if (beforeSibling.ListId != destinationList.Id)
            {
                return Failure.Validation("Validation failed",
                    new Dictionary<string, string[]> { ["beforeCardPublicId"] = ["This card is not in the destination list."] });
            }

            var precedingPosition = await db.Cards
                .Where(c => c.ListId == destinationList.Id && c.Id != card.Id && c.Position < beforeSibling.Position)
                .OrderByDescending(c => c.Position)
                .Select(c => (double?)c.Position)
                .FirstOrDefaultAsync(cancellationToken);
            newPosition = Ordering.InsertBetween(precedingPosition ?? 0, beforeSibling.Position);
        }
        else
        {
            var lastPosition = await db.Cards
                .Where(c => c.ListId == destinationList.Id && c.Id != card.Id)
                .OrderByDescending(c => c.Position)
                .Select(c => (double?)c.Position)
                .FirstOrDefaultAsync(cancellationToken);
            newPosition = Ordering.Append(lastPosition);
        }

        var listChanged = card.ListId != destinationList.Id;
        var fromListName = card.List.Name;
        var now = DateTime.UtcNow;
        var callerIdString = callerPublicId.ToString();

        // ADR-21: moves have no concurrency precondition at all — ExecuteUpdateAsync writes
        // directly, bypassing the change tracker's RowVersion concurrency check that a normal
        // SaveChangesAsync on this tracked `card` would otherwise apply. Last write wins.
        await db.Cards
            .Where(c => c.Id == card.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(c => c.ListId, destinationList.Id)
                .SetProperty(c => c.Position, newPosition)
                .SetProperty(c => c.UpdatedDate, now)
                .SetProperty(c => c.UpdatedBy, callerIdString),
                cancellationToken);

        if (listChanged)
        {
            var actorId = await GetCallerIdAsync(callerPublicId, cancellationToken);
            db.ActivityEvents.Add(NewEvent(
                card, actorId, callerPublicId, ActivityEventType.CardMoved,
                new { fromListName, toListName = destinationList.Name }));
            await db.SaveChangesAsync(cancellationToken);
        }

        return Result<Unit>.Success(Unit.Value);
    }

    private static bool CanMutate(BoardRole role) => role is BoardRole.BoardAdmin or BoardRole.BoardMember;

    private async Task<ResolvedCard?> ResolveCardAsync(Guid cardPublicId, Guid callerPublicId, CancellationToken cancellationToken)
    {
        var card = await db.Cards
            .Include(c => c.List).ThenInclude(l => l.Board)
            .FirstOrDefaultAsync(c => c.PublicId == cardPublicId, cancellationToken);
        if (card is null)
        {
            return null;
        }

        var access = await boardAccess.ResolveAsync(card.List.Board.PublicId, callerPublicId, cancellationToken);
        return access is null ? null : new ResolvedCard(card, access);
    }

    private async Task<ResolvedChecklistItem?> ResolveChecklistItemAsync(
        Guid checklistItemPublicId, Guid callerPublicId, CancellationToken cancellationToken)
    {
        var item = await db.ChecklistItems
            .Include(ci => ci.Card).ThenInclude(c => c.List).ThenInclude(l => l.Board)
            .FirstOrDefaultAsync(ci => ci.PublicId == checklistItemPublicId, cancellationToken);
        if (item is null)
        {
            return null;
        }

        var access = await boardAccess.ResolveAsync(item.Card.List.Board.PublicId, callerPublicId, cancellationToken);
        return access is null ? null : new ResolvedChecklistItem(item, item.Card, access);
    }

    private async Task<CardDetailDto> BuildDetailDtoAsync(Card card, CancellationToken cancellationToken)
    {
        var labels = await db.CardLabels.AsNoTracking()
            .Where(cl => cl.CardId == card.Id)
            .Select(cl => new LabelSummaryDto(cl.Label.PublicId, cl.Label.Name, cl.Label.Color))
            .ToListAsync(cancellationToken);

        var members = await db.CardMembers.AsNoTracking()
            .Where(cm => cm.CardId == card.Id)
            .Select(cm => new MemberUserDto(cm.User.PublicId, cm.User.DisplayName, cm.User.Initials, cm.User.AvatarColor))
            .ToListAsync(cancellationToken);

        var checklistItems = await db.ChecklistItems.AsNoTracking()
            .Where(ci => ci.CardId == card.Id)
            .OrderBy(ci => ci.Position)
            .Select(ci => new ChecklistItemDetailDto(ci.PublicId, ci.Text, ci.Done))
            .ToListAsync(cancellationToken);

        return new CardDetailDto(
            card.PublicId,
            card.Title,
            card.Description,
            card.DueAt,
            card.DueComplete,
            CardDueStatus.Compute(card.DueAt, card.DueComplete, DateTime.UtcNow),
            card.List.PublicId,
            card.List.Name,
            card.List.Board.PublicId,
            card.List.Board.Name,
            labels,
            members,
            checklistItems);
    }

    private async Task<int> GetCallerIdAsync(Guid callerPublicId, CancellationToken cancellationToken) =>
        await db.Users.Where(u => u.PublicId == callerPublicId).Select(u => u.Id).FirstAsync(cancellationToken);

    private static ActivityEvent NewEvent(Card card, int actorId, Guid actorPublicId, string type, object payload) =>
        new()
        {
            Card = card,
            ActorId = actorId,
            Type = type,
            Payload = JsonSerializer.Serialize(payload, PayloadJsonOptions),
            CreatedDate = DateTime.UtcNow,
            CreatedBy = actorPublicId.ToString(),
        };

    private static string EncodeCursor(DateTime createdDate, int id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{createdDate:O}|{id}"));

    private static bool TryDecodeCursor(string cursor, out ActivityCursorKey key)
    {
        key = default;
        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split('|');
            if (parts.Length != 2)
            {
                return false;
            }

            var createdDate = DateTime.Parse(parts[0], null, DateTimeStyles.RoundtripKind);
            var id = int.Parse(parts[1]);
            key = new ActivityCursorKey(createdDate, id);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or IndexOutOfRangeException or OverflowException)
        {
            return false;
        }
    }
}
