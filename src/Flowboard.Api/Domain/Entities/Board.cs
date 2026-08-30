// specs/002-auth-workspaces/data-model.md#Board (schema origin, ADR-11); Color/Starred
// added by specs/003-board-view-readonly/data-model.md#Board. RowVersion added by
// specs/006-board-list-management/data-model.md#Board for rename If-Match concurrency.
namespace Flowboard.Api.Domain.Entities;

public sealed class Board
{
    public int Id { get; set; }

    public Guid PublicId { get; set; }

    public int WorkspaceId { get; set; }

    public Workspace Workspace { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    public string Color { get; set; } = "#64748b";

    public bool Starred { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public DateTime CreatedDate { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTime? UpdatedDate { get; set; }

    public string? UpdatedBy { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedDate { get; set; }

    public string? DeletedBy { get; set; }
}
