using Xunit;
using FluentAssertions;
using System.Security.Cryptography;
using System.Text.Json;
using System.IO.Pipes;
using System.Reflection;
using WpfDevTools.Injector;
using WpfDevTools.Injector.Discovery;
using WpfDevTools.Injector.Injection;
using WpfDevTools.Inspector.Host;
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
        _dummyBootstrapperPath = EnsureSharedDummyBootstrapperExists();
    }

    public void Dispose()
    {
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
    public async Task Execute_WithPersistedDefaultAuthenticationAndConfiguredTls_ShouldForwardSecureBootstrapOptions()
    {
        EnsureDummyBootstrapperExists();

        var secretFilePath = Path.Combine(Path.GetTempPath(), $"wpf-devtools-auth-{Guid.NewGuid():N}.bin");
        var certDirectory = Path.Combine(Path.GetTempPath(), $"wpf-devtools-generated-auth-{Guid.NewGuid():N}");
        Directory.CreateDirectory(certDirectory);
        var transportSecurity = TransportSecurityConfiguration.Create(
            null,
            certDirectory,
            new PersistedAuthenticationSecretStore(secretFilePath));
        var expectedAuthSecret = Convert.ToBase64String(transportSecurity.AuthenticationManager.GetSharedSecret());
        var expectedCertDirectory = transportSecurity.CertificateManager.CertificateDirectory;
        try
        {
            using var sessionManager = new SessionManager(
                authManager: transportSecurity.AuthenticationManager,
                certManager: transportSecurity.CertificateManager);
            var injector = new FakeProcessInjector
            {
                ShouldFailInjection = true,
                InjectionErrorMessage = "stop after inspecting persisted default request",
                FailedError = InjectionError.BootstrapFailed
            };
            var tool = CreateTool(sessionManager: sessionManager, injector: injector);

            var result = await tool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), CancellationToken.None);

            var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
            resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
            injector.LastInjectionRequest.Should().NotBeNull();
            injector.LastInjectionRequest!.AuthenticationSecretBase64.Should().Be(expectedAuthSecret);
            injector.LastInjectionRequest.CertificateDirectory.Should().Be(expectedCertDirectory);
            injector.CertificateFileExistedAtInjection.Should().BeTrue(
                "the certificate should already exist before the bootstrapper request is sent");
            injector.PasswordFileExistedAtInjection.Should().BeTrue(
                "the certificate password should already exist before the bootstrapper request is sent");
            File.Exists(Path.Combine(expectedCertDirectory, "server.pfx")).Should().BeTrue();
            File.Exists(Path.Combine(expectedCertDirectory, "server.pwd")).Should().BeTrue();
        }
        finally
        {
            transportSecurity.AuthenticationManager.Dispose();
            if (File.Exists(secretFilePath))
            {
                File.Delete(secretFilePath);
            }
            if (Directory.Exists(expectedCertDirectory))
            {
                Directory.Delete(expectedCertDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Execute_AfterServerRestartWithPersistedDefaultAuthentication_ShouldReconnectToExistingSecureInspectorHost()
    {
        EnsureDummyBootstrapperExists();

        var processId = Environment.ProcessId;
        var secretFilePath = Path.Combine(Path.GetTempPath(), $"wpf-devtools-auth-{Guid.NewGuid():N}.bin");
        var certDirectory = Path.Combine(Path.GetTempPath(), $"wpf-devtools-reconnect-{Guid.NewGuid():N}");
        Directory.CreateDirectory(certDirectory);

        var initialTransportSecurity = TransportSecurityConfiguration.Create(
            null,
            certDirectory,
            new PersistedAuthenticationSecretStore(secretFilePath));

        using var host = new InspectorHost(
            processId,
            initialTransportSecurity.AuthenticationManager,
            initialTransportSecurity.CertificateManager);
        host.Start();

        var restartedTransportSecurity = TransportSecurityConfiguration.Create(
            null,
            certDirectory,
            new PersistedAuthenticationSecretStore(secretFilePath));

        try
        {
            using var sessionManager = new SessionManager(
                authManager: restartedTransportSecurity.AuthenticationManager,
                certManager: restartedTransportSecurity.CertificateManager);
            var injector = new FakeProcessInjector();
            var tool = CreateTool(sessionManager: sessionManager, injector: injector);

            var result = await tool.ExecuteAsync(ToJsonElement(new { processId }), CancellationToken.None);

            var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
            resultJson.GetProperty("success").GetBoolean().Should().BeTrue();
            resultJson.GetProperty("reusedExistingHost").GetBoolean().Should().BeTrue();
            injector.InjectWithBootstrapCallCount.Should().Be(0);
            sessionManager.GetPipeClient(processId).Should().NotBeNull();
            sessionManager.GetPipeClient(processId)!.IsConnected.Should().BeTrue();
        }
        finally
        {
            host.Stop();
            restartedTransportSecurity.AuthenticationManager.Dispose();
            initialTransportSecurity.AuthenticationManager.Dispose();
            if (File.Exists(secretFilePath))
            {
                File.Delete(secretFilePath);
            }
            if (Directory.Exists(certDirectory))
            {
                Directory.Delete(certDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Execute_WhenNoExistingHost_ShouldFallbackToInjectionWithoutConsumingConnectBudget()
    {
        EnsureDummyBootstrapperExists();

        const int processId = 24680;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        using var injector = new BootstrapStartsPipeInjector();
        var tool = CreateTool(injector: injector);

        var result = await tool.ExecuteAsync(ToJsonElement(new { processId }), cts.Token);

        var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
        resultJson.GetProperty("success").GetBoolean().Should().BeTrue();
        injector.InjectWithBootstrapCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Execute_WithUnsupportedPackagingAndExistingSecureHost_ShouldReuseExistingHostWithoutInjection()
    {
        EnsureDummyBootstrapperExists();

        var processId = Environment.ProcessId;
        var certDirectory = Path.Combine(Path.GetTempPath(), $"wpf-devtools-sdk-reuse-{Guid.NewGuid():N}");
        Directory.CreateDirectory(certDirectory);
        var sharedSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        using var hostAuthManager = new AuthenticationManager(() => sharedSecret);
        using var clientAuthManager = new AuthenticationManager(() => sharedSecret);
        var hostCertificateManager = new CertificateManager(certDirectory);
        var clientCertificateManager = new CertificateManager(certDirectory);
        using var host = new InspectorHost(processId, hostAuthManager, hostCertificateManager);
        host.Start();

        try
        {
            using var sessionManager = new SessionManager(
                authManager: clientAuthManager,
                certManager: clientCertificateManager);
            var injector = new FakeProcessInjector();
            var executablePath = CreateSdkOnlyExecutablePath();
            var tool = CreateTool(
                sessionManager: sessionManager,
                injector: injector,
                processDetector: new FakeProcessDetector(executablePath: executablePath));

            try
            {
                var result = await tool.ExecuteAsync(ToJsonElement(new { processId }), CancellationToken.None);

                var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
                resultJson.GetProperty("success").GetBoolean().Should().BeTrue();
                resultJson.GetProperty("reusedExistingHost").GetBoolean().Should().BeTrue();
                injector.InjectWithBootstrapCallCount.Should().Be(0);
            }
            finally
            {
                DeleteSdkOnlyExecutablePath(executablePath);
            }
        }
        finally
        {
            host.Stop();
            if (Directory.Exists(certDirectory))
            {
                Directory.Delete(certDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Execute_WithUnsupportedPackagingAndDelayedSecureHost_ShouldReuseExistingHostWithinConnectBudget()
    {
        EnsureDummyBootstrapperExists();

        var processId = Environment.ProcessId;
        var certDirectory = Path.Combine(Path.GetTempPath(), $"wpf-devtools-sdk-delayed-{Guid.NewGuid():N}");
        Directory.CreateDirectory(certDirectory);
        var sharedSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        using var hostAuthManager = new AuthenticationManager(() => sharedSecret);
        using var clientAuthManager = new AuthenticationManager(() => sharedSecret);
        var hostCertificateManager = new CertificateManager(certDirectory);
        var clientCertificateManager = new CertificateManager(certDirectory);
        using var host = new InspectorHost(processId, hostAuthManager, hostCertificateManager);

        try
        {
            using var sessionManager = new SessionManager(
                authManager: clientAuthManager,
                certManager: clientCertificateManager);
            var injector = new FakeProcessInjector();
            var executablePath = CreateSdkOnlyExecutablePath();
            var tool = CreateTool(
                sessionManager: sessionManager,
                injector: injector,
                processDetector: new FakeProcessDetector(executablePath: executablePath));
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            try
            {
                var startTask = Task.Run(async () =>
                {
                    await Task.Delay(600, cts.Token);
                    host.Start();
                }, cts.Token);

                var result = await tool.ExecuteAsync(ToJsonElement(new { processId }), cts.Token);
                await startTask;

                var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
                resultJson.GetProperty("success").GetBoolean().Should().BeTrue();
                resultJson.GetProperty("reusedExistingHost").GetBoolean().Should().BeTrue();
                injector.InjectWithBootstrapCallCount.Should().Be(0);
            }
            finally
            {
                DeleteSdkOnlyExecutablePath(executablePath);
            }
        }
        finally
        {
            host.Stop();
            if (Directory.Exists(certDirectory))
            {
                Directory.Delete(certDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Execute_WithExistingPipeThatClosesDuringSecureHandshake_ShouldReturnSecurityErrorWithoutInjection()
    {
        EnsureDummyBootstrapperExists();

        const int processId = 24681;
        var secretFilePath = Path.Combine(Path.GetTempPath(), $"wpf-devtools-auth-{Guid.NewGuid():N}.bin");
        var certDirectory = Path.Combine(Path.GetTempPath(), $"wpf-devtools-plaintext-host-{Guid.NewGuid():N}");
        Directory.CreateDirectory(certDirectory);

        using var server = new NamedPipeServerStream(
            $"WpfDevTools_{processId}",
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        var acceptTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            server.Dispose();
        });

        var transportSecurity = TransportSecurityConfiguration.Create(
            null,
            certDirectory,
            new PersistedAuthenticationSecretStore(secretFilePath));

        try
        {
            using var sessionManager = new SessionManager(
                authManager: transportSecurity.AuthenticationManager,
                certManager: transportSecurity.CertificateManager);
            var injector = new FakeProcessInjector();
            var tool = CreateTool(sessionManager: sessionManager, injector: injector);

            var result = await tool.ExecuteAsync(ToJsonElement(new { processId }), CancellationToken.None);
            await acceptTask;

            var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
            resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
            resultJson.GetProperty("errorCode").GetString().Should().Be("SecurityError");
            injector.InjectWithBootstrapCallCount.Should().Be(0);
        }
        finally
        {
            transportSecurity.AuthenticationManager.Dispose();
            if (File.Exists(secretFilePath))
            {
                File.Delete(secretFilePath);
            }
            if (Directory.Exists(certDirectory))
            {
                Directory.Delete(certDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Execute_WithExistingSecureHostAndMismatchedAuthenticationSecret_ShouldReturnSecurityErrorWithoutInjection()
    {
        EnsureDummyBootstrapperExists();

        const int processId = 24682;
        var certDirectory = Path.Combine(Path.GetTempPath(), $"wpf-devtools-auth-mismatch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(certDirectory);

        var hostSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var clientSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        using var hostAuthManager = new AuthenticationManager(() => hostSecret);
        using var clientAuthManager = new AuthenticationManager(() => clientSecret);
        var hostCertificateManager = new CertificateManager(certDirectory);
        var clientCertificateManager = new CertificateManager(certDirectory);
        using var host = new InspectorHost(processId, hostAuthManager, hostCertificateManager);
        host.Start();

        try
        {
            using var sessionManager = new SessionManager(
                authManager: clientAuthManager,
                certManager: clientCertificateManager);
            var injector = new FakeProcessInjector();
            var tool = CreateTool(sessionManager: sessionManager, injector: injector);

            var result = await tool.ExecuteAsync(ToJsonElement(new { processId }), CancellationToken.None);

            var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
            resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
            resultJson.GetProperty("errorCode").GetString().Should().Be("SecurityError");
            injector.InjectWithBootstrapCallCount.Should().Be(0);
        }
        finally
        {
            host.Stop();
            if (Directory.Exists(certDirectory))
            {
                Directory.Delete(certDirectory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task Execute_WhenSecureTransportArtifactsCannotBeCreated_ShouldReturnClearError()
    {
        var certDirectory = Path.GetTempFileName();
        using var sessionManager = new SessionManager(certManager: new CertificateManager(certDirectory));
        var injector = new FakeProcessInjector();
        var tool = CreateTool(sessionManager: sessionManager, injector: injector);

        try
        {
            var result = await tool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), CancellationToken.None);

            var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
            resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
            resultJson.GetProperty("errorCode").GetString().Should().Be("SecureTransportInitializationFailed");
            resultJson.GetProperty("error").GetString().Should().Contain("Failed to prepare secure transport artifacts");
            injector.InjectWithBootstrapCallCount.Should().Be(0);
        }
        finally
        {
            if (File.Exists(certDirectory))
            {
                File.Delete(certDirectory);
            }
        }
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

    private sealed class FakeProcessDetector(bool isElevated = false, string? executablePath = null) : WpfProcessDetector
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
                IsElevated = isElevated,
                ExecutablePath = executablePath
            };
        }
    }

    private static string CreateSdkOnlyExecutablePath()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"wpf-devtools-sdk-only-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var executablePath = Path.Combine(directory, "TestApp.exe");
        File.WriteAllBytes(executablePath, Array.Empty<byte>());
        return executablePath;
    }

    private static void DeleteSdkOnlyExecutablePath(string executablePath)
    {
        if (File.Exists(executablePath))
        {
            File.Delete(executablePath);
        }

        var directory = Path.GetDirectoryName(executablePath);
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private sealed class EmptyProcessDetector : WpfProcessDetector
    {
        public override IReadOnlyList<WpfProcessInfo> GetAllWpfProcesses(ProcessWindowFilter windowFilter)
            => [];
    }

    private class FakeProcessInjector : IProcessInjector
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
        public Func<InjectionRequest, CancellationToken, InjectionResult>? InjectWithBootstrapHandler { get; init; }

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

            if (InjectWithBootstrapHandler != null)
            {
                return InjectWithBootstrapHandler(request, cancellationToken);
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

    private sealed class BootstrapStartsPipeInjector : FakeProcessInjector, IDisposable
    {
        private NamedPipeServerStream? _server;

        public BootstrapStartsPipeInjector()
        {
            InjectWithBootstrapHandler = StartPipeServer;
        }

        private InjectionResult StartPipeServer(InjectionRequest request, CancellationToken cancellationToken)
        {
            _server = new NamedPipeServerStream(
                request.ExpectedPipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            _ = _server.WaitForConnectionAsync(cancellationToken);

            return InjectionResult.CreateSuccess(
                request.ProcessId,
                request.InspectorDllPath,
                bootstrapExitCode: 0,
                pipeName: request.ExpectedPipeName);
        }

        public void Dispose()
        {
            _server?.Dispose();
        }
    }
}
