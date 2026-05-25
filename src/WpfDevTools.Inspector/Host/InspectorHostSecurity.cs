using System.IO;
using System.IO.Pipes;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Security.AccessControl;
using WpfDevTools.Shared.Configuration;
using WpfDevTools.Shared.Security;

namespace WpfDevTools.Inspector.Host;

/// <summary>
/// Security methods for InspectorHost: pipe ACLs, authentication, and TLS
/// </summary>
public sealed partial class InspectorHost
{
    private static readonly SecurityIdentifier LocalSystemSid = new(WellKnownSidType.LocalSystemSid, null);
    private static readonly SecurityIdentifier EveryoneSid = new(WellKnownSidType.WorldSid, null);
    private static readonly SecurityIdentifier AuthenticatedUsersSid = new(WellKnownSidType.AuthenticatedUserSid, null);
    private static readonly SecurityIdentifier BuiltinUsersSid = new(WellKnownSidType.BuiltinUsersSid, null);

    internal static Action<System.Security.Cryptography.X509Certificates.X509Certificate2>? ServerCertificateLoadedCallback { get; set; }

    private NamedPipeServerStream CreateSecurePipeServer()
    {
        if (_pipeServerFactory != null)
        {
            return _pipeServerFactory();
        }

        var pipeSecurity = CreatePipeSecurityForCurrentUser();

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

    internal static PipeSecurity CreatePipeSecurityForCurrentUser()
    {
        var pipeSecurity = new PipeSecurity();
        pipeSecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);

        var currentUser = WindowsIdentity.GetCurrent().User
            ?? throw new InvalidOperationException("Unable to resolve current Windows user SID for named pipe ACL.");
        pipeSecurity.AddAccessRule(new PipeAccessRule(
            currentUser,
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        pipeSecurity.AddAccessRule(new PipeAccessRule(
            LocalSystemSid,
            PipeAccessRights.FullControl,
            AccessControlType.Allow));

        ValidatePipeSecurityDoesNotGrantBroadPrincipals(pipeSecurity);
        return pipeSecurity;
    }

    internal static void ValidatePipeSecurityDoesNotGrantBroadPrincipals(PipeSecurity pipeSecurity)
    {
        ArgumentNullException.ThrowIfNull(pipeSecurity);

        foreach (PipeAccessRule rule in pipeSecurity.GetAccessRules(
            includeExplicit: true,
            includeInherited: true,
            typeof(SecurityIdentifier)))
        {
            if (rule.AccessControlType != AccessControlType.Allow ||
                rule.IdentityReference is not SecurityIdentifier sid)
            {
                continue;
            }

            if (sid.Equals(EveryoneSid) ||
                sid.Equals(AuthenticatedUsersSid) ||
                sid.Equals(BuiltinUsersSid))
            {
                throw new InvalidOperationException(
                    $"Named pipe ACL must not grant access to broad principal '{sid.Value}'.");
            }
        }
    }

    private async Task<bool> AuthenticateClientAsync(
        NamedPipeServerStream pipe,
        CancellationToken cancellationToken)
    {
        byte[]? challenge = null;
        byte[]? response = null;
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(InspectorConfig.AuthenticationTimeout);
            var token = timeoutCts.Token;

            // 1. Generate and send 32-byte challenge
            challenge = _challengeGenerator.GenerateChallenge();
            await pipe.WriteAsync(challenge, 0, challenge.Length, token).ConfigureAwait(false);
            await pipe.FlushAsync(token).ConfigureAwait(false);

            // 2. Read 32-byte response from client
            response = new byte[32];
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
            // GetSharedSecret returns a clone; zero it after use to minimize secret exposure in memory
            var secretCopy = _authManager!.GetSharedSecret();
            bool isValid;
            try
            {
                using var calculator = new ResponseCalculator(secretCopy);
                isValid = calculator.VerifyResponse(challenge, response);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(secretCopy);
            }

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
        finally
        {
            if (challenge != null)
            {
                CryptographicOperations.ZeroMemory(challenge);
            }

            if (response != null)
            {
                CryptographicOperations.ZeroMemory(response);
            }
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
        SslStream? sslStream = null;
        var certificate = _certManager!.GetOrCreateCertificate();
        try
        {
            ServerCertificateLoadedCallback?.Invoke(certificate);
            sslStream = new SslStream(pipe, leaveInnerStreamOpen: true);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(InspectorConfig.TlsHandshakeTimeout);

#if NET48
            var authTask = sslStream.AuthenticateAsServerAsync(
                certificate,
                clientCertificateRequired: false,
                enabledSslProtocols: SecureTransportProtocols.InspectorTransport,
                checkCertificateRevocation: false);
            var timeoutTask = Task.Delay(InspectorConfig.TlsHandshakeTimeout, timeoutCts.Token);
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
                EnabledSslProtocols = SecureTransportProtocols.InspectorTransport,
                CertificateRevocationCheckMode = System.Security.Cryptography.X509Certificates.X509RevocationMode.NoCheck
            };
            await sslStream.AuthenticateAsServerAsync(sslOptions, timeoutCts.Token).ConfigureAwait(false);
#endif
            return sslStream;
        }
        catch (OperationCanceledException)
        {
            sslStream?.Dispose();
            LogError("TLS handshake timed out");
            return null;
        }
        catch (AuthenticationException ex)
        {
            sslStream?.Dispose();
            LogError($"TLS handshake failed: {ex.Message}");
            return null;
        }
        catch (IOException ex)
        {
            sslStream?.Dispose();
            LogError($"TLS I/O error: {ex.Message}");
            return null;
        }
        catch (ObjectDisposedException ex)
        {
            sslStream?.Dispose();
            LogError($"TLS stream disposed during handshake: {ex.Message}");
            return null;
        }
        finally
        {
            certificate.Dispose();
        }
    }
}


