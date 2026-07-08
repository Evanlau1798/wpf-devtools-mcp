using System.Security.Cryptography;
using System.Text;

namespace WpfDevTools.Tests.Unit.Release;

internal static partial class ReleaseScriptTestHarness
{
    private static void CopyBuiltinComposerPacks(string packageDirectory)
    {
        CopyDirectory(
            GetRepoFilePath(Path.Combine("packs", "builtin")),
            Path.Combine(packageDirectory, "packs", "builtin"));
    }

    private static string GetBuiltinComposerPackContentHash()
    {
        var root = GetRepoFilePath(Path.Combine("packs", "builtin"));
        var builder = new StringBuilder();
        foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Order(StringComparer.OrdinalIgnoreCase))
        {
            var relativePath = Path.GetRelativePath(root, file).Replace('\\', '/');
            builder.Append(relativePath).Append(':').Append(GetFileContentHash(file)).Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }
}
