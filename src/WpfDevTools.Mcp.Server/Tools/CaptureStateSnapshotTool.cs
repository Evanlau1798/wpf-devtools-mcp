using System.Text.Json;
using WpfDevTools.Mcp.Server.State;
using WpfDevTools.Shared.ErrorHandling;

namespace WpfDevTools.Mcp.Server.Tools;

public sealed partial class CaptureStateSnapshotTool(SessionManager sessionManager) : PipeConnectedToolBase(sessionManager)
{
    internal const int MaxSnapshotPropertyNameCount = BatchItemLimits.MaxQueryInputItems;
    internal const int MaxSnapshotPropertyNameLength = 256;

    public async Task<object> ExecuteAsync(JsonElement? arguments, CancellationToken cancellationToken)
    {
        var (processId, elementId, error) = ParseCommonParams(arguments, _sessionManager);
        if (error != null)
        {
            return error;
        }

        var propertyNames = ParsePropertyNameArray(arguments, "propertyNames");
        if (propertyNames.Error != null)
        {
            return propertyNames.Error;
        }

        var viewModelPropertyNames = ParsePropertyNameArray(arguments, "viewModelPropertyNames");
        if (viewModelPropertyNames.Error != null)
        {
            return viewModelPropertyNames.Error;
        }

        var includeFocus = ParseBoolParam(arguments, "includeFocus") ?? false;
        var snapshotName = ParseStringParam(arguments, "snapshotName");

        if (propertyNames.Values.Count == 0 && viewModelPropertyNames.Values.Count == 0 && !includeFocus)
        {
            return CreateMissingParamError("propertyNames / viewModelPropertyNames / includeFocus");
        }

        if (!_sessionManager.TryGetSessionGeneration(processId, out var sessionGeneration))
        {
            return CreateNotConnectedError(processId);
        }

        var dependencyProperties = new List<StoredDependencyPropertySnapshot>();
        var skippedDependencyProperties = new List<object>();
        ToolErrorPayload? firstDependencyPropertyFailure = null;
        foreach (var propertyName in propertyNames.Values)
        {
            var response = JsonSerializer.SerializeToElement(await SendInspectorRequestAsync(
                processId,
                sessionGeneration,
                "get_dp_value_source",
                new { elementId, propertyName },
                cancellationToken,
                piggybackPendingEvents: false).ConfigureAwait(false));

            if (!IsSuccess(response))
            {
                firstDependencyPropertyFailure ??= CreateStepFailure("get_dp_value_source", propertyName, response);
                skippedDependencyProperties.Add(CreateSkippedDependencyPropertyCapture(elementId, propertyName, response));
                continue;
            }

            var isExpression = GetOptionalBool(response, "isExpression");
            var hadLocalValue = response.GetProperty("hadLocalValue").GetBoolean();
            var localValue = GetOptionalString(response, "localValue");
            var currentValue = GetOptionalString(response, "currentValue");
            var baseValueSource = GetOptionalString(response, "baseValueSource");
            var localValueType = GetOptionalString(response, "localValueType");
            var expressionRestore = isExpression
                ? await TryCaptureDependencyPropertyExpressionRestoreAsync(
                    processId,
                    sessionGeneration,
                    elementId,
                    propertyName,
                    cancellationToken).ConfigureAwait(false)
                : GetDirectDependencyPropertyRestore(propertyName, hadLocalValue, localValue, localValueType);
            var canRestore = expressionRestore.canRestore;
            dependencyProperties.Add(new StoredDependencyPropertySnapshot(
                elementId,
                propertyName,
                hadLocalValue,
                localValue,
                currentValue,
                baseValueSource,
                isExpression,
                canRestore,
                canRestore ? null : expressionRestore.skipReason ?? GetDependencyPropertySkipReason(propertyName, isExpression),
                expressionRestore.restoreToken,
                expressionRestore.expressionKind));
        }

        var viewModelProperties = new List<StoredViewModelPropertySnapshot>();
        if (viewModelPropertyNames.Values.Count > 0)
        {
            var response = JsonSerializer.SerializeToElement(await SendInspectorRequestAsync(
                processId,
                sessionGeneration,
                "get_viewmodel",
                new { elementId },
                cancellationToken,
                piggybackPendingEvents: false).ConfigureAwait(false));

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

            foreach (var propertyName in viewModelPropertyNames.Values)
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

                var canWrite = !property.TryGetProperty("canWrite", out var canWriteProperty) ||
                    (canWriteProperty.ValueKind == JsonValueKind.True);
                var propertyType = GetOptionalString(property, "type");
                var propertyValue = GetOptionalString(property, "value");
                var canRestore = canWrite && IsRestorableViewModelValue(propertyType, propertyValue);
                viewModelProperties.Add(new StoredViewModelPropertySnapshot(
                    elementId,
                    propertyName,
                    propertyType,
                    propertyValue,
                    canRestore,
                    GetRestoreSkipReason(propertyName, canWrite, propertyType, propertyValue)));
            }
        }

        StoredFocusSnapshot? focus = null;
        if (includeFocus)
        {
            var response = JsonSerializer.SerializeToElement(await SendInspectorRequestAsync(
                processId,
                sessionGeneration,
                "get_focus_state",
                new { elementId },
                cancellationToken,
                piggybackPendingEvents: false).ConfigureAwait(false));

            if (!IsSuccess(response))
            {
                return CreateStepFailure("get_focus_state", null, response);
            }

            focus = new StoredFocusSnapshot(
                GetOptionalString(response, "focusKind"),
                GetOptionalString(response, "focusedElementId"));
        }

        if (dependencyProperties.Count == 0 &&
            viewModelProperties.Count == 0 &&
            focus == null &&
            firstDependencyPropertyFailure != null)
        {
            return firstDependencyPropertyFailure;
        }

        var (bindingErrors, hasBindingErrorBaseline, bindingBaselineError) = await TryCaptureBindingErrorBaselineAsync(
            processId,
            sessionGeneration,
            cancellationToken).ConfigureAwait(false);
        if (bindingBaselineError != null)
        {
            return bindingBaselineError;
        }

        var (validationErrors, hasValidationBaseline, validationBaselineError) = await TryCaptureValidationBaselineAsync(
            processId,
            sessionGeneration,
            elementId,
            cancellationToken).ConfigureAwait(false);
        if (validationBaselineError != null)
        {
            return validationBaselineError;
        }

        var warnings = new List<string>();
        if (!hasBindingErrorBaseline)
        {
            warnings.Add("Could not capture get_binding_errors baseline; get_state_diff will omit binding error additions and resolutions for this snapshot.");
        }

        if (!hasValidationBaseline)
        {
            warnings.Add("Could not capture get_validation_errors baseline; get_state_diff will omit validation changes for this snapshot.");
        }

        if (skippedDependencyProperties.Count > 0)
        {
            warnings.Add($"Skipped {skippedDependencyProperties.Count} DependencyProperty capture request(s); inspect skippedDependencyProperties and call get_dp_value_source with exact propertyName values before retrying.");
        }

        var snapshotId = $"snapshot_{Guid.NewGuid():N}";
        var snapshot = new StoredStateSnapshot(
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
            DateTimeOffset.UtcNow,
            sessionGeneration);
        if (!_sessionManager.SaveStateSnapshot(processId, snapshot, sessionGeneration))
        {
            return CreateNotConnectedError(processId);
        }

        if (!_sessionManager.TrySetActiveSnapshotId(processId, snapshotId, sessionGeneration))
        {
            _sessionManager.RemoveStateSnapshot(processId, snapshotId);
            return CreateNotConnectedError(processId);
        }

        return new
        {
            success = true,
            snapshotId,
            snapshotName,
            snapshotSummary = new
            {
                dependencyPropertyCount = dependencyProperties.Count,
                restorableDependencyPropertyCount = dependencyProperties.Count(snapshot => snapshot.CanRestore),
                skippedDependencyPropertyCount = dependencyProperties.Count(snapshot => !snapshot.CanRestore) + skippedDependencyProperties.Count,
                viewModelPropertyCount = viewModelProperties.Count,
                capturedFocus = focus != null
            },
            snapshotCompleteness = new
            {
                bindingErrorBaselineCaptured = hasBindingErrorBaseline,
                validationBaselineCaptured = hasValidationBaseline
            },
            skippedDependencyProperties,
            warnings
        };
    }

}
