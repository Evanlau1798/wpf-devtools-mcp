using System.Text.Json;
using WpfDevTools.Mcp.Server.Navigation.ContextRefs;
using WpfDevTools.Mcp.Server.Schema;

namespace WpfDevTools.Mcp.Server.Navigation.Rules;

internal static class BindingNavigationRules
{
    public static void Register(ToolNavigationRegistry registry)
    {
        registry.Register("get_binding_errors", BuildBindingErrors);
        registry.Register("get_binding_mismatches", BuildBindingMismatches);
        registry.Register("get_validation_errors", BuildValidationErrors);
    }

    private static ToolNavigationEnvelope BuildBindingErrors(ToolNavigationContext context)
    {
        if (!TryGetArray(context.Payload, "errors", out var errors))
        {
            return ToolNavigationEnvelope.Empty;
        }

        var recommended = new List<ToolNextStep>();
        var alternatives = new List<ToolNextStep>();
        ToolNavigationReference? contextRef = null;
        foreach (var error in errors.EnumerateArray())
        {
            if (!TryGetDiagnosticElementId(error, out var elementId))
            {
                continue;
            }

            if (IsPathMismatch(error))
            {
                AddUnique(recommended, CreateDiagnostic(
                    "get_datacontext_chain",
                    1,
                    "Inspect the DataContext inheritance for the failing element.",
                    ("elementId", elementId)));
                AddUnique(alternatives, CreateDiagnostic(
                    "get_bindings",
                    2,
                    "Inspect the binding declaration on the failing element.",
                    ("elementId", elementId)));
                contextRef ??= BindingIssueContextRefBuilder.TryBuild(error, "PathMismatch");
            }

            if (IsConverterOrUpdateFailure(error) && TryGetString(error, "propertyName", out var propertyName))
            {
                AddUnique(recommended, CreateDiagnostic(
                    "get_binding_value_chain",
                    1,
                    "Trace the binding value flow for the failing property.",
                    ("elementId", elementId),
                    ("propertyName", propertyName)));
                contextRef ??= BindingIssueContextRefBuilder.TryBuild(error, "ConverterOrUpdateFailure");
            }
        }

        return ToolNavigationEnvelope.FromRecommended(
            recommended,
            alternatives,
            NavigationLoadHint.ToolNames(alternatives.Select(step => step.Tool).ToArray()),
            contextRef is null ? [] : [contextRef]);
    }

    private static ToolNavigationEnvelope BuildBindingMismatches(ToolNavigationContext context)
    {
        if (!TryGetArray(context.Payload, "mismatches", out var mismatches))
        {
            return ToolNavigationEnvelope.Empty;
        }

        var steps = new List<ToolNextStep>();
        ToolNavigationReference? contextRef = null;
        foreach (var mismatch in mismatches.EnumerateArray())
        {
            if (!TryGetString(mismatch, "elementId", out var elementId))
            {
                continue;
            }

            if (!TryGetString(mismatch, "diagnosis", out var diagnosis))
            {
                continue;
            }

            if (string.Equals(diagnosis, "PathMismatch", StringComparison.Ordinal))
            {
                AddUnique(steps, CreateDiagnostic(
                    "get_bindings",
                    1,
                    "Inspect the binding declaration for the mismatched path.",
                    ("elementId", elementId)));
                contextRef ??= BindingIssueContextRefBuilder.TryBuild(mismatch, diagnosis);
                continue;
            }

            if (!TryGetString(mismatch, "propertyName", out var propertyName))
            {
                continue;
            }

            if (string.Equals(diagnosis, "TypeMismatch", StringComparison.Ordinal)
                || string.Equals(diagnosis, "TypeMismatchWithConverter", StringComparison.Ordinal))
            {
                AddUnique(steps, CreateDiagnostic(
                    "get_dp_value_source",
                    1,
                    "Inspect the target dependency property value source.",
                    ("elementId", elementId),
                    ("propertyName", propertyName)));
                contextRef ??= BindingIssueContextRefBuilder.TryBuild(mismatch, diagnosis);
            }

            if (string.Equals(diagnosis, "NullabilityMismatch", StringComparison.Ordinal))
            {
                AddUnique(steps, CreateDiagnostic(
                    "get_binding_value_chain",
                    2,
                    "Trace how the binding resolved into a nullability mismatch.",
                    ("elementId", elementId),
                    ("propertyName", propertyName)));
                contextRef ??= BindingIssueContextRefBuilder.TryBuild(mismatch, diagnosis);
            }
        }

        return ToolNavigationEnvelope.FromRecommended(
            steps,
            contextRefs: contextRef is null ? [] : [contextRef]);
    }

    private static IReadOnlyList<ToolNextStep> BuildValidationErrors(ToolNavigationContext context)
    {
        if (!HasPositiveCount(context.Payload, "errorCount") || !TryGetScopedElementId(context, out var elementId))
        {
            return Array.Empty<ToolNextStep>();
        }

        var steps = new List<ToolNextStep>
        {
            CreateDiagnostic(
                "get_bindings",
                1,
                "Inspect bindings for the scoped element with validation issues.",
                ("elementId", elementId))
        };

        if (ValidationSuggestsViewModelInspection(context.Payload))
        {
            steps.Add(CreateDiagnostic(
                "get_viewmodel",
                2,
                "Inspect the ViewModel data behind the validation issue.",
                ("elementId", elementId)));
        }

        return steps;
    }

    private static bool ValidationSuggestsViewModelInspection(JsonElement payload)
    {
        if (!TryGetArray(payload, "errors", out var errors))
        {
            return false;
        }

        foreach (var error in errors.EnumerateArray())
        {
            if (TryGetString(error, "ruleType", out var ruleType)
                && (ruleType.Contains("DataError", StringComparison.OrdinalIgnoreCase)
                    || ruleType.Contains("ExceptionValidation", StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPathMismatch(JsonElement error) =>
        ContainsAny(error, "eventType", "Path", "PathError")
        || ContainsAny(error, "message", "path error", "property not found", "cannot find");

    private static bool IsConverterOrUpdateFailure(JsonElement error) =>
        ContainsAny(error, "eventType", "UpdateTarget", "UpdateSource")
        || ContainsAny(error, "message", "converter", "convert", "update target", "update source");

    private static ToolNextStep CreateDiagnostic(
        string tool,
        int priority,
        string reason,
        params (string name, object? value)[] parameters) =>
        new(tool, NavigationParamBuilders.Create(parameters), reason, ToolNextStepKind.Diagnostic, priority);

    private static bool TryGetScopedElementId(ToolNavigationContext context, out string elementId)
    {
        if (context.Arguments is { } arguments && TryGetString(arguments, "elementId", out elementId))
        {
            return true;
        }

        elementId = string.Empty;
        return false;
    }

    private static bool TryGetDiagnosticElementId(JsonElement error, out string elementId)
    {
        if (TryGetString(error, "elementId", out elementId))
        {
            return true;
        }

        if (!TryGetString(error, "suggestedElementId", out elementId)
            || !TryGetString(error, "matchConfidence", out var confidence))
        {
            return false;
        }

        return string.Equals(confidence, "high", StringComparison.OrdinalIgnoreCase)
            || string.Equals(confidence, "exact", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasPositiveCount(JsonElement element, string propertyName) =>
        element.TryGetProperty(propertyName, out var property)
        && property.ValueKind == JsonValueKind.Number
        && property.GetInt32() > 0;

    private static bool ContainsAny(JsonElement element, string propertyName, params string[] needles)
    {
        if (!TryGetString(element, propertyName, out var value))
        {
            return false;
        }

        return needles.Any(needle => value.Contains(needle, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryGetArray(JsonElement element, string propertyName, out JsonElement property)
    {
        if (element.TryGetProperty(propertyName, out property) && property.ValueKind == JsonValueKind.Array)
        {
            return true;
        }

        property = default;
        return false;
    }

    private static bool TryGetString(JsonElement element, string propertyName, out string value)
    {
        if (element.TryGetProperty(propertyName, out var property) && property.ValueKind == JsonValueKind.String)
        {
            var candidate = property.GetString();
            if (!string.IsNullOrWhiteSpace(candidate))
            {
                value = candidate;
                return true;
            }
        }

        value = string.Empty;
        return false;
    }

    private static void AddUnique(List<ToolNextStep> steps, ToolNextStep step)
    {
        if (steps.Any(existing => existing.Tool == step.Tool && existing.Params.GetRawText() == step.Params.GetRawText()))
        {
            return;
        }

        steps.Add(step);
    }
}
