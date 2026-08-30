// Shared If-Match/ETag encoding for every RowVersion-backed PATCH (Card, Board, List) —
// mirrors Ordering.cs's "one module per shared calculation" precedent for protocol-level
// concerns (backend-rules.md).
namespace Flowboard.Api.Endpoints;

public static class ETagHeader
{
    public static string ToETag(byte[] rowVersion) => $"\"{Convert.ToBase64String(rowVersion)}\"";

    public static bool TryParse(string? etag, out byte[] rowVersion)
    {
        rowVersion = [];
        if (string.IsNullOrWhiteSpace(etag))
        {
            return false;
        }

        var trimmed = etag.Trim();
        if (trimmed.StartsWith("W/", StringComparison.Ordinal))
        {
            trimmed = trimmed[2..];
        }

        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            trimmed = trimmed[1..^1];
        }

        try
        {
            rowVersion = Convert.FromBase64String(trimmed);
            return rowVersion.Length > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
