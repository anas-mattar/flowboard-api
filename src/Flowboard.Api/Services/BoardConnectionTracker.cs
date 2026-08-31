// data-model.md's BoardConnectionTracker entry — in-memory only, not persisted, not
// distributed (research.md R-8: single-instance, no backplane). Populated in
// BoardHub.JoinBoard, cleaned up in LeaveBoard/OnDisconnectedAsync, read by
// BoardEventPublisher's eviction paths (research.md R-7).
namespace Flowboard.Api.Services;

public sealed class BoardConnectionTracker
{
    private readonly object _lock = new();
    private readonly Dictionary<(Guid BoardPublicId, Guid UserPublicId), HashSet<string>> _connections = new();

    public void AddConnection(Guid boardPublicId, Guid userPublicId, string connectionId)
    {
        lock (_lock)
        {
            var key = (boardPublicId, userPublicId);
            if (!_connections.TryGetValue(key, out var set))
            {
                set = [];
                _connections[key] = set;
            }
            set.Add(connectionId);
        }
    }

    public void RemoveConnection(Guid boardPublicId, Guid userPublicId, string connectionId)
    {
        lock (_lock)
        {
            RemoveConnectionLocked((boardPublicId, userPublicId), connectionId);
        }
    }

    /// <summary>BoardHub.OnDisconnectedAsync doesn't track which board(s) a connection had
    /// joined, so this removes it from every key it might appear under.</summary>
    public void RemoveConnectionEverywhere(string connectionId)
    {
        lock (_lock)
        {
            foreach (var key in _connections.Keys.ToArray())
            {
                RemoveConnectionLocked(key, connectionId);
            }
        }
    }

    public IReadOnlyList<string> GetConnectionIds(Guid boardPublicId, Guid userPublicId)
    {
        lock (_lock)
        {
            return _connections.TryGetValue((boardPublicId, userPublicId), out var set)
                ? set.ToArray()
                : [];
        }
    }

    /// <summary>Every connection currently tracked for a board, across all users — used by
    /// EvictBoardAsync when the whole board becomes inaccessible (archive/delete).</summary>
    public IReadOnlyList<string> GetBoardConnectionIds(Guid boardPublicId)
    {
        lock (_lock)
        {
            return _connections
                .Where(kv => kv.Key.BoardPublicId == boardPublicId)
                .SelectMany(kv => kv.Value)
                .ToArray();
        }
    }

    /// <summary>Clears the tracker entry entirely after EvictUserAsync removes those
    /// connections from the SignalR group — otherwise the tracker would keep pointing at
    /// connections that are open but no longer members of the board's group.</summary>
    public void RemoveAllForBoardUser(Guid boardPublicId, Guid userPublicId)
    {
        lock (_lock)
        {
            _connections.Remove((boardPublicId, userPublicId));
        }
    }

    /// <summary>Same as <see cref="RemoveAllForBoardUser"/>, but for every user tracked on
    /// a board — used after EvictBoardAsync.</summary>
    public void RemoveAllForBoard(Guid boardPublicId)
    {
        lock (_lock)
        {
            foreach (var key in _connections.Keys.Where(k => k.BoardPublicId == boardPublicId).ToArray())
            {
                _connections.Remove(key);
            }
        }
    }

    private void RemoveConnectionLocked((Guid BoardPublicId, Guid UserPublicId) key, string connectionId)
    {
        if (_connections.TryGetValue(key, out var set) && set.Remove(connectionId) && set.Count == 0)
        {
            _connections.Remove(key);
        }
    }
}
