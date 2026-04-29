using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace WpfDevTools.Mcp.Server.McpTools;

internal sealed class McpToolExecutionPolicy
{
    private static readonly HashSet<string> DestructiveTools = new(StringComparer.Ordinal)
    {
        "set_dp_value",
        "clear_dp_value",
        "click_element",
        "fire_routed_event",
        "execute_command",
        "modify_viewmodel",
        "override_style_setter",
        "invalidate_layout",
        "drag_and_drop",
        "focus_element",
        "simulate_keyboard",
        "force_binding_update",
        "wait_for_dp_change_after_mutation",
        "scroll_to_element",
        "highlight_element",
        "measure_element_render_time",
        "restore_state_snapshot",
        "batch_mutate"
    };

    private static readonly HashSet<string> ScreenshotTools = new(StringComparer.Ordinal)
    {
        "element_screenshot"
    };

    private static readonly HashSet<string> ViewModelInspectionTools = new(StringComparer.Ordinal)
    {
        "get_viewmodel",
        "get_commands",
        "execute_command",
        "modify_viewmodel"
    };

    private readonly PolicyGate _destructiveTools;
    private readonly PolicyGate _screenshots;
    private readonly PolicyGate _viewModelInspection;

    private McpToolExecutionPolicy(
        PolicyGate destructiveTools,
        PolicyGate screenshots,
        PolicyGate viewModelInspection)
    {
        _destructiveTools = destructiveTools;
        _screenshots = screenshots;
        _viewModelInspection = viewModelInspection;
    }

    internal static McpToolExecutionPolicy FromEnvironment()
        => FromConfiguredValues(
            Environment.GetEnvironmentVariable(McpServerConfiguration.AllowDestructiveToolsEnvVar),
            Environment.GetEnvironmentVariable(McpServerConfiguration.AllowScreenshotsEnvVar),
            Environment.GetEnvironmentVariable(McpServerConfiguration.AllowViewModelInspectionEnvVar));

    internal static McpToolExecutionPolicy FromConfiguredValues(
        string? allowDestructiveTools,
        string? allowScreenshots,
        string? allowViewModelInspection)
        => new(
            PolicyGate.Parse(McpServerConfiguration.AllowDestructiveToolsEnvVar, allowDestructiveTools),
            PolicyGate.Parse(McpServerConfiguration.AllowScreenshotsEnvVar, allowScreenshots),
            PolicyGate.Parse(McpServerConfiguration.AllowViewModelInspectionEnvVar, allowViewModelInspection));

    internal McpToolPolicyDecision EvaluateToolCall(string? toolName)
        => EvaluateToolCall(toolName, arguments: null);

    internal McpToolPolicyDecision EvaluateToolCall(
        string? toolName,
        IDictionary<string, JsonElement>? arguments)
    {
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return McpToolPolicyDecision.Allowed;
        }

        if (ScreenshotTools.Contains(toolName))
        {
            var decision = EvaluateGate(
                _screenshots,
                toolName,
                policyCategory: "screenshots",
                capabilityDescription: "capture or return target UI screenshots");
            if (!decision.IsAllowed)
            {
                return decision;
            }
        }

        if (RequiresViewModelInspection(toolName, arguments))
        {
            var decision = EvaluateGate(
                _viewModelInspection,
                toolName,
                policyCategory: "viewmodel-inspection",
                capabilityDescription: "inspect or modify target ViewModel state");
            if (!decision.IsAllowed)
            {
                return decision;
            }
        }

        if (DestructiveTools.Contains(toolName))
        {
            var decision = EvaluateGate(
                _destructiveTools,
                toolName,
                policyCategory: "destructive-tools",
                capabilityDescription: "mutate the running target application");
            if (!decision.IsAllowed)
            {
                return decision;
            }
        }

        return McpToolPolicyDecision.Allowed;
    }

    private static bool RequiresViewModelInspection(
        string toolName,
        IDictionary<string, JsonElement>? arguments)
    {
        if (ViewModelInspectionTools.Contains(toolName))
        {
            return true;
        }

        return string.Equals(toolName, "capture_state_snapshot", StringComparison.Ordinal)
            ? ContainsNonEmptyArray(arguments, "viewModelPropertyNames")
            : string.Equals(toolName, "batch_mutate", StringComparison.Ordinal)
                ? BatchMutateUsesViewModel(arguments)
                : string.Equals(toolName, "wait_for_dp_change_after_mutation", StringComparison.Ordinal)
                    && TriggerMutationUsesViewModel(arguments);
    }

    private static bool BatchMutateUsesViewModel(IDictionary<string, JsonElement>? arguments)
    {
        if (ContainsNestedViewModelPropertyNames(arguments, "captureSnapshot"))
        {
            return true;
        }

        if (!TryGetArrayArgument(arguments, "mutations", out var mutations))
        {
            return false;
        }

        return mutations.EnumerateArray().Any(MutationStepUsesViewModel);
    }

    private static bool TriggerMutationUsesViewModel(IDictionary<string, JsonElement>? arguments)
        => TryGetObjectArgument(arguments, "triggerMutation", out var triggerMutation)
           && MutationStepUsesViewModel(triggerMutation);

    private static bool MutationStepUsesViewModel(JsonElement mutationStep)
        => mutationStep.ValueKind == JsonValueKind.Object
           && mutationStep.TryGetProperty("tool", out var tool)
           && tool.ValueKind == JsonValueKind.String
           && string.Equals(tool.GetString(), "modify_viewmodel", StringComparison.Ordinal);

    private static bool ContainsNestedViewModelPropertyNames(
        IDictionary<string, JsonElement>? arguments,
        string propertyName)
        => TryGetObjectArgument(arguments, propertyName, out var nested)
           && nested.TryGetProperty("viewModelPropertyNames", out var viewModelPropertyNames)
           && JsonArrayHasValues(viewModelPropertyNames);

    private static bool ContainsNonEmptyArray(
        IDictionary<string, JsonElement>? arguments,
        string propertyName)
        => TryGetArrayArgument(arguments, propertyName, out var value)
           && JsonArrayHasValues(value);

    private static bool JsonArrayHasValues(JsonElement value)
        => value.ValueKind == JsonValueKind.Array && value.GetArrayLength() > 0;

    private static bool TryGetObjectArgument(
        IDictionary<string, JsonElement>? arguments,
        string propertyName,
        out JsonElement value)
        => TryGetJsonArgument(arguments, propertyName, JsonValueKind.Object, out value);

    private static bool TryGetArrayArgument(
        IDictionary<string, JsonElement>? arguments,
        string propertyName,
        out JsonElement value)
        => TryGetJsonArgument(arguments, propertyName, JsonValueKind.Array, out value);

    private static bool TryGetJsonArgument(
        IDictionary<string, JsonElement>? arguments,
        string propertyName,
        JsonValueKind expectedKind,
        out JsonElement value)
    {
        value = default;
        if (arguments?.TryGetValue(propertyName, out var rawValue) != true)
        {
            return false;
        }

        if (rawValue.ValueKind == expectedKind)
        {
            value = rawValue;
            return true;
        }

        if (rawValue.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        var serializedValue = rawValue.GetString();
        if (string.IsNullOrWhiteSpace(serializedValue))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(serializedValue);
            if (document.RootElement.ValueKind != expectedKind)
            {
                return false;
            }

            value = document.RootElement.Clone();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static McpToolPolicyDecision EvaluateGate(
        PolicyGate gate,
        string toolName,
        string policyCategory,
        string capabilityDescription)
    {
        if (gate.IsAllowed)
        {
            return McpToolPolicyDecision.Allowed;
        }

        if (gate.ConfigurationError is string configurationError)
        {
            return McpToolPolicyDecision.Denied(
                error: $"Invalid MCP policy configuration for {gate.EnvironmentVariable}.",
                errorCode: "InvalidPolicyConfiguration",
                hint: $"{configurationError} Set {gate.EnvironmentVariable}=true or false.",
                suggestedAction: $"Fix {gate.EnvironmentVariable} and restart the MCP server.",
                policyCategory: policyCategory);
        }

        return McpToolPolicyDecision.Denied(
            error: $"MCP policy blocks '{toolName}' because {policyCategory} are disabled.",
            errorCode: "SecurityError",
            hint: $"Set {gate.EnvironmentVariable}=true only for trusted local MCP sessions that are allowed to {capabilityDescription}.",
            suggestedAction: $"Review the request, then enable {gate.EnvironmentVariable} only when this MCP client and target process are trusted.",
            policyCategory: policyCategory);
    }

    private readonly record struct PolicyGate(
        string EnvironmentVariable,
        bool IsAllowed,
        string? ConfigurationError)
    {
        internal static PolicyGate Parse(string environmentVariable, string? configuredValue)
        {
            if (string.IsNullOrWhiteSpace(configuredValue))
            {
                return new PolicyGate(environmentVariable, IsAllowed: false, ConfigurationError: null);
            }

            if (IsEnabledValue(configuredValue))
            {
                return new PolicyGate(environmentVariable, IsAllowed: true, ConfigurationError: null);
            }

            if (IsDisabledValue(configuredValue))
            {
                return new PolicyGate(environmentVariable, IsAllowed: false, ConfigurationError: null);
            }

            return new PolicyGate(
                environmentVariable,
                IsAllowed: false,
                ConfigurationError: $"Value '{configuredValue}' is not valid; use true or false.");
        }

        private static bool IsEnabledValue(string value)
            => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, "1", StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, "on", StringComparison.OrdinalIgnoreCase);

        private static bool IsDisabledValue(string value)
            => string.Equals(value, "false", StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, "0", StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, "no", StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, "off", StringComparison.OrdinalIgnoreCase);
    }
}

internal readonly record struct McpToolPolicyDecision(
    bool IsAllowed,
    string? Error,
    string? ErrorCode,
    string? Hint,
    string? SuggestedAction,
    string? PolicyCategory)
{
    internal static McpToolPolicyDecision Allowed { get; } = new(
        IsAllowed: true,
        Error: null,
        ErrorCode: null,
        Hint: null,
        SuggestedAction: null,
        PolicyCategory: null);

    internal static McpToolPolicyDecision Denied(
        string error,
        string errorCode,
        string hint,
        string suggestedAction,
        string policyCategory)
        => new(
            IsAllowed: false,
            Error: error,
            ErrorCode: errorCode,
            Hint: hint,
            SuggestedAction: suggestedAction,
            PolicyCategory: policyCategory);

    internal CallToolResult ToCallToolResult()
        => ToolCallHelper.CreateStructuredErrorResult(
            Error ?? "MCP tool call blocked by policy.",
            ErrorCode ?? "SecurityError",
            Hint,
            SuggestedAction);
}