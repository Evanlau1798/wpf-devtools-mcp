using System.Text.Json;

namespace WpfDevTools.Mcp.Server.McpTools;

public static partial class ToolCallHelper
{
    private const long EstimatedJsonStringPayloadBytes = 4096;
    private const long EstimatedJsonPropertyNameBytes = 64;
    private const long EstimatedJsonNumberPayloadBytes = 32;

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
            ? EstimatePayloadBytesForMetrics(payload.Value)
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

    // Metrics need a stable pressure signal; exact JSON byte counting would duplicate large payload text.
    internal static long EstimatePayloadBytesForMetrics(JsonElement payload) =>
        EstimateJsonBytes(payload);

    private static long EstimateJsonBytes(JsonElement value) =>
        value.ValueKind switch
        {
            JsonValueKind.Object => EstimateObjectBytes(value),
            JsonValueKind.Array => EstimateArrayBytes(value),
            JsonValueKind.String => 2 + EstimatedJsonStringPayloadBytes,
            JsonValueKind.Number => EstimatedJsonNumberPayloadBytes,
            JsonValueKind.True => 4,
            JsonValueKind.False => 5,
            JsonValueKind.Null => 4,
            _ => 0
        };

    private static long EstimateObjectBytes(JsonElement value)
    {
        long bytes = 2;
        var count = 0;
        foreach (var property in value.EnumerateObject())
        {
            if (count++ > 0)
            {
                bytes++;
            }

            bytes += 2 + EstimatedJsonPropertyNameBytes;
            bytes++;
            bytes += EstimateJsonBytes(property.Value);
        }

        return bytes;
    }

    private static long EstimateArrayBytes(JsonElement value)
    {
        long bytes = 2;
        var count = 0;
        foreach (var item in value.EnumerateArray())
        {
            if (count++ > 0)
            {
                bytes++;
            }

            bytes += EstimateJsonBytes(item);
        }

        return bytes;
    }
}
