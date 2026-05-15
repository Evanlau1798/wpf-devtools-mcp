using System.Diagnostics;
using System.IO.Pipes;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using System.Runtime.InteropServices;
using WpfDevTools.Shared.Configuration;
using WpfDevTools.Shared.Security;

namespace WpfDevTools.Mcp.Server;

public sealed partial class NamedPipeClient
{
    private async Task<bool> HandleConnectRetryAsync(
        int attempt,
        int maxRetries,
        TimeSpan totalTimeout,
        Stopwatch timeoutBudget,
        CancellationToken cancellationToken)
    {
        ResetConnectionState();

        if (attempt == maxRetries)
            return false;

        var remainingTimeout = totalTimeout - timeoutBudget.Elapsed;
        if (remainingTimeout <= TimeSpan.Zero)
            return false;

        var retryDelay = remainingTimeout < TimeSpan.FromMilliseconds(500)
            ? remainingTimeout
            : TimeSpan.FromMilliseconds(500);
        if (retryDelay <= TimeSpan.Zero)
            return false;

        await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false);
        return true;
    }

    private void ResetConnectionState()
    {
        NamedPipeClientStream? pipeToDispose = null;
        Stream? streamToDispose = null;

        lock (_lock)
        {
            streamToDispose = _communicationStream;
            pipeToDispose = _pipeClient;
            _communicationStream = null;
            _pipeClient = null;
        }

        if (streamToDispose != null && !ReferenceEquals(streamToDispose, pipeToDispose))
        {
            try { streamToDispose.Dispose(); } catch (IOException) { }
        }

        try { pipeToDispose?.Dispose(); } catch (IOException) { }
    }

    private async Task<SslStream?> CreateClientSslStreamAsync(
        NamedPipeClientStream pipe, CancellationToken cancellationToken)
    {
        SslStream? sslStream = null;
        try
        {
            var expectedThumbprint = GetExpectedServerThumbprint();
            sslStream = new SslStream(pipe, leaveInnerStreamOpen: true,
                (sender, cert, chain, errors) =>
                {
                    return IsExpectedServerCertificate(cert, expectedThumbprint);
                });

#if NET48
            await WaitForConnectPhaseAsync(
                sslStream.AuthenticateAsClientAsync(
                    "WpfDevTools-Inspector",
                    null,
                    SecureTransportProtocols.InspectorTransport,
                    checkCertificateRevocation: false),
                cancellationToken).ConfigureAwait(false);
#else
            var sslOptions = new SslClientAuthenticationOptions
            {
                TargetHost = "WpfDevTools-Inspector",
                EnabledSslProtocols = SecureTransportProtocols.InspectorTransport,
                CertificateRevocationCheckMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck
            };
            await WaitForConnectPhaseAsync(
                sslStream.AuthenticateAsClientAsync(sslOptions, cancellationToken),
                cancellationToken).ConfigureAwait(false);
#endif
            return sslStream;
        }
        catch (OperationCanceledException)
        {
            sslStream?.Dispose();
            SetLastConnectFailure(NamedPipeConnectFailure.Timeout);
            return null;
        }
        catch (AuthenticationException)
        {
            sslStream?.Dispose();
            SetLastConnectFailure(NamedPipeConnectFailure.SecureTransportFailed);
            return null;
        }
        catch (IOException)
        {
            sslStream?.Dispose();
            SetLastConnectFailure(NamedPipeConnectFailure.SecureTransportFailed);
            return null;
        }
        catch (ObjectDisposedException)
        {
            sslStream?.Dispose();
            SetLastConnectFailure(NamedPipeConnectFailure.SecureTransportFailed);
            return null;
        }
    }

    internal static bool IsExpectedServerCertificate(X509Certificate? certificate, string? expectedThumbprint)
    {
        if (certificate == null || string.IsNullOrWhiteSpace(expectedThumbprint))
            return false;

        using var certificateCopy = new X509Certificate2(certificate);
        return string.Equals(certificateCopy.Subject, InspectorCertificateSubject, StringComparison.Ordinal)
            && string.Equals(certificateCopy.Thumbprint, expectedThumbprint, StringComparison.OrdinalIgnoreCase);
    }

    private string? GetExpectedServerThumbprint()
    {
        var configuredThumbprint = Environment.GetEnvironmentVariable("WPFDEVTOOLS_CERT_THUMBPRINT");
        if (!string.IsNullOrWhiteSpace(configuredThumbprint))
        {
            return configuredThumbprint;
        }

        using var cert = _certManager?.GetOrCreateCertificate();
        return cert?.Thumbprint;
    }

    private async Task<bool> AuthenticateToInspectorAsync(
        NamedPipeClientStream pipe, CancellationToken cancellationToken)
    {
        try
        {
            // 1. Read 32-byte challenge from server
            var challenge = new byte[32];
            var totalRead = 0;
            while (totalRead < 32)
            {
                var read = await WaitForConnectPhaseAsync(
                    pipe.ReadAsync(challenge, totalRead, 32 - totalRead, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
                if (read == 0)
                {
                    SetLastConnectFailure(NamedPipeConnectFailure.AuthenticationFailed);
                    return false;
                }
                totalRead += read;
            }

            // 2. Compute HMAC-SHA256 response
            // GetSharedSecret returns a clone; zero it after use to minimize secret exposure in memory
            var secretCopy = _authManager!.GetSharedSecret();
            byte[] response;
            try
            {
                using var calculator = new ResponseCalculator(secretCopy);
                response = calculator.ComputeResponse(challenge);
            }
            finally
            {
                Array.Clear(secretCopy, 0, secretCopy.Length);
            }

            // 3. Send response
            await WaitForConnectPhaseAsync(
                pipe.WriteAsync(response, 0, response.Length, cancellationToken),
                cancellationToken).ConfigureAwait(false);
            await WaitForConnectPhaseAsync(
                pipe.FlushAsync(cancellationToken),
                cancellationToken).ConfigureAwait(false);

            // 4. Read 1-byte result from server (1=success, 0=failure)
            var resultBuf = new byte[1];
            var resultRead = await WaitForConnectPhaseAsync(
                pipe.ReadAsync(resultBuf, 0, 1, cancellationToken),
                cancellationToken).ConfigureAwait(false);
            if (resultRead == 0)
            {
                SetLastConnectFailure(NamedPipeConnectFailure.AuthenticationFailed);
                return false;
            }

            var authenticated = resultBuf[0] == 1;
            if (!authenticated)
            {
                SetLastConnectFailure(NamedPipeConnectFailure.AuthenticationFailed);
            }

            return authenticated;
        }
        catch (OperationCanceledException)
        {
            SetLastConnectFailure(NamedPipeConnectFailure.Timeout);
            return false;
        }
        catch (IOException)
        {
            SetLastConnectFailure(NamedPipeConnectFailure.AuthenticationFailed);
            return false;
        }
    }

    private void SetLastConnectFailure(NamedPipeConnectFailure failure)
    {
        Volatile.Write(ref _lastConnectFailure, (int)failure);
    }

    private async Task<NamedPipeConnectFailure> ValidateConnectedHostAsync(
        NamedPipeClientStream pipe,
        CancellationToken cancellationToken)
    {
        var serverProcessId = TryGetConnectedServerProcessId(pipe);
        if (serverProcessId.HasValue && serverProcessId.Value != _processId && !IsSameProcessDefaultPipeHost(serverProcessId.Value))
        {
            return NamedPipeConnectFailure.ServerProcessMismatch;
        }

        try
        {
            var response = await SendRequestCoreAsync(
                "ping",
                $"connect-verify-{Guid.NewGuid():N}",
                new { },
                cancellationToken).ConfigureAwait(false);

            if (response.Error != null || response.Result is null)
            {
                return NamedPipeConnectFailure.IncompatibleHost;
            }

            var result = response.Result.Value;
            if (!TryGetInt32Property(result, "processId", out var hostProcessId) || hostProcessId != _processId)
            {
                return NamedPipeConnectFailure.ServerProcessMismatch;
            }

            if (!TryGetInt32Property(result, "protocolVersion", out var protocolVersion) ||
                protocolVersion != InspectorCompatibilityContract.ProtocolVersion)
            {
                return NamedPipeConnectFailure.IncompatibleHost;
            }

            if (!TryGetStringProperty(result, "buildFingerprint", out var buildFingerprint) ||
                !string.Equals(buildFingerprint, CurrentBuildFingerprint, StringComparison.Ordinal))
            {
                return NamedPipeConnectFailure.IncompatibleHost;
            }

            return NamedPipeConnectFailure.None;
        }
        catch (OperationCanceledException)
        {
            return NamedPipeConnectFailure.Timeout;
        }
        catch (IOException)
        {
            return NamedPipeConnectFailure.IncompatibleHost;
        }
        catch (ObjectDisposedException)
        {
            return NamedPipeConnectFailure.Timeout;
        }
        catch (InvalidOperationException)
        {
            return NamedPipeConnectFailure.IncompatibleHost;
        }
        catch (JsonException)
        {
            return NamedPipeConnectFailure.IncompatibleHost;
        }
    }

    private static bool TryGetInt32Property(JsonElement element, string propertyName, out int value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.Number &&
            property.TryGetInt32(out value);
    }

    private static bool TryGetStringProperty(JsonElement element, string propertyName, out string? value)
    {
        value = null;
        return element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(value = property.GetString());
    }

    private bool IsSameProcessDefaultPipeHost(int serverProcessId)
    {
        return serverProcessId == Environment.ProcessId &&
            string.Equals(_pipeName, BuildPipeName(_processId), StringComparison.Ordinal);
    }

    private static int? TryGetConnectedServerProcessId(NamedPipeClientStream pipe)
    {
        try
        {
            return GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var serverProcessId)
                ? checked((int)serverProcessId)
                : null;
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
        catch (PlatformNotSupportedException)
        {
            return null;
        }
    }
}
