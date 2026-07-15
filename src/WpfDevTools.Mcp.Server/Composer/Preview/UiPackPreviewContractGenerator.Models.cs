using WpfDevTools.Mcp.Server.Composer.Contracts;

namespace WpfDevTools.Mcp.Server.Composer.Preview;

internal sealed partial class UiPackPreviewContractGenerator
{
    private static PreviewWindowRoot? ResolveWindowRoot(
        string renderedXaml,
        IReadOnlyList<ResolvedPreviewContract> contracts)
    {
        var rootElement = RootElementPattern.Match(renderedXaml);
        if (!rootElement.Success)
        {
            return null;
        }

        var prefix = rootElement.Groups["prefix"].Value;
        var typeName = rootElement.Groups["type"].Value;
        var contract = contracts.FirstOrDefault(candidate =>
            string.Equals(candidate.Prefix, prefix, StringComparison.Ordinal)
            && candidate.Contract.Types.TryGetValue(typeName, out var type)
            && type.BaseKind == "window");
        return contract is null
            ? null
            : new PreviewWindowRoot(
                prefix + ":" + typeName,
                contract.Contract.ClrNamespace + "." + typeName);
    }

    private static IReadOnlyList<PreviewDiagnostic> ValidateRuntimePackageIdentities(
        IReadOnlyDictionary<string, IReadOnlyList<PreviewRuntimeNuGetPackage>> packagesByPack)
        => packagesByPack.Values
            .SelectMany(packages => packages)
            .GroupBy(package => package.Id, StringComparer.OrdinalIgnoreCase)
            .Where(group => group
                .Select(package => package.ExactVersion + "\0" + package.ContentHash)
                .Distinct(StringComparer.Ordinal)
                .Count() > 1)
            .Select(group => Diagnostic(
                "PreviewPackageIdentityConflict",
                $"Runtime package '{group.Key}' has conflicting exact version or SHA-512 identities across selected packs."))
            .ToArray();

    private static PreviewDiagnostic Diagnostic(string code, string message)
        => new(code, message, "$.packs", string.Empty);

    private static string Escape(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal);
}

internal sealed record PreviewContractGenerationResult(
    bool Success,
    string Source,
    IReadOnlyDictionary<string, string> XmlNamespaces,
    string? WindowRootTag,
    string? WindowRootType,
    IReadOnlyList<PreviewDiagnostic> Diagnostics)
{
    public bool UsesStructuralStubs { get; init; }
    public bool UsesRuntimeDependencies { get; init; }
    public IReadOnlyList<PreviewRuntimeNuGetPackage> RuntimeNuGetPackages { get; init; } = [];
    public IReadOnlyList<string> RuntimeResources { get; init; } = [];
    public IReadOnlyList<PreviewDiagnostic> Advisories { get; init; } = [];
}

internal sealed record ResolvedPreviewContract(
    string PackId,
    string Prefix,
    UiPackPreviewContract Contract,
    bool RuntimeBacked);

internal sealed record PreviewWindowRoot(string Tag, string ClrType);
