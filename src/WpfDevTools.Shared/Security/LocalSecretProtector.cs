using System.Security.Cryptography;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;

namespace WpfDevTools.Shared.Security;

#if !NET48
[SupportedOSPlatform("windows")]
#endif
internal static class LocalSecretProtector
{
    private const string AllowLocalMachineFallbackEnvVar = "WPFDEVTOOLS_ALLOW_LOCALMACHINE_DPAPI_FALLBACK";
    private static readonly byte[] CurrentUserHeader = Encoding.ASCII.GetBytes("WPFDEVTOOLS-DPAPI:CurrentUser\n");
    private static readonly byte[] LocalMachineHeader = Encoding.ASCII.GetBytes("WPFDEVTOOLS-DPAPI:LocalMachine\n");
    private static readonly Func<byte[], DataProtectionScope, byte[]> DefaultProtectCore =
        static (plaintext, scope) => ProtectedData.Protect(plaintext, null, scope);
    private static readonly AsyncLocal<TestScopeOverrides?> TestOverrides = new();

    public static byte[] Protect(byte[] plaintext)
    {
        try
        {
            return ProtectWithHeader(plaintext, DataProtectionScope.CurrentUser, CurrentUserHeader);
        }
        catch (CryptographicException)
        {
            if (!IsLocalMachineFallbackAllowed())
            {
                throw;
            }

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
            if (!IsLocalMachineFallbackAllowed())
            {
                throw new CryptographicException(
                    $"LocalMachine DPAPI fallback is disabled. Set {AllowLocalMachineFallbackEnvVar}=1 only for explicit legacy secret migration.");
            }

            return ProtectedData.Unprotect(localMachinePayload, null, DataProtectionScope.LocalMachine);
        }

        return ProtectedData.Unprotect(protectedPayload, null, DataProtectionScope.CurrentUser);
    }

    private static byte[] ProtectWithHeader(byte[] plaintext, DataProtectionScope scope, byte[] header)
    {
        var protectedBytes = (TestOverrides.Value?.ProtectCore ?? DefaultProtectCore)(plaintext, scope);
        var result = new byte[header.Length + protectedBytes.Length];
        Buffer.BlockCopy(header, 0, result, 0, header.Length);
        Buffer.BlockCopy(protectedBytes, 0, result, header.Length, protectedBytes.Length);
        return result;
    }

    internal static IDisposable BeginTestScope(
        Func<byte[], DataProtectionScope, byte[]> protectCore,
        Func<bool> localMachineFallbackAllowed)
    {
        var previous = TestOverrides.Value;
        TestOverrides.Value = new TestScopeOverrides(protectCore, localMachineFallbackAllowed);
        return new TestScope(() => TestOverrides.Value = previous);
    }

    private static bool IsLocalMachineFallbackAllowed() =>
        (TestOverrides.Value?.LocalMachineFallbackAllowed ?? IsLocalMachineFallbackAllowedByEnvironment)();

    private static bool IsLocalMachineFallbackAllowedByEnvironment()
    {
        var value = Environment.GetEnvironmentVariable(AllowLocalMachineFallbackEnvVar);
        return string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);
    }

    private sealed class TestScope(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }

    private sealed record TestScopeOverrides(
        Func<byte[], DataProtectionScope, byte[]> ProtectCore,
        Func<bool> LocalMachineFallbackAllowed);

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
