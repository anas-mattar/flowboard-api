// specs/003-board-view-readonly/data-model.md#List. Soft-delete entity (invariant 4:
// "Boards, lists and cards"). Position is invariant 2's float-ordering column. RowVersion
// added by specs/006-board-list-management/data-model.md#List for rename If-Match
// concurrency.
namespace Flowboard.Api.Domain.Entities;

public sealed class List
{
    public int Id { get; set; }

    public Guid PublicId { get; set; }

    public int BoardId { get; set; }

    public Board Board { get; set; } = null!;

    public string Name { get; set; } = string.Empty;

    public double Position { get; set; }

    public int? WipLimit { get; set; }

    public byte[] RowVersion { get; set; } = [];

    public DateTime CreatedDate { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTime? UpdatedDate { get; set; }

    public string? UpdatedBy { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime? DeletedDate { get; set; }

    public string? DeletedBy { get; set; }
}
