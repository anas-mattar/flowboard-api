// specs/002-auth-workspaces/data-model.md#Board. ADR-11: minimal placeholder schema —
// full board CRUD (color, starred, the prototype's three-board seed) is 003/006's scope.
namespace Flowboard.Api.Domain.Entities;

public sealed class Board
{
    public int Id { get; set; }

    public Guid PublicId { get; set; }

    public int WorkspaceId { get; set; }

    public Workspace Workspace { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    public DateTime CreatedDate { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTime? UpdatedDate { get; set; }

    public string? UpdatedBy { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedDate { get; set; }

    public string? DeletedBy { get; set; }
}
