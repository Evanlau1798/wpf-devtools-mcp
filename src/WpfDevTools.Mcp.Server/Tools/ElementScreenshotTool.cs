using System.Text.Json;
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
            : result;
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
                if (IsLocalPathProperty(property.Name))
                {
                    continue;
                }

                if (string.Equals(property.Name, "outputMode", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                property.WriteTo(writer);
            }

            writer.WriteString("outputMode", "file");
            if (screenshot is not null)
            {
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
}
