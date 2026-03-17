using System.Text.Json;

namespace WpfDevTools.Mcp.Server;

public sealed partial class SessionManager
{
    private readonly Dictionary<int, JsonElement> _pendingEventReplay = new();

    internal void SavePendingEventReplay(int processId, JsonElement drainPayload)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            _pendingEventReplay[processId] = drainPayload.Clone();
        }
    }

    internal bool TryTakePendingEventReplay(int processId, out JsonElement drainPayload)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            if (_pendingEventReplay.TryGetValue(processId, out var payload))
            {
                _pendingEventReplay.Remove(processId);
                drainPayload = payload.Clone();
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
            if (_pendingEventReplay.TryGetValue(processId, out var payload))
            {
                drainPayload = payload.Clone();
                return true;
            }
        }

        drainPayload = default;
        return false;
    }
}
