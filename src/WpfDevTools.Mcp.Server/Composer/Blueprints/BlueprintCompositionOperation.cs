using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using WpfDevTools.Shared.Validation;

namespace WpfDevTools.Mcp.Server.Composer.Blueprints;

public sealed record BlueprintCompositionOperation
{
    public const int MaxOperations = 16;

    [Required]
    [StringLength(BoundaryStringLimits.MaxStringArgumentLength)]
    [Description("Target slot, including aliases created by an earlier operation in this batch.")]
    public string TargetPath { get; init; } = string.Empty;

    [Required]
    [StringLength(BoundaryStringLimits.MaxLabelLength)]
    [Description("Exact pack-qualified block kind to insert.")]
    public string Kind { get; init; } = string.Empty;

    [StringLength(BoundaryStringLimits.MaxLabelLength)]
    public string? ElementName { get; init; }

    [StringLength(BoundaryStringLimits.MaxLabelLength)]
    public string? AutomationId { get; init; }

    [Description("Optional pack-defined property values for the inserted node.")]
    public JsonElement? Properties { get; init; }

    [Description("Optional zero-based insertion index. Omit to append.")]
    public int? InsertionIndex { get; init; }
}
