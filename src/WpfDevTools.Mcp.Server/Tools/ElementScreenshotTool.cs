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
    /// <returns>Tool result containing screenshot metadata or error</returns>
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

        var screenshotDirectory = string.Equals(outputMode, "file", StringComparison.Ordinal)
            ? _sessionManager.GetOrCreateScreenshotStorageRoot(processId)
            : null;
        var result = await SendInspectorRequestAsync(processId, "element_screenshot",
            new
            {
                elementId,
                outputMode,
                maxWidth,
                maxHeight,
                screenshotDirectory
            }, cancellationToken);

        return outputMode == "file"
            ? RedactFilePathFromScreenshotResult(processId, result)
            : result;
    }

    private object RedactFilePathFromScreenshotResult(int processId, object result)
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
            string? path = null;
            string? screenshotId = null;
            string? sha256 = null;
            foreach (var property in payload.EnumerateObject())
            {
                if (IsLocalPathProperty(property.Name))
                {
                    if (property.Value.ValueKind == JsonValueKind.String)
                    {
                        path = property.Value.GetString();
                        fileName ??= Path.GetFileName(path);
                    }

                    continue;
                }

                if (string.Equals(property.Name, "outputMode", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.Equals(property.Name, "screenshotId", StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.String)
                {
                    screenshotId = property.Value.GetString();
                }

                if (string.Equals(property.Name, "sha256", StringComparison.OrdinalIgnoreCase) &&
                    property.Value.ValueKind == JsonValueKind.String)
                {
                    sha256 = property.Value.GetString();
                }

                property.WriteTo(writer);
            }

            writer.WriteString("outputMode", "file");
            if (!string.IsNullOrWhiteSpace(path) &&
                !string.IsNullOrWhiteSpace(screenshotId))
            {
                var screenshot = _sessionManager.RegisterScreenshotResource(processId, screenshotId, path, sha256);
                writer.WriteString("resourceUri", screenshot.ResourceUri);
                writer.WriteString("expiresAtUtc", screenshot.ExpiresAtUtc);
            }

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
