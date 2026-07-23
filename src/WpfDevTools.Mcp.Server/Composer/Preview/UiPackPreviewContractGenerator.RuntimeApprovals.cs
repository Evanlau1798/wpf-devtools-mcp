using WpfDevTools.Mcp.Server.Composer.Contracts;
using WpfDevTools.Mcp.Server.Composer.Packs;

namespace WpfDevTools.Mcp.Server.Composer.Preview;

internal sealed partial class UiPackPreviewContractGenerator
{
    private static RuntimeApprovalResolution ResolveRuntimeApprovals(
        IReadOnlyList<ComposerPackReference> runtimeCandidates,
        IReadOnlyDictionary<string, UiPreviewPackSnapshot> snapshots,
        IReadOnlyDictionary<string, UiPackManifest> manifests,
        IReadOnlyDictionary<string, IReadOnlyList<string>> resourcesByPack)
    {
        var approvedPackIds = new HashSet<string>(StringComparer.Ordinal);
        var packagesByPack = new Dictionary<string, IReadOnlyList<PreviewRuntimeNuGetPackage>>(StringComparer.Ordinal);
        var errors = new List<PreviewDiagnostic>();
        var advisories = new List<PreviewDiagnostic>();
        var reviews = new List<PreviewRuntimePackApprovalReview>();

        foreach (var packRef in runtimeCandidates)
        {
            var pack = snapshots[packRef.Id].RegistryItem;
            var approvalSource = UiPreviewRuntimeDependencyPolicy.GetApprovalSource(pack);
            var authorized = approvalSource != "none";
            var packagesResolved = UiPreviewRuntimeDependencyPolicy.TryResolvePackages(
                manifests[packRef.Id].NugetPackages,
                out var packages,
                out var packageError);
            var resourceDiagnostics = UiPreviewRuntimeDependencyPolicy.ValidateResources(
                resourcesByPack[packRef.Id]);
            var runtimeEligible = packagesResolved && resourceDiagnostics.Count == 0;
            var eligibilityCode = !packagesResolved
                ? "PreviewRuntimePackageNotImmutable"
                : resourceDiagnostics.FirstOrDefault()?.Code;
            var eligibilityMessage = !packagesResolved
                ? packageError
                : resourceDiagnostics.FirstOrDefault()?.Message;

            if (pack.Scope != PackScope.Builtin)
            {
                reviews.Add(new PreviewRuntimePackApprovalReview(
                    packRef.Id,
                    packRef.Version,
                    ToScopeName(pack.Scope),
                    pack.Fingerprint,
                    runtimeEligible ? UiPreviewRuntimeDependencyPolicy.CreateApprovalToken(pack) : null,
                    "content-bound-installed-pack",
                    approvalSource,
                    authorized && runtimeEligible,
                    runtimeEligible,
                    eligibilityCode,
                    eligibilityMessage,
                    resourcesByPack[packRef.Id].ToArray(),
                    packagesResolved ? packages : []));
            }

            if (!packagesResolved)
            {
                advisories.Add(Diagnostic("PreviewRuntimePackageNotImmutable", packageError!));
                continue;
            }

            if (resourceDiagnostics.Count > 0)
            {
                (authorized ? errors : advisories).AddRange(resourceDiagnostics);
                continue;
            }

            if (!authorized)
            {
                advisories.Add(Diagnostic(
                    "PreviewRuntimeDependenciesNotApproved",
                    $"Pack '{packRef.Id}@{packRef.Version}' remains structural. Review runtimePackApprovalReviews, then configure its approvalToken in {McpServerConfiguration.ComposerTrustedRuntimePacksEnvVar} before starting or restarting the MCP server."));
                continue;
            }

            approvedPackIds.Add(packRef.Id);
            packagesByPack[packRef.Id] = packages;
        }

        return new RuntimeApprovalResolution(approvedPackIds, packagesByPack, errors, advisories, reviews);
    }

    private static string ToScopeName(PackScope scope)
        => scope switch
        {
            PackScope.ProjectLocal => "project-local",
            PackScope.UserGlobal => "user-global",
            _ => "built-in"
        };

    private sealed record RuntimeApprovalResolution(
        HashSet<string> ApprovedPackIds,
        IReadOnlyDictionary<string, IReadOnlyList<PreviewRuntimeNuGetPackage>> PackagesByPack,
        IReadOnlyList<PreviewDiagnostic> Errors,
        IReadOnlyList<PreviewDiagnostic> Advisories,
        IReadOnlyList<PreviewRuntimePackApprovalReview> Reviews);
}
