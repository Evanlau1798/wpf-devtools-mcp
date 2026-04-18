using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Inspector.Host;
using WpfDevTools.Mcp.Server;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Security;
using WpfDevTools.Shared.Serialization;
using Xunit;

namespace WpfDevTools.Tests.Unit.McpServer;

public class NamedPipeClientAuthTests
{
    [Fact]
    public async Task ConnectAsync_WithMatchingSecret_ShouldSucceed()
    {
        // Arrange
        var pid = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        var secret = new byte[32];
        RandomNumberGenerator.Fill(secret);
        var authManager = new AuthenticationManager(
            envSecretProvider: () => Convert.ToBase64String(secret));

        using var host = new InspectorHost(pid, authManager);
        host.Start();

        using var client = new NamedPipeClient(pid, authManager);

        // Act
        var connected = await client.ConnectAsync(TimeSpan.FromSeconds(5));

        // Assert
        connected.Should().BeTrue();
        client.IsConnected.Should().BeTrue();
    }

    [Fact]
    public async Task ConnectAsync_WithMismatchedSecret_ShouldReturnFalse()
    {
        // Arrange
        var pid = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        var serverSecret = new byte[32];
        RandomNumberGenerator.Fill(serverSecret);
        var serverAuth = new AuthenticationManager(
            envSecretProvider: () => Convert.ToBase64String(serverSecret));

        var clientSecret = new byte[32];
        RandomNumberGenerator.Fill(clientSecret);
        var clientAuth = new AuthenticationManager(
            envSecretProvider: () => Convert.ToBase64String(clientSecret));

        using var host = new InspectorHost(pid, serverAuth);
        host.Start();

        using var client = new NamedPipeClient(pid, clientAuth);

        // Act
        var connected = await client.ConnectAsync(TimeSpan.FromSeconds(5));

        // Assert
        connected.Should().BeFalse();
        client.LastConnectFailure.Should().Be(NamedPipeConnectFailure.AuthenticationFailed);
    }

    [Fact]
    public async Task ConnectAsync_WithAuth_ThenSendRequest_ShouldWork()
    {
        // Arrange
        var pid = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        var secret = new byte[32];
        RandomNumberGenerator.Fill(secret);
        var authManager = new AuthenticationManager(
            envSecretProvider: () => Convert.ToBase64String(secret));

        using var host = new InspectorHost(pid, authManager);
        host.Start();

        using var client = new NamedPipeClient(pid, authManager);
        var connected = await client.ConnectAsync(TimeSpan.FromSeconds(5));
        connected.Should().BeTrue();

        // Act
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var response = await client.SendRequestAsync(
            "ping", "test-ping", new { }, cts.Token);

        // Assert
        response.Should().NotBeNull();
        response.Error.Should().BeNull();
    }

    [Fact]
    public async Task ConnectAsync_NoAuth_BackwardCompatible_ShouldWork()
    {
        // Arrange - both sides without auth
        var pid = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        using var host = new InspectorHost(pid);
        host.Start();

        using var client = new NamedPipeClient(pid);

        // Act
        var connected = await client.ConnectAsync(TimeSpan.FromSeconds(5));

        // Assert
        connected.Should().BeTrue();
    }
}
