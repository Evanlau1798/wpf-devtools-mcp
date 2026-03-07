using Xunit;
using FluentAssertions;
using System.Text.Json;
using System.Reflection;
using WpfDevTools.Injector;
using WpfDevTools.Injector.Injection;
using WpfDevTools.Shared.Enums;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public class ConnectToolTests
{
    [Fact]
    public async Task Execute_WithInvalidProcessId_ShouldReturnError()
    {
        // Arrange
        var tool = new ConnectTool(new SessionManager());
        var parameters = new { processId = 999999 };

        // Act
        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Execute_WithMissingProcessId_ShouldReturnError()
    {
        // Arrange
        var tool = new ConnectTool(new SessionManager());
        var parameters = new { };

        // Act
        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("processId");
    }

    [Fact]
    public async Task Execute_WithNonWpfProcess_ShouldReturnError()
    {
        // Arrange
        var tool = new ConnectTool(
            new SessionManager(),
            new FakeProcessInjector { ValidationResult = InjectionError.NotWpfApplication },
            GetTestInspectorDllPath());
        var parameters = new { processId = 12345 };

        // Act
        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("not a WPF application");
    }

    [Fact]
    public void Constructor_WithUnsignedDllInTrustedRoot_ShouldNotThrowInDebug()
    {
        // In DEBUG builds, unsigned DLLs within trusted roots (app/solution directory)
        // should NOT require signature verification - this enables local development
        // without needing Authenticode code signing.
        var unsignedDllPath = Path.Combine(AppContext.BaseDirectory, "WpfDevTools.Inspector.dll");

#if DEBUG
        var act = () => new ConnectTool(new SessionManager(), unsignedDllPath);
        act.Should().NotThrow(
            "DEBUG builds should auto-skip signature verification for DLLs in trusted roots");
#else
        var act = () => new ConnectTool(new SessionManager(), unsignedDllPath);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*signature*");
#endif
    }

    [Theory]
    [InlineData("..\\..\\System32\\evil.dll")]
    [InlineData("C:\\Windows\\System32\\evil.dll")]
    [InlineData("\\\\network\\share\\evil.dll")]
    public void Constructor_WithMaliciousPath_ShouldThrow(string maliciousPath)
    {
        // Arrange

        // Act & Assert - should throw due to path validation
        var act = () => new ConnectTool(new SessionManager(), maliciousPath);
        act.Should().Throw<Exception>()
            .Where(ex => ex.GetType() == typeof(ArgumentException) || ex.GetType() == typeof(FileNotFoundException),
                "malicious or invalid DLL paths must never be accepted");
    }

    [Fact]
    public void Constructor_WithPathOutsideAppDirectory_ShouldThrow()
    {
        // Arrange
        var outsidePath = Path.Combine(Path.GetTempPath(), "evil.dll");

        // Act & Assert - should throw because path is outside application directory
        var act = () => new ConnectTool(new SessionManager(), outsidePath);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*application directory*");
    }

    [Fact]
    public void Constructor_WithTrustedSolutionRelativeDllPath_ShouldNotThrow()
    {


        var solutionRoot = FindSolutionRoot();
        var artifactsDir = Path.Combine(solutionRoot, ".test-artifacts");
        Directory.CreateDirectory(artifactsDir);

        var trustedDllPath = Path.Combine(artifactsDir, "WpfDevTools.Inspector.dll");
        File.WriteAllText(trustedDllPath, string.Empty);

        try
        {
            var act = () => new ConnectTool(new SessionManager(), trustedDllPath);
            act.Should().NotThrow();
        }
        finally
        {
            if (File.Exists(trustedDllPath))
            {
                File.Delete(trustedDllPath);
            }

            if (Directory.Exists(artifactsDir) && !Directory.EnumerateFileSystemEntries(artifactsDir).Any())
            {
                Directory.Delete(artifactsDir);
            }
        }
    }

    [Fact]
    public async Task Execute_WithArchitectureMismatch_ShouldReturnError()
    {
        // Arrange
        var tool = new ConnectTool(
            new SessionManager(),
            new FakeProcessInjector { ValidationResult = InjectionError.ArchitectureMismatch },
            GetTestInspectorDllPath());
        var parameters = new { processId = 12345 };

        // Act
        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("Architecture mismatch");
    }

    [Fact]
    public async Task Execute_AlreadyConnected_ShouldReturnSuccessImmediately()
    {
        // Arrange: simulate a process that's already in the session manager

        var sessionManager = new SessionManager();
        sessionManager.AddSession(42);

        var tool = new ConnectTool(
            sessionManager,
            new FakeProcessInjector(),
            GetTestInspectorDllPath());
        var parameters = new { processId = 42 };

        // Act: connect to already-connected process
        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        // Assert: should return success=true immediately (idempotent)
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeTrue();
        resultJson.GetProperty("message").GetString().Should().Contain("Already connected");
    }

    [Fact]
    public async Task Execute_InjectionFailure_ShouldPropagateError()
    {
        // Arrange
        var injector = new FakeProcessInjector
        {
            ValidationResult = InjectionError.None,
            ShouldFailInjection = true,
            InjectionErrorMessage = "DLL load failed in target process"
        };
        var tool = new ConnectTool(
            new SessionManager(),
            injector,
            GetTestInspectorDllPath());
        var parameters = new { processId = 12345 };

        // Act
        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        // Assert
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("DLL load failed");
    }

    [Fact]
    public async Task Execute_WhenPipeConnectionCancelled_ShouldCleanupSession()
    {
        // Arrange: injection succeeds, but CancellationToken fires during pipe connection

        var sessionManager = new SessionManager();
        var tool = new ConnectTool(
            sessionManager,
            new FakeProcessInjector(),
            GetTestInspectorDllPath());
        var parameters = new { processId = 12345 };

        // Use a CTS that cancels quickly to simulate ToolCallHelper timeout
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        // Act: should throw OperationCanceledException because pipe has no server
        var act = () => tool.ExecuteAsync(ToJsonElement(parameters), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        // Assert: session must be cleaned up after cancellation
        sessionManager.HasSession(12345).Should().BeFalse(
            "session must be removed when pipe connection is cancelled to prevent state divergence");
    }

    [Fact]
    public async Task Execute_WhenPipeConnectionFails_ShouldCleanupSession()
    {
        // Arrange: injection succeeds, but pipe connection returns false (no Inspector server)

        var sessionManager = new SessionManager();
        var tool = new ConnectTool(
            sessionManager,
            new FakeProcessInjector(),
            GetTestInspectorDllPath());
        var parameters = new { processId = 12345 };

        // Act: pipe connection will fail because there's no Inspector pipe server
        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        // Assert: session must be cleaned up after failed pipe connection
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        sessionManager.HasSession(12345).Should().BeFalse(
            "session must be removed when pipe connection fails");
    }

    [Fact]
    public async Task Execute_RateLimitExceeded_ShouldReturnError()
    {
        // Arrange
        var sessionManager = new SessionManager(maxRequestsPerMinute: 2);
        var tool = new ConnectTool(sessionManager);
        var parameters = new { processId = 12345 };

        // Consume rate limit
        await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);
        await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        // Act - third request should be rate limited
        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        // Assert
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("Rate limit");
    }

    private static string FindSolutionRoot()
    {
        var current = new DirectoryInfo(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!);

        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "WpfDevTools.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate solution root for ConnectTool test.");
    }

    private static string GetTestInspectorDllPath()
    {
        return Path.Combine(AppContext.BaseDirectory, "WpfDevTools.Inspector.dll");
    }

    private sealed class FakeProcessInjector : IProcessInjector
    {
        public InjectionError ValidationResult { get; init; } = InjectionError.None;
        public bool ShouldFailInjection { get; init; }
        public string InjectionErrorMessage { get; init; } = "Injection failed";

        public InjectionResult Inject(int processId, string dllPath, TimeSpan? timeout = null)
        {
            if (ShouldFailInjection)
            {
                return InjectionResult.CreateFailure(processId, InjectionError.Unknown, InjectionErrorMessage);
            }
            return InjectionResult.CreateSuccess(processId, dllPath);
        }

        public InjectionError ValidateTarget(int processId)
        {
            return ValidationResult;
        }

        public InjectionResult InjectWithBootstrap(InjectionRequest request)
        {
            if (ShouldFailInjection)
            {
                return InjectionResult.CreateFailure(request.ProcessId, InjectionError.BootstrapFailed, InjectionErrorMessage);
            }
            return InjectionResult.CreateSuccess(request.ProcessId, request.InspectorDllPath);
        }
    }
}
