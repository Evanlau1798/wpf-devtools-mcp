using System.Text.Json;

namespace WpfDevTools.Mcp.Server;

public sealed partial class SessionManager
{
    private readonly Dictionary<int, PendingEventReplaySnapshot> _pendingEventReplay = new();

    internal void SavePendingEventReplay(int processId, JsonElement drainPayload)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            _pendingEventReplay[processId] = new PendingEventReplaySnapshot(
                drainPayload.Clone(),
                DateTimeOffset.UtcNow);
        }
    }

    internal bool TryTakePendingEventReplay(int processId, out JsonElement drainPayload)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            if (_pendingEventReplay.TryGetValue(processId, out var snapshot))
            {
                _pendingEventReplay.Remove(processId);
                drainPayload = snapshot.Payload.Clone();
                return true;
            }
        }

        drainPayload = default;
        return false;
    }

    internal bool TryPeekPendingEventReplay(int processId, out JsonElement drainPayload)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            if (_pendingEventReplay.TryGetValue(processId, out var snapshot))
            {
                drainPayload = snapshot.Payload.Clone();
                return true;
            }
        }

        drainPayload = default;
        return false;
    }

    internal bool TryPeekPendingEventReplayMetadata(
        int processId,
        out JsonElement drainPayload,
        out DateTimeOffset savedAtUtc)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            if (_pendingEventReplay.TryGetValue(processId, out var snapshot))
            {
                drainPayload = snapshot.Payload.Clone();
                savedAtUtc = snapshot.SavedAtUtc;
                return true;
            }
        }

        drainPayload = default;
        savedAtUtc = default;
        return false;
    }

    private sealed record PendingEventReplaySnapshot(JsonElement Payload, DateTimeOffset SavedAtUtc);
}
