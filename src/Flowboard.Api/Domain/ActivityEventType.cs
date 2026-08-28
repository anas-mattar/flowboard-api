// specs/004-card-crud/data-model.md#ActivityEvent — the fixed Type enumeration.
namespace Flowboard.Api.Domain;

public static class ActivityEventType
{
    public const string CardCreated = "card.created";
    public const string CardRenamed = "card.renamed";
    public const string CardDescribed = "card.described";
    public const string LabelAdded = "label.added";
    public const string LabelRemoved = "label.removed";
    public const string MemberAssigned = "member.assigned";
    public const string MemberUnassigned = "member.unassigned";
    public const string DueSet = "due.set";
    public const string DueCleared = "due.cleared";
    public const string DueCompleted = "due.completed";
    public const string ChecklistItemAdded = "checklist.item.added";
    public const string ChecklistItemChecked = "checklist.item.checked";
    public const string ChecklistItemUnchecked = "checklist.item.unchecked";
    public const string ChecklistItemDeleted = "checklist.item.deleted";
    public const string CommentAdded = "comment.added";
}
