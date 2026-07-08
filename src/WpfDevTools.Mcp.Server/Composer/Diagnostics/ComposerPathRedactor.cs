using System.Text.RegularExpressions;

namespace WpfDevTools.Mcp.Server.Composer.Diagnostics;

internal static partial class ComposerPathRedactor
{
    public const string RedactedPath = "<absolute-path-redacted>";
    private const string RedactedSecret = "<redacted>";

    public static string Redact(string value)
    {
        var withoutPaths = AbsolutePathPattern().Replace(value, RedactedPath);
        return SecretPattern().Replace(withoutPaths, RedactedSecret);
    }

    [GeneratedRegex(@"(?i)([a-z]:[\\/][^'""\s<>)]*|\\\\[^'""\s<>)]*|/(?:users|home|tmp|var|workspace|workspaces|mnt|private|runner|a)(?:/|$)[^'""\s<>)]*)", RegexOptions.CultureInvariant)]
    private static partial Regex AbsolutePathPattern();

    [GeneratedRegex(@"(?i)(sk_[a-z0-9_=-]+|secret|password|token|api[_-]?key|connectionstring)", RegexOptions.CultureInvariant)]
    private static partial Regex SecretPattern();
}
