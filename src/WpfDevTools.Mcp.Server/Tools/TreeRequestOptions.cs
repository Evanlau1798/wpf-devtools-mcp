using System.Text.Json;
using WpfDevTools.Shared.Utilities;

namespace WpfDevTools.Mcp.Server.Tools;

internal sealed class TreeRequestOptions
{
    public const int MaxDepthLimit = 100;
    public const int MaxNodesLimit = 10000;
    public const int MaxChildrenPerNodeLimit = 1000;

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
            error = new { success = false, error = $"depth must be between 0 and {MaxDepthLimit} to prevent invalid traversal" };
            return false;
        }

        if (maxNodes.HasValue && (maxNodes.Value <= 0 || maxNodes.Value > MaxNodesLimit))
        {
            options = null!;
            error = new { success = false, error = $"maxNodes must be between 1 and {MaxNodesLimit}" };
            return false;
        }

        if (maxChildrenPerNode.HasValue && (maxChildrenPerNode.Value <= 0 || maxChildrenPerNode.Value > MaxChildrenPerNodeLimit))
        {
            options = null!;
            error = new { success = false, error = $"maxChildrenPerNode must be between 1 and {MaxChildrenPerNodeLimit}" };
            return false;
        }

        options = new TreeRequestOptions(depth, compact, summaryOnly, maxNodes, maxChildrenPerNode);
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
