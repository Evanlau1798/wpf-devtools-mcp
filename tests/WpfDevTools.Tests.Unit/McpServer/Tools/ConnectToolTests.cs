using Xunit;
using FluentAssertions;
using System.Text.Json;
using System.IO.Pipes;
using System.Reflection;
using WpfDevTools.Injector;
using WpfDevTools.Injector.Discovery;
using WpfDevTools.Injector.Injection;
using WpfDevTools.Shared.Enums;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public class ConnectToolTests : IDisposable
{
    private string? _dummyBootstrapperPath;

    private static ConnectTool CreateTool(
        SessionManager? sessionManager = null,
        FakeProcessInjector? injector = null,
        WpfProcessDetector? processDetector = null,
        Action<string>? dllPathValidator = null)
    {
        return new ConnectTool(
            sessionManager ?? new SessionManager(),
            injector ?? new FakeProcessInjector(),
            processDetector ?? new FakeProcessDetector(),
            dllPathValidator ?? (_ => { }));
    }

    private void EnsureDummyBootstrapperExists()
    {
        _dummyBootstrapperPath = Path.Combine(
            AppContext.BaseDirectory, "WpfDevTools.Bootstrapper.x64.dll");
        if (!File.Exists(_dummyBootstrapperPath))
        {
            File.WriteAllBytes(_dummyBootstrapperPath, Array.Empty<byte>());
        }
    }

    public void Dispose()
    {
        if (_dummyBootstrapperPath != null && File.Exists(_dummyBootstrapperPath))
        {
            try { File.Delete(_dummyBootstrapperPath); }
            catch { }
        }
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

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Execute_WithNonPositiveProcessId_ShouldReturnValidationError(int invalidProcessId)
    {
        var tool = new ConnectTool(new SessionManager());
        var parameters = new { processId = invalidProcessId };

        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("positive integer");
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
        var act = () => DllPathValidator.ValidateDllPath(unsignedDllPath);
        act.Should().NotThrow(
            "DEBUG builds should auto-skip signature verification for DLLs in trusted roots");
#else
        var act = () => DllPathValidator.ValidateDllPath(unsignedDllPath);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*signature*");
#endif
    }

    [Theory]
    [InlineData("C:\\Windows\\System32\\evil.dll")]
    [InlineData("\\\\network\\share\\evil.dll")]
    public void ValidateDllPath_WithMaliciousPath_ShouldThrow(string maliciousPath)
    {
        var act = () => DllPathValidator.ValidateDllPath(maliciousPath);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void ValidateDllPath_WithPathOutsideAppDirectory_ShouldThrow()
    {
        var outsidePath = Path.Combine(Path.GetTempPath(), "evil.dll");

        var act = () => DllPathValidator.ValidateDllPath(outsidePath);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*application directory*");
    }

    [Fact]
    public void ValidateDllPath_WithTrustedSolutionRelativeDllPath_ShouldRespectBuildSignaturePolicy()
    {
        var solutionRoot = FindSolutionRoot();
        var artifactsDir = Path.Combine(solutionRoot, ".test-artifacts");
        Directory.CreateDirectory(artifactsDir);

        var trustedDllPath = Path.Combine(artifactsDir, "WpfDevTools.Inspector.dll");
        File.WriteAllText(trustedDllPath, string.Empty);

        try
        {
            var act = () => DllPathValidator.ValidateDllPath(trustedDllPath);
#if DEBUG
            act.Should().NotThrow();
#else
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*signature*");
#endif
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
    public async Task Execute_WithElevatedTargetAccessDenied_ShouldExplainAdministratorRequirement()
    {
        var tool = CreateTool(
            injector: new FakeProcessInjector { ValidationResult = InjectionError.AccessDenied },
            processDetector: new FakeProcessDetector(isElevated: true));

        var result = await tool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), CancellationToken.None);

        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("elevated");
        resultJson.GetProperty("error").GetString().Should().Contain("administrator");
        resultJson.GetProperty("requiresElevationToConnect").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Execute_AlreadyConnected_ShouldReturnSuccessImmediately()
    {
        var processId = Random.Shared.Next(100_000, 999_999);
        var pipeName = $"WpfDevTools_Test_{Guid.NewGuid():N}";
        using var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        var acceptTask = Task.Run(async () => await server.WaitForConnectionAsync());

        using var sessionManager = new SessionManager();
        sessionManager.AddSession(processId);
        using var pipeClient = new NamedPipeClient(processId, pipeName);

        var connected = await pipeClient.ConnectAsync(
            TimeSpan.FromSeconds(5),
            maxRetries: 3,
            cancellationToken: CancellationToken.None);
        connected.Should().BeTrue();
        await acceptTask;

        ReplacePipeClient(sessionManager, processId, pipeClient);

        var tool = CreateTool(sessionManager: sessionManager);
        var parameters = new { processId };

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

        sessionManager.HasSession(12345).Should().BeFalse();
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
        sessionManager.HasSession(12345).Should().BeFalse();
    }

    [Fact]
    public async Task Execute_WithDisconnectedStaleSession_ShouldRemoveItAndAttemptFreshInjection()
    {
        EnsureDummyBootstrapperExists();
        var sessionManager = new SessionManager();
        sessionManager.AddSession(12345);

        var injector = new FakeProcessInjector
        {
            ShouldFailInjection = true,
            InjectionErrorMessage = "Fresh injection attempted"
        };

        var tool = CreateTool(sessionManager: sessionManager, injector: injector);

        var result = await tool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), CancellationToken.None);

        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("Fresh injection attempted");
        sessionManager.HasSession(12345).Should().BeFalse();
        injector.InjectWithBootstrapCallCount.Should().Be(1);
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

    [Fact]
    public async Task Execute_ShouldForwardCancellationTokenToBootstrapInjector()
    {
        EnsureDummyBootstrapperExists();

        var injector = new FakeProcessInjector
        {
            ShouldFailInjection = true,
            InjectionErrorMessage = "Stop after injector call"
        };
        var tool = CreateTool(injector: injector);
        using var cts = new CancellationTokenSource();

        var result = await tool.ExecuteAsync(
            ToJsonElement(new { processId = 12345 }),
            cts.Token);

        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        injector.InjectWithBootstrapCallCount.Should().Be(1);
        injector.LastInjectWithBootstrapCancellationToken.Should().Be(cts.Token);
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

    private static void ReplacePipeClient(SessionManager sessionManager, int processId, NamedPipeClient replacement)
    {
        var field = typeof(SessionManager).GetField("_pipeClients", BindingFlags.Instance | BindingFlags.NonPublic);
        field.Should().NotBeNull();

        var pipeClients = field!.GetValue(sessionManager) as Dictionary<int, NamedPipeClient>;
        pipeClients.Should().NotBeNull();

        if (pipeClients!.TryGetValue(processId, out var existingClient))
        {
            existingClient.Dispose();
        }

        pipeClients[processId] = replacement;
    }

    private sealed class FakeProcessDetector(bool isElevated = false) : WpfProcessDetector
    {
        public override WpfProcessInfo? GetProcessInfo(int processId)
        {
            return new WpfProcessInfo
            {
                ProcessId = processId,
                ProcessName = "TestApp",
                Architecture = ProcessArchitecture.X64,
                Runtime = TargetRuntime.NetCore,
                IsWpfApplication = true,
                IsElevated = isElevated
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
        public int InjectWithBootstrapCallCount { get; private set; }
        public CancellationToken LastInjectWithBootstrapCancellationToken { get; private set; }

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

        public InjectionResult InjectWithBootstrap(
            InjectionRequest request,
            CancellationToken cancellationToken = default)
        {
            InjectWithBootstrapCallCount++;
            LastInjectWithBootstrapCancellationToken = cancellationToken;

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
