using WpfDevTools.Mcp.Server.Composer.Contracts;
using WpfDevTools.Mcp.Server.Composer.Packs;

namespace WpfDevTools.Mcp.Server.Composer.Preview;

internal sealed partial class UiPackPreviewContractGenerator
{
    private static RuntimeApprovalResolution ResolveRuntimeApprovals(
        IReadOnlyList<ComposerPackReference> runtimeCandidates,
        IReadOnlyDictionary<string, UiPreviewPackSnapshot> snapshots,
        IReadOnlyDictionary<string, UiPackManifest> manifests,
        IReadOnlyDictionary<string, IReadOnlyList<string>> resourcesByPack,
        IReadOnlyCollection<string>? callApprovalTokens)
    {
        var approvedPackIds = new HashSet<string>(StringComparer.Ordinal);
        var packagesByPack = new Dictionary<string, IReadOnlyList<PreviewRuntimeNuGetPackage>>(StringComparer.Ordinal);
        var advisories = new List<PreviewDiagnostic>();
        var reviews = new List<PreviewRuntimePackApprovalReview>();

        foreach (var packRef in runtimeCandidates)
        {
            var pack = snapshots[packRef.Id].RegistryItem;
            var approved = UiPreviewRuntimeDependencyPolicy.IsApproved(pack, callApprovalTokens);
            var packagesResolved = UiPreviewRuntimeDependencyPolicy.TryResolvePackages(
                manifests[packRef.Id].NugetPackages,
                out var packages,
                out var packageError);

            if (pack.Scope != PackScope.Builtin)
            {
                reviews.Add(new PreviewRuntimePackApprovalReview(
                    packRef.Id,
                    packRef.Version,
                    ToScopeName(pack.Scope),
                    pack.Fingerprint,
                    UiPreviewRuntimeDependencyPolicy.CreateApprovalToken(pack),
                    "one-preview-call",
                    approved,
                    resourcesByPack[packRef.Id].ToArray(),
                    packagesResolved ? packages : []));
            }

            if (!approved)
            {
                advisories.Add(Diagnostic(
                    "PreviewRuntimeDependenciesNotApproved",
                    $"Pack '{packRef.Id}@{packRef.Version}' remains structural. Review runtimePackApprovalReviews, then pass its approvalToken in runtimePackApprovalTokens after enabling {McpServerConfiguration.AllowComposerRuntimeApprovalsEnvVar}, or configure {McpServerConfiguration.ComposerTrustedRuntimePacksEnvVar}."));
                continue;
            }

            if (!packagesResolved)
            {
                advisories.Add(Diagnostic("PreviewRuntimePackageNotImmutable", packageError!));
                continue;
            }

            approvedPackIds.Add(packRef.Id);
            packagesByPack[packRef.Id] = packages;
        }

        return new RuntimeApprovalResolution(approvedPackIds, packagesByPack, advisories, reviews);
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
        IReadOnlyList<PreviewDiagnostic> Advisories,
        IReadOnlyList<PreviewRuntimePackApprovalReview> Reviews);
}
