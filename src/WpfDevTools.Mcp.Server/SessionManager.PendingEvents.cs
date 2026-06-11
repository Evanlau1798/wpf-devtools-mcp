using System.Text.Json;

namespace WpfDevTools.Mcp.Server;

public sealed partial class SessionManager
{
    private readonly Dictionary<int, PendingEventReplaySnapshot> _pendingEventReplay = new();
    private readonly Dictionary<int, PendingEventReplayLockState> _pendingEventReplayLocks = new();

    internal async Task<PendingEventReplayLockScope> AcquirePendingEventReplayLockAsync(int processId, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();

        PendingEventReplayLockState replayLock;
        long sessionGeneration;
        lock (_lock)
        {
            if (!_sessionGenerations.TryGetValue(processId, out sessionGeneration))
            {
                replayLock = PendingEventReplayLockState.CreateTransient();
            }
            else
            {
                if (!_pendingEventReplayLocks.TryGetValue(processId, out replayLock!))
                {
                    replayLock = PendingEventReplayLockState.CreateRetained();
                    _pendingEventReplayLocks[processId] = replayLock;
                }
            }

            replayLock.AddReference();
        }

        try
        {
            await replayLock.Semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            ReleasePendingEventReplayLockReference(replayLock);
            throw;
        }

        return new PendingEventReplayLockScope(this, replayLock, sessionGeneration);
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

    private PendingEventReplayLockState? RemovePendingEventReplayLockForSessionLocked(int processId)
    {
        if (_pendingEventReplayLocks.Remove(processId, out var replayLock) && replayLock.MarkRemoved())
        {
            return replayLock;
        }

        return null;
    }

    private List<PendingEventReplayLockState> ClearPendingEventReplayLocksLocked()
    {
        var locksToDispose = _pendingEventReplayLocks.Values
            .Where(static replayLock => replayLock.MarkRemoved())
            .ToList();
        _pendingEventReplayLocks.Clear();
        return locksToDispose;
    }

    private void ReleasePendingEventReplayLockReference(PendingEventReplayLockState replayLock)
    {
        PendingEventReplayLockState? replayLockToDispose = null;
        lock (_lock)
        {
            if (replayLock.ReleaseReference())
            {
                replayLockToDispose = replayLock;
            }
        }

        replayLockToDispose?.Dispose();
    }

    internal sealed class PendingEventReplayLockState : IDisposable
    {
        private int _referenceCount;
        private bool _removed;
        private bool _disposed;

        private PendingEventReplayLockState(bool removed)
        {
            _removed = removed;
        }

        internal SemaphoreSlim Semaphore { get; } = new(1, 1);

        internal static PendingEventReplayLockState CreateRetained() => new(removed: false);

        internal static PendingEventReplayLockState CreateTransient() => new(removed: true);

        internal void AddReference()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(PendingEventReplayLockState));
            }

            _referenceCount++;
        }

        internal bool MarkRemoved()
        {
            _removed = true;
            return _referenceCount == 0;
        }

        internal bool ReleaseReference()
        {
            if (_referenceCount <= 0)
            {
                throw new InvalidOperationException("Pending event replay lock reference count is already zero.");
            }

            _referenceCount--;
            return _removed && _referenceCount == 0;
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Semaphore.Dispose();
        }
    }

    internal sealed class PendingEventReplayLockScope(
        SessionManager owner,
        PendingEventReplayLockState replayLock,
        long sessionGeneration) : IDisposable
    {
        private int _disposeState;

        internal long SessionGeneration { get; } = sessionGeneration;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposeState, 1) != 0)
            {
                return;
            }

            replayLock.Semaphore.Release();
            owner.ReleasePendingEventReplayLockReference(replayLock);
        }
    }
}
