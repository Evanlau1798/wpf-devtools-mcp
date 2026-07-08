using WpfDevTools.Mcp.Server.Composer.Contracts;

namespace WpfDevTools.Mcp.Server.Composer.Blueprints;

internal sealed record BlueprintValidationResult(
    IReadOnlyList<BlueprintValidationIssue> Errors,
    IReadOnlyList<BlueprintValidationIssue> Warnings,
    IReadOnlyList<string> Diagnostics)
{
    public bool Success => Errors.Count == 0;
}

internal sealed record BlueprintValidationIssue(
    string JsonPath,
    string Code,
    string Message,
    string RepairSuggestion,
    IReadOnlyList<string> AllowedKinds,
    IReadOnlyList<string> AllowedValues,
    string? ParentSlot);

internal sealed record BlueprintValidationContext(
    IReadOnlySet<string> DeclaredPackIds,
    IReadOnlySet<string> LoadedPackIds,
    IReadOnlySet<string> OptionalMissingPackIds,
    IReadOnlyDictionary<string, UiBlockDefinition> Blocks,
    IReadOnlyDictionary<string, string[]> PackKinds);
