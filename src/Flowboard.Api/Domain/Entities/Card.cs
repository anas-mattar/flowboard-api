// specs/003-board-view-readonly/data-model.md#Card. Soft-delete entity (invariant 4).
// RowVersion is required by database-rules.md even though no endpoint uses If-Match yet
// (004 adds card edits) — the column exists now so 004 needs no later schema change.
namespace Flowboard.Api.Domain.Entities;

public sealed class Card
{
    public int Id { get; set; }

    public Guid PublicId { get; set; }

    public int ListId { get; set; }

    public List List { get; set; } = null!;

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    public double Position { get; set; }

    public DateTime? DueAt { get; set; }

    public bool DueComplete { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public DateTime CreatedDate { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTime? UpdatedDate { get; set; }

    public string? UpdatedBy { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedDate { get; set; }

    public string? DeletedBy { get; set; }

    public ICollection<CardLabel> CardLabels { get; set; } = new List<CardLabel>();

    public ICollection<CardMember> CardMembers { get; set; } = new List<CardMember>();

    public ICollection<ChecklistItem> ChecklistItems { get; set; } = new List<ChecklistItem>();

    public ICollection<Comment> Comments { get; set; } = new List<Comment>();

    public ICollection<Attachment> Attachments { get; set; } = new List<Attachment>();
}
