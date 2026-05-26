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
using WpfDevTools.Shared.Configuration;
using WpfDevTools.Shared.Enums;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Security;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public partial class ConnectToolTests
{
    [Fact]
    public async Task Execute_WithSecureSessionConfiguration_ShouldForwardProcessScopedSecureBootstrapOptions()
    {
        EnsureDummyBootstrapperExists();

        var authSecret = Convert.ToBase64String(new byte[32]);
        using var authManager = new AuthenticationManager(() => authSecret);
        var certDirectory = Path.Combine(Path.GetTempPath(), $"wpf-devtools-certs-{Guid.NewGuid():N}");
        Directory.CreateDirectory(certDirectory);
        try
        {
            using var sessionManager = CreateSecureSessionManager(
                authManager,
                new CertificateManager(certDirectory),
                processId: 12345);
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
            var expectedAuthSecret = CreateExpectedSecretProvider(authManager)
                .GetAuthenticationSecretBase64(12345, injector.LastInjectionRequest!.ExpectedPipeName);
            injector.LastInjectionRequest!.AuthenticationSecretBase64.Should().Be(expectedAuthSecret);
            injector.LastInjectionRequest.AuthenticationSecretBase64.Should().NotBe(authSecret);
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
    public async Task Execute_WithPersistedDefaultAuthenticationAndConfiguredTls_ShouldForwardProcessScopedSecureBootstrapOptions()
    {
        EnsureDummyBootstrapperExists();

        var secretFilePath = Path.Combine(Path.GetTempPath(), $"wpf-devtools-auth-{Guid.NewGuid():N}.bin");
        var certDirectory = Path.Combine(Path.GetTempPath(), $"wpf-devtools-generated-auth-{Guid.NewGuid():N}");
        Directory.CreateDirectory(certDirectory);
        var transportSecurity = TransportSecurityConfiguration.Create(
            null,
            certDirectory,
            new PersistedAuthenticationSecretStore(secretFilePath));
        var expectedCertDirectory = transportSecurity.CertificateManager.CertificateDirectory;
        try
        {
            using var sessionManager = CreateSecureSessionManager(
                transportSecurity.AuthenticationManager,
                transportSecurity.CertificateManager,
                processId: 12345);
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
            var expectedAuthSecret = CreateExpectedSecretProvider(transportSecurity.AuthenticationManager)
                .GetAuthenticationSecretBase64(12345, injector.LastInjectionRequest!.ExpectedPipeName);
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
    public async Task Execute_WhenProcessScopedIdentityIsIncomplete_ShouldFailClosedWithoutInjection()
    {
        EnsureDummyBootstrapperExists();

        var authSecret = Convert.ToBase64String(new byte[32]);
        using var authManager = new AuthenticationManager(() => authSecret);
        using var sessionManager = new SessionManager(
            McpServerConfiguration.RateLimitRequestsPerMinute,
            authManager,
            certManager: null,
            utcNowProvider: null,
            processIdentityProvider: processId => new SessionManager.ProcessIdentity(
                processId,
                StartTimeUtcTicks: null));
        var injector = new FakeProcessInjector();
        var tool = CreateTool(sessionManager: sessionManager, injector: injector);

        var result = await tool.ExecuteAsync(ToJsonElement(new { processId = 12345 }), CancellationToken.None);

        var payload = JsonSerializer.SerializeToElement(result);
        payload.GetProperty("success").GetBoolean().Should().BeFalse();
        payload.GetProperty("errorCode").GetString().Should().Be("SecurityError");
        injector.InjectWithBootstrapCallCount.Should().Be(0);
    }

    [Fact]
    public async Task Execute_AfterServerRestartWithPersistedDefaultAuthentication_ShouldReconnectToExistingSecureInspectorHost()
    {
        EnsureDummyBootstrapperExists();

        var processId = Environment.ProcessId;
        var pipeName = CreateUniquePipeName($"WpfDevTools_{processId}");
        var secretFilePath = Path.Combine(Path.GetTempPath(), $"wpf-devtools-auth-{Guid.NewGuid():N}.bin");
        var certDirectory = Path.Combine(Path.GetTempPath(), $"wpf-devtools-reconnect-{Guid.NewGuid():N}");
        Directory.CreateDirectory(certDirectory);

        var initialTransportSecurity = TransportSecurityConfiguration.Create(
            null,
            certDirectory,
            new PersistedAuthenticationSecretStore(secretFilePath));
        using var initialSessionManager = new SessionManager(
            authManager: initialTransportSecurity.AuthenticationManager,
            certManager: initialTransportSecurity.CertificateManager);
        var hostAuthenticationSecretBase64 = initialSessionManager.GetAuthenticationSecretBase64(processId, pipeName);

        using var hostAuthenticationManager = new AuthenticationManager(() => hostAuthenticationSecretBase64);
        using var host = new InspectorHost(
            processId,
            pipeName,
            hostAuthenticationManager,
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
            var pipeReadyProbe = new PipeReadyProbe(
                (pipePath, _) => string.Equals(pipePath, $@"\\.\pipe\{pipeName}", StringComparison.Ordinal),
                () => DateTime.UtcNow,
                _ => { });
            var tool = CreateTool(sessionManager: sessionManager, injector: injector, pipeReadyProbe: pipeReadyProbe);

            var result = await tool.ExecuteAsync(ToJsonElement(new { processId }), CancellationToken.None);

            var resultPayload = JsonSerializer.Serialize(result);
            var resultJson = JsonSerializer.Deserialize<JsonElement>(resultPayload);
            resultJson.GetProperty("success").GetBoolean().Should().BeTrue(resultPayload);
            resultJson.GetProperty("message").GetString().Should().Contain("get_ui_summary");
            resultJson.GetProperty("message").GetString().Should().Contain("get_element_snapshot");
            resultJson.GetProperty("message").GetString().Should().Contain("get_form_summary");
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

    private static SessionManager CreateSecureSessionManager(
        AuthenticationManager authManager,
        CertificateManager certificateManager,
        int processId)
        => new(
            McpServerConfiguration.RateLimitRequestsPerMinute,
            authManager,
            certificateManager,
            utcNowProvider: null,
            processIdentityProvider: candidateProcessId => candidateProcessId == processId
                ? new SessionManager.ProcessIdentity(candidateProcessId, StartTimeUtcTicks: 1_000_000 + candidateProcessId)
                : null);

    private static ProcessAuthenticationSecretProvider CreateExpectedSecretProvider(AuthenticationManager authManager)
        => new(
            authManager,
            processId => new ProcessAuthenticationSecretProvider.ProcessIdentity(
                processId,
                StartTimeUtcTicks: 1_000_000 + processId));
}
