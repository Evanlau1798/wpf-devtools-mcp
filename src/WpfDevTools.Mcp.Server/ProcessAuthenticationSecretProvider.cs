using System.Security.Cryptography;
using System.Text;
using WpfDevTools.Shared.Configuration;
using WpfDevTools.Shared.Security;

namespace WpfDevTools.Mcp.Server;

internal sealed class ProcessAuthenticationSecretProvider
{
    private const string DerivationPurpose = "WpfDevTools.ProcessAuthenticationSecret.v1";
    private static readonly string BuildFingerprint =
        InspectorCompatibilityContract.GetBuildFingerprint(typeof(ProcessAuthenticationSecretProvider));

    private readonly AuthenticationManager? _rootAuthenticationManager;
    private readonly Func<int, ProcessIdentity?> _processIdentityProvider;

    public ProcessAuthenticationSecretProvider(
        AuthenticationManager? rootAuthenticationManager,
        Func<int, ProcessIdentity?>? processIdentityProvider = null)
    {
        _rootAuthenticationManager = rootAuthenticationManager;
        _processIdentityProvider = processIdentityProvider ?? (_ => null);
    }

    public bool IsEnabled => _rootAuthenticationManager?.IsAuthenticationEnabled == true;

    public string? GetAuthenticationSecretBase64(int processId, string? pipeName = null)
    {
        if (processId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId), "Process ID must be positive.");
        }

        if (!IsEnabled)
        {
            return null;
        }

        var rootSecret = _rootAuthenticationManager!.GetSharedSecret();
        byte[]? context = null;
        byte[]? derivedSecret = null;
        try
        {
            var effectivePipeName = string.IsNullOrWhiteSpace(pipeName)
                ? $"WpfDevTools_{processId}"
                : pipeName;
            var processIdentity = _processIdentityProvider(processId);
            context = Encoding.UTF8.GetBytes(
                $"{DerivationPurpose}|build={BuildFingerprint}|pid={processId}|pipe={effectivePipeName}|startTicks={FormatStartTicks(processIdentity)}");
            using var hmac = new HMACSHA256(rootSecret);
            derivedSecret = hmac.ComputeHash(context);
            return Convert.ToBase64String(derivedSecret);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(rootSecret);
            if (context != null)
            {
                CryptographicOperations.ZeroMemory(context);
            }

            if (derivedSecret != null)
            {
                CryptographicOperations.ZeroMemory(derivedSecret);
            }
        }
    }

    public AuthenticationManager? CreateAuthenticationManager(int processId, string? pipeName = null)
    {
        var secretBase64 = GetAuthenticationSecretBase64(processId, pipeName);
        return string.IsNullOrWhiteSpace(secretBase64)
            ? null
            : new AuthenticationManager(() => secretBase64);
    }

    private static string FormatStartTicks(ProcessIdentity? identity)
        => identity?.StartTimeUtcTicks?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "unknown";

    internal readonly record struct ProcessIdentity(
        int ProcessId,
        long? StartTimeUtcTicks);
}
