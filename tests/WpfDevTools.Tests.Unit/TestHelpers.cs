using System.Text.Json;
using System.Reflection;
using WpfDevTools.Mcp.Server;

namespace WpfDevTools.Tests.Unit;

/// <summary>
/// Shared test helper methods
/// </summary>
public static class TestHelpers
{
    private static int s_nextSyntheticProcessId = 1_500_000_000;
    public const string OnlineInstallerDefinitionBoundaryMarker = "# TEST_BOUNDARY_MARKER: definition-only loading stops before the main entrypoint.";

    /// <summary>
    /// Convert an anonymous object to JsonElement for tool parameter passing
    /// </summary>
    public static JsonElement ToJsonElement(object value)
    {
        var json = JsonSerializer.Serialize(value);
        return JsonSerializer.Deserialize<JsonElement>(json);
    }

    public static int NextSyntheticProcessId() =>
        System.Threading.Interlocked.Increment(ref s_nextSyntheticProcessId);

    /// <summary>
    /// Disable the SessionManager background cleanup timer in tests that need
    /// deterministic control over pipe lifetime and teardown timing.
    /// </summary>
    public static void DisableSessionManagerCleanupTimer(SessionManager sessionManager)
    {
        var timerField = typeof(SessionManager).GetField("_cleanupTimer", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("SessionManager cleanup timer field was not found.");
        var timer = timerField.GetValue(sessionManager) as System.Threading.Timer
            ?? throw new InvalidOperationException("SessionManager cleanup timer was not initialized.");

        timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Replace the cached pipe client for a synthetic session in tests that
    /// stand up their own in-memory pipe server.
    /// </summary>
    public static void ReplaceSessionManagerPipeClient(SessionManager sessionManager, int processId, NamedPipeClient replacement)
    {
        var field = typeof(SessionManager).GetField("_pipeClients", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("SessionManager pipe client cache field was not found.");
        var pipeClients = field.GetValue(sessionManager) as Dictionary<int, NamedPipeClient>
            ?? throw new InvalidOperationException("SessionManager pipe client cache was not initialized.");

        if (pipeClients.TryGetValue(processId, out var existingClient))
        {
            existingClient.Dispose();
        }

        pipeClients[processId] = replacement;
    }
}

