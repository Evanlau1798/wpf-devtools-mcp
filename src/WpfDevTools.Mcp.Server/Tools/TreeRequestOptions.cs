using System.Text.Json;
using WpfDevTools.Shared.Configuration;
using WpfDevTools.Shared.ErrorHandling;
using WpfDevTools.Shared.Utilities;

namespace WpfDevTools.Mcp.Server.Tools;

internal sealed class TreeRequestOptions
{
    public const int MaxDepthLimit = TreeTraversalDefaults.MaxDepthLimit;
    public const int MaxNodesLimit = TreeTraversalDefaults.MaxNodesLimit;
    public const int MaxChildrenPerNodeLimit = TreeTraversalDefaults.MaxChildrenPerNodeLimit;

    public int? Depth { get; }
    public bool Compact { get; }
    public bool SummaryOnly { get; }
    public int? MaxNodes { get; }
    public int? MaxChildrenPerNode { get; }

    private TreeRequestOptions(
        int? depth,
        bool compact,
        bool summaryOnly,
        int? maxNodes,
        int? maxChildrenPerNode)
    {
        Depth = depth;
        Compact = compact;
        SummaryOnly = summaryOnly;
        MaxNodes = maxNodes;
        MaxChildrenPerNode = maxChildrenPerNode;
    }

    public static bool TryParse(JsonElement? arguments, out TreeRequestOptions options, out object? error)
    {
        var compact = ParameterParser.ParseBoolParam(arguments, "compact") ?? false;
        var summaryOnly = ParameterParser.ParseBoolParam(arguments, "summaryOnly") ?? false;

        if (!BoundaryParameterValidator.TryGetOptionalIntInRange(
            arguments,
            "depth",
            0,
            MaxDepthLimit,
            out var depth,
            out var depthError))
        {
            options = null!;
            error = depthError;
            return false;
        }

        if (!BoundaryParameterValidator.TryGetOptionalIntInRange(
            arguments,
            "maxNodes",
            1,
            MaxNodesLimit,
            out var maxNodes,
            out var maxNodesError))
        {
            options = null!;
            error = maxNodesError;
            return false;
        }

        if (!BoundaryParameterValidator.TryGetOptionalIntInRange(
            arguments,
            "maxChildrenPerNode",
            1,
            MaxChildrenPerNodeLimit,
            out var maxChildrenPerNode,
            out var maxChildrenPerNodeError))
        {
            options = null!;
            error = maxChildrenPerNodeError;
            return false;
        }

        options = new TreeRequestOptions(
            depth,
            compact,
            summaryOnly,
            maxNodes ?? TreeTraversalDefaults.DefaultMaxNodes,
            maxChildrenPerNode ?? TreeTraversalDefaults.DefaultMaxChildrenPerNode);
        error = null;
        return true;
    }

    public object ToInspectorParams(string? elementId)
    {
        return new
        {
            elementId,
            depth = Depth,
            compact = Compact,
            summaryOnly = SummaryOnly,
            maxNodes = MaxNodes,
            maxChildrenPerNode = MaxChildrenPerNode
        };
    }

}
