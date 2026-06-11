using System.Threading;
using WpfDevTools.Shared.Security;

namespace WpfDevTools.Mcp.Server.Tools;

internal static partial class DllPathValidator
{
    private static readonly AsyncLocal<Func<string, bool>?> ReparsePointChainDetectorOverrideForTestingState = new();

    internal static Func<string, bool>? ReparsePointChainDetectorOverrideForTesting
    {
        get => ReparsePointChainDetectorOverrideForTestingState.Value;
        set => ReparsePointChainDetectorOverrideForTestingState.Value = value;
    }

    private static void EnsureDllPathDoesNotTraverseReparsePoint(string fullPath, string parameterName)
    {
        var detector = ReparsePointChainDetectorOverrideForTesting;
        detector ??= static path => CertificateStorageSecurity.ContainsReparsePointInPathChain(path);
        if (!detector(fullPath))
        {
            return;
        }

        throw new ArgumentException(
            "DLL path must not traverse symbolic links or reparse points.",
            parameterName);
    }
}
