// data-model.md's BoardRealtimeEvent — "type" values with no ActivityEvent counterpart
// (research.md R-5, ADR-35). Card-scoped broadcasts instead reuse ActivityEventType
// verbatim from the ActivityEvent row just persisted (invariant 1).
namespace Flowboard.Api.Domain;

public static class RealtimeEventType
{
    public const string CardArchived = "card.archived";
    public const string BoardRenamed = "board.renamed";
    public const string BoardStarred = "board.starred";
    public const string BoardUnstarred = "board.unstarred";
    public const string BoardArchived = "board.archived";
    public const string ListCreated = "list.created";
    public const string ListRenamed = "list.renamed";
    public const string ListMoved = "list.moved";
    public const string ListArchived = "list.archived";
    public const string ListWipLimitChanged = "list.wip_limit_changed";
    public const string AccessRevoked = "access.revoked";
}
