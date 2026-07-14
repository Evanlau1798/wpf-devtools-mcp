using WpfDevTools.Mcp.Server.Composer.Contracts;

namespace WpfDevTools.Mcp.Server.Composer.Blueprints;

internal sealed record BlueprintValidationResult(
    IReadOnlyList<BlueprintValidationIssue> Errors,
    IReadOnlyList<BlueprintValidationIssue> Warnings,
    IReadOnlyList<string> Diagnostics,
    BlueprintResolutionPlan Resolution)
{
    public bool Success => Errors.Count == 0;
}

internal sealed record BlueprintResolutionPlan(
    IReadOnlyList<ResolvedPackPlan> Packs,
    IReadOnlyList<string> ResourceOrder,
    IReadOnlyList<BlueprintPackConflictPlan> Conflicts)
{
    public static readonly BlueprintResolutionPlan Empty = new([], [], []);
}

internal sealed record ResolvedPackPlan(
    string Id,
    string RequestedVersion,
    string ResolvedVersion,
    string Role,
    string SuggestedRole,
    bool Required,
    string Scope,
    string Kind,
    string Status);

internal sealed record BlueprintPackConflictPlan(
    string Code,
    string Severity,
    IReadOnlyList<string> PackIds,
    string? Resource,
    string Message,
    string RepairSuggestion);

internal sealed record BlueprintValidationIssue(
    string JsonPath,
    string Code,
    string Message,
    string RepairSuggestion,
    IReadOnlyList<string> AllowedKinds,
    IReadOnlyList<string> AllowedValues,
    string? ParentSlot)
{
    public IReadOnlyList<string> RelatedJsonPaths { get; init; } = [];
    public string? ObservedValueKind { get; init; }
    public string? ExpectedJsonShape { get; init; }
}

internal sealed record BlueprintValidationContext(
    IReadOnlySet<string> DeclaredPackIds,
    IReadOnlySet<string> LoadedPackIds,
    IReadOnlySet<string> OptionalMissingPackIds,
    IReadOnlyDictionary<string, UiBlockDefinition> Blocks,
    IReadOnlyDictionary<string, string[]> PackKinds);
