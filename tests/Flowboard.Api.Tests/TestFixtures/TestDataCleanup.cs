// Shared test-only cleanup for a test user (and everything they own) — specs/006's new
// board/list creation write paths mean tests can now create real boards via the API, and
// every Board/List/Card FK is Restrict (database-rules.md: never cascade), so this must
// unwind bottom-up before the owning user's Workspace can be removed.
using Flowboard.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace Flowboard.Api.Tests.TestFixtures;

public static class TestDataCleanup
{
    public static async Task RemoveUserOwnedDataAsync(FlowboardDbContext db, IReadOnlyCollection<string> emails)
    {
        if (emails.Count == 0)
        {
            return;
        }

        var users = await db.Users.Where(u => emails.Contains(u.Email)).ToListAsync();
        var userIds = users.Select(u => u.Id).ToList();

        var workspaceIds = await db.Workspaces
            .IgnoreQueryFilters()
            .Where(w => userIds.Contains(w.OwnerUserId))
            .Select(w => w.Id)
            .ToListAsync();
        var boardIds = await db.Boards
            .IgnoreQueryFilters()
            .Where(b => workspaceIds.Contains(b.WorkspaceId))
            .Select(b => b.Id)
            .ToListAsync();
        var listIds = await db.Lists
            .IgnoreQueryFilters()
            .Where(l => boardIds.Contains(l.BoardId))
            .Select(l => l.Id)
            .ToListAsync();
        var cardIds = await db.Cards
            .IgnoreQueryFilters()
            .Where(c => listIds.Contains(c.ListId))
            .Select(c => c.Id)
            .ToListAsync();

        // Each step is saved immediately (rather than batched into one SaveChangesAsync) so
        // this never depends on EF's cross-table delete ordering — every FK here is Restrict
        // (database-rules.md), so children must be fully committed gone before their parent.
        // IgnoreQueryFilters is required on all five: each filters via `!x.Card.IsDeleted`
        // (ActivityEventConfiguration.cs et al.), which would silently hide — not remove —
        // every row belonging to a card this feature's own archive/delete paths soft-deleted.
        db.ActivityEvents.RemoveRange(await db.ActivityEvents.IgnoreQueryFilters().Where(e => cardIds.Contains(e.CardId)).ToListAsync());
        db.Comments.RemoveRange(await db.Comments.IgnoreQueryFilters().Where(c => cardIds.Contains(c.CardId)).ToListAsync());
        db.ChecklistItems.RemoveRange(await db.ChecklistItems.IgnoreQueryFilters().Where(c => cardIds.Contains(c.CardId)).ToListAsync());
        db.CardLabels.RemoveRange(await db.CardLabels.IgnoreQueryFilters().Where(c => cardIds.Contains(c.CardId)).ToListAsync());
        db.CardMembers.RemoveRange(await db.CardMembers.IgnoreQueryFilters().Where(c => cardIds.Contains(c.CardId)).ToListAsync());
        await db.SaveChangesAsync();

        db.Cards.RemoveRange(await db.Cards.IgnoreQueryFilters().Where(c => cardIds.Contains(c.Id)).ToListAsync());
        await db.SaveChangesAsync();

        db.Lists.RemoveRange(await db.Lists.IgnoreQueryFilters().Where(l => listIds.Contains(l.Id)).ToListAsync());
        db.BoardMembers.RemoveRange(await db.BoardMembers.IgnoreQueryFilters()
            .Where(m => boardIds.Contains(m.BoardId) || userIds.Contains(m.UserId))
            .ToListAsync());
        await db.SaveChangesAsync();

        db.Boards.RemoveRange(await db.Boards.IgnoreQueryFilters().Where(b => boardIds.Contains(b.Id)).ToListAsync());
        await db.SaveChangesAsync();

        db.Workspaces.RemoveRange(await db.Workspaces.IgnoreQueryFilters().Where(w => workspaceIds.Contains(w.Id)).ToListAsync());
        db.Users.RemoveRange(users);
        await db.SaveChangesAsync();
    }
}
