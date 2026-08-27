// specs/002-auth-workspaces/data-model.md#Invitation. Status lifecycle is the audit record —
// no soft delete; Pending -> Accepted (claimed at signup/login, FR-010/FR-011) or -> Revoked.
namespace Flowboard.Api.Domain.Entities;

public enum InvitationStatus
{
    Pending,
    Accepted,
    Revoked,
}

public sealed class Invitation
{
    public int Id { get; set; }

    public Guid PublicId { get; set; }

    public int BoardId { get; set; }

    public Board Board { get; set; } = null!;

    public string Email { get; set; } = string.Empty;

    public BoardRole Role { get; set; }

    public int InvitedByUserId { get; set; }

    public User InvitedBy { get; set; } = null!;

    public InvitationStatus Status { get; set; }

    public DateTime CreatedDate { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTime? UpdatedDate { get; set; }

    public string? UpdatedBy { get; set; }
}
