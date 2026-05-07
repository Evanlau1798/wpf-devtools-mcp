using System.Security.Cryptography;
using System.Runtime.Versioning;
using System.Text;

namespace WpfDevTools.Shared.Security;

#if !NET48
[SupportedOSPlatform("windows")]
#endif
internal static class LocalSecretProtector
{
    private static readonly byte[] CurrentUserHeader = Encoding.ASCII.GetBytes("WPFDEVTOOLS-DPAPI:CurrentUser\n");
    private static readonly byte[] LocalMachineHeader = Encoding.ASCII.GetBytes("WPFDEVTOOLS-DPAPI:LocalMachine\n");

    public static byte[] Protect(byte[] plaintext)
    {
        try
        {
            return ProtectWithHeader(plaintext, DataProtectionScope.CurrentUser, CurrentUserHeader);
        }
        catch (CryptographicException)
        {
            return ProtectWithHeader(plaintext, DataProtectionScope.LocalMachine, LocalMachineHeader);
        }
    }

    public static byte[] Unprotect(byte[] protectedPayload)
    {
        if (TryGetPayload(protectedPayload, CurrentUserHeader, out var currentUserPayload))
        {
            return ProtectedData.Unprotect(currentUserPayload, null, DataProtectionScope.CurrentUser);
        }

        if (TryGetPayload(protectedPayload, LocalMachineHeader, out var localMachinePayload))
        {
            return ProtectedData.Unprotect(localMachinePayload, null, DataProtectionScope.LocalMachine);
        }

        return ProtectedData.Unprotect(protectedPayload, null, DataProtectionScope.CurrentUser);
    }

    private static byte[] ProtectWithHeader(byte[] plaintext, DataProtectionScope scope, byte[] header)
    {
        var protectedBytes = ProtectedData.Protect(plaintext, null, scope);
        var result = new byte[header.Length + protectedBytes.Length];
        Buffer.BlockCopy(header, 0, result, 0, header.Length);
        Buffer.BlockCopy(protectedBytes, 0, result, header.Length, protectedBytes.Length);
        return result;
    }

    private static bool TryGetPayload(byte[] protectedPayload, byte[] header, out byte[] payload)
    {
        payload = Array.Empty<byte>();
        if (protectedPayload.Length <= header.Length)
        {
            return false;
        }

        for (var index = 0; index < header.Length; index++)
        {
            if (protectedPayload[index] != header[index])
            {
                return false;
            }
        }

        payload = new byte[protectedPayload.Length - header.Length];
        Buffer.BlockCopy(protectedPayload, header.Length, payload, 0, payload.Length);
        return true;
    }
}