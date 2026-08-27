// ADR-12 (specs/003-board-view-readonly/plan.md): shared opaque-cursor pagination shape
// for every list endpoint. `NextCursor` is a serialized sort key, never a raw offset, and
// is only ever produced/consumed by the server — the client passes it back unparsed.
namespace Flowboard.Api.Domain;

public sealed class CursorPage<T>
{
    public required IReadOnlyList<T> Items { get; init; }

    public string? NextCursor { get; init; }
}
