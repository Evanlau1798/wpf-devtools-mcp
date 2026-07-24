using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using WpfDevTools.Mcp.Server.Composer.Contracts;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.Composer.Rendering;

namespace WpfDevTools.Mcp.Server.Composer.Preview;

internal static partial class UiPreviewRuntimeDependencyPolicy
{
    private static readonly Regex ExactVersionPattern = new(
        "^\\[(?<version>[A-Za-z0-9][A-Za-z0-9.+-]*)\\]$",
        RegexOptions.CultureInvariant);

    public static string GetApprovalSource(
        PackRegistryItem pack,
        IReadOnlyList<string>? requestApprovalTokens = null)
    {
        if (pack.Scope == PackScope.Builtin)
        {
            return "built-in";
        }

        var expected = CreateApprovalToken(pack);
        if (requestApprovalTokens?.Contains(expected, StringComparer.Ordinal) == true)
        {
            return "request-token";
        }

        var configured = Environment.GetEnvironmentVariable(
            McpServerConfiguration.ComposerTrustedRuntimePacksEnvVar);
        var configuredTokens = configured?.Split(
                new[] { ';', ',' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            ?? [];
        return configuredTokens.Contains(expected, StringComparer.Ordinal)
            ? "environment-token"
            : "none";
    }

    public static string CreateApprovalToken(PackRegistryItem pack)
    {
        var root = Path.GetFullPath(pack.RootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .ToUpperInvariant();
        var identity = string.Join(
            '\0',
            pack.Scope,
            root,
            pack.Id,
            pack.Version,
            pack.Fingerprint);
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        return $"{pack.Id}@{pack.Version}#{digest}";
    }

    public static bool TryResolvePackages(
        IReadOnlyList<UiPackNuGetPackage> packages,
        out IReadOnlyList<PreviewRuntimeNuGetPackage> resolved,
        out string? error)
    {
        var result = new List<PreviewRuntimeNuGetPackage>(packages.Count);
        foreach (var package in packages)
        {
            var match = ExactVersionPattern.Match(package.VersionRange);
            if (!match.Success || !IsSha512ContentHash(package.ContentHash))
            {
                resolved = [];
                error = $"Runtime package '{package.Id}' must use an exact [version] and declare its NuGet SHA-512 contentHash.";
                return false;
            }

            result.Add(new PreviewRuntimeNuGetPackage(
                package.Id,
                package.VersionRange,
                match.Groups["version"].Value,
                package.ContentHash));
        }

        resolved = result;
        error = null;
        return true;
    }

    public static IReadOnlyList<PreviewDiagnostic> ValidateResources(IReadOnlyList<string> resources)
    {
        var declaredSources = resources
            .SelectMany(XamlSafetyScanner.ExtractResourceDictionarySources)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var directSourceDiagnostics = resources
            .Where(resource => !resource.TrimStart().StartsWith('<'))
            .Where(resource => !PreviewResourcePolicy.IsApplicationLocalPackSource(resource))
            .Select(_ => new PreviewDiagnostic(
                "UnsafePreviewResource",
                "Preview resources must be inline XAML or application-local pack URIs.",
                "$.packs",
                string.Empty));
        return directSourceDiagnostics.Concat(resources
            .SelectMany(resource => XamlSafetyScanner.Scan(resource, [], declaredSources))
            .Select(issue => new PreviewDiagnostic(
                "UnsafePreviewResource",
                issue.Message,
                "$.packs",
                string.Empty)))
            .ToArray();
    }

    public static IReadOnlyList<PreviewDiagnostic> ValidateRestoredPackages(
        string previewRoot,
        IReadOnlyList<PreviewRuntimeNuGetPackage> packages)
    {
        var diagnostics = packages
            .SelectMany(package => ValidateRestoredPackage(previewRoot, package))
            .ToList();
        var declared = packages
            .Select(package => package.Id.ToLowerInvariant() + "\0" + package.ExactVersion)
            .ToHashSet(StringComparer.Ordinal);
        var cacheRoot = Path.Combine(previewRoot, ".nuget", "packages");
        var undeclared = Directory.Exists(cacheRoot)
            ? Directory.EnumerateFiles(cacheRoot, "*.nupkg.sha512", SearchOption.AllDirectories)
                .Select(path => new FileInfo(path).Directory)
                .Where(directory => directory?.Parent is not null)
                .Select(directory => directory!.Parent!.Name.ToLowerInvariant() + "\0" + directory.Name)
                .Where(identity => !declared.Contains(identity))
                .Order(StringComparer.Ordinal)
                .ToArray()
            : [];
        if (undeclared.Length > 0)
        {
            diagnostics.Add(Diagnostic(
                "PreviewPackageClosureMismatch",
                "The restored dependency closure contains packages not declared with exact versions and SHA-512 hashes by the pack."));
        }

        return diagnostics;
    }

    private static IReadOnlyList<PreviewDiagnostic> ValidateRestoredPackage(
        string previewRoot,
        PreviewRuntimeNuGetPackage package)
    {
        var packageId = package.Id.ToLowerInvariant();
        var packageRoot = Path.Combine(previewRoot, ".nuget", "packages", packageId, package.ExactVersion);
        var hashPath = Path.Combine(packageRoot, packageId + "." + package.ExactVersion + ".nupkg.sha512");
        if (!File.Exists(hashPath))
        {
            return [Diagnostic("PreviewPackageHashMissing", $"Restored package '{package.Id}@{package.ExactVersion}' did not expose a verifiable SHA-512 hash in the preview-local cache.")];
        }

        var actual = File.ReadAllText(hashPath).Trim();
        return string.Equals(actual, package.ContentHash, StringComparison.Ordinal)
            ? []
            : [Diagnostic("PreviewPackageHashMismatch", $"Restored package '{package.Id}@{package.ExactVersion}' does not match the pack-declared SHA-512 hash.")];
    }

    private static bool IsSha512ContentHash(string value)
    {
        try
        {
            return Convert.FromBase64String(value).Length == 64;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static PreviewDiagnostic Diagnostic(string code, string message)
        => new(code, message, "$.packs", string.Empty);
}

internal sealed record PreviewRuntimeNuGetPackage(
    string Id,
    string VersionRange,
    string ExactVersion,
    string ContentHash);
