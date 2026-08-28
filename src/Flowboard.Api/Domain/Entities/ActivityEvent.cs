// specs/004-card-crud/data-model.md#ActivityEvent. Append-only (invariant 1) — no
// Updated*/soft-delete columns; no code path ever updates or deletes a row here.
namespace Flowboard.Api.Domain.Entities;

public sealed class ActivityEvent
{
    public int Id { get; set; }

    public int CardId { get; set; }

    public Card Card { get; set; } = null!;

    public int ActorId { get; set; }

    public User Actor { get; set; } = null!;

    public string Type { get; set; } = string.Empty;

    public string Payload { get; set; } = string.Empty;

    public DateTime CreatedDate { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
}
