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
}
