// Independent, controlled-time coverage of the classification logic itself
// (src/Flowboard.Api/Domain/CardDueStatus.cs). BoardsEndpointTests.cs's golden-fixture
// test uses this same function as its own oracle when checking the API's DueStatus
// against persisted DueAt — that integration check can never independently catch a
// regression inside Compute itself (the second-model adversarial review on
// fix/golden-fixture-due-status-date-drift flagged that gap). This file exists to close
// it, with a fixed `now` so it never depends on wall-clock time.
using Flowboard.Api.Domain;

namespace Flowboard.Api.Tests;

public sealed class CardDueStatusTests
{
    private static readonly DateTime Now = new(2026, 6, 15, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Compute_NoDueDate_ReturnsNull() =>
        Assert.Null(CardDueStatus.Compute(dueAt: null, dueComplete: false, Now));

    [Fact]
    public void Compute_DueComplete_ReturnsCompleteRegardlessOfDate() =>
        Assert.Equal("complete", CardDueStatus.Compute(Now.AddDays(-10), dueComplete: true, Now));

    [Fact]
    public void Compute_DueInPast_ReturnsOverdue() =>
        Assert.Equal("overdue", CardDueStatus.Compute(Now.AddDays(-1), dueComplete: false, Now));

    [Fact]
    public void Compute_DueRightNow_ReturnsSoon_NotOverdue() =>
        // dueAt == now is not "< now", so it must not fall into the overdue branch.
        Assert.Equal("soon", CardDueStatus.Compute(Now, dueComplete: false, Now));

    [Fact]
    public void Compute_DueOneTickInPast_ReturnsOverdue() =>
        // The overdue boundary is exclusive of `now` itself — one tick earlier must flip it.
        Assert.Equal("overdue", CardDueStatus.Compute(Now.AddTicks(-1), dueComplete: false, Now));

    [Fact]
    public void Compute_DueWithinTwoDays_ReturnsSoon() =>
        Assert.Equal("soon", CardDueStatus.Compute(Now.AddDays(1), dueComplete: false, Now));

    [Fact]
    public void Compute_DueExactlyTwoDaysOut_ReturnsSoon() =>
        // The "soon" boundary is inclusive of now + 2 days.
        Assert.Equal("soon", CardDueStatus.Compute(Now.AddDays(2), dueComplete: false, Now));

    [Fact]
    public void Compute_DueJustPastTwoDaysOut_ReturnsFuture() =>
        Assert.Equal("future", CardDueStatus.Compute(Now.AddDays(2).AddTicks(1), dueComplete: false, Now));

    [Fact]
    public void Compute_DueFarInFuture_ReturnsFuture() =>
        Assert.Equal("future", CardDueStatus.Compute(Now.AddDays(14), dueComplete: false, Now));
}
