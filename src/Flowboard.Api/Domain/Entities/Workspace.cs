// specs/002-auth-workspaces/data-model.md#Workspace. ADR-10: exactly one admin (OwnerUserId) —
// no WorkspaceMember table in this feature's scope.
namespace Flowboard.Api.Domain.Entities;

public sealed class Workspace
{
    public int Id { get; set; }

    public Guid PublicId { get; set; }

    public string Name { get; set; } = string.Empty;

    public int OwnerUserId { get; set; }

    public User Owner { get; set; } = null!;

    public DateTime CreatedDate { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTime? UpdatedDate { get; set; }

    public string? UpdatedBy { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedDate { get; set; }

    public string? DeletedBy { get; set; }
}
