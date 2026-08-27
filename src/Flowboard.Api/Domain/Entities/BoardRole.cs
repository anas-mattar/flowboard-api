// FUNCTIONAL_SPEC.md §6 roles. Stored as NVARCHAR with a CHECK constraint (database-rules.md).
namespace Flowboard.Api.Domain.Entities;

public enum BoardRole
{
    BoardAdmin,
    BoardMember,
    Observer,
}
