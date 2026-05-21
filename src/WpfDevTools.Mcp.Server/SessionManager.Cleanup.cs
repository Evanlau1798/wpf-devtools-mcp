using Microsoft.Extensions.Logging;

namespace WpfDevTools.Mcp.Server;

public sealed partial class SessionManager
{
    /// <summary>
    /// Perform cleanup of both dead and idle sessions.
    /// Called by the one-shot cleanup timer.
    /// </summary>
    internal void PerformCleanup()
    {
        if (Volatile.Read(ref _disposeState) != 0) return;

        try
        {
            CleanupDeadSessions();

            var idleSessions = GetIdleSessions(McpServerConfiguration.SessionIdleTimeout);
            foreach (var processId in idleSessions)
            {
                RemoveSession(processId);
            }
        }
        catch (ObjectDisposedException)
        {
            // SessionManager was disposed during cleanup - safe to ignore
            return;
        }
        catch (Exception ex)
        {
            // Prevent Timer callback exceptions from stopping future cleanup cycles.
            // Background cleanup failures are non-critical.
            if (_logger != null)
            {
                _logger.LogError(ex, "Session cleanup failed");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"SessionManager: Cleanup error: {ex.Message}");
            }
        }
        finally
        {
            // Dispose() may set _disposeState between cleanup work and timer re-arm.
            // The volatile read avoids scheduling new cleanup after disposal has begun;
            // ObjectDisposedException covers the narrower race where the timer is
            // disposed after the read but before Change() executes.
            if (Volatile.Read(ref _disposeState) == 0)
            {
                try { _cleanupTimer.Change(McpServerConfiguration.SessionCleanupInterval, Timeout.InfiniteTimeSpan); }
                catch (ObjectDisposedException) { /* Timer disposed during shutdown */ }
            }
        }
    }

    /// <summary>
    /// Clean up sessions for processes that no longer exist.
    /// Prevents memory leak from dead sessions.
    /// </summary>
    internal void CleanupDeadSessions()
    {
        List<int> deadProcessIds;

        lock (_lock)
        {
            deadProcessIds = new List<int>();

            foreach (var (processId, session) in _sessions)
            {
                if (!IsSessionProcessCurrent(session))
                {
                    deadProcessIds.Add(processId);
                }
            }
        }

        // Remove dead sessions outside the lock to avoid holding lock during disposal
        foreach (var processId in deadProcessIds)
        {
            RemoveSession(processId);
        }
    }

    /// <summary>
    /// Check if a process is still running
    /// </summary>
    private bool IsSessionProcessCurrent(SessionInfo session)
    {
        var currentIdentity = _processIdentityProvider(session.ProcessId);
        if (currentIdentity == null)
        {
            return false;
        }

        if (session.ProcessIdentity == null
            || session.ProcessIdentity.Value.StartTimeUtcTicks == null
            || currentIdentity.Value.StartTimeUtcTicks == null)
        {
            return currentIdentity.Value.ProcessId == session.ProcessId;
        }

        return currentIdentity.Value.Equals(session.ProcessIdentity.Value);
    }

    private static ProcessIdentity? GetCurrentProcessIdentity(int processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            if (process.HasExited)
            {
                return null;
            }

            long? startTimeUtcTicks = null;
            try
            {
                startTimeUtcTicks = process.StartTime.ToUniversalTime().Ticks;
            }
            catch (System.ComponentModel.Win32Exception)
            {
            }
            catch (InvalidOperationException)
            {
            }
            catch (NotSupportedException)
            {
            }

            return new ProcessIdentity(processId, startTimeUtcTicks);
        }
        catch (ArgumentException)
        {
            // Process doesn't exist
            return null;
        }
        catch (InvalidOperationException)
        {
            // Process has exited
            return null;
        }
    }

}
