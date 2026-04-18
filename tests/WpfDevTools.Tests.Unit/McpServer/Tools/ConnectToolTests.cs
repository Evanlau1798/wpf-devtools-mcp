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
using WpfDevTools.Shared.Security;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

[Collection("TimingSensitive")]
public partial class ConnectToolTests : IDisposable
{
    private string? _dummyBootstrapperPath;

    private static ConnectTool CreateTool(
        SessionManager? sessionManager = null,
        FakeProcessInjector? injector = null,
        WpfProcessDetector? processDetector = null,
        Action<string>? dllPathValidator = null,
        Func<bool>? isCurrentProcessElevated = null)
    {
        return new ConnectTool(
            sessionManager ?? new SessionManager(),
            injector ?? new FakeProcessInjector(),
            processDetector ?? new FakeProcessDetector(),
            dllPathValidator ?? (_ => { }),
            isCurrentProcessElevated ?? (() => false));
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
    public async Task Execute_WithMissingProcessId_ShouldReturnAutoDiscoveryError()
    {
        var tool = CreateTool(processDetector: new EmptyProcessDetector());
        var parameters = new { };

        var result = await tool.ExecuteAsync(ToJsonElement(parameters), CancellationToken.None);

        result.Should().NotBeNull();
        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("errorCode").GetString().Should().Be("NoWpfProcessesFound");
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
        var act = () => DllPathValidator.ValidateDllPath(unsignedDllPath);
        var signatureAction = DllPathValidator.GetCurrentBuildSignatureAction();

        if (signatureAction == SignaturePolicy.Action.Skip)
        {
            act.Should().NotThrow(
                "development builds should auto-skip signature verification for DLLs in trusted roots");
        }
        else
        {
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*signature*");
        }
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
            var signatureAction = DllPathValidator.GetCurrentBuildSignatureAction();

            if (signatureAction == SignaturePolicy.Action.Skip)
            {
                act.Should().NotThrow();
            }
            else
            {
                act.Should().Throw<InvalidOperationException>()
                    .WithMessage("*signature*");
            }
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
            processDetector: new FakeProcessDetector(isElevated: true),
            isCurrentProcessElevated: () => false);

        var result = await tool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), CancellationToken.None);

        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("elevated");
        resultJson.GetProperty("error").GetString().Should().Contain("administrator");
        resultJson.GetProperty("requiresElevationToConnect").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Execute_WithElevatedTargetAndNonElevatedServer_ShouldReturnPreflightPermissionWarning()
    {
        var injector = new FakeProcessInjector();
        var tool = CreateTool(
            injector: injector,
            processDetector: new FakeProcessDetector(isElevated: true),
            isCurrentProcessElevated: () => false);

        var result = await tool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), CancellationToken.None);

        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("errorCode").GetString().Should().Be("AccessDenied");
        resultJson.GetProperty("targetIsElevated").GetBoolean().Should().BeTrue();
        resultJson.GetProperty("requiresElevationToConnect").GetBoolean().Should().BeTrue();
        resultJson.GetProperty("canConnectFromCurrentServer").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("administrator");
        injector.InjectWithBootstrapCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Execute_WithElevatedTargetAndElevatedServer_ShouldProceedPastPreflight()
    {
        EnsureDummyBootstrapperExists();

        var injector = new FakeProcessInjector
        {
            ShouldFailInjection = true,
            InjectionErrorMessage = "Expected downstream injection failure",
            FailedError = InjectionError.BootstrapFailed
        };

        var tool = CreateTool(
            injector: injector,
            processDetector: new FakeProcessDetector(isElevated: true),
            isCurrentProcessElevated: () => true);

        var result = await tool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), CancellationToken.None);

        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("error").GetString().Should().Contain("Expected downstream injection failure");
        injector.InjectWithBootstrapCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Execute_WithSecureSessionConfiguration_ShouldForwardSecureBootstrapOptions()
    {
        EnsureDummyBootstrapperExists();

        var authSecret = Convert.ToBase64String(new byte[32]);
        var authManager = new AuthenticationManager(() => authSecret);
        var certDirectory = Path.Combine(Path.GetTempPath(), $"wpf-devtools-certs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(certDirectory);
        try
        {
            using var sessionManager = new SessionManager(authManager: authManager, certManager: new CertificateManager(certDirectory));
            var injector = new FakeProcessInjector
            {
                ShouldFailInjection = true,
                InjectionErrorMessage = "stop after inspecting request",
                FailedError = InjectionError.BootstrapFailed
            };
            var tool = CreateTool(sessionManager: sessionManager, injector: injector);

            var result = await tool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), CancellationToken.None);

            var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
            resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
            injector.LastInjectionRequest.Should().NotBeNull();
            injector.LastInjectionRequest!.AuthenticationSecretBase64.Should().Be(authSecret);
            injector.LastInjectionRequest.CertificateDirectory.Should().Be(certDirectory);
            injector.CertificateFileExistedAtInjection.Should().BeTrue(
                "the certificate should already exist before the bootstrapper request is sent");
            injector.PasswordFileExistedAtInjection.Should().BeTrue(
                "the certificate password should already exist before the bootstrapper request is sent");
            File.Exists(Path.Combine(certDirectory, "server.pfx")).Should().BeTrue(
                "the client and injected inspector must share a pre-created certificate to avoid first-connect races");
            File.Exists(Path.Combine(certDirectory, "server.pwd")).Should().BeTrue(
                "the protected certificate password must exist before secure bootstrap starts");
        }
        finally
        {
            Directory.Delete(certDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Execute_WhenSecureTransportArtifactsCannotBeCreated_ShouldReturnClearError()
    {
        var certDirectory = "invalid" + '\0' + "cert-path";
        using var sessionManager = new SessionManager(certManager: new CertificateManager(certDirectory));
        var injector = new FakeProcessInjector();
        var tool = CreateTool(sessionManager: sessionManager, injector: injector);

        var result = await tool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), CancellationToken.None);

        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("errorCode").GetString().Should().Be("SecureTransportInitializationFailed");
        resultJson.GetProperty("error").GetString().Should().Contain("Failed to prepare secure transport artifacts");
        injector.InjectWithBootstrapCallCount.Should().Be(0);
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

    private sealed class EmptyProcessDetector : WpfProcessDetector
    {
        public override IReadOnlyList<WpfProcessInfo> GetAllWpfProcesses(ProcessWindowFilter windowFilter)
            => [];
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
        public InjectionRequest? LastInjectionRequest { get; private set; }
        public bool CertificateFileExistedAtInjection { get; private set; }
        public bool PasswordFileExistedAtInjection { get; private set; }

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
            LastInjectionRequest = request;
            LastInjectWithBootstrapCancellationToken = cancellationToken;
            if (!string.IsNullOrWhiteSpace(request.CertificateDirectory))
            {
                CertificateFileExistedAtInjection = File.Exists(Path.Combine(request.CertificateDirectory, "server.pfx"));
                PasswordFileExistedAtInjection = File.Exists(Path.Combine(request.CertificateDirectory, "server.pwd"));
            }

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
