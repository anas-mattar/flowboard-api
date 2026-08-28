// Golden-fixture coverage for specs/004-card-crud/plan.md ADR-18 — the module reserved by
// 001's ADR-4, first exercised by this feature's card create/copy write paths.
using Flowboard.Api.Domain;

namespace Flowboard.Api.Tests;

public sealed class OrderingTests
{
    [Fact]
    public void Append_EmptyList_ReturnsFirstStep()
    {
        Assert.Equal(1000, Ordering.Append(null));
    }

    [Fact]
    public void Append_ExistingLastPosition_AddsOneStep()
    {
        Assert.Equal(2000, Ordering.Append(1000));
        Assert.Equal(4500, Ordering.Append(3500));
    }

    [Fact]
    public void InsertBetween_WithUpperSibling_ReturnsMidpoint()
    {
        Assert.Equal(1500, Ordering.InsertBetween(1000, 2000));
    }

    [Fact]
    public void InsertBetween_NoUpperSibling_AddsOneStep()
    {
        Assert.Equal(3000, Ordering.InsertBetween(2000, null));
    }

    [Fact]
    public void InsertBetween_TightGap_StillProducesDistinctOrderablePosition()
    {
        var result = Ordering.InsertBetween(1000, 1000.001);
        Assert.True(result > 1000 && result < 1000.001);
    }

    // specs/005-drag-drop-ordering/research.md R-1 — CardService.MoveCardAsync's and
    // ListService.MoveListAsync's own insertion-point resolution, restated here as a
    // golden-fixture test against hand-worked expected values.
    [Fact]
    public void ResolveInsertionPoint_BeforeFirstSibling_HasNoPrecedingSibling()
    {
        Assert.Equal(500, ResolveInsertionPoint([1000, 2000, 3000], beforePosition: 1000));
    }

    [Fact]
    public void ResolveInsertionPoint_BeforeMiddleSibling_UsesImmediatePredecessor()
    {
        Assert.Equal(1500, ResolveInsertionPoint([1000, 2000, 3000], beforePosition: 2000));
    }

    [Fact]
    public void ResolveInsertionPoint_BeforeLastSibling_UsesImmediatePredecessor()
    {
        Assert.Equal(2500, ResolveInsertionPoint([1000, 2000, 3000], beforePosition: 3000));
    }

    [Fact]
    public void ResolveInsertionPoint_NoBeforeSiblingSupplied_AppendsAfterLast()
    {
        Assert.Equal(4000, ResolveInsertionPoint([1000, 2000, 3000], beforePosition: null));
    }

    [Fact]
    public void ResolveInsertionPoint_NoBeforeSiblingSupplied_EmptyList_ReturnsFirstStep()
    {
        Assert.Equal(1000, ResolveInsertionPoint([], beforePosition: null));
    }

    /// <summary>Mirrors the resolution algorithm without a database: the preceding
    /// sibling's position is the greatest sibling position below the target, or none when
    /// the target is first.</summary>
    private static double ResolveInsertionPoint(double[] orderedSiblingPositions, double? beforePosition)
    {
        if (beforePosition is not { } upper)
        {
            return Ordering.Append(orderedSiblingPositions.Length == 0 ? null : orderedSiblingPositions[^1]);
        }

        double? preceding = null;
        foreach (var position in orderedSiblingPositions)
        {
            if (position < upper)
            {
                preceding = position;
            }
        }

        return Ordering.InsertBetween(preceding ?? 0, upper);
    }
}
