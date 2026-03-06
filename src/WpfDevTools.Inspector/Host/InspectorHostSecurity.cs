using System.IO;
using System.IO.Pipes;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Principal;
using System.Security.AccessControl;
using WpfDevTools.Shared.Security;

namespace WpfDevTools.Inspector.Host;

/// <summary>
/// Security methods for InspectorHost: pipe ACLs, authentication, and TLS
/// </summary>
public sealed partial class InspectorHost
{
    private NamedPipeServerStream CreateSecurePipeServer()
    {
        var pipeSecurity = new PipeSecurity();

        // Allow current user
        var currentUser = WindowsIdentity.GetCurrent().User;
        if (currentUser != null)
        {
            pipeSecurity.AddAccessRule(new PipeAccessRule(
                currentUser,
                PipeAccessRights.FullControl,
                AccessControlType.Allow));
        }

        // Allow SYSTEM account
        var systemSid = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            systemSid,
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

#if NET48
        return new NamedPipeServerStream(
            _pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            0,
            0,
            pipeSecurity);
#else
        return NamedPipeServerStreamAcl.Create(
            _pipeName,
            PipeDirection.InOut,
            1,
            PipeTransmissionMode.Byte,
            PipeOptions.Asynchronous,
            0,
            0,
            pipeSecurity);
#endif
    }

    private async Task<bool> AuthenticateClientAsync(
        NamedPipeServerStream pipe,
        CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
            var token = timeoutCts.Token;

            // 1. Generate and send 32-byte challenge
            var challenge = _challengeGenerator.GenerateChallenge();
            await pipe.WriteAsync(challenge, 0, challenge.Length, token).ConfigureAwait(false);
            await pipe.FlushAsync(token).ConfigureAwait(false);

            // 2. Read 32-byte response from client
            var response = new byte[32];
            var totalRead = 0;
            while (totalRead < 32)
            {
                var read = await pipe.ReadAsync(response, totalRead, 32 - totalRead, token).ConfigureAwait(false);
                if (read == 0)
                {
                    await SendAuthResult(pipe, false, token).ConfigureAwait(false);
                    return false;
                }
                totalRead += read;
            }

            // 3. Verify response using HMAC-SHA256
            using var calculator = new ResponseCalculator(_authManager!.GetSharedSecret());
            var isValid = calculator.VerifyResponse(challenge, response);

            // 4. Send 1-byte result to client (1=success, 0=failure)
            await SendAuthResult(pipe, isValid, token).ConfigureAwait(false);

            return isValid;
        }
        catch (OperationCanceledException)
        {
            LogError("Authentication timed out");
            return false;
        }
        catch (IOException ex)
        {
            LogError($"Authentication I/O error: {ex.Message}");
            return false;
        }
    }

    private static async Task SendAuthResult(
        NamedPipeServerStream pipe, bool success, CancellationToken cancellationToken)
    {
        try
        {
            var resultByte = new byte[] { (byte)(success ? 1 : 0) };
            await pipe.WriteAsync(resultByte, 0, 1, cancellationToken).ConfigureAwait(false);
            await pipe.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Best effort - client may have already disconnected
            System.Diagnostics.Debug.WriteLine($"InspectorHost: Failed to send auth result: {ex.Message}");
        }
    }

    private async Task<SslStream?> CreateServerSslStreamAsync(
        NamedPipeServerStream pipe, CancellationToken cancellationToken)
    {
        try
        {
            var certificate = _certManager!.GetOrCreateCertificate();
            var sslStream = new SslStream(pipe, leaveInnerStreamOpen: true);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(10));

#if NET48
            var authTask = sslStream.AuthenticateAsServerAsync(certificate);
            var timeoutTask = Task.Delay(TimeSpan.FromSeconds(10), timeoutCts.Token);
            var completed = await Task.WhenAny(authTask, timeoutTask).ConfigureAwait(false);
            if (completed == timeoutTask)
            {
                sslStream.Dispose();
                throw new OperationCanceledException("TLS handshake timed out");
            }
            await authTask.ConfigureAwait(false); // Propagate any exception
#else
            var sslOptions = new SslServerAuthenticationOptions
            {
                ServerCertificate = certificate,
                ClientCertificateRequired = false,
                EnabledSslProtocols = SslProtocols.Tls12,
                CertificateRevocationCheckMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck
            };
            await sslStream.AuthenticateAsServerAsync(sslOptions, timeoutCts.Token).ConfigureAwait(false);
#endif
            return sslStream;
        }
        catch (OperationCanceledException)
        {
            LogError("TLS handshake timed out");
            return null;
        }
        catch (AuthenticationException ex)
        {
            LogError($"TLS handshake failed: {ex.Message}");
            return null;
        }
        catch (IOException ex)
        {
            LogError($"TLS I/O error: {ex.Message}");
            return null;
        }
    }
}
