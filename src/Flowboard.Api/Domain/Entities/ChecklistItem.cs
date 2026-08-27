// specs/003-board-view-readonly/data-model.md#ChecklistItem. Not exposed individually by
// this feature's API — GET /v1/boards/{id} returns only the aggregate checklistDone/
// checklistTotal counts per card. No PublicId yet; a future feature adds one when it
// starts addressing items individually (check/uncheck/delete).
namespace Flowboard.Api.Domain.Entities;

public sealed class ChecklistItem
{
    public int Id { get; set; }

    public int CardId { get; set; }

    public Card Card { get; set; } = null!;

    public string Text { get; set; } = string.Empty;

    public bool Done { get; set; }

    public double Position { get; set; }

    public DateTime CreatedDate { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
}
