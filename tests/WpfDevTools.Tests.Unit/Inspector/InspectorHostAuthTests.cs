using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Inspector.Host;
using WpfDevTools.Shared.Messages;
using WpfDevTools.Shared.Security;
using WpfDevTools.Shared.Serialization;
using Xunit;

namespace WpfDevTools.Tests.Unit.Inspector;

public class InspectorHostAuthTests
{
    [Fact]
    public async Task AuthEnabled_ValidClient_ShouldAllowCommunication()
    {
        // Arrange
        var pid = Random.Shared.Next(100_000, 999_999);
        var secret = new byte[32];
        RandomNumberGenerator.Fill(secret);
        var authManager = new AuthenticationManager(
            envSecretProvider: () => Convert.ToBase64String(secret));

        using var host = new InspectorHost(pid, authManager);
        host.Start();

        using var client = new NamedPipeClientStream(
            ".", $"WpfDevTools_{pid}", PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(5_000);

        // Act - perform client-side authentication
        var authenticated = await PerformClientAuthentication(client, secret);
        authenticated.Should().BeTrue();

        // Send a ping after authentication
        var request = new InspectorRequest
        {
            Id = "auth-ping-1",
            Method = "ping",
            Params = null
        };
        await MessageFraming.WriteMessageAsync(client, JsonSerializer.Serialize(request));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var responseJson = await MessageFraming.ReadMessageAsync(client, cts.Token);
        var response = JsonSerializer.Deserialize<InspectorResponse>(responseJson);

        // Assert
        response.Should().NotBeNull();
        response!.Id.Should().Be("auth-ping-1");
        response.Error.Should().BeNull();
    }

    [Fact]
    public async Task AuthEnabled_InvalidSecret_ShouldDisconnectClient()
    {
        // Arrange
        var pid = Random.Shared.Next(100_000, 999_999);
        var serverSecret = new byte[32];
        RandomNumberGenerator.Fill(serverSecret);
        var authManager = new AuthenticationManager(
            envSecretProvider: () => Convert.ToBase64String(serverSecret));

        using var host = new InspectorHost(pid, authManager);
        host.Start();

        using var client = new NamedPipeClientStream(
            ".", $"WpfDevTools_{pid}", PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(5_000);

        // Act - use wrong secret
        var wrongSecret = new byte[32];
        RandomNumberGenerator.Fill(wrongSecret);
        var authenticated = await PerformClientAuthentication(client, wrongSecret);

        // Assert - authentication should fail and pipe should be disconnected
        authenticated.Should().BeFalse();
    }

    [Fact]
    public async Task AuthDisabled_ShouldAllowDirectCommunication()
    {
        // Arrange - no auth (backward compatibility)
        var pid = Random.Shared.Next(100_000, 999_999);
        using var host = new InspectorHost(pid);
        host.Start();

        using var client = new NamedPipeClientStream(
            ".", $"WpfDevTools_{pid}", PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(5_000);

        // Act - send ping directly without auth handshake
        var request = new InspectorRequest
        {
            Id = "no-auth-1",
            Method = "ping",
            Params = null
        };
        await MessageFraming.WriteMessageAsync(client, JsonSerializer.Serialize(request));

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var responseJson = await MessageFraming.ReadMessageAsync(client, cts.Token);
        var response = JsonSerializer.Deserialize<InspectorResponse>(responseJson);

        // Assert
        response.Should().NotBeNull();
        response!.Id.Should().Be("no-auth-1");
        response.Error.Should().BeNull();
    }

    [Fact]
    public async Task AuthEnabled_ClientSendsGarbageResponse_ShouldDisconnect()
    {
        // Arrange
        var pid = Random.Shared.Next(100_000, 999_999);
        var secret = new byte[32];
        RandomNumberGenerator.Fill(secret);
        var authManager = new AuthenticationManager(
            envSecretProvider: () => Convert.ToBase64String(secret));

        using var host = new InspectorHost(pid, authManager);
        host.Start();

        using var client = new NamedPipeClientStream(
            ".", $"WpfDevTools_{pid}", PipeDirection.InOut, PipeOptions.Asynchronous);
        await client.ConnectAsync(5_000);

        // Act - read challenge but send garbage
        var challenge = new byte[32];
        var bytesRead = await client.ReadAsync(challenge, 0, 32);
        bytesRead.Should().Be(32);

        var garbage = new byte[32]; // all zeros
        await client.WriteAsync(garbage, 0, garbage.Length);
        await client.FlushAsync();

        // Assert - read auth result (should be 0 = failure)
        var resultBuf = new byte[1];
        var resultRead = await client.ReadAsync(resultBuf, 0, 1);

        // Either we get a failure byte (0) or the pipe is disconnected (0 bytes read)
        if (resultRead == 1)
        {
            resultBuf[0].Should().Be(0, "server should send failure indicator");
        }
        else
        {
            resultRead.Should().Be(0, "pipe should be disconnected on auth failure");
        }
    }

    /// <summary>
    /// Performs client-side challenge-response authentication.
    /// Protocol:
    /// 1. Server sends 32-byte challenge
    /// 2. Client computes HMAC-SHA256(secret, challenge) and sends 32-byte response
    /// 3. Server sends 1-byte result (1=success, 0=failure)
    /// </summary>
    private static async Task<bool> PerformClientAuthentication(
        NamedPipeClientStream pipe, byte[] sharedSecret)
    {
        try
        {
            // 1. Read 32-byte challenge from server
            var challenge = new byte[32];
            var totalRead = 0;
            while (totalRead < 32)
            {
                var read = await pipe.ReadAsync(challenge, totalRead, 32 - totalRead);
                if (read == 0) return false;
                totalRead += read;
            }

            // 2. Compute HMAC-SHA256 response
            var calculator = new ResponseCalculator(sharedSecret);
            var response = calculator.ComputeResponse(challenge);

            // 3. Send response
            await pipe.WriteAsync(response, 0, response.Length);
            await pipe.FlushAsync();

            // 4. Read 1-byte result from server
            var resultBuf = new byte[1];
            var resultRead = await pipe.ReadAsync(resultBuf, 0, 1);
            if (resultRead == 0) return false;

            return resultBuf[0] == 1;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
