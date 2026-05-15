using System.Globalization;
using System.IO.Pipes;
using System.Net.Security;
#if !NET48
using System.Runtime.Versioning;
#endif
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using WpfDevTools.Shared.Security;

#if !NET48
[assembly: SupportedOSPlatform("windows")]
#endif

namespace WpfDevTools.Tests.TlsNegotiationHarness;

internal static class Program
{
    private const string TargetHost = "WpfDevTools-Inspector";
    private static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(15);

    private static int Main(string[] args)
    {
        try
        {
            RunAsync(args).GetAwaiter().GetResult();
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            return 1;
        }
    }

    private static async Task RunAsync(string[] args)
    {
        if (args.Length == 0)
            throw new ArgumentException("Expected role argument: server or client.");

        var options = HarnessOptions.Parse(args.Skip(1).ToArray());
        switch (args[0].ToLowerInvariant())
        {
            case "server":
                await RunServerAsync(options).ConfigureAwait(false);
                break;
            case "client":
                await RunClientAsync(options).ConfigureAwait(false);
                break;
            default:
                throw new ArgumentException($"Unsupported role: {args[0]}");
        }
    }

    private static async Task RunServerAsync(HarnessOptions options)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(options.ReadyFile)!);
        Directory.CreateDirectory(Path.GetDirectoryName(options.ResultFile)!);

        using var server = new NamedPipeServerStream(
            options.PipeName,
            PipeDirection.InOut,
            maxNumberOfServerInstances: 1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous);

        File.WriteAllText(options.ReadyFile, RuntimeFramework);
        server.WaitForConnection();

        using var certificate = new CertificateManager(options.CertificateDirectory).GetOrCreateCertificate();
        using var ssl = new SslStream(server, leaveInnerStreamOpen: true);

        await ssl.AuthenticateAsServerAsync(
            certificate,
            clientCertificateRequired: false,
            enabledSslProtocols: SecureTransportProtocols.InspectorTransport,
            checkCertificateRevocation: false).ConfigureAwait(false);

        var request = await ReadExactStringAsync(ssl, 4).ConfigureAwait(false);
        if (!string.Equals(request, "PING", StringComparison.Ordinal))
            throw new InvalidOperationException($"Unexpected client payload: {request}");

        await WriteStringAsync(ssl, "PONG").ConfigureAwait(false);
        WriteResult(options.ResultFile, "server", ssl.SslProtocol);
    }

    private static async Task RunClientAsync(HarnessOptions options)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(options.ResultFile)!);
        using var expectedCertificate = new CertificateManager(options.CertificateDirectory).GetOrCreateCertificate();

        using var client = new NamedPipeClientStream(
            ".",
            options.PipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);
        client.Connect((int)options.ConnectTimeout.TotalMilliseconds);

        using var ssl = new SslStream(
            client,
            leaveInnerStreamOpen: true,
            (_, certificate, _, _) => IsExpectedServerCertificate(certificate, expectedCertificate));

        await ssl.AuthenticateAsClientAsync(
            TargetHost,
            clientCertificates: null,
            enabledSslProtocols: SecureTransportProtocols.InspectorTransport,
            checkCertificateRevocation: false).ConfigureAwait(false);

        await WriteStringAsync(ssl, "PING").ConfigureAwait(false);
        var response = await ReadExactStringAsync(ssl, 4).ConfigureAwait(false);
        if (!string.Equals(response, "PONG", StringComparison.Ordinal))
            throw new InvalidOperationException($"Unexpected server payload: {response}");

        WriteResult(options.ResultFile, "client", ssl.SslProtocol);
    }

    private static bool IsExpectedServerCertificate(
        X509Certificate? certificate,
        X509Certificate2 expectedCertificate)
    {
        if (certificate is null)
            return false;

        using var certificateCopy = new X509Certificate2(certificate);
        return string.Equals(certificateCopy.Subject, expectedCertificate.Subject, StringComparison.Ordinal)
            && string.Equals(certificateCopy.Thumbprint, expectedCertificate.Thumbprint, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task WriteStringAsync(Stream stream, string value)
    {
        var payload = Encoding.UTF8.GetBytes(value);
        await stream.WriteAsync(payload, 0, payload.Length).ConfigureAwait(false);
        await stream.FlushAsync().ConfigureAwait(false);
    }

    private static async Task<string> ReadExactStringAsync(Stream stream, int length)
    {
        var buffer = new byte[length];
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer, offset, buffer.Length - offset).ConfigureAwait(false);
            if (read == 0)
                throw new EndOfStreamException("Secure stream closed before the expected payload was read.");

            offset += read;
        }

        return Encoding.UTF8.GetString(buffer);
    }

    private static void WriteResult(string path, string role, SslProtocols protocol)
    {
        File.WriteAllLines(path, new[]
        {
            $"role={role}",
            $"framework={RuntimeFramework}",
            $"protocol={protocol}",
            $"policy={SecureTransportProtocols.InspectorTransport}"
        });
    }

#if NET48
    private static string RuntimeFramework => "net48";
#else
    private static string RuntimeFramework => "net8.0";
#endif

    private sealed class HarnessOptions
    {
        private HarnessOptions(
            string pipeName,
            string certificateDirectory,
            string readyFile,
            string resultFile,
            TimeSpan connectTimeout)
        {
            PipeName = pipeName;
            CertificateDirectory = certificateDirectory;
            ReadyFile = readyFile;
            ResultFile = resultFile;
            ConnectTimeout = connectTimeout;
        }

        public string PipeName { get; }

        public string CertificateDirectory { get; }

        public string ReadyFile { get; }

        public string ResultFile { get; }

        public TimeSpan ConnectTimeout { get; }

        public static HarnessOptions Parse(string[] args)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (var index = 0; index < args.Length; index += 2)
            {
                if (!args[index].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException($"Expected option name but found '{args[index]}'.");

                if (index + 1 >= args.Length)
                    throw new ArgumentException($"Missing value for option '{args[index]}'.");

                values[args[index].Substring(2)] = args[index + 1];
            }

            return new HarnessOptions(
                Require(values, "pipe"),
                Require(values, "cert-dir"),
                Require(values, "ready-file"),
                Require(values, "result-file"),
                ParseConnectTimeout(values));
        }

        private static TimeSpan ParseConnectTimeout(IReadOnlyDictionary<string, string> values)
        {
            if (!values.TryGetValue("connect-timeout-seconds", out var value))
                return DefaultConnectTimeout;

            if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) || seconds <= 0)
                throw new ArgumentException("--connect-timeout-seconds must be a positive integer.");

            return TimeSpan.FromSeconds(seconds);
        }

        private static string Require(IReadOnlyDictionary<string, string> values, string name)
        {
            if (!values.TryGetValue(name, out var value) || string.IsNullOrWhiteSpace(value))
                throw new ArgumentException($"Missing required option --{name}.");

            return value;
        }
    }
}
