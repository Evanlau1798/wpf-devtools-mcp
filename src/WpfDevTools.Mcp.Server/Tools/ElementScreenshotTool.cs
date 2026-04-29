using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// MCP tool to capture screenshots of WPF elements
/// </summary>
public sealed class ElementScreenshotTool : PipeConnectedToolBase
{
    /// <summary>
    /// Initializes a new instance of the ElementScreenshotTool class
    /// </summary>
    /// <param name="sessionManager">Session manager for tracking connected processes</param>
    public ElementScreenshotTool(SessionManager sessionManager) : base(sessionManager) { }

    /// <summary>
    /// Execute the element_screenshot tool to capture a screenshot of an element
    /// </summary>
    /// <param name="arguments">JSON arguments containing processId and optional elementId</param>
    /// <param name="cancellationToken">Cancellation token for async operation</param>
    /// <returns>Tool result containing screenshot path or error</returns>
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments, _sessionManager);
        if (error != null) return error;
        if (!BoundaryParameterValidator.TryGetOptionalStringEnum(
            arguments,
            "outputMode",
            "metadata",
            ["metadata", "file", "base64"],
            out var outputMode,
            out var outputModeError))
        {
            return outputModeError!;
        }

        if (!BoundaryParameterValidator.TryGetOptionalIntInRange(
            arguments,
            "maxWidth",
            1,
            int.MaxValue,
            out var maxWidth,
            out var maxWidthError))
        {
            return maxWidthError!;
        }

        if (!BoundaryParameterValidator.TryGetOptionalIntInRange(
            arguments,
            "maxHeight",
            1,
            int.MaxValue,
            out var maxHeight,
            out var maxHeightError))
        {
            return maxHeightError!;
        }

        var result = await SendInspectorRequestAsync(processId, "element_screenshot",
            new
            {
                elementId,
                outputMode,
                maxWidth,
                maxHeight
            }, cancellationToken);

        return outputMode == "file"
            ? RedactFilePathFromScreenshotResult(result)
            : result;
    }

    private static object RedactFilePathFromScreenshotResult(object result)
    {
        var payload = result is JsonElement jsonElement
            ? jsonElement
            : JsonSerializer.SerializeToElement(result);
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return result;
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            string? fileName = null;
            foreach (var property in payload.EnumerateObject())
            {
                if (IsLocalPathProperty(property.Name))
                {
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        fileName ??= Path.GetFileName(property.Value.GetString());
                    }

                    continue;
                }

                property.WriteTo(writer);
            }

            writer.WriteString("outputMode", "file");
            if (!string.IsNullOrWhiteSpace(fileName))
            {
                writer.WriteString("fileName", fileName);
            }

            writer.WriteBoolean("localPathRedacted", true);
            writer.WriteEndObject();
        }

        return JsonDocument.Parse(stream.ToArray()).RootElement.Clone();
    }

    private static bool IsLocalPathProperty(string propertyName) =>
        string.Equals(propertyName, "path", StringComparison.OrdinalIgnoreCase)
        || string.Equals(propertyName, "filePath", StringComparison.OrdinalIgnoreCase)
        || string.Equals(propertyName, "absolutePath", StringComparison.OrdinalIgnoreCase)
        || string.Equals(propertyName, "screenshotPath", StringComparison.OrdinalIgnoreCase);
}
