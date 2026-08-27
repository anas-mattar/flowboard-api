// specs/003-board-view-readonly/data-model.md#Label. Board-scoped (invariant 7). Not a
// soft-delete entity — not named in invariant 4's list (boards/lists/cards only); label
// archive/delete is deferred to whichever future feature adds label management.
namespace Flowboard.Api.Domain.Entities;

public sealed class Label
{
    public int Id { get; set; }

    public Guid PublicId { get; set; }

    public int BoardId { get; set; }

    public Board Board { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    public string Color { get; set; } = string.Empty;

    public DateTime CreatedDate { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTime? UpdatedDate { get; set; }

    public string? UpdatedBy { get; set; }
}
