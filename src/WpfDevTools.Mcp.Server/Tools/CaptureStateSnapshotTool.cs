using System.Text.Json;
using WpfDevTools.Mcp.Server.State;

namespace WpfDevTools.Mcp.Server.Tools;

public sealed class CaptureStateSnapshotTool(SessionManager sessionManager) : PipeConnectedToolBase(sessionManager)
{
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments);
        if (error != null)
        {
            return error;
        }

        var propertyNames = ParseStringArray(arguments, "propertyNames");
        var viewModelPropertyNames = ParseStringArray(arguments, "viewModelPropertyNames");
        var includeFocus = ParseBoolParam(arguments, "includeFocus") ?? false;
        var snapshotName = ParseStringParam(arguments, "snapshotName");

        if (propertyNames.Count == 0 && viewModelPropertyNames.Count == 0 && !includeFocus)
        {
            return CreateMissingParamError("propertyNames / viewModelPropertyNames / includeFocus");
        }

        var dependencyProperties = new List<StoredDependencyPropertySnapshot>();
        foreach (var propertyName in propertyNames)
        {
            var response = JsonSerializer.SerializeToElement(await SendInspectorRequestAsync(
                processId,
                "get_dp_value_source",
                new { elementId, propertyName },
                cancellationToken).ConfigureAwait(false));

            if (!IsSuccess(response))
            {
                return CreateStepFailure("get_dp_value_source", propertyName, response);
            }

            dependencyProperties.Add(new StoredDependencyPropertySnapshot(
                elementId,
                propertyName,
                response.GetProperty("hadLocalValue").GetBoolean(),
                GetOptionalString(response, "localValue"),
                GetOptionalString(response, "currentValue"),
                GetOptionalString(response, "baseValueSource")));
        }

        var viewModelProperties = new List<StoredViewModelPropertySnapshot>();
        if (viewModelPropertyNames.Count > 0)
        {
            var response = JsonSerializer.SerializeToElement(await SendInspectorRequestAsync(
                processId,
                "get_viewmodel",
                new { elementId },
                cancellationToken).ConfigureAwait(false));

            if (!IsSuccess(response))
            {
                return CreateStepFailure("get_viewmodel", null, response);
            }

            var availableProperties = response.GetProperty("properties")
                .EnumerateArray()
                .ToDictionary(
                    item => item.GetProperty("name").GetString() ?? string.Empty,
                    item => item,
                    StringComparer.Ordinal);

            foreach (var propertyName in viewModelPropertyNames)
            {
                if (!availableProperties.TryGetValue(propertyName, out var property))
                {
                    return new { success = false, error = $"ViewModel property '{propertyName}' was not found in the current DataContext." };
                }

                viewModelProperties.Add(new StoredViewModelPropertySnapshot(
                    elementId,
                    propertyName,
                    GetOptionalString(property, "type"),
                    GetOptionalString(property, "value")));
            }
        }

        StoredFocusSnapshot? focus = null;
        if (includeFocus)
        {
            var response = JsonSerializer.SerializeToElement(await SendInspectorRequestAsync(
                processId,
                "get_focus_state",
                new { elementId },
                cancellationToken).ConfigureAwait(false));

            if (!IsSuccess(response))
            {
                return CreateStepFailure("get_focus_state", null, response);
            }

            focus = new StoredFocusSnapshot(
                GetOptionalString(response, "focusKind"),
                GetOptionalString(response, "focusedElementId"));
        }

        var snapshotId = $"snapshot_{Guid.NewGuid():N}";
        _sessionManager.SaveStateSnapshot(processId, new StoredStateSnapshot(
            snapshotId,
            snapshotName,
            elementId,
            dependencyProperties,
            viewModelProperties,
            focus,
            DateTimeOffset.UtcNow));

        return new
        {
            success = true,
            snapshotId,
            snapshotName,
            snapshotSummary = new
            {
                dependencyPropertyCount = dependencyProperties.Count,
                viewModelPropertyCount = viewModelProperties.Count,
                capturedFocus = focus != null
            }
        };
    }

    private static List<string> ParseStringArray(JsonElement? arguments, string propertyName)
    {
        if (arguments == null ||
            !arguments.Value.TryGetProperty(propertyName, out var property) ||
            property.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return property.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToList();
    }

    private static bool IsSuccess(JsonElement response) =>
        response.TryGetProperty("success", out var successProperty) && successProperty.GetBoolean();

    private static string? GetOptionalString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind != JsonValueKind.Null
            ? property.ToString()
            : null;

    private static object CreateStepFailure(string method, string? propertyName, JsonElement response) =>
        new
        {
            success = false,
            error = propertyName == null
                ? $"Failed during {method}."
                : $"Failed during {method} for '{propertyName}'.",
            details = response
        };
}
