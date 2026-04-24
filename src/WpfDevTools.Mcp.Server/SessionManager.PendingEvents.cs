using System.Text.Json;

namespace WpfDevTools.Mcp.Server;

public sealed partial class SessionManager
{
    private readonly Dictionary<int, PendingEventReplaySnapshot> _pendingEventReplay = new();
    private readonly Dictionary<int, SemaphoreSlim> _pendingEventReplayLocks = new();

    internal async Task<PendingEventReplayLockScope> AcquirePendingEventReplayLockAsync(int processId, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        SemaphoreSlim replayLock;
        long sessionGeneration;
        lock (_lock)
        {
            if (!_pendingEventReplayLocks.TryGetValue(processId, out replayLock!))
            {
                replayLock = new SemaphoreSlim(1, 1);
                _pendingEventReplayLocks[processId] = replayLock;
            }

            sessionGeneration = _sessionGenerations.TryGetValue(processId, out var currentGeneration)
                ? currentGeneration
                : 0;
        }

        await replayLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new PendingEventReplayLockScope(replayLock, sessionGeneration);
    }

    internal void SavePendingEventReplay(int processId, JsonElement drainPayload)
    {
        if (!TryGetSessionGeneration(processId, out var sessionGeneration))
        {
            return;
        }

        SavePendingEventReplay(processId, sessionGeneration, drainPayload);
    }

    internal bool SavePendingEventReplay(int processId, long sessionGeneration, JsonElement drainPayload)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            if (_sessionGenerations.TryGetValue(processId, out var currentGeneration)
                && currentGeneration == sessionGeneration)
            {
                _pendingEventReplay[processId] = new PendingEventReplaySnapshot(
                    drainPayload.Clone(),
                    _utcNowProvider(),
                    sessionGeneration);
                return true;
            }
        }

        return false;
    }

    internal bool TryTakePendingEventReplay(int processId, out JsonElement drainPayload)
    {
        if (!TryGetSessionGeneration(processId, out var sessionGeneration))
        {
            drainPayload = default;
            return false;
        }

        return TryTakePendingEventReplay(processId, sessionGeneration, out drainPayload);
    }

    internal bool TryTakePendingEventReplay(int processId, long sessionGeneration, out JsonElement drainPayload)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            if (_sessionGenerations.TryGetValue(processId, out var currentGeneration)
                && currentGeneration == sessionGeneration
                && _pendingEventReplay.TryGetValue(processId, out var snapshot)
                && snapshot.SessionGeneration == sessionGeneration)
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
        if (!TryGetSessionGeneration(processId, out var sessionGeneration))
        {
            drainPayload = default;
            return false;
        }

        return TryPeekPendingEventReplay(processId, sessionGeneration, out drainPayload);
    }

    internal bool TryPeekPendingEventReplay(int processId, long sessionGeneration, out JsonElement drainPayload)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            if (_sessionGenerations.TryGetValue(processId, out var currentGeneration)
                && currentGeneration == sessionGeneration
                && _pendingEventReplay.TryGetValue(processId, out var snapshot)
                && snapshot.SessionGeneration == sessionGeneration)
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
        if (!TryGetSessionGeneration(processId, out var sessionGeneration))
        {
            drainPayload = default;
            savedAtUtc = default;
            return false;
        }

        return TryPeekPendingEventReplayMetadata(processId, sessionGeneration, out drainPayload, out savedAtUtc);
    }

    internal bool TryPeekPendingEventReplayMetadata(
        int processId,
        long sessionGeneration,
        out JsonElement drainPayload,
        out DateTimeOffset savedAtUtc)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            if (_sessionGenerations.TryGetValue(processId, out var currentGeneration)
                && currentGeneration == sessionGeneration
                && _pendingEventReplay.TryGetValue(processId, out var snapshot)
                && snapshot.SessionGeneration == sessionGeneration)
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

    internal bool TryGetSessionGeneration(int processId, out long sessionGeneration)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            if (_sessionGenerations.TryGetValue(processId, out sessionGeneration))
            {
                return true;
            }
        }

        sessionGeneration = default;
        return false;
    }

    internal bool IsCurrentSessionGeneration(int processId, long sessionGeneration)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            return _sessionGenerations.TryGetValue(processId, out var currentGeneration)
                && currentGeneration == sessionGeneration;
        }
    }

    private sealed record PendingEventReplaySnapshot(
        JsonElement Payload,
        DateTimeOffset SavedAtUtc,
        long SessionGeneration);

    internal sealed class PendingEventReplayLockScope(SemaphoreSlim replayLock, long sessionGeneration) : IDisposable
    {
        internal long SessionGeneration { get; } = sessionGeneration;

        public void Dispose()
        {
            replayLock.Release();
        }
    }
}
