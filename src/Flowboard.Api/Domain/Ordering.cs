// specs/001-solution-scaffold/plan.md ADR-4: one module for every position/ranking
// calculation (backend-rules.md). Reserved since 001, first used by specs/004-card-crud
// (ADR-18) — card creation (Append) and card copy (InsertBetween). Sparse float positions
// per domain invariant 2; the caller is responsible for the occasional renumber this
// scheme eventually needs (not exercised by any write path yet).
namespace Flowboard.Api.Domain;

public static class Ordering
{
    private const double Step = 1000;

    /// <summary>Position for a new item appended to the end of a list.</summary>
    public static double Append(double? lastPosition) => (lastPosition ?? 0) + Step;

    /// <summary>Position for an item inserted between two siblings; pass null for
    /// <paramref name="upper"/> when inserting after the last item.</summary>
    public static double InsertBetween(double lower, double? upper) =>
        upper is { } u ? lower + (u - lower) / 2 : lower + Step;
}
