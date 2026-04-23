using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using WpfDevTools.Tests.Integration.TestSupport;

namespace WpfDevTools.Tests.Integration.E2E;

/// <summary>
/// xUnit fixture that manages TestApp + MCP Server lifecycle for E2E testing.
/// Starts both processes once, connects via MCP protocol, and shares across all tests.
/// </summary>
public sealed class McpE2eFixture : IAsyncLifetime, IDisposable
{
    private Process? _testApp;
    private McpStdioClient? _client;

    public McpStdioClient Client => _client
        ?? throw new InvalidOperationException("E2E fixture not initialized");

    public int TestAppProcessId => _testApp?.Id
        ?? throw new InvalidOperationException("TestApp not started");

    /// <summary>
    /// Non-null if the fixture could not start (missing prerequisites).
    /// Tests should check this and fail with this message.
    /// </summary>
    public string? SkipReason { get; private set; }

    /// <summary>
    /// Non-null when a prior shared-session reset failed and later tests should stop using this fixture.
    /// </summary>
    public string? QuarantineReason { get; private set; }

    public void Quarantine(string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        if (string.IsNullOrWhiteSpace(QuarantineReason))
        {
            QuarantineReason = reason;
        }
    }

    public async Task InitializeAsync()
    {
        var serverExe = FindExecutable(
            "src", "WpfDevTools.Mcp.Server", "net8.0", "WpfDevTools.Mcp.Server.exe");

        if (serverExe == null)
        {
            SkipReason = "MCP Server executable not found for the current test configuration. Build src/WpfDevTools.Mcp.Server first.";
            return;
        }

        var testAppExe = FindExecutable(
            "tests", "WpfDevTools.Tests.TestApp", "net8.0-windows", "WpfDevTools.Tests.TestApp.exe");

        if (testAppExe == null)
        {
            SkipReason = "TestApp executable not found for the current test configuration. Build tests/WpfDevTools.Tests.TestApp first.";
            return;
        }

        if (!BootstrapperArtifactLocator.HasNativeBootstrapper(AppContext.BaseDirectory))
        {
            SkipReason = "Native bootstrapper DLLs not found. Build src/WpfDevTools.Bootstrapper first.";
            return;
        }

        try
        {
            _testApp = TestAppProcessLauncher.StartAndWaitForMainWindow(testAppExe, TimeSpan.FromSeconds(15));

            _client = new McpStdioClient();
            await _client.StartAsync(serverExe);

            var connectResult = await _client.CallToolAsync(
                "connect",
                new { processId = _testApp.Id },
                timeoutMs: 90000);

            if (!connectResult.TryGetProperty("success", out var success) ||
                !success.GetBoolean())
            {
                var error = connectResult.TryGetProperty("error", out var e) ? e.GetString() : "unknown";
                SkipReason = $"Failed to connect to TestApp: {error}";
            }
        }
        catch (Exception ex)
        {
            var stderrFull = _client?.ServerStderr ?? "";
            SkipReason = $"E2E fixture initialization failed: {ex.Message}\n---STDERR---\n{stderrFull}";
        }
    }

    public Task DisposeAsync()
    {
        Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _client?.Dispose();
        _client = null;

        if (_testApp != null)
        {
            try
            {
                if (!_testApp.HasExited)
                {
                    _testApp.CloseMainWindow();
                    if (!_testApp.WaitForExit(3000))
                    {
                        _testApp.Kill();
                        _testApp.WaitForExit(3000);
                    }
                }
            }
            catch
            {
                // Process may not be in a valid state (never started, already exited, etc.)
                try { _testApp.Kill(); } catch { /* best effort */ }
            }
            finally
            {
                _testApp.Dispose();
                _testApp = null;
            }
        }
    }
    private static string? FindExecutable(
        string projectDir, string projectName, string framework, string exeName)
    {
        return IntegrationExecutableLocator.FindExecutable(
            AppContext.BaseDirectory,
            projectDir,
            projectName,
            framework,
            exeName);
    }
}
