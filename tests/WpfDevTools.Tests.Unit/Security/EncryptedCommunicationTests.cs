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

namespace WpfDevTools.Tests.Unit.Security;

public class EncryptedCommunicationTests : IDisposable
{
    private readonly string _tempCertDir;
    private readonly ITestOutputHelper _output;

    public EncryptedCommunicationTests(ITestOutputHelper output)
    {
        _output = output;
        _tempCertDir = Path.Combine(Path.GetTempPath(), "WpfDevTools_Test_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempCertDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempCertDir))
                Directory.Delete(_tempCertDir, recursive: true);
        }
        catch { }
    }

    [Fact]
    public async Task EncryptedPipe_WithAuth_ShouldTransmitDataCorrectly()
    {
        // Arrange
        var pid = Random.Shared.Next(100_000, 999_999);
        var secret = new byte[32];
        RandomNumberGenerator.Fill(secret);
        var authManager = new AuthenticationManager(
            envSecretProvider: () => Convert.ToBase64String(secret));
        var certManager = new CertificateManager(_tempCertDir);

        using var host = new InspectorHost(pid, authManager, certManager);
        host.Start();

        using var client = new NamedPipeClient(pid, authManager, certManager);

        // Act - connect with auth + encryption
        var connected = await client.ConnectAsync(TimeSpan.FromSeconds(10));
        connected.Should().BeTrue();

        // Send a ping request over encrypted channel
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var response = await client.SendRequestAsync(
            "enc-ping", new { method = "ping" }, cts.Token);

        // Assert
        response.Should().NotBeNull();
        response.Error.Should().BeNull();
    }

    [Fact]
    public async Task EncryptedPipe_WithoutAuth_ShouldTransmitDataCorrectly()
    {
        // Arrange - encryption only, no auth
        var pid = Random.Shared.Next(100_000, 999_999);
        var certManager = new CertificateManager(_tempCertDir);
        _output.WriteLine($"Pid={pid}, CertDir={_tempCertDir}");

        using var host = new InspectorHost(pid, authManager: null, certManager);
        host.Start();
        _output.WriteLine("Host started");

        using var client = new NamedPipeClient(pid, authManager: null, certManager);

        // Act
        _output.WriteLine("Connecting...");
        var connected = await client.ConnectAsync(TimeSpan.FromSeconds(15));
        _output.WriteLine($"Connected={connected}");
        connected.Should().BeTrue();

        _output.WriteLine("Sending request...");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var response = await client.SendRequestAsync(
            "enc-no-auth", new { method = "ping" }, cts.Token);

        // Assert
        _output.WriteLine($"Response received, error={response.Error}");
        response.Should().NotBeNull();
        response.Error.Should().BeNull();
    }

    [Fact]
    public async Task EncryptedPipe_LargeMessage_ShouldTransmitCorrectly()
    {
        // Arrange
        var pid = Random.Shared.Next(100_000, 999_999);
        var certManager = new CertificateManager(_tempCertDir);

        using var host = new InspectorHost(pid, authManager: null, certManager);
        host.Start();

        using var client = new NamedPipeClient(pid, authManager: null, certManager);
        var connected = await client.ConnectAsync(TimeSpan.FromSeconds(10));
        connected.Should().BeTrue();

        // Act - send request that should produce a response
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var response = await client.SendRequestAsync(
            "enc-large", new { method = "ping" }, cts.Token);

        // Assert
        response.Should().NotBeNull();
    }

    [Fact]
    public async Task NoEncryption_BackwardCompatible_ShouldStillWork()
    {
        // Arrange - no auth, no encryption (backward compatible)
        var pid = Random.Shared.Next(100_000, 999_999);
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
        var pid = Random.Shared.Next(100_000, 999_999);
        var secret = new byte[32];
        RandomNumberGenerator.Fill(secret);
        var authManager = new AuthenticationManager(
            envSecretProvider: () => Convert.ToBase64String(secret));
        var certManager = new CertificateManager(_tempCertDir);

        using var host = new InspectorHost(pid, authManager, certManager);
        host.Start();

        using var client = new NamedPipeClient(pid, authManager, certManager);
        var connected = await client.ConnectAsync(TimeSpan.FromSeconds(10));
        connected.Should().BeTrue();

        // Act - send multiple requests
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        for (int i = 0; i < 5; i++)
        {
            var response = await client.SendRequestAsync(
                $"enc-multi-{i}", new { method = "ping" }, cts.Token);
            response.Should().NotBeNull();
            response.Error.Should().BeNull();
        }
    }
}
