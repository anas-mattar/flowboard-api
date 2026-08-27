// specs/003-board-view-readonly/data-model.md#Comment. Not exposed individually by this
// feature's API — only commentCount per card. No PublicId yet (same reasoning as
// ChecklistItem).
namespace Flowboard.Api.Domain.Entities;

public sealed class Comment
{
    public int Id { get; set; }

    public int CardId { get; set; }

    public Card Card { get; set; } = null!;

    public int AuthorId { get; set; }

    public User Author { get; set; } = null!;

    public string Body { get; set; } = string.Empty;

    public DateTime CreatedDate { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
}
