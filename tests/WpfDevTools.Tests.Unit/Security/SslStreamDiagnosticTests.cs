using System.IO.Pipes;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace WpfDevTools.Tests.Unit.Security;

public class SslStreamDiagnosticTests
{
    private readonly ITestOutputHelper _output;

    public SslStreamDiagnosticTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task SslStream_WithInspectorHost_ShouldWork()
    {
        // Test InspectorHost with SslStream using raw client
        var pid = global::WpfDevTools.Tests.Unit.TestHelpers.NextSyntheticProcessId();
        var tempDir = Path.Combine(Path.GetTempPath(), "SslTest_" + Guid.NewGuid());
        Directory.CreateDirectory(tempDir);

        try
        {
            var certManager = new WpfDevTools.Shared.Security.CertificateManager(tempDir);
            using var host = new WpfDevTools.Inspector.Host.InspectorHost(pid, null, certManager);
            host.Start();
            _output.WriteLine("Host started");

            using var client = new NamedPipeClientStream(
                ".", $"WpfDevTools_{pid}", PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(5000);
            _output.WriteLine("Pipe connected");

            // Manual TLS handshake
            using var ssl = new SslStream(client, true, (s, c, ch, e) => true);
            await ssl.AuthenticateAsClientAsync("WpfDevTools-Inspector",
                null, SslProtocols.Tls12, false);
            _output.WriteLine($"TLS done: {ssl.SslProtocol}");

            // Send a ping request
            var request = new WpfDevTools.Shared.Messages.InspectorRequest
            {
                Id = "ssl-ping", Method = "ping", Params = null
            };
            var json = System.Text.Json.JsonSerializer.Serialize(request);
            _output.WriteLine("Writing request...");
            await WpfDevTools.Shared.Serialization.MessageFraming.WriteMessageAsync(ssl, json);
            _output.WriteLine("Request sent, reading response...");

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var responseJson = await WpfDevTools.Shared.Serialization.MessageFraming.ReadMessageAsync(ssl, cts.Token);
            _output.WriteLine($"Response: {responseJson}");

            var response = System.Text.Json.JsonSerializer.Deserialize<WpfDevTools.Shared.Messages.InspectorResponse>(responseJson);
            response.Should().NotBeNull();
            response!.Error.Should().BeNull();
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public async Task SslStream_OverNamedPipe_ShouldComplete()
    {
        // Create self-signed cert - must export/re-import PFX for SslStream compatibility
        using var rsa = RSA.Create(2048);
        var req = new CertificateRequest("CN=WpfDevTools-Inspector",
            rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var tempCert = req.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddYears(1));
        var pfxBytes = tempCert.Export(X509ContentType.Pfx, "test");
        tempCert.Dispose();
        using var cert = new X509Certificate2(
            pfxBytes,
            "test",
            X509KeyStorageFlags.Exportable | X509KeyStorageFlags.MachineKeySet);

        var pipeName = "ssl_diag_" + Guid.NewGuid().ToString("N")[..8];
        _output.WriteLine($"Pipe: {pipeName}");

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

        var serverTask = Task.Run(async () =>
        {
            using var server = new NamedPipeServerStream(
                pipeName, PipeDirection.InOut, 1,
                PipeTransmissionMode.Byte, PipeOptions.Asynchronous);

            _output.WriteLine("Server: waiting for connection");
            await server.WaitForConnectionAsync(cts.Token);
            _output.WriteLine("Server: connected, starting TLS");

            using var ssl = new SslStream(server, leaveInnerStreamOpen: true,
                (s, c, ch, e) => true);

            await ssl.AuthenticateAsServerAsync(cert, false, SslProtocols.Tls12, false);
            _output.WriteLine($"Server: TLS done, protocol={ssl.SslProtocol}");

            // Read
            var buf = new byte[5];
            var read = await ssl.ReadAsync(buf, 0, 5, cts.Token);
            _output.WriteLine($"Server: received '{Encoding.UTF8.GetString(buf, 0, read)}'");

            // Write
            var resp = Encoding.UTF8.GetBytes("WORLD");
            await ssl.WriteAsync(resp, 0, resp.Length, cts.Token);
            await ssl.FlushAsync(cts.Token);
            _output.WriteLine("Server: sent WORLD");
        }, cts.Token);

        var clientTask = Task.Run(async () =>
        {
            using var client = new NamedPipeClientStream(
                ".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

            await client.ConnectAsync(cts.Token);
            _output.WriteLine("Client: pipe connected, starting TLS");

            using var ssl = new SslStream(client, leaveInnerStreamOpen: true,
                (s, c, ch, e) => true);

            await ssl.AuthenticateAsClientAsync("WpfDevTools-Inspector",
                null, SslProtocols.Tls12, false);
            _output.WriteLine($"Client: TLS done, protocol={ssl.SslProtocol}");

            // Write
            var msg = Encoding.UTF8.GetBytes("HELLO");
            await ssl.WriteAsync(msg, 0, msg.Length, cts.Token);
            await ssl.FlushAsync(cts.Token);
            _output.WriteLine("Client: sent HELLO");

            // Read
            var buf = new byte[5];
            var read = await ssl.ReadAsync(buf, 0, 5, cts.Token);
            _output.WriteLine($"Client: received '{Encoding.UTF8.GetString(buf, 0, read)}'");
        }, cts.Token);

        await Task.WhenAll(serverTask, clientTask);
        _output.WriteLine("SUCCESS");
    }
}
