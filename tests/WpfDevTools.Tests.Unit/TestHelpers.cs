using System.IO;
using System.Text.Json;
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

    public static string CreateUniquePipeName(string prefix = "WpfDevTools_Test") =>
        $"{prefix}_{Guid.NewGuid():N}";

    public static string EnsureSharedDummyBootstrapperExists()
    {
        var bootstrapperPath = Path.Combine(AppContext.BaseDirectory, "WpfDevTools.Bootstrapper.x64.dll");

        try
        {
            using var stream = new FileStream(
                bootstrapperPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.ReadWrite);
        }
        catch (IOException) when (File.Exists(bootstrapperPath))
        {
        }

        return bootstrapperPath;
    }

    /// <summary>
    /// Disable the SessionManager background cleanup timer in tests that need
    /// deterministic control over pipe lifetime and teardown timing.
    /// </summary>
    public static void DisableSessionManagerCleanupTimer(SessionManager sessionManager)
    {
        sessionManager._cleanupTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <summary>
    /// Replace the cached pipe client for a synthetic session in tests that
    /// stand up their own in-memory pipe server.
    /// </summary>
    public static void ReplaceSessionManagerPipeClient(SessionManager sessionManager, int processId, NamedPipeClient replacement)
    {
        sessionManager.ReplacePipeClientForTesting(processId, replacement);
    }
}

