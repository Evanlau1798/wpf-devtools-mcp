using System.Security.Cryptography;
using System.Text;
using WpfDevTools.Shared.Security;

namespace WpfDevTools.Mcp.Server;

internal sealed class ProcessAuthenticationSecretProvider
{
    private const string DerivationPurpose = "WpfDevTools.ProcessAuthenticationSecret.v1";
    private readonly AuthenticationManager? _rootAuthenticationManager;

    public ProcessAuthenticationSecretProvider(AuthenticationManager? rootAuthenticationManager)
    {
        _rootAuthenticationManager = rootAuthenticationManager;
    }

    public bool IsEnabled => _rootAuthenticationManager?.IsAuthenticationEnabled == true;

    public string? GetAuthenticationSecretBase64(int processId)
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
        byte[]? derivedSecret = null;
        try
        {
            var context = Encoding.UTF8.GetBytes(
                $"{DerivationPurpose}|pid={processId}|pipe=WpfDevTools_{processId}");
            using var hmac = new HMACSHA256(rootSecret);
            derivedSecret = hmac.ComputeHash(context);
            return Convert.ToBase64String(derivedSecret);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(rootSecret);
            if (derivedSecret != null)
            {
                CryptographicOperations.ZeroMemory(derivedSecret);
            }
        }
    }

    public AuthenticationManager? CreateAuthenticationManager(int processId)
    {
        var secretBase64 = GetAuthenticationSecretBase64(processId);
        return string.IsNullOrWhiteSpace(secretBase64)
            ? null
            : new AuthenticationManager(() => secretBase64);
    }
}