using System.Text.Json;
using WpfDevTools.Mcp.Server.State;
using WpfDevTools.Shared.ErrorHandling;

namespace WpfDevTools.Mcp.Server.Tools;

public sealed class CaptureStateSnapshotTool(SessionManager sessionManager) : PipeConnectedToolBase(sessionManager)
{
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments, _sessionManager);
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
                    return new ToolErrorPayload
                    {
                        Error = $"ViewModel property '{propertyName}' was not found in the current DataContext.",
                        ErrorCode = ToolErrorCode.PropertyNotFound.ToString(),
                        Hint = "Call get_viewmodel first to inspect the available propertyName values on the current DataContext."
                    };
                }

                var canRestore = !property.TryGetProperty("canWrite", out var canWriteProperty) ||
                    (canWriteProperty.ValueKind == JsonValueKind.True);
                viewModelProperties.Add(new StoredViewModelPropertySnapshot(
                    elementId,
                    propertyName,
                    GetOptionalString(property, "type"),
                    GetOptionalString(property, "value"),
                    canRestore,
                    canRestore ? null : $"Property '{propertyName}' is read-only or derived and cannot be restored via modify_viewmodel."));
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

        var (bindingErrors, hasBindingErrorBaseline) = await TryCaptureBindingErrorBaselineAsync(
            processId,
            cancellationToken).ConfigureAwait(false);
        var (validationErrors, hasValidationBaseline) = await TryCaptureValidationBaselineAsync(
            processId,
            elementId,
            cancellationToken).ConfigureAwait(false);

        var snapshotId = $"snapshot_{Guid.NewGuid():N}";
        _sessionManager.SaveStateSnapshot(processId, new StoredStateSnapshot(
            snapshotId,
            snapshotName,
            elementId,
            dependencyProperties,
            viewModelProperties,
            focus,
            bindingErrors,
            hasBindingErrorBaseline,
            validationErrors,
            hasValidationBaseline,
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

    private static ToolErrorPayload CreateStepFailure(string method, string? propertyName, JsonElement response)
    {
        var contextMessage = propertyName == null
            ? $"Failed during {method}."
            : $"Failed during {method} for '{propertyName}'.";

        var originalError = response.TryGetProperty("error", out var errorProperty)
            ? errorProperty.GetString()
            : null;
        var errorCode = response.TryGetProperty("errorCode", out var errorCodeProperty)
            ? errorCodeProperty.GetString()
            : null;
        var hint = response.TryGetProperty("hint", out var hintProperty)
            ? hintProperty.GetString()
            : null;

        return new ToolErrorPayload
        {
            Error = originalError is { Length: > 0 }
                ? $"{contextMessage} {originalError}"
                : contextMessage,
            ErrorCode = string.IsNullOrWhiteSpace(errorCode)
                ? ToolErrorCode.OperationFailed.ToString()
                : errorCode!,
            Hint = hint ?? $"Inspect the failing {method} step and re-query the current runtime state before retrying capture_state_snapshot.",
            ErrorData = response.Clone()
        };
    }

    private async Task<(IReadOnlyList<StoredBindingErrorSnapshot> bindingErrors, bool success)> TryCaptureBindingErrorBaselineAsync(
        int processId,
        CancellationToken cancellationToken)
    {
        var response = JsonSerializer.SerializeToElement(await SendInspectorRequestAsync(
            processId,
            "get_binding_errors",
            new { },
            cancellationToken).ConfigureAwait(false));

        if (!IsSuccess(response) || !response.TryGetProperty("errors", out var errors) || errors.ValueKind != JsonValueKind.Array)
        {
            return (Array.Empty<StoredBindingErrorSnapshot>(), false);
        }

        var snapshots = errors.EnumerateArray()
            .Select(error => new StoredBindingErrorSnapshot(
                GetOptionalString(error, "elementId"),
                GetOptionalString(error, "suggestedElementId"),
                GetOptionalString(error, "matchConfidence"),
                GetOptionalString(error, "propertyName"),
                GetOptionalString(error, "bindingPath"),
                GetOptionalString(error, "message")))
            .ToArray();

        return (snapshots, true);
    }

    private async Task<(IReadOnlyList<StoredValidationErrorSnapshot> validationErrors, bool success)> TryCaptureValidationBaselineAsync(
        int processId,
        string? elementId,
        CancellationToken cancellationToken)
    {
        var response = JsonSerializer.SerializeToElement(await SendInspectorRequestAsync(
            processId,
            "get_validation_errors",
            new { elementId },
            cancellationToken).ConfigureAwait(false));

        if (!IsSuccess(response) || !response.TryGetProperty("errors", out var errors) || errors.ValueKind != JsonValueKind.Array)
        {
            return (Array.Empty<StoredValidationErrorSnapshot>(), false);
        }

        var snapshots = errors.EnumerateArray()
            .Select(error => new StoredValidationErrorSnapshot(
                GetOptionalString(error, "elementType") ?? "Unknown",
                GetOptionalString(error, "elementName"),
                GetOptionalString(error, "errorContent") ?? string.Empty,
                error.TryGetProperty("isRuleError", out var isRuleErrorProperty) && isRuleErrorProperty.GetBoolean(),
                GetOptionalString(error, "ruleType")))
            .ToArray();

        return (snapshots, true);
    }
}
