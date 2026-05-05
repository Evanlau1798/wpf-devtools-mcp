using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using WpfDevTools.Mcp.Server;
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
    private string? _serverExePath;
    private string? _testAppExePath;
    private readonly string _authSecret = CreateAuthSecret();
    private readonly string _certDirectory = CreateCertificateDirectoryPath();
    private readonly int? _testAppProcessIdOverride;
    private readonly Func<Task>? _reconnectClientAsyncOverride;
    private readonly Func<string, object?, Task<JsonElement>>? _callToolAsyncOverride;

    public McpE2eFixture()
    {
    }

    internal McpE2eFixture(
        int testAppProcessId,
        Func<Task> reconnectClientAsync,
        Func<string, object?, Task<JsonElement>> callToolAsync)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(testAppProcessId);
        ArgumentNullException.ThrowIfNull(reconnectClientAsync);
        ArgumentNullException.ThrowIfNull(callToolAsync);

        _testAppProcessIdOverride = testAppProcessId;
        _reconnectClientAsyncOverride = reconnectClientAsync;
        _callToolAsyncOverride = callToolAsync;
    }

    public McpStdioClient Client => _client
        ?? throw new InvalidOperationException("E2E fixture not initialized");

    public int TestAppProcessId => _testAppProcessIdOverride
        ?? _testApp?.Id
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

        _serverExePath = serverExe;
        _testAppExePath = testAppExe;

        try
        {
            _testApp = TestAppProcessLauncher.StartAndWaitForMainWindow(testAppExe, TimeSpan.FromSeconds(15));
            await ReconnectClientAsync();
        }
        catch (Exception ex) when (ShouldConvertInitializationFailureToSkip(ex))
        {
            var stderrFull = _client?.ServerStderr ?? "";
            SkipReason = $"E2E fixture initialization failed: {ex.Message}\n---STDERR---\n{stderrFull}";
        }
    }

    internal static bool ShouldConvertInitializationFailureToSkip(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception is TimeoutException
            or IOException
            or Win32Exception
            or UnauthorizedAccessException;
    }

    public async Task ReconnectClientAsync()
    {
        if (_reconnectClientAsyncOverride != null)
        {
            await _reconnectClientAsyncOverride().ConfigureAwait(false);
            return;
        }

        if (string.IsNullOrWhiteSpace(_serverExePath))
        {
            throw new InvalidOperationException("MCP server executable path is not available for reconnect.");
        }

        if (_testApp == null)
        {
            throw new InvalidOperationException("TestApp must be started before reconnecting the MCP session.");
        }

        var testAppExePath = _testAppExePath
            ?? throw new InvalidOperationException("TestApp executable path is not available for reconnect.");

        _client?.Dispose();
        _client = new McpStdioClient();
        Directory.CreateDirectory(_certDirectory);

        await _client.StartAsync(
            _serverExePath,
            CreateServerEnvironment(testAppExePath, _authSecret, _certDirectory)).ConfigureAwait(false);

        var connectResult = await _client.CallToolAsync(
            "connect",
            new { processId = _testApp.Id },
            timeoutMs: 90000).ConfigureAwait(false);

        if (!connectResult.TryGetProperty("success", out var success) ||
            !success.GetBoolean())
        {
            var error = connectResult.TryGetProperty("error", out var errorProperty)
                ? errorProperty.GetString()
                : "unknown";
            throw new InvalidOperationException($"Failed to reconnect to TestApp: {error}");
        }
    }

    internal Task<JsonElement> CallToolAsync(string toolName, object? arguments)
        => _callToolAsyncOverride != null
            ? _callToolAsyncOverride(toolName, arguments)
            : Client.CallToolAsync(toolName, arguments);

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

        DeleteCertificateDirectory();
    }

    internal static IReadOnlyDictionary<string, string> CreateServerEnvironment(
        string testAppExePath,
        string authSecret,
        string certDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(testAppExePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(authSecret);
        ArgumentException.ThrowIfNullOrWhiteSpace(certDirectory);

        return new Dictionary<string, string>
        {
            [McpServerConfiguration.AllowedTargetsEnvVar] = testAppExePath,
            [McpServerConfiguration.RawInjectionAllowedTargetsEnvVar] = testAppExePath,
            [McpServerConfiguration.AllowDestructiveToolsEnvVar] = "true",
            [McpServerConfiguration.AllowScreenshotsEnvVar] = "true",
            [McpServerConfiguration.AllowViewModelInspectionEnvVar] = "true",
            ["WPFDEVTOOLS_AUTH_SECRET"] = authSecret,
            ["WPFDEVTOOLS_CERT_DIR"] = certDirectory
        };
    }

    private static string CreateAuthSecret()
    {
        var secretBytes = new byte[32];
        RandomNumberGenerator.Fill(secretBytes);
        return Convert.ToBase64String(secretBytes);
    }

    private static string CreateCertificateDirectoryPath() =>
        Path.Combine(Path.GetTempPath(), "WpfDevTools.McpE2eCerts." + Guid.NewGuid().ToString("N"));

    private void DeleteCertificateDirectory()
    {
        try
        {
            if (Directory.Exists(_certDirectory))
            {
                Directory.Delete(_certDirectory, recursive: true);
            }
        }
        catch
        {
            // Best-effort cleanup; a child process may still be releasing certificate files.
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
