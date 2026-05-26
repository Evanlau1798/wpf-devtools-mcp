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
    public async Task Execute_WithExistingPipeThatClosesDuringSecureHandshake_ShouldReturnSecurityErrorWithoutInjection()
    {
        EnsureDummyBootstrapperExists();

        var processId = Environment.ProcessId;
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
            (await Task.WhenAny(acceptTask, Task.Delay(TimeSpan.FromSeconds(5))) == acceptTask)
                .Should().BeTrue("connect should reach the fake default pipe before reporting secure handshake failure");

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
    public async Task Execute_WithExistingPlaintextHostThatNeverCompletesSecureHandshake_ShouldReturnTimeoutWithoutInjection()
    {
        EnsureDummyBootstrapperExists();

        var processId = Environment.ProcessId;
        var secretFilePath = Path.Combine(Path.GetTempPath(), $"wpf-devtools-auth-{Guid.NewGuid():N}.bin");
        var certDirectory = Path.Combine(Path.GetTempPath(), $"wpf-devtools-plaintext-timeout-{Guid.NewGuid():N}");
        Directory.CreateDirectory(certDirectory);

        using var server = new NamedPipeServerStream(
            $"WpfDevTools_{processId}",
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);
        using var serverRelease = new ManualResetEventSlim(false);
        var acceptTask = Task.Run(async () =>
        {
            await server.WaitForConnectionAsync();
            serverRelease.Wait(TimeSpan.FromSeconds(5));
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
            var tool = CreateTool(sessionManager: sessionManager, injector: injector,
                connectTimeout: TimeSpan.FromSeconds(3));

            var result = await tool.ExecuteAsync(ToJsonElement(new { processId }), CancellationToken.None);

            var resultJson = JsonSerializer.Deserialize<JsonElement>(JsonSerializer.Serialize(result));
            resultJson.GetProperty("success").GetBoolean().Should().BeFalse();
            resultJson.GetProperty("errorCode").GetString().Should().Be("Timeout");
            injector.InjectWithBootstrapCallCount.Should().Be(0);
        }
        finally
        {
            serverRelease.Set();
            await acceptTask;
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

        var processId = Environment.ProcessId;
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
    public async Task Execute_WithExistingSecureHostAndMismatchedCertificateDirectory_ShouldReturnSecurityErrorWithoutInjection()
    {
        EnsureDummyBootstrapperExists();

        var processId = Environment.ProcessId;
        var hostCertDirectory = Path.Combine(Path.GetTempPath(), $"wpf-devtools-host-cert-mismatch-{Guid.NewGuid():N}");
        var clientCertDirectory = Path.Combine(Path.GetTempPath(), $"wpf-devtools-client-cert-mismatch-{Guid.NewGuid():N}");
        Directory.CreateDirectory(hostCertDirectory);
        Directory.CreateDirectory(clientCertDirectory);

        var sharedSecret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        using var hostAuthManager = new AuthenticationManager(() => sharedSecret);
        using var clientAuthManager = new AuthenticationManager(() => sharedSecret);
        var hostCertificateManager = new CertificateManager(hostCertDirectory);
        var clientCertificateManager = new CertificateManager(clientCertDirectory);
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
            injector.InjectWithBootstrapCallCount.Should().Be(0,
                "an SDK-host with a different certificate directory must not be reused or followed by raw injection");
        }
        finally
        {
            host.Stop();
            if (Directory.Exists(hostCertDirectory))
            {
                Directory.Delete(hostCertDirectory, recursive: true);
            }
            if (Directory.Exists(clientCertDirectory))
            {
                Directory.Delete(clientCertDirectory, recursive: true);
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
}
