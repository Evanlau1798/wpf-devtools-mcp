using System.Text.RegularExpressions;

namespace WpfDevTools.Mcp.Server.Composer.Apply;

internal static partial class ExistingCodeBehindClassResolver
{
    public static ExistingCodeBehindClassResolution Resolve(string path, string expectedClassName)
    {
        try
        {
            var source = CommentPattern().Replace(File.ReadAllText(path), string.Empty);
            var classMatches = PartialClassPattern().Matches(source)
                .Where(match => string.Equals(
                    match.Groups["name"].Value,
                    expectedClassName,
                    StringComparison.Ordinal))
                .ToArray();
            if (classMatches.Length != 1)
            {
                return ExistingCodeBehindClassResolution.Failed(
                    "The existing code-behind must declare exactly one matching partial class.");
            }

            var classIndex = classMatches[0].Index;
            var namespaceMatch = NamespacePattern().Matches(source[..classIndex]).LastOrDefault();
            var namespaceName = namespaceMatch?.Groups["name"].Value;
            var fullClassName = string.IsNullOrWhiteSpace(namespaceName)
                ? expectedClassName
                : $"{namespaceName}.{expectedClassName}";
            return ExistingCodeBehindClassResolution.Resolved(fullClassName);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return ExistingCodeBehindClassResolution.Failed(
                "The existing code-behind could not be read safely.");
        }
    }

    [GeneratedRegex(@"//.*?$|/\*.*?\*/", RegexOptions.Multiline | RegexOptions.Singleline)]
    private static partial Regex CommentPattern();

    [GeneratedRegex(@"\bpartial\s+class\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\b")]
    private static partial Regex PartialClassPattern();

    [GeneratedRegex(@"\bnamespace\s+(?<name>[A-Za-z_][A-Za-z0-9_]*(?:\.[A-Za-z_][A-Za-z0-9_]*)*)\s*(?:;|\{)")]
    private static partial Regex NamespacePattern();
}

internal sealed record ExistingCodeBehindClassResolution(
    bool Success,
    string? FullClassName,
    string? FailureReason)
{
    public static ExistingCodeBehindClassResolution Resolved(string fullClassName)
        => new(true, fullClassName, null);

    public static ExistingCodeBehindClassResolution Failed(string reason)
        => new(false, null, reason);
}
