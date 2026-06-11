using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Tests.Integration;

internal static class TrustedLocalReleaseSignatureSkip
{
    public static void ValidateDllPath(string dllPath)
        => DllPathValidator.ValidateDllPath(
            dllPath,
            AppContext.BaseDirectory,
            trustedLocalDevelopmentSkipOptIn: true);
}
