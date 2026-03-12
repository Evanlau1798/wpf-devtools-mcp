using System.Text.Json;
using WpfDevTools.Mcp.Server.Diagnostics;
using WpfDevTools.Mcp.Server.State;
using WpfDevTools.Shared.ErrorHandling;

namespace WpfDevTools.Mcp.Server.Tools;

public sealed class GetStateDiffTool(SessionManager sessionManager) : PipeConnectedToolBase(sessionManager)
{
    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, _, error) = ParseCommonParams(arguments, _sessionManager);
        if (error != null)
        {
            return error;
        }

        var snapshotId = ParseStringParam(arguments, "snapshotId");
        if (string.IsNullOrWhiteSpace(snapshotId))
        {
            return CreateMissingParamError("snapshotId");
        }

        var trigger = ParseStringParam(arguments, "trigger");

        if (!_sessionManager.TryGetStateSnapshot(processId, snapshotId, out var snapshot) || snapshot == null)
        {
            return new ToolErrorPayload
            {
                Error = $"No stored snapshot found for snapshotId '{snapshotId}'.",
                ErrorCode = ToolErrorCode.InvalidArgument.ToString(),
                Hint = "Call capture_state_snapshot first or verify the snapshotId before retrying get_state_diff."
            };
        }

        var dependencyProperties = await GetCurrentDependencyPropertyStatesAsync(processId, snapshot, cancellationToken).ConfigureAwait(false);
        if (dependencyProperties.error != null)
        {
            return dependencyProperties.error;
        }

        var viewModelProperties = await GetCurrentViewModelStatesAsync(processId, snapshot, cancellationToken).ConfigureAwait(false);
        if (viewModelProperties.error != null)
        {
            return viewModelProperties.error;
        }

        var bindingErrors = await GetCurrentBindingErrorsAsync(processId, cancellationToken).ConfigureAwait(false);
        if (bindingErrors.error != null)
        {
            return bindingErrors.error;
        }

        var validationErrors = await GetCurrentValidationErrorsAsync(processId, snapshot.ElementId, cancellationToken).ConfigureAwait(false);
        if (validationErrors.error != null)
        {
            return validationErrors.error;
        }

        var focusState = await GetCurrentFocusStateAsync(processId, snapshot, cancellationToken).ConfigureAwait(false);
        if (focusState.error != null)
        {
            return focusState.error;
        }

        var result = SceneStateDiffCalculator.Calculate(
            snapshot,
            new CurrentSceneState(
                dependencyProperties.states!,
                viewModelProperties.states!,
                focusState.state,
                bindingErrors.errors!,
                validationErrors.errors!),
            trigger,
            DateTimeOffset.UtcNow);

        return new
        {
            success = result.Success,
            snapshotId = result.SnapshotId,
            trigger = result.Trigger,
            durationMs = result.DurationMs,
            propertyChanges = result.PropertyChanges.Select(change => new
            {
                elementId = change.ElementId,
                propertyName = change.PropertyName,
                beforeValue = change.BeforeValue,
                afterValue = change.AfterValue,
                beforeBaseValueSource = change.BeforeBaseValueSource,
                afterBaseValueSource = change.AfterBaseValueSource
            }),
            viewModelChanges = result.ViewModelChanges.Select(change => new
            {
                elementId = change.ElementId,
                propertyName = change.PropertyName,
                beforeValue = change.BeforeValue,
                afterValue = change.AfterValue
            }),
            newBindingErrors = result.NewBindingErrors.Select(errorItem => new
            {
                elementId = errorItem.ElementId,
                suggestedElementId = errorItem.SuggestedElementId,
                matchConfidence = errorItem.MatchConfidence,
                propertyName = errorItem.PropertyName,
                bindingPath = errorItem.BindingPath,
                message = errorItem.Message
            }),
            resolvedBindingErrors = result.ResolvedBindingErrors.Select(errorItem => new
            {
                elementId = errorItem.ElementId,
                suggestedElementId = errorItem.SuggestedElementId,
                matchConfidence = errorItem.MatchConfidence,
                propertyName = errorItem.PropertyName,
                bindingPath = errorItem.BindingPath,
                message = errorItem.Message
            }),
            validationChanges = result.ValidationChanges.Select(change => new
            {
                changeType = change.ChangeType,
                elementType = change.ElementType,
                elementName = change.ElementName,
                errorContent = change.ErrorContent,
                isRuleError = change.IsRuleError,
                ruleType = change.RuleType
            }),
            focusChange = result.FocusChange == null
                ? null
                : new
                {
                    changed = result.FocusChange.Changed,
                    beforeFocusKind = result.FocusChange.BeforeFocusKind,
                    beforeFocusedElementId = result.FocusChange.BeforeFocusedElementId,
                    afterFocusKind = result.FocusChange.AfterFocusKind,
                    afterFocusedElementId = result.FocusChange.AfterFocusedElementId
                }
        };
    }

    private async Task<(IReadOnlyList<CurrentDependencyPropertyState>? states, object? error)> GetCurrentDependencyPropertyStatesAsync(
        int processId,
        StoredStateSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        var states = new List<CurrentDependencyPropertyState>();
        foreach (var property in snapshot.DependencyProperties)
        {
            var response = JsonSerializer.SerializeToElement(await SendInspectorRequestAsync(
                processId,
                "get_dp_value_source",
                new { elementId = property.ElementId, propertyName = property.PropertyName },
                cancellationToken).ConfigureAwait(false));

            if (!IsSuccess(response))
            {
                return (null, CreateStepFailure("get_dp_value_source", property.PropertyName, response));
            }

            states.Add(new CurrentDependencyPropertyState(
                property.ElementId,
                property.PropertyName,
                GetOptionalString(response, "currentValue"),
                GetOptionalString(response, "baseValueSource")));
        }

        return (states, null);
    }

    private async Task<(IReadOnlyList<CurrentViewModelPropertyState>? states, object? error)> GetCurrentViewModelStatesAsync(
        int processId,
        StoredStateSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (snapshot.ViewModelProperties.Count == 0)
        {
            return (Array.Empty<CurrentViewModelPropertyState>(), null);
        }

        var response = JsonSerializer.SerializeToElement(await SendInspectorRequestAsync(
            processId,
            "get_viewmodel",
            new
            {
                elementId = snapshot.ElementId,
                propertyNames = snapshot.ViewModelProperties.Select(item => item.PropertyName).Distinct().ToArray()
            },
            cancellationToken).ConfigureAwait(false));

        if (!IsSuccess(response))
        {
            return (null, CreateStepFailure("get_viewmodel", null, response));
        }

        var states = response.GetProperty("properties")
            .EnumerateArray()
            .Select(property => new CurrentViewModelPropertyState(
                snapshot.ElementId,
                GetOptionalString(property, "name") ?? string.Empty,
                GetOptionalString(property, "value")))
            .ToArray();

        return (states, null);
    }

    private async Task<(IReadOnlyList<StoredBindingErrorSnapshot>? errors, object? error)> GetCurrentBindingErrorsAsync(
        int processId,
        CancellationToken cancellationToken)
    {
        var response = JsonSerializer.SerializeToElement(await SendInspectorRequestAsync(
            processId,
            "get_binding_errors",
            new { },
            cancellationToken).ConfigureAwait(false));

        if (!IsSuccess(response))
        {
            return (null, CreateStepFailure("get_binding_errors", null, response));
        }

        return (ParseBindingErrors(response), null);
    }

    private async Task<(IReadOnlyList<StoredValidationErrorSnapshot>? errors, object? error)> GetCurrentValidationErrorsAsync(
        int processId,
        string? elementId,
        CancellationToken cancellationToken)
    {
        var response = JsonSerializer.SerializeToElement(await SendInspectorRequestAsync(
            processId,
            "get_validation_errors",
            new { elementId },
            cancellationToken).ConfigureAwait(false));

        if (!IsSuccess(response))
        {
            return (null, CreateStepFailure("get_validation_errors", null, response));
        }

        return (ParseValidationErrors(response), null);
    }

    private async Task<(CurrentFocusState? state, object? error)> GetCurrentFocusStateAsync(
        int processId,
        StoredStateSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (snapshot.Focus == null)
        {
            return (null, null);
        }

        var response = JsonSerializer.SerializeToElement(await SendInspectorRequestAsync(
            processId,
            "get_focus_state",
            new { elementId = snapshot.ElementId },
            cancellationToken).ConfigureAwait(false));

        if (!IsSuccess(response))
        {
            return (null, CreateStepFailure("get_focus_state", null, response));
        }

        return (new CurrentFocusState(
            GetOptionalString(response, "focusKind"),
            GetOptionalString(response, "focusedElementId")), null);
    }

    private static IReadOnlyList<StoredBindingErrorSnapshot> ParseBindingErrors(JsonElement response) =>
        response.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array
            ? errors.EnumerateArray()
                .Select(error => new StoredBindingErrorSnapshot(
                    GetOptionalString(error, "elementId"),
                    GetOptionalString(error, "suggestedElementId"),
                    GetOptionalString(error, "matchConfidence"),
                    GetOptionalString(error, "propertyName"),
                    GetOptionalString(error, "bindingPath"),
                    GetOptionalString(error, "message")))
                .ToArray()
            : Array.Empty<StoredBindingErrorSnapshot>();

    private static IReadOnlyList<StoredValidationErrorSnapshot> ParseValidationErrors(JsonElement response) =>
        response.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array
            ? errors.EnumerateArray()
                .Select(error => new StoredValidationErrorSnapshot(
                    GetOptionalString(error, "elementType") ?? "Unknown",
                    GetOptionalString(error, "elementName"),
                    GetOptionalString(error, "errorContent") ?? string.Empty,
                    error.TryGetProperty("isRuleError", out var isRuleErrorProperty) && isRuleErrorProperty.GetBoolean(),
                    GetOptionalString(error, "ruleType")))
                .ToArray()
            : Array.Empty<StoredValidationErrorSnapshot>();

    private static bool IsSuccess(JsonElement response) =>
        response.TryGetProperty("success", out var successProperty) && successProperty.GetBoolean();

    private static string? GetOptionalString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind != JsonValueKind.Null
            ? property.ToString()
            : null;

    private static ToolErrorPayload CreateStepFailure(string method, string? subject, JsonElement response)
    {
        var context = string.IsNullOrWhiteSpace(subject)
            ? $"Failed during {method} while computing state diff."
            : $"Failed during {method} for '{subject}' while computing state diff.";

        return new ToolErrorPayload
        {
            Error = response.TryGetProperty("error", out var errorProperty)
                ? $"{context} {errorProperty.GetString()}".Trim()
                : context,
            ErrorCode = response.TryGetProperty("errorCode", out var errorCodeProperty)
                ? errorCodeProperty.GetString() ?? ToolErrorCode.OperationFailed.ToString()
                : ToolErrorCode.OperationFailed.ToString(),
            Hint = response.TryGetProperty("hint", out var hintProperty)
                ? hintProperty.GetString()
                : $"Inspect the failing {method} step and retry get_state_diff after refreshing the runtime state.",
            ErrorData = response.Clone()
        };
    }
}
