// specs/002-auth-workspaces/data-model.md#BoardMember. Pure join entity — never addressed by
// the API via its own identifier (always via board+user PublicIds), so no PublicId column.
// The workspace owner's implicit BoardAdmin access is NOT a row here — see ADR-9 /
// BoardAccessService. Removal is a hard delete (not master data, no soft-delete fields).
namespace Flowboard.Api.Domain.Entities;

public sealed class BoardMember
{
    public int Id { get; set; }

    public int BoardId { get; set; }

    public Board Board { get; set; } = null!;

    public int UserId { get; set; }

    public User User { get; set; } = null!;

    public BoardRole Role { get; set; }

    public DateTime CreatedDate { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
}
