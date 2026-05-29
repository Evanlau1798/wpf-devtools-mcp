using System.Text.Json;
using System.IO.Pipes;
using FluentAssertions;
using WpfDevTools.Injector;
using WpfDevTools.Injector.Discovery;
using WpfDevTools.Injector.Injection;
using WpfDevTools.Inspector.Host;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Configuration;
using WpfDevTools.Shared.Enums;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Security;
using WpfDevTools.Shared.Serialization;
using WpfDevTools.Tests.Unit.McpServer;
using WpfDevTools.Tests.Unit.TestSupport;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

[Collection("TimingSensitive")]
public sealed class ConnectToolRawInjectionPolicyTests : IDisposable
{
    private string? _dummyBootstrapperPath;

    [Fact]
    public async Task Execute_WhenRawInjectionTargetIsOutsideTrustedScope_ShouldReturnSecurityErrorWithoutInjection()
    {
        EnsureDummyBootstrapperExists();

        using var allowlistScope = new EnvironmentVariableScope(McpServerConfiguration.RawInjectionAllowedTargetsEnvVar, null);
        var injector = new FakeProcessInjector();
        var tool = new ConnectTool(
            new SessionManager(),
            injector,
            new FakeProcessDetector(executablePath: @"C:\ExternalApps\ThirdParty\TestApp.exe"),
            _ => { },
            () => false,
            targetPolicy: ConnectToolTestPolicies.AllowAllTargets);

        var result = await tool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), CancellationToken.None);

        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
        resultJson.GetProperty("errorCode").GetString().Should().Be("SecurityError");
        resultJson.GetProperty("requiresExplicitTargetOptIn").GetBoolean().Should().BeTrue();
        resultJson.GetProperty("allowlistEnvVar").GetString().Should().Be(McpServerConfiguration.RawInjectionAllowedTargetsEnvVar);
        injector.InjectWithBootstrapCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Execute_WhenExternalSdkOnlyTargetIsNotAllowlisted_ShouldReturnSecurityErrorBeforePackagingError()
    {
        EnsureDummyBootstrapperExists();

        var sdkOnlyDirectory = Directory.CreateTempSubdirectory("wpf-devtools-external-sdk-only-");
        var executablePath = Path.Combine(sdkOnlyDirectory.FullName, "ExternalSdkOnlyApp.exe");
        File.WriteAllBytes(executablePath, []);
        var processId = Environment.ProcessId;

        try
        {
            using var allowlistScope = new EnvironmentVariableScope(McpServerConfiguration.RawInjectionAllowedTargetsEnvVar, null);
            var injector = new FakeProcessInjector();
            var tool = new ConnectTool(
                new SessionManager(),
                injector,
                new FakeProcessDetector(executablePath: executablePath),
                _ => { },
                () => false,
                pipeReadyProbe: new PipeReadyProbe((_, _) => false, () => DateTime.UtcNow, _ => { }),
                targetPolicy: ConnectToolTestPolicies.AllowAllTargets);

            var result = await tool.ExecuteAsync(ToJsonElement(new { processId }), CancellationToken.None);

            var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
            resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
            resultJson.GetProperty("errorCode").GetString().Should().Be("SecurityError");
            resultJson.GetProperty("errorCode").GetString().Should().NotBe(InjectionError.SingleFileApplication.ToString(),
                "raw injection opt-in must be enforced before surfacing SDK-only packaging diagnostics");
            resultJson.GetProperty("requiresExplicitTargetOptIn").GetBoolean().Should().BeTrue();
            injector.InjectWithBootstrapCallCount.Should().Be(0);
        }
        finally
        {
            sdkOnlyDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Execute_WhenExternalSdkOnlyTargetStartsSecureHostWithinGracePeriod_ShouldReuseExistingHost()
    {
        EnsureDummyBootstrapperExists();

        var processId = Environment.ProcessId;
        var pipeName = CreateUniquePipeName($"WpfDevTools_{processId}");
        var certDirectory = Path.Combine(Path.GetTempPath(), $"wpf-devtools-external-sdk-grace-{Guid.NewGuid():N}");
        Directory.CreateDirectory(certDirectory);
        var sharedSecret = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var externalDirectory = Directory.CreateTempSubdirectory("wpf-devtools-external-sdk-delay-");
        var executablePath = Path.Combine(externalDirectory.FullName, "ExternalSdkOnlyApp.exe");
        File.WriteAllBytes(executablePath, []);

        using var hostAuthManager = new AuthenticationManager(() => sharedSecret);
        using var clientAuthManager = new AuthenticationManager(() => sharedSecret);
        var hostCertificateManager = new CertificateManager(certDirectory);
        var clientCertificateManager = new CertificateManager(certDirectory);
        using var host = new InspectorHost(processId, pipeName, hostAuthManager, hostCertificateManager);

        try
        {
            using var allowlistScope = new EnvironmentVariableScope(McpServerConfiguration.RawInjectionAllowedTargetsEnvVar, null);
            using var sessionManager = new SessionManager(
                authManager: clientAuthManager,
                certManager: clientCertificateManager);
            var injector = new FakeProcessInjector();
            var tool = new ConnectTool(
                sessionManager,
                injector,
                new FakeProcessDetector(executablePath: executablePath),
                _ => { },
                () => false,
                pipeReadyProbe: CreateExactPipeReadyProbe(pipeName),
                targetPolicy: ConnectToolTestPolicies.AllowAllTargets);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

            var startTask = Task.Run(async () =>
            {
                await Task.Delay(100, cts.Token);
                host.Start();
            }, cts.Token);

            var result = await tool.ExecuteAsync(ToJsonElement(new { processId }), cts.Token);
            await startTask;

            var resultPayload = JsonSerializer.Serialize(result);
            var resultJson = JsonSerializer.Deserialize<JsonElement>(resultPayload);
            resultJson.GetProperty("success").GetBoolean().Should().BeTrue(resultPayload);
            resultJson.GetProperty("reusedExistingHost").GetBoolean().Should().BeTrue();
            injector.InjectWithBootstrapCallCount.Should().Be(0);
        }
        finally
        {
            host.Stop();
            if (Directory.Exists(certDirectory))
            {
                Directory.Delete(certDirectory, recursive: true);
            }

            externalDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    [NamedPipeMitmScenario(
        "rejected-fake-host-no-raw-injection-fallback",
        "A rejected fake existing host must not fall through to raw injection unless policy explicitly allows injection.")]
    public async Task Execute_WhenBlockedExternalTargetFindsIncompatibleExistingHostDuringGracePeriod_ShouldReturnCompatibilityErrorWithoutInjection()
    {
        EnsureDummyBootstrapperExists();

        var processId = Environment.ProcessId;
        var pipeName = CreateUniquePipeName($"WpfDevTools_{processId}");
        var externalDirectory = Directory.CreateTempSubdirectory("wpf-devtools-external-sdk-stale-host-");
        var executablePath = Path.Combine(externalDirectory.FullName, "ExternalSdkOnlyApp.exe");
        File.WriteAllBytes(executablePath, []);
        using var incompatibleHost = CreateIncompatibleExistingHost(processId, pipeName);

        try
        {
            using var allowlistScope = new EnvironmentVariableScope(McpServerConfiguration.RawInjectionAllowedTargetsEnvVar, null);
            var injector = new FakeProcessInjector();
            var tool = new ConnectTool(
                new SessionManager(),
                injector,
                new FakeProcessDetector(executablePath: executablePath),
                _ => { },
                () => false,
                pipeReadyProbe: CreateExactPipeReadyProbe(pipeName),
                targetPolicy: ConnectToolTestPolicies.AllowAllTargets);

            var result = await tool.ExecuteAsync(ToJsonElement(new { processId }), CancellationToken.None);

            var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
            resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
            resultJson.GetProperty("errorCode").GetString().Should().Be("CompatibilityError");
            injector.InjectWithBootstrapCallCount.Should().Be(0,
                "a stale or spoofed preexisting pipe must not bypass the raw injection policy or fall through to bootstrap injection");
        }
        finally
        {
            externalDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public async Task Execute_WhenRawInjectionTargetMatchesAllowlist_ShouldProceedToInjection()
    {
        EnsureDummyBootstrapperExists();

        var processId = NextSyntheticProcessId();
        var targetDirectory = Directory.CreateTempSubdirectory("wpf-devtools-allowed-target-");
        var executablePath = Path.Combine(targetDirectory.FullName, "AllowedApp.exe");
        File.WriteAllBytes(executablePath, []);
        File.WriteAllText(Path.Combine(targetDirectory.FullName, "AllowedApp.runtimeconfig.json"), "{}");

        try
        {
            using var allowlistScope = new EnvironmentVariableScope(
                McpServerConfiguration.RawInjectionAllowedTargetsEnvVar,
                executablePath);
            var injector = new FakeProcessInjector
            {
                ShouldFailInjection = true,
                InjectionErrorMessage = "Expected downstream injection failure",
                FailedError = InjectionError.BootstrapFailed
            };
            using var sessionManager = new SessionManager();
            var tool = new ConnectTool(
                sessionManager,
                injector,
                new FakeProcessDetector(executablePath: executablePath),
                _ => { },
                () => false,
                targetPolicy: ConnectToolTestPolicies.AllowAllTargets);

            var result = await tool.ExecuteAsync(ToJsonElement(new { processId }), CancellationToken.None);

            var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
            resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
            resultJson.GetProperty("errorCode").GetString().Should().Be("BootstrapFailed");
            resultJson.GetProperty("error").GetString().Should().Contain("Bootstrap failed");
            injector.InjectWithBootstrapCallCount.Should().Be(1);
        }
        finally
        {
            targetDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Authorize_WhenTargetIsUnderRepositoryRootWithoutAllowlist_ShouldFailClosed()
    {
        var repoTargetPath = TestRepositoryPaths.GetRepoFilePath("tests/WpfDevTools.Tests.TestApp/WpfDevTools.Tests.TestApp.csproj");
        var processInfo = CreateProcessInfo(repoTargetPath);

        var authorization = RawInjectionTargetPolicy.Authorize(
            processInfo,
            AppContext.BaseDirectory,
            configuredAllowedTargets: null,
            path => Path.GetFullPath(path));

        authorization.IsAllowed.Should().BeFalse();
        authorization.Error.Should().Contain("exact allowlist");
        authorization.Hint.Should().Contain(McpServerConfiguration.RawInjectionAllowedTargetsEnvVar);
    }

    [Fact]
    public void Authorize_WhenRepositoryTargetMatchesAllowlist_ShouldAllowExplicitOptIn()
    {
        var repoTargetPath = TestRepositoryPaths.GetRepoFilePath("tests/WpfDevTools.Tests.TestApp/WpfDevTools.Tests.TestApp.csproj");
        var processInfo = CreateProcessInfo(repoTargetPath);

        var authorization = RawInjectionTargetPolicy.Authorize(
            processInfo,
            AppContext.BaseDirectory,
            configuredAllowedTargets: repoTargetPath,
            path => Path.GetFullPath(path));

        authorization.IsAllowed.Should().BeTrue();
    }

    [Fact]
    public void Authorize_WhenRepositoryRootCannotBeResolved_ShouldFailClosed()
    {
        var tempDirectory = Directory.CreateTempSubdirectory("wpf-devtools-no-sln-");
        var targetDirectory = Directory.CreateTempSubdirectory("wpf-devtools-external-target-");
        var targetPath = Path.Combine(targetDirectory.FullName, "ExternalApp.exe");
        File.WriteAllBytes(targetPath, []);

        try
        {
            var processInfo = CreateProcessInfo(targetPath, processName: "ExternalApp");

            var authorization = RawInjectionTargetPolicy.Authorize(
                processInfo,
                tempDirectory.FullName,
                configuredAllowedTargets: null,
                path => File.Exists(path) || Directory.Exists(path) ? Path.GetFullPath(path) : null);

            authorization.IsAllowed.Should().BeFalse();
            authorization.Error.Should().Contain("blocked by the server's target policy");
        }
        finally
        {
            tempDirectory.Delete(recursive: true);
            targetDirectory.Delete(recursive: true);
        }
    }

    [Fact]
    public void Authorize_WhenResolvedPhysicalPathDiffersFromReviewedPath_ShouldFailClosed()
    {
        var repoRoot = TestRepositoryPaths.ResolveRepoRoot(AppContext.BaseDirectory);
        var linkedTargetPath = Path.Combine(repoRoot, "virtual-link", "ExternalApp.exe");
        var outsideTargetPath = @"C:\ExternalApps\ThirdParty\ExternalApp.exe";
        var processInfo = CreateProcessInfo(linkedTargetPath, processName: "ExternalApp");

        var authorization = RawInjectionTargetPolicy.Authorize(
            processInfo,
            AppContext.BaseDirectory,
            configuredAllowedTargets: null,
            path => string.Equals(Path.GetFullPath(path), Path.GetFullPath(linkedTargetPath), StringComparison.OrdinalIgnoreCase)
                ? outsideTargetPath
                : Path.GetFullPath(path));

        authorization.IsAllowed.Should().BeFalse();
        authorization.Error.Should().Contain("local absolute path");
    }

    [Fact]
    public void Authorize_WhenAllowlistContainsMalformedEntry_ShouldFailClosed()
    {
        const string targetPath = @"C:\Explicit\AllowedApp.exe";
        var processInfo = CreateProcessInfo(targetPath);

        var authorization = RawInjectionTargetPolicy.Authorize(
            processInfo,
            AppContext.BaseDirectory,
            configuredAllowedTargets: $@"relative\app.exe;{targetPath}",
            path => Path.GetFullPath(path));

        authorization.IsAllowed.Should().BeFalse();
        authorization.Error.Should().Contain("Invalid raw injection allowlist configuration");
        authorization.Hint.Should().Contain(McpServerConfiguration.RawInjectionAllowedTargetsEnvVar);
    }

    [Fact]
    public void GetConfiguredAllowedTargets_WithMalformedEntry_ShouldFailClosed()
    {
        var configuredTargets = RawInjectionTargetPolicy.GetConfiguredAllowedTargets(
            @"relative\app.exe;C:drive-relative\app.exe;C:\Explicit\AllowedApp.exe",
            path => Path.GetFullPath(path));

        configuredTargets.Should().BeEmpty();
    }

    public void Dispose()
    {
    }

    private void EnsureDummyBootstrapperExists()
    {
        _dummyBootstrapperPath = EnsureSharedDummyBootstrapperExists();
    }

    private static WpfProcessInfo CreateProcessInfo(string executablePath, string processName = "TestApp")
        => new()
        {
            ProcessId = NextSyntheticProcessId(),
            ProcessName = processName,
            Architecture = ProcessArchitecture.X64,
            Runtime = TargetRuntime.NetCore,
            IsWpfApplication = true,
            ExecutablePath = executablePath
        };

    private static NamedPipeServerStream CreateIncompatibleExistingHost(int processId, string pipeName)
    {
        var server = new NamedPipeServerStream(
            pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        _ = Task.Run(async () =>
        {
            try
            {
                await server.WaitForConnectionAsync();
                var requestJson = await MessageFraming.ReadMessageAsync(server, CancellationToken.None);
                var request = JsonSerializer.Deserialize<InspectorRequest>(requestJson);

                request.Should().NotBeNull();
                request!.Method.Should().Be("ping");

                var response = new InspectorResponse
                {
                    Id = request.Id,
                    CorrelationId = request.CorrelationId,
                    Result = JsonSerializer.SerializeToElement(new
                    {
                        success = true,
                        status = "pong",
                        processId,
                        protocolVersion = InspectorCompatibilityContract.ProtocolVersion,
                        buildFingerprint = "stale-build"
                    }),
                    Error = null
                };

                await MessageFraming.WriteMessageAsync(
                    server,
                    JsonSerializer.Serialize(response),
                    CancellationToken.None);
            }
            catch (IOException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        });

        return server;
    }

    private static PipeReadyProbe CreateExactPipeReadyProbe(string pipeName)
        => new(
            (pipePath, _) => string.Equals(pipePath, $@"\\.\pipe\{pipeName}", StringComparison.Ordinal),
            () => DateTime.UtcNow,
            _ => { },
            () => [pipeName]);

    private sealed class EnvironmentVariableScope : IDisposable
    {
        private readonly string _name;
        private readonly string? _originalValue;

        public EnvironmentVariableScope(string name, string? value)
        {
            _name = name;
            _originalValue = Environment.GetEnvironmentVariable(name);
            Environment.SetEnvironmentVariable(name, value);
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(_name, _originalValue);
        }
    }

    private sealed class FakeProcessDetector(string? executablePath = null) : WpfProcessDetector
    {
        public override WpfProcessInfo? GetProcessInfo(int processId)
            => new()
            {
                ProcessId = processId,
                ProcessName = "TestApp",
                Architecture = ProcessArchitecture.X64,
                Runtime = TargetRuntime.NetCore,
                IsWpfApplication = true,
                IsElevated = false,
                ExecutablePath = executablePath
            };
    }

    private sealed class FakeProcessInjector : IProcessInjector
    {
        public bool ShouldFailInjection { get; init; }
        public string InjectionErrorMessage { get; init; } = "Injection failed";
        public InjectionError FailedError { get; init; } = InjectionError.BootstrapFailed;
        public int InjectWithBootstrapCallCount { get; private set; }

        public InjectionResult Inject(int processId, string dllPath, TimeSpan? timeout = null)
            => InjectionResult.CreateSuccess(processId, dllPath);

        public InjectionError ValidateTarget(int processId)
            => InjectionError.None;

        public InjectionResult InjectWithBootstrap(
            InjectionRequest request,
            CancellationToken cancellationToken = default)
        {
            InjectWithBootstrapCallCount++;
            return ShouldFailInjection
                ? InjectionResult.CreateFailure(request.ProcessId, FailedError, InjectionErrorMessage)
                : InjectionResult.CreateSuccess(request.ProcessId, request.InspectorDllPath, bootstrapExitCode: 0, pipeName: request.ExpectedPipeName);
        }
    }
}
