using System.Text.Json;
using WpfDevTools.Mcp.Server.State;
using WpfDevTools.Shared.ErrorHandling;

namespace WpfDevTools.Mcp.Server.Tools;

public sealed partial class CaptureStateSnapshotTool
{
    private static (IReadOnlyList<string> Values, object? Error) ParsePropertyNameArray(
        JsonElement? arguments,
        string parameterName)
    {
        if (arguments == null ||
            !arguments.Value.TryGetProperty(parameterName, out var property) ||
            property.ValueKind != JsonValueKind.Array)
        {
            return (Array.Empty<string>(), null);
        }

        var rawItemCount = property.GetArrayLength();
        if (rawItemCount > MaxSnapshotPropertyNameCount)
        {
            return (Array.Empty<string>(), BatchItemLimits.CreateInvalidArgumentError(
                parameterName,
                rawItemCount,
                MaxSnapshotPropertyNameCount,
                $"{parameterName} must contain at most {MaxSnapshotPropertyNameCount} items; received {rawItemCount}.",
                $"Split large state snapshot captures into requests with {MaxSnapshotPropertyNameCount} or fewer property names."));
        }

        var values = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in property.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            var rawValue = item.GetString();
            if (rawValue?.Length > MaxSnapshotPropertyNameLength)
            {
                return (Array.Empty<string>(), CreatePropertyNameLengthError(parameterName, rawValue.Length));
            }

            var value = rawValue?.Trim();
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (seen.Add(value))
            {
                values.Add(value);
            }
        }

        return (values, null);
    }

    private static ToolErrorPayload CreatePropertyNameLengthError(string parameterName, int actualLength) =>
        new()
        {
            Error = $"{parameterName} values must be {MaxSnapshotPropertyNameLength} characters or fewer; received {actualLength}.",
            ErrorCode = ToolErrorCode.InvalidArgument.ToString(),
            Hint = "Use exact WPF DependencyProperty or ViewModel property names and split unusually large capture sets into smaller requests.",
            ErrorData = new Dictionary<string, object?>
            {
                ["parameter"] = parameterName,
                ["actualLength"] = actualLength,
                ["maxLength"] = MaxSnapshotPropertyNameLength
            }
        };

    private static bool IsSuccess(JsonElement response) =>
        response.TryGetProperty("success", out var successProperty) && successProperty.GetBoolean();

    private static string? GetOptionalString(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind != JsonValueKind.Null
            ? property.ToString()
            : null;

    private static bool GetOptionalBool(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.True;

    private static bool IsRestorableViewModelValue(string? propertyType, string? propertyValue)
    {
        if (propertyValue != null)
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(propertyType))
        {
            return false;
        }

        return propertyType switch
        {
            "String" or "Boolean" or "Byte" or "SByte" or "Char" or "Decimal" or "Double" or "Single" or
            "Int16" or "Int32" or "Int64" or "UInt16" or "UInt32" or "UInt64" or "DateTime" or "Guid" or
            "TimeSpan" or "Uri" => true,
            _ when propertyType.StartsWith("Nullable<", StringComparison.Ordinal) => true,
            _ => false
        };
    }

    private static string? GetRestoreSkipReason(
        string propertyName,
        bool canWrite,
        string? propertyType,
        string? propertyValue)
    {
        if (!canWrite)
        {
            return $"Property '{propertyName}' is read-only or derived and cannot be restored via modify_viewmodel.";
        }

        if (!IsRestorableViewModelValue(propertyType, propertyValue))
        {
            return $"Property '{propertyName}' is a complex reference with a null snapshot value and cannot be deterministically restored via modify_viewmodel.";
        }

        return null;
    }

    private static string? GetDependencyPropertySkipReason(string propertyName, bool isExpression)
    {
        if (!isExpression)
        {
            return null;
        }

        return $"Property '{propertyName}' is expression-backed and cannot be deterministically restored after a local mutation replaces the expression.";
    }

    private static (bool canRestore, string? skipReason, string? restoreToken, string? expressionKind)
        GetDirectDependencyPropertyRestore(
            string propertyName,
            bool hadLocalValue,
            string? localValue,
            string? localValueType)
    {
        if (!hadLocalValue || localValue == null || IsStringConvertibleLocalValue(localValueType))
        {
            return (true, null, null, null);
        }

        var typeLabel = string.IsNullOrWhiteSpace(localValueType) ? "unknown" : localValueType;
        return (
            false,
            $"Property '{propertyName}' has a complex local value of type '{typeLabel}' and cannot be deterministically restored via set_dp_value.",
            null,
            null);
    }

    private static bool IsStringConvertibleLocalValue(string? localValueType)
    {
        if (string.IsNullOrWhiteSpace(localValueType))
        {
            return true;
        }

        return localValueType switch
        {
            "String" or "Boolean" or "Byte" or "SByte" or "Char" or "Decimal" or "Double" or "Single" or
            "Int16" or "Int32" or "Int64" or "UInt16" or "UInt32" or "UInt64" or "DateTime" or
            "DateTimeOffset" or "Guid" or "TimeSpan" or "Uri" or "Thickness" or "CornerRadius" or
            "GridLength" or "Color" or "SolidColorBrush" or "FontWeight" or "FontStyle" or "FontStretch" or
            "Visibility" => true,
            _ => false
        };
    }

    private async Task<(bool canRestore, string? skipReason, string? restoreToken, string? expressionKind)> TryCaptureDependencyPropertyExpressionRestoreAsync(
        int processId,
        long sessionGeneration,
        string? elementId,
        string propertyName,
        CancellationToken cancellationToken)
    {
        var response = JsonSerializer.SerializeToElement(await SendInspectorRequestAsync(
            processId,
            sessionGeneration,
            "capture_dp_expression_restore",
            new { elementId, propertyName },
            cancellationToken,
            piggybackPendingEvents: false).ConfigureAwait(false));

        if (!IsSuccess(response))
        {
            return (false, $"Property '{propertyName}' is expression-backed but its restore handle could not be captured for this session.", null, null);
        }

        if (!GetOptionalBool(response, "canRestore"))
        {
            return (
                false,
                GetOptionalString(response, "reason") ?? GetDependencyPropertySkipReason(propertyName, isExpression: true),
                null,
                GetOptionalString(response, "expressionKind"));
        }

        return (
            true,
            null,
            GetOptionalString(response, "restoreToken"),
            GetOptionalString(response, "expressionKind"));
    }

    private static ToolErrorPayload CreateStepFailure(string method, string? propertyName, JsonElement response)
    {
        var contextMessage = propertyName == null
            ? $"Failed during {method}."
            : $"Failed during {method} for '{propertyName}'.";

        return ToolRecoveryPayload.CreateStepFailure(
            contextMessage,
            $"Inspect the failing {method} step and re-query the current runtime state before retrying capture_state_snapshot.",
            response);
    }

    private async Task<(
        IReadOnlyList<StoredBindingErrorSnapshot> bindingErrors,
        bool success,
        ToolErrorPayload? error)> TryCaptureBindingErrorBaselineAsync(
        int processId,
        long sessionGeneration,
        CancellationToken cancellationToken)
    {
        var response = JsonSerializer.SerializeToElement(await SendInspectorRequestAsync(
            processId,
            sessionGeneration,
            "get_binding_errors",
            new { },
            cancellationToken,
            piggybackPendingEvents: false).ConfigureAwait(false));

        if (!IsSuccess(response) || !response.TryGetProperty("errors", out var errors) || errors.ValueKind != JsonValueKind.Array)
        {
            var recoveryError = ToolRecoveryPayload.IsTimeoutOrTransportRecovery(response)
                || ToolRecoveryPayload.HasRecoveryGuidance(response)
                ? CreateStepFailure("get_binding_errors", null, response)
                : null;
            return (Array.Empty<StoredBindingErrorSnapshot>(), false, recoveryError);
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

        return (snapshots, true, null);
    }

    private async Task<(
        IReadOnlyList<StoredValidationErrorSnapshot> validationErrors,
        bool success,
        ToolErrorPayload? error)> TryCaptureValidationBaselineAsync(
        int processId,
        long sessionGeneration,
        string? elementId,
        CancellationToken cancellationToken)
    {
        var response = JsonSerializer.SerializeToElement(await SendInspectorRequestAsync(
            processId,
            sessionGeneration,
            "get_validation_errors",
            new { elementId },
            cancellationToken,
            piggybackPendingEvents: false).ConfigureAwait(false));

        if (!IsSuccess(response) || !response.TryGetProperty("errors", out var errors) || errors.ValueKind != JsonValueKind.Array)
        {
            var recoveryError = ToolRecoveryPayload.IsTimeoutOrTransportRecovery(response)
                || ToolRecoveryPayload.HasRecoveryGuidance(response)
                ? CreateStepFailure("get_validation_errors", null, response)
                : null;
            return (Array.Empty<StoredValidationErrorSnapshot>(), false, recoveryError);
        }

        var snapshots = errors.EnumerateArray()
            .Select(error => new StoredValidationErrorSnapshot(
                GetOptionalString(error, "elementType") ?? "Unknown",
                GetOptionalString(error, "elementName"),
                GetOptionalString(error, "errorContent") ?? string.Empty,
                error.TryGetProperty("isRuleError", out var isRuleErrorProperty) && isRuleErrorProperty.GetBoolean(),
                GetOptionalString(error, "ruleType")))
            .ToArray();

        return (snapshots, true, null);
    }
}
