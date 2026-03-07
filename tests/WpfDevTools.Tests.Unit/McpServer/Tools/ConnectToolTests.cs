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
        using var _ = new SkipSignatureCheckScope();
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
        using var _ = new SkipSignatureCheckScope();
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
        using var _ = new SkipSignatureCheckScope();
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

    [Fact]
    public void Constructor_WithSkipSignatureCheck_ShouldNotThrow()
    {
        // Arrange
        using var _ = new SkipSignatureCheckScope();
        var unsignedDllPath = Path.Combine(AppContext.BaseDirectory, "WpfDevTools.Inspector.dll");

        // Act & Assert - should not throw when signature check is skipped
        var act = () => new ConnectTool(new SessionManager(), unsignedDllPath);
        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("..\\..\\System32\\evil.dll")]
    [InlineData("C:\\Windows\\System32\\evil.dll")]
    [InlineData("\\\\network\\share\\evil.dll")]
    public void Constructor_WithMaliciousPath_ShouldThrow(string maliciousPath)
    {
        // Arrange
        using var _ = new SkipSignatureCheckScope();

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
        using var _ = new SkipSignatureCheckScope();
        var outsidePath = Path.Combine(Path.GetTempPath(), "evil.dll");

        // Act & Assert - should throw because path is outside application directory
        var act = () => new ConnectTool(new SessionManager(), outsidePath);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*application directory*");
    }

    [Fact]
    public void Constructor_WithTrustedSolutionRelativeDllPath_ShouldNotThrow()
    {
        using var _ = new SkipSignatureCheckScope();

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
        using var _ = new SkipSignatureCheckScope();
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
        using var _ = new SkipSignatureCheckScope();
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
        using var _ = new SkipSignatureCheckScope();
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
    public async Task Execute_RateLimitExceeded_ShouldReturnError()
    {
        // Arrange
        using var _ = new SkipSignatureCheckScope();
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
    }
}
