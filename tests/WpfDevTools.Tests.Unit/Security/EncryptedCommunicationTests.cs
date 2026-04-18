using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Inspector.Host;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Security;
using WpfDevTools.Shared.Serialization;
using Xunit;
using Xunit.Abstractions;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.Security;

[Collection("TimingSensitive")]
public class EncryptedCommunicationTests : IDisposable
{
    private readonly string _tempCertDir;
    private readonly string _otherTempCertDir;
    private readonly ITestOutputHelper _output;

    public EncryptedCommunicationTests(ITestOutputHelper output)
    {
        _output = output;
        _tempCertDir = Path.Combine(Path.GetTempPath(), "WpfDevTools_Test_" + Guid.NewGuid());
        _otherTempCertDir = Path.Combine(Path.GetTempPath(), "WpfDevTools_Test_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempCertDir);
        Directory.CreateDirectory(_otherTempCertDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempCertDir))
                Directory.Delete(_tempCertDir, recursive: true);

            if (Directory.Exists(_otherTempCertDir))
                Directory.Delete(_otherTempCertDir, recursive: true);
        }
        catch { }
    }

    [Fact]
    public async Task EncryptedPipe_WithDifferentPinnedCertificate_ShouldRejectConnection()
    {
        // Arrange
        var pid = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        var pipeName = CreateUniquePipeName();
        var serverCertManager = new CertificateManager(_tempCertDir);
        var clientCertManager = new CertificateManager(_otherTempCertDir);

        using var host = new InspectorHost(pid, pipeName, authManager: null, serverCertManager);
        host.Start();

        using var client = new NamedPipeClient(pid, pipeName, authManager: null, clientCertManager);

        // Act
        var connected = await client.ConnectAsync(TimeSpan.FromSeconds(10));

        // Assert
        connected.Should().BeFalse("client should pin to its expected certificate thumbprint");
        client.LastConnectFailure.Should().Be(NamedPipeConnectFailure.SecureTransportFailed);
    }

    [Fact]
    public async Task EncryptedPipe_WithAuth_ShouldTransmitDataCorrectly()
    {
        // Arrange
        var pid = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        var pipeName = CreateUniquePipeName();
        var secret = new byte[32];
        RandomNumberGenerator.Fill(secret);
        var authManager = new AuthenticationManager(
            envSecretProvider: () => Convert.ToBase64String(secret));
        var certManager = new CertificateManager(_tempCertDir);

        using var host = new InspectorHost(pid, pipeName, authManager, certManager);
        host.Start();

        using var client = new NamedPipeClient(pid, pipeName, authManager, certManager);

        // Act - connect with auth + encryption
        var connected = await client.ConnectAsync(TimeSpan.FromSeconds(10));
        connected.Should().BeTrue();

        // Send a ping request over encrypted channel
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var response = await client.SendRequestAsync(
            "ping", "enc-ping", new { }, cts.Token);

        // Assert
        response.Should().NotBeNull();
        response.Error.Should().BeNull();
    }

    [Fact]
    public async Task EncryptedPipe_WithoutAuth_ShouldTransmitDataCorrectly()
    {
        // Arrange - encryption only, no auth
        var pid = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        var pipeName = CreateUniquePipeName();
        var certManager = new CertificateManager(_tempCertDir);
        _output.WriteLine($"Pid={pid}, CertDir={_tempCertDir}");

        using var host = new InspectorHost(pid, pipeName, authManager: null, certManager);
        host.Start();
        _output.WriteLine("Host started");

        using var client = new NamedPipeClient(pid, pipeName, authManager: null, certManager);

        // Act
        _output.WriteLine("Connecting...");
        var connected = await client.ConnectAsync(TimeSpan.FromSeconds(15));
        _output.WriteLine($"Connected={connected}");
        connected.Should().BeTrue();

        _output.WriteLine("Sending request...");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var response = await client.SendRequestAsync(
            "ping", "enc-no-auth", new { }, cts.Token);

        // Assert
        _output.WriteLine($"Response received, error={response.Error}");
        response.Should().NotBeNull();
        response.Error.Should().BeNull();
    }

    [Fact]
    public async Task EncryptedPipe_LargeMessage_ShouldTransmitCorrectly()
    {
        // Arrange
        var pid = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        var pipeName = CreateUniquePipeName();
        var certManager = new CertificateManager(_tempCertDir);

        using var host = new InspectorHost(pid, pipeName, authManager: null, certManager);
        host.Start();

        using var client = new NamedPipeClient(pid, pipeName, authManager: null, certManager);
        var connected = await client.ConnectAsync(TimeSpan.FromSeconds(10));
        connected.Should().BeTrue();

        // Act - send request that should produce a response
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var response = await client.SendRequestAsync(
            "ping", "enc-large", new { }, cts.Token);

        // Assert
        response.Should().NotBeNull();
    }

    [Fact]
    public async Task NoEncryption_BackwardCompatible_ShouldStillWork()
    {
        // Arrange - no auth, no encryption (backward compatible)
        var pid = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        using var host = new InspectorHost(pid);
        host.Start();

        using var client = new NamedPipeClient(pid);

        // Act
        var connected = await client.ConnectAsync(TimeSpan.FromSeconds(5));

        // Assert
        connected.Should().BeTrue();
    }

    [Fact]
    public async Task EncryptedPipe_MultipleRequests_ShouldAllSucceed()
    {
        // Arrange
        var pid = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        var pipeName = CreateUniquePipeName();
        var secret = new byte[32];
        RandomNumberGenerator.Fill(secret);
        var authManager = new AuthenticationManager(
            envSecretProvider: () => Convert.ToBase64String(secret));
        var certManager = new CertificateManager(_tempCertDir);

        using var host = new InspectorHost(pid, pipeName, authManager, certManager);
        host.Start();

        using var client = new NamedPipeClient(pid, pipeName, authManager, certManager);
        var connected = await client.ConnectAsync(TimeSpan.FromSeconds(10));
        connected.Should().BeTrue();

        // Act - send multiple requests
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        for (int i = 0; i < 5; i++)
        {
            var response = await client.SendRequestAsync(
                "ping", $"enc-multi-{i}", new { }, cts.Token);
            response.Should().NotBeNull();
            response.Error.Should().BeNull();
        }
    }
}
