using WpfDevTools.Mcp.Server.Composer.Contracts;
using WpfDevTools.Mcp.Server.Composer.Packs;

namespace WpfDevTools.Mcp.Server.Composer.Preview;

internal sealed partial class UiPackPreviewContractGenerator
{
    private static Dictionary<string, UiPreviewPackSnapshot> LoadPackSnapshots(
        IEnumerable<ComposerPackReference> packReferences,
        IReadOnlyDictionary<(string Id, string Version), PackRegistryItem> available,
        IReadOnlyDictionary<string, string>? renderedPackFingerprints,
        ICollection<PreviewDiagnostic> diagnostics)
    {
        var snapshots = new Dictionary<string, UiPreviewPackSnapshot>(StringComparer.Ordinal);
        foreach (var packRef in packReferences.Where(packRef =>
                     available.ContainsKey((packRef.Id, packRef.Version))))
        {
            if (UiPreviewPackSnapshot.TryCreate(
                    available[(packRef.Id, packRef.Version)],
                    out var snapshot,
                    out var error))
            {
                if (renderedPackFingerprints?.TryGetValue(packRef.Id, out var renderedFingerprint) == true
                    && !string.Equals(
                        renderedFingerprint,
                        snapshot!.RegistryItem.Fingerprint,
                        StringComparison.Ordinal))
                {
                    diagnostics.Add(Diagnostic(
                        "PreviewPackContentChanged",
                        $"Pack '{packRef.Id}@{packRef.Version}' changed after rendering; retry preview to render and approve one stable exact-content snapshot."));
                    continue;
                }

                snapshots[packRef.Id] = snapshot!;
            }
            else
            {
                diagnostics.Add(Diagnostic("PreviewPackContentChanged", error!));
            }
        }

        return snapshots;
    }
}
