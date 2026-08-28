// specs/003-board-view-readonly/data-model.md#ChecklistItem. 004 adds PublicId — items are
// now addressed individually (check/uncheck/delete) via specs/004-card-crud/data-model.md.
namespace Flowboard.Api.Domain.Entities;

public sealed class ChecklistItem
{
    public int Id { get; set; }

    public Guid PublicId { get; set; }

    public int CardId { get; set; }

    public Card Card { get; set; } = null!;

    public string Text { get; set; } = string.Empty;

    public bool Done { get; set; }

    public double Position { get; set; }

    public DateTime CreatedDate { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
}
