// specs/004-card-crud/research.md R-5: extracted from BoardContentService (003) once a
// second caller (CardService, 004) needed the same bucketing — backend-rules.md: "two
// implementations of one formula always diverge."
namespace Flowboard.Api.Domain;

public static class CardDueStatus
{
    public static string? Compute(DateTime? dueAt, bool dueComplete, DateTime now)
    {
        if (dueAt is null)
        {
            return null;
        }

        if (dueComplete)
        {
            return "complete";
        }

        if (dueAt.Value < now)
        {
            return "overdue";
        }

        return dueAt.Value <= now.AddDays(2) ? "soon" : "future";
    }
}
