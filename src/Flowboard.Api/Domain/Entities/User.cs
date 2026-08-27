// specs/002-auth-workspaces/data-model.md#User. Not soft-delete (database-standards.md §4
// lists the soft-delete entities and User is not among them — no account-erasure flow yet).
namespace Flowboard.Api.Domain.Entities;

public sealed class User
{
    public int Id { get; set; }

    public Guid PublicId { get; set; }

    public string Email { get; set; } = string.Empty;

    public string PasswordHash { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Initials { get; set; } = string.Empty;

    public string AvatarColor { get; set; } = string.Empty;

    public DateTime CreatedDate { get; set; }

    public string CreatedBy { get; set; } = string.Empty;

    public DateTime? UpdatedDate { get; set; }

    public string? UpdatedBy { get; set; }
}
