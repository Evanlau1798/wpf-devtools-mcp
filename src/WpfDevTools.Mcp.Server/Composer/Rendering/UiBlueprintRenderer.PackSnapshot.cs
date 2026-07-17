using WpfDevTools.Mcp.Server.Composer.Blueprints;

namespace WpfDevTools.Mcp.Server.Composer.Rendering;

internal sealed partial class UiBlueprintRenderer
{
    internal static BlueprintValidationIssue? ValidatePackSnapshotContinuity(
        IReadOnlyDictionary<string, string> validationFingerprints,
        IReadOnlyDictionary<string, string> renderFingerprints)
    {
        var changedPackId = validationFingerprints.Keys
            .Concat(renderFingerprints.Keys)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .FirstOrDefault(packId =>
                !validationFingerprints.TryGetValue(packId, out var validationFingerprint)
                || !renderFingerprints.TryGetValue(packId, out var renderFingerprint)
                || !string.Equals(validationFingerprint, renderFingerprint, StringComparison.Ordinal));
        return changedPackId is null
            ? null
            : Issue(
                "$.packs",
                "PackContentChanged",
                $"Pack '{changedPackId}' changed between blueprint validation and rendering.",
                "Retry after pack contents are stable, then review any new exact-content approval token.");
    }
}
