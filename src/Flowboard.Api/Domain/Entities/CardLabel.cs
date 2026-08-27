// specs/003-board-view-readonly/data-model.md#CardLabel. Pure join entity — never
// addressed by the API via its own identifier, so no PublicId column (same convention as
// BoardMember, 002).
namespace Flowboard.Api.Domain.Entities;

public sealed class CardLabel
{
    public int Id { get; set; }

    public int CardId { get; set; }

    public Card Card { get; set; } = null!;

    public int LabelId { get; set; }

    public Label Label { get; set; } = null!;

    public DateTime CreatedDate { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
}
