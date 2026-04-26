using System.Text;
using System.Text.Json;

namespace WpfDevTools.Mcp.Server.McpTools;

public static partial class ToolCallHelper
{
    private static void RecordRequestMetrics(
        MetricsCollector? metricsCollector,
        string toolName,
        long latencyMs,
        bool success,
        JsonElement? payload)
    {
        if (metricsCollector is null)
        {
            return;
        }

        var payloadByteLength = payload.HasValue
            ? Encoding.UTF8.GetByteCount(payload.Value.GetRawText())
            : (long?)null;
        var truncated = payload.HasValue && HasTruncationPressure(payload.Value);

        metricsCollector.RecordRequest(
            toolName,
            latencyMs,
            success,
            payloadByteLength,
            truncated);
    }

    private static bool HasTruncationPressure(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        if (payload.TryGetProperty("truncated", out var truncated)
            && truncated.ValueKind is JsonValueKind.True)
        {
            return true;
        }

        return payload.TryGetProperty("truncationMetadata", out var metadata)
            && metadata.ValueKind == JsonValueKind.Object
            && metadata.TryGetProperty("reasons", out var reasons)
            && reasons.ValueKind == JsonValueKind.Array
            && reasons.GetArrayLength() > 0;
    }
}
