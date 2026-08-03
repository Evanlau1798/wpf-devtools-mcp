using System.Text.Json;
using WpfDevTools.Mcp.Server.McpTools;
using WpfDevTools.Mcp.Server.Tools;

namespace WpfDevTools.Mcp.Server.Composer.Preview;

internal static partial class UiBlueprintPreviewDiagnosticsBridge
{
    private static readonly string[] VisualContractSnapshotProperties =
    [
        "ComputedHorizontalScrollBarVisibility",
        "ComputedVerticalScrollBarVisibility"
    ];

    private static async Task<IReadOnlyList<PreviewRuntimeDiagnostic>> CaptureVisualContractSnapshotsAsync(
        SessionManager sessionManager,
        int processId,
        McpToolExecutionPolicy policy,
        IReadOnlyList<PreviewRuntimeDiagnostic> lookupDiagnostics,
        PreviewVisualLayoutContract? contract,
        CancellationToken cancellationToken)
    {
        if (contract is null)
        {
            return [];
        }

        var exactMatches = BuildVisualContractElementLookup(lookupDiagnostics);
        var diagnostics = new List<PreviewRuntimeDiagnostic>(contract.Regions.Count);
        foreach (var region in contract.Regions)
        {
            if (!exactMatches.TryGetValue(region.ElementName, out var elementId))
            {
                continue;
            }

            var diagnostic = await RunGatedAsync(
                policy,
                "get_element_snapshot",
                ct => new GetElementSnapshotTool(sessionManager).ExecuteAsync(
                    ToolCallHelper.BuildJsonArgs(
                        ("processId", processId),
                        ("elementId", elementId),
                        ("includeProperties", VisualContractSnapshotProperties)),
                    ct),
                cancellationToken).ConfigureAwait(false);
            diagnostics.Add(diagnostic with { TargetElementIds = [elementId] });
        }

        return diagnostics;
    }

    internal static IReadOnlyDictionary<string, string> BuildVisualContractElementLookup(
        IReadOnlyList<PreviewRuntimeDiagnostic> lookupDiagnostics)
        => lookupDiagnostics
            .Where(item => item.Success && item.Tool == "find_elements")
            .SelectMany(item => ReadSearchResults(item.Payload))
            .Select(result =>
            {
                var hasName = TryReadString(result, "elementName", out var name);
                var hasId = TryReadString(result, "elementId", out var id);
                return (hasName, hasId, name, id);
            })
            .Where(item => item.hasName && item.hasId)
            .GroupBy(item => item.name!, StringComparer.Ordinal)
            .Select(group => new
            {
                Name = group.Key,
                Ids = group.Select(item => item.id!).Distinct(StringComparer.Ordinal).ToArray()
            })
            .Where(item => item.Ids.Length == 1)
            .ToDictionary(item => item.Name, item => item.Ids[0], StringComparer.Ordinal);

    private static bool TryReadString(JsonElement value, string propertyName, out string? result)
    {
        result = null;
        if (!value.TryGetProperty(propertyName, out var property)
            || property.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        result = property.GetString();
        return !string.IsNullOrWhiteSpace(result);
    }
}
