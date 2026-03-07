using Xunit;
using FluentAssertions;
using System.Text.Json;
using System.Reflection;
using WpfDevTools.Injector;
using WpfDevTools.Injector.Discovery;
using WpfDevTools.Injector.Injection;
using WpfDevTools.Shared.Enums;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public class ConnectToolTests
{
    private static ConnectTool CreateTool(
        SessionManager? sessionManager = null,
        FakeProcessInjector? injector = null)
    {
        return new ConnectTool(
            sessionManager ?? new SessionManager(),
            injector ?? new FakeProcessInjector(),
            new FakeProcessDetector());
    }

    [Fact]
    public async Task Execute_WithInvalidProcessId_ShouldReturnError()
    {
        var tool = new ConnectTool(new SessionManager());
        var parameters = new { processId = 999999 };

        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        result.Should().NotBeNull();
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task Execute_WithMissingProcessId_ShouldReturnError()
    {
        var tool = new ConnectTool(new SessionManager());
        var parameters = new { };

        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        result.Should().NotBeNull();
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("processId");
    }

    [Fact]
    public async Task Execute_WithNonWpfProcess_ShouldReturnError()
    {
        var tool = CreateTool(injector:
            new FakeProcessInjector { ValidationResult = InjectionError.NotWpfApplication });
        var parameters = new { processId = 12345 };

        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        result.Should().NotBeNull();
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("not a WPF application");
    }

    [Fact]
    public void ValidateDllPath_WithUnsignedDllInTrustedRoot_ShouldNotThrowInDebug()
    {
        var unsignedDllPath = Path.Combine(AppContext.BaseDirectory, "WpfDevTools.Inspector.dll");

#if DEBUG
        var act = () => ConnectTool.ValidateDllPath(unsignedDllPath);
        act.Should().NotThrow(
            "DEBUG builds should auto-skip signature verification for DLLs in trusted roots");
#else
        var act = () => ConnectTool.ValidateDllPath(unsignedDllPath);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*signature*");
#endif
    }

    [Theory]
    [InlineData("C:\\Windows\\System32\\evil.dll")]
    [InlineData("\\\\network\\share\\evil.dll")]
    public void ValidateDllPath_WithMaliciousPath_ShouldThrow(string maliciousPath)
    {
        var act = () => ConnectTool.ValidateDllPath(maliciousPath);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ValidateDllPath_WithPathOutsideAppDirectory_ShouldThrow()
    {
        var outsidePath = Path.Combine(Path.GetTempPath(), "evil.dll");

        var act = () => ConnectTool.ValidateDllPath(outsidePath);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*application directory*");
    }

    [Fact]
    public void ValidateDllPath_WithTrustedSolutionRelativeDllPath_ShouldNotThrow()
    {
        var solutionRoot = FindSolutionRoot();
        var artifactsDir = Path.Combine(solutionRoot, ".test-artifacts");
        Directory.CreateDirectory(artifactsDir);

        var trustedDllPath = Path.Combine(artifactsDir, "WpfDevTools.Inspector.dll");
        File.WriteAllText(trustedDllPath, string.Empty);

        try
        {
            var act = () => ConnectTool.ValidateDllPath(trustedDllPath);
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
        var tool = CreateTool(injector:
            new FakeProcessInjector { ValidationResult = InjectionError.ArchitectureMismatch });
        var parameters = new { processId = 12345 };

        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        result.Should().NotBeNull();
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("Architecture mismatch");
    }

    [Fact]
    public async Task Execute_AlreadyConnected_ShouldReturnSuccessImmediately()
    {
        var sessionManager = new SessionManager();
        sessionManager.AddSession(42);

        var tool = CreateTool(sessionManager: sessionManager);
        var parameters = new { processId = 42 };

        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeTrue();
        resultJson.GetProperty("message").GetString().Should().Contain("Already connected");
    }

    [Fact]
    public async Task Execute_BootstrapInjectionFailure_ShouldPropagateError()
    {
        EnsureDummyBootstrapperExists();
        var injector = new FakeProcessInjector
        {
            ValidationResult = InjectionError.None,
            ShouldFailInjection = true,
            InjectionErrorMessage = "CLR hosting initialization failed",
            FailedStage = BootstrapStage.ClrHosting,
            FailedExitCode = 0x11
        };
        var tool = CreateTool(injector: injector);
        var parameters = new { processId = 12345 };

        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("CLR hosting");
        resultJson.GetProperty("stage").GetString().Should().Be("ClrHosting");
    }

    [Fact]
    public async Task Execute_BootstrapPipeTimeout_ShouldReturnPipeError()
    {
        EnsureDummyBootstrapperExists();
        var injector = new FakeProcessInjector
        {
            ValidationResult = InjectionError.None,
            ShouldFailInjection = true,
            InjectionErrorMessage = "Named Pipe did not become ready",
            FailedStage = BootstrapStage.PipeReady,
            FailedExitCode = 0,
            FailedError = InjectionError.PipeReadyTimeout
        };
        var tool = CreateTool(injector: injector);
        var parameters = new { processId = 12345 };

        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("Pipe");
    }

    [Fact]
    public async Task Execute_WhenPipeConnectionCancelled_ShouldCleanupSession()
    {
        EnsureDummyBootstrapperExists();
        var sessionManager = new SessionManager();
        var tool = CreateTool(sessionManager: sessionManager);
        var parameters = new { processId = 12345 };

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var act = () => tool.ExecuteAsync(ToJsonElement(parameters), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();

        sessionManager.HasSession(12345).Should().BeFalse(
            "session must be removed when pipe connection is cancelled to prevent state divergence");
    }

    [Fact]
    public async Task Execute_WhenPipeConnectionFails_ShouldCleanupSession()
    {
        var sessionManager = new SessionManager();
        var tool = CreateTool(sessionManager: sessionManager);
        var parameters = new { processId = 12345 };

        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        sessionManager.HasSession(12345).Should().BeFalse(
            "session must be removed when pipe connection fails");
    }

    [Fact]
    public async Task Execute_RateLimitExceeded_ShouldReturnError()
    {
        var sessionManager = new SessionManager(maxRequestsPerMinute: 2);
        var tool = new ConnectTool(sessionManager);
        var parameters = new { processId = 12345 };

        await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);
        await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("Rate limit");
    }

    private static string EnsureDummyBootstrapperExists()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "WpfDevTools.Bootstrapper.x64.dll");
        if (!File.Exists(path))
        {
            File.WriteAllBytes(path, Array.Empty<byte>());
        }
        return path;
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

    private sealed class FakeProcessDetector : WpfProcessDetector
    {
        public override WpfProcessInfo? GetProcessInfo(int processId)
        {
            return new WpfProcessInfo
            {
                ProcessId = processId,
                ProcessName = "TestApp",
                Architecture = ProcessArchitecture.X64,
                Runtime = TargetRuntime.NetCore,
                IsWpfApplication = true
            };
        }
    }

    private sealed class FakeProcessInjector : IProcessInjector
    {
        public InjectionError ValidationResult { get; init; } = InjectionError.None;
        public bool ShouldFailInjection { get; init; }
        public string InjectionErrorMessage { get; init; } = "Injection failed";
        public BootstrapStage? FailedStage { get; init; }
        public int? FailedExitCode { get; init; }
        public InjectionError FailedError { get; init; } = InjectionError.BootstrapFailed;

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
                return InjectionResult.CreateFailure(
                    request.ProcessId,
                    FailedError,
                    InjectionErrorMessage,
                    failedAtStage: FailedStage,
                    bootstrapExitCode: FailedExitCode);
            }
            return InjectionResult.CreateSuccess(
                request.ProcessId,
                request.InspectorDllPath,
                bootstrapExitCode: 0,
                pipeName: request.ExpectedPipeName);
        }
    }
}
