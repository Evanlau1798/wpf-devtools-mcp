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
        if (_isDisposed) return;

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
            // Reschedule the one-shot timer for the next cleanup cycle
            if (!_isDisposed)
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

            foreach (var processId in _sessions.Keys)
            {
                if (!IsProcessAlive(processId))
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
    private static bool IsProcessAlive(int processId)
    {
        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(processId);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            // Process doesn't exist
            return false;
        }
        catch (InvalidOperationException)
        {
            // Process has exited
            return false;
        }
    }
}
