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
        var depth = ParameterParser.ParseIntParam(arguments, "depth");
        var compact = ParameterParser.ParseBoolParam(arguments, "compact") ?? false;
        var summaryOnly = ParameterParser.ParseBoolParam(arguments, "summaryOnly") ?? false;
        var maxNodes = ParameterParser.ParseIntParam(arguments, "maxNodes");
        var maxChildrenPerNode = ParameterParser.ParseIntParam(arguments, "maxChildrenPerNode");

        if (depth.HasValue && (depth.Value < 0 || depth.Value > MaxDepthLimit))
        {
            options = null!;
            error = CreateInvalidArgumentError(
                $"depth must be between 0 and {MaxDepthLimit} to prevent invalid traversal",
                "Provide a depth value between 0 and 100.");
            return false;
        }

        if (maxNodes.HasValue && (maxNodes.Value <= 0 || maxNodes.Value > MaxNodesLimit))
        {
            options = null!;
            error = CreateInvalidArgumentError(
                $"maxNodes must be between 1 and {MaxNodesLimit}",
                "Provide a maxNodes value between 1 and 10000.");
            return false;
        }

        if (maxChildrenPerNode.HasValue && (maxChildrenPerNode.Value <= 0 || maxChildrenPerNode.Value > MaxChildrenPerNodeLimit))
        {
            options = null!;
            error = CreateInvalidArgumentError(
                $"maxChildrenPerNode must be between 1 and {MaxChildrenPerNodeLimit}",
                "Provide a maxChildrenPerNode value between 1 and 1000.");
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

    private static ToolErrorPayload CreateInvalidArgumentError(string message, string hint) =>
        new()
        {
            Error = message,
            ErrorCode = ToolErrorCode.InvalidArgument.ToString(),
            Hint = hint
        };
}
