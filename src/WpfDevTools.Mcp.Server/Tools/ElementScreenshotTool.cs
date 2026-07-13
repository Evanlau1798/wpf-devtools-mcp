using System.Text.Json;
using WpfDevTools.Mcp.Server.McpResources;
using WpfDevTools.Shared.ErrorHandling;

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

        var fileOutputMode = string.Equals(outputMode, "file", StringComparison.Ordinal);
        long? sessionGeneration = null;
        if (fileOutputMode)
        {
            if (!_sessionManager.TryGetSessionGeneration(processId, out var currentGeneration))
            {
                return CreateNotConnectedError(processId);
            }

            sessionGeneration = currentGeneration;
        }

        string? screenshotDirectory = null;
        if (fileOutputMode)
        {
            try
            {
                screenshotDirectory = _sessionManager.GetOrCreateScreenshotStorageRoot(
                    processId,
                    sessionGeneration!.Value);
            }
            catch (InvalidOperationException)
            {
                return CreateNotConnectedError(processId);
            }
        }

        var result = fileOutputMode
            ? await SendInspectorRequestAsync(
                processId,
                sessionGeneration!.Value,
                "element_screenshot",
                new
                {
                    elementId,
                    outputMode,
                    maxWidth,
                    maxHeight,
                    screenshotDirectory
                },
                cancellationToken).ConfigureAwait(false)
            : await SendInspectorRequestAsync(processId, "element_screenshot",
            new
            {
                elementId,
                outputMode,
                maxWidth,
                maxHeight,
                screenshotDirectory
            }, cancellationToken).ConfigureAwait(false);

        return fileOutputMode
            ? RedactFilePathFromScreenshotResult(
                processId,
                sessionGeneration!.Value,
                screenshotDirectory!,
                result)
            : AddMetadataModePixelEvidenceStep(processId, elementId, outputMode, maxWidth, maxHeight, result);
    }

    private static object AddMetadataModePixelEvidenceStep(
        int processId,
        string? elementId,
        string outputMode,
        int? maxWidth,
        int? maxHeight,
        object result)
    {
        if (!string.Equals(outputMode, "metadata", StringComparison.Ordinal))
        {
            return result;
        }

        var payload = result is JsonElement jsonElement
            ? jsonElement
            : JsonSerializer.SerializeToElement(result);
        if (payload.ValueKind != JsonValueKind.Object ||
            (payload.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.False) ||
            payload.TryGetProperty("nextSteps", out _))
        {
            return result;
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in payload.EnumerateObject())
            {
                property.WriteTo(writer);
            }

            WriteFileModeNextStep(writer, processId, elementId, maxWidth, maxHeight);
            writer.WriteEndObject();
        }

        return JsonDocument.Parse(stream.ToArray()).RootElement.Clone();
    }

    private static void WriteFileModeNextStep(
        Utf8JsonWriter writer,
        int processId,
        string? elementId,
        int? maxWidth,
        int? maxHeight)
    {
        writer.WritePropertyName("nextSteps");
        writer.WriteStartArray();
        writer.WriteStartObject();
        writer.WriteString("tool", "element_screenshot");
        writer.WritePropertyName("params");
        writer.WriteStartObject();
        writer.WriteNumber("processId", processId);
        if (!string.IsNullOrWhiteSpace(elementId))
        {
            writer.WriteString("elementId", elementId);
        }

        writer.WriteString("outputMode", "file");
        if (maxWidth.HasValue)
        {
            writer.WriteNumber("maxWidth", maxWidth.Value);
        }

        if (maxHeight.HasValue)
        {
            writer.WriteNumber("maxHeight", maxHeight.Value);
        }

        writer.WriteEndObject();
        writer.WriteString("reason", "Request file mode when pixel evidence or image bytes are required; metadata mode reports dimensions and render metadata only.");
        writer.WriteEndObject();
        writer.WriteEndArray();
    }

    private object RedactFilePathFromScreenshotResult(
        int processId,
        long sessionGeneration,
        string screenshotDirectory,
        object result)
    {
        var payload = result is JsonElement jsonElement
            ? jsonElement
            : JsonSerializer.SerializeToElement(result);
        if (payload.ValueKind != JsonValueKind.Object)
        {
            return CreateUnregisteredScreenshotError(processId);
        }

        if (payload.TryGetProperty("success", out var success) &&
            success.ValueKind == JsonValueKind.False)
        {
            return result;
        }

        string? fileName = null;
        string? path = null;
        string? screenshotId = null;
        string? sha256 = null;
        foreach (var property in payload.EnumerateObject())
        {
            if (IsLocalPathProperty(property.Name) &&
                property.Value.ValueKind == JsonValueKind.String)
            {
                path = property.Value.GetString();
                fileName ??= Path.GetFileName(path);
                continue;
            }

            if (string.Equals(property.Name, "screenshotId", StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.String)
            {
                screenshotId = property.Value.GetString();
                continue;
            }

            if (string.Equals(property.Name, "sha256", StringComparison.OrdinalIgnoreCase) &&
                property.Value.ValueKind == JsonValueKind.String)
            {
                sha256 = property.Value.GetString();
            }
        }

        StoredScreenshotResource? screenshot = null;
        if (string.IsNullOrWhiteSpace(path))
        {
            return CreateUnregisteredScreenshotError(processId);
        }

        if (!string.IsNullOrWhiteSpace(path))
        {
            try
            {
                if (string.IsNullOrWhiteSpace(screenshotId))
                {
                    throw new ArgumentException("File-mode screenshot responses must include a screenshotId.");
                }

                screenshot = _sessionManager.RegisterScreenshotResource(
                    processId,
                    sessionGeneration,
                    screenshotId,
                    path,
                    sha256);
            }
            catch (ArgumentException)
            {
                _sessionManager.TryDeleteUnregisteredScreenshotFile(processId, path, screenshotDirectory);
                return CreateUnregisteredScreenshotError(processId);
            }
            catch (InvalidOperationException)
            {
                _sessionManager.TryDeleteUnregisteredScreenshotFile(processId, path, screenshotDirectory);
                return CreateUnregisteredScreenshotError(processId);
            }
            catch (IOException)
            {
                _sessionManager.TryDeleteUnregisteredScreenshotFile(processId, path, screenshotDirectory);
                return CreateUnregisteredScreenshotError(processId);
            }
            catch (UnauthorizedAccessException)
            {
                _sessionManager.TryDeleteUnregisteredScreenshotFile(processId, path, screenshotDirectory);
                return CreateUnregisteredScreenshotError(processId);
            }
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var property in payload.EnumerateObject())
            {
                if (IsLocalPathProperty(property.Name)
                    || IsServerOwnedFileOutputProperty(property.Name))
                {
                    continue;
                }

                property.WriteTo(writer);
            }

            writer.WriteString("outputMode", "file");
            if (screenshot is not null)
            {
                writer.WriteString("resourceUri", screenshot.ResourceUri);
                writer.WritePropertyName("resourceRead");
                writer.WriteStartObject();
                writer.WriteString("method", "resources/read");
                writer.WritePropertyName("params");
                writer.WriteStartObject();
                writer.WriteString("uri", screenshot.ResourceUri);
                writer.WriteEndObject();
                writer.WriteBoolean("sameSessionRequired", true);
                writer.WritePropertyName("chunking");
                writer.WriteStartObject();
                writer.WriteString(
                    "uriTemplate",
                    $"wpf://screenshots/{screenshot.ScreenshotId}/chunks/{{offset}}/{{length}}");
                writer.WriteNumber("maxChunkBytes", ScreenshotResources.MaxChunkBytes);
                writer.WriteString(
                    "assembly",
                    "Read decoded chunks in offset order until byteLength, concatenate them, then verify the screenshot sha256.");
                writer.WriteEndObject();
                writer.WriteEndObject();
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

    private static ToolErrorPayload CreateUnregisteredScreenshotError(int processId) =>
        new()
        {
            Error = "Inspector returned a file-mode screenshot that could not be registered as a server-owned resource.",
            ErrorCode = ToolErrorCode.SecurityError.ToString(),
            Hint = "Retry the screenshot request. If this repeats, reconnect to the target process before retrying.",
            ProcessId = processId
        };

    private static bool IsLocalPathProperty(string propertyName) =>
        string.Equals(propertyName, "path", StringComparison.OrdinalIgnoreCase)
        || string.Equals(propertyName, "filePath", StringComparison.OrdinalIgnoreCase)
        || string.Equals(propertyName, "absolutePath", StringComparison.OrdinalIgnoreCase)
        || string.Equals(propertyName, "screenshotPath", StringComparison.OrdinalIgnoreCase);

    private static bool IsServerOwnedFileOutputProperty(string propertyName) =>
        string.Equals(propertyName, "outputMode", StringComparison.OrdinalIgnoreCase)
        || string.Equals(propertyName, "resourceUri", StringComparison.OrdinalIgnoreCase)
        || string.Equals(propertyName, "resourceRead", StringComparison.OrdinalIgnoreCase)
        || string.Equals(propertyName, "expiresAtUtc", StringComparison.OrdinalIgnoreCase)
        || string.Equals(propertyName, "fileName", StringComparison.OrdinalIgnoreCase)
        || string.Equals(propertyName, "localPathRedacted", StringComparison.OrdinalIgnoreCase);
}
