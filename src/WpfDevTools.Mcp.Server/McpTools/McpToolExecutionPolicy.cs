using System.Text.Json;
using ModelContextProtocol.Protocol;
using WpfDevTools.Mcp.Server;

namespace WpfDevTools.Mcp.Server.McpTools;

internal sealed class McpToolExecutionPolicy
{
    private static readonly HashSet<string> DestructiveTools =
        McpToolCapabilityCatalog.DiscoverToolNamesWithPolicyTag(McpToolPolicyTags.DestructiveTools);

    private static readonly HashSet<string> ScreenshotTools =
        McpToolCapabilityCatalog.DiscoverToolNamesWithPolicyTag(McpToolPolicyTags.Screenshots);

    private static readonly HashSet<string> SensitiveReadTools =
        McpToolCapabilityCatalog.DiscoverToolNamesWithPolicyTag(McpToolPolicyTags.SensitiveReads);

    private static readonly HashSet<string> ViewModelInspectionTools =
        McpToolCapabilityCatalog.DiscoverToolNamesWithPolicyTag(McpToolPolicyTags.ViewModelInspection);

    private readonly PolicyGate _destructiveTools;
    private readonly PolicyGate _screenshots;
    private readonly PolicyGate _sensitiveReads;
    private readonly PolicyGate _viewModelInspection;
    private readonly PolicyGate _composerRuntimeApprovals;

    private McpToolExecutionPolicy(
        PolicyGate destructiveTools,
        PolicyGate screenshots,
        PolicyGate sensitiveReads,
        PolicyGate viewModelInspection,
        PolicyGate composerRuntimeApprovals)
    {
        _destructiveTools = destructiveTools;
        _screenshots = screenshots;
        _sensitiveReads = sensitiveReads;
        _viewModelInspection = viewModelInspection;
        _composerRuntimeApprovals = composerRuntimeApprovals;
    }

    internal static McpToolExecutionPolicy FromEnvironment()
        => FromConfiguredValues(
            allowDestructiveTools: Environment.GetEnvironmentVariable(McpServerConfiguration.AllowDestructiveToolsEnvVar),
            allowScreenshots: Environment.GetEnvironmentVariable(McpServerConfiguration.AllowScreenshotsEnvVar),
            allowViewModelInspection: Environment.GetEnvironmentVariable(McpServerConfiguration.AllowViewModelInspectionEnvVar),
            allowSensitiveReads: Environment.GetEnvironmentVariable(McpServerConfiguration.AllowSensitiveReadsEnvVar),
            allowComposerRuntimeApprovals: Environment.GetEnvironmentVariable(McpServerConfiguration.AllowComposerRuntimeApprovalsEnvVar));

    internal static McpToolExecutionPolicy FromConfiguredValues(
        string? allowDestructiveTools,
        string? allowScreenshots,
        string? allowViewModelInspection,
        string? allowSensitiveReads = null,
        string? allowComposerRuntimeApprovals = null)
        => new(
            PolicyGate.Parse(McpServerConfiguration.AllowDestructiveToolsEnvVar, allowDestructiveTools),
            PolicyGate.Parse(McpServerConfiguration.AllowScreenshotsEnvVar, allowScreenshots),
            PolicyGate.Parse(McpServerConfiguration.AllowSensitiveReadsEnvVar, allowSensitiveReads),
            PolicyGate.Parse(McpServerConfiguration.AllowViewModelInspectionEnvVar, allowViewModelInspection),
            PolicyGate.Parse(McpServerConfiguration.AllowComposerRuntimeApprovalsEnvVar, allowComposerRuntimeApprovals));

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

        if (RequiresScreenshot(toolName, arguments))
        {
            var decision = EvaluateGate(
                _screenshots,
                toolName,
                policyCategory: McpToolPolicyTags.Screenshots,
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
                policyCategory: McpToolPolicyTags.ViewModelInspection,
                capabilityDescription: "inspect or modify target ViewModel state");
            if (!decision.IsAllowed)
            {
                return decision;
            }
        }

        if (RequiresDestructiveGate(toolName, arguments))
        {
            var decision = EvaluateGate(
                _destructiveTools,
                toolName,
                policyCategory: McpToolPolicyTags.DestructiveTools,
                capabilityDescription: "mutate the running target application or persist generated project files");
            if (!decision.IsAllowed)
            {
                return decision;
            }
        }

        if (RequiresSensitiveRead(toolName, arguments))
        {
            var decision = EvaluateGate(
                _sensitiveReads,
                toolName,
                policyCategory: McpToolPolicyTags.SensitiveReads,
                capabilityDescription: "read target UI text, dependency property values, bindings, runtime event data, or state snapshots");
            if (!decision.IsAllowed)
            {
                return decision;
            }
        }

        if (string.Equals(toolName, "preview_ui_blueprint", StringComparison.Ordinal)
            && ContainsNonEmptyArray(arguments, "runtimePackApprovalTokens"))
        {
            var decision = EvaluateGate(
                _composerRuntimeApprovals,
                toolName,
                policyCategory: "composer-runtime-approvals",
                capabilityDescription: "approve reviewed content-bound third-party runtime packs for this preview request");
            if (!decision.IsAllowed)
            {
                return decision;
            }
        }

        return McpToolPolicyDecision.Allowed;
    }

    private static bool RequiresSensitiveRead(
        string toolName,
        IDictionary<string, JsonElement>? arguments)
    {
        if (SensitiveReadTools.Contains(toolName))
        {
            return true;
        }

        return string.Equals(toolName, "batch_mutate", StringComparison.Ordinal)
            && (HasEffectiveBatchSnapshot(arguments)
                || BatchMutateReturnsSensitiveRead(arguments))
            || string.Equals(toolName, "preview_ui_blueprint", StringComparison.Ordinal)
            && (IsEnabledArgument(arguments, "includeRuntimeDiagnostics")
                || IsEnabledArgument(arguments, "includeScreenshotDiagnostics"));
    }

    private static bool HasEffectiveBatchSnapshot(IDictionary<string, JsonElement>? arguments)
        => (arguments?.TryGetValue("captureSnapshot", out var captureSnapshot) == true
            && captureSnapshot.ValueKind == JsonValueKind.True)
           || TryGetObjectArgument(arguments, "captureSnapshot", out _);

    private static bool RequiresScreenshot(
        string toolName,
        IDictionary<string, JsonElement>? arguments)
        => ScreenshotTools.Contains(toolName)
           || string.Equals(toolName, "preview_ui_blueprint", StringComparison.Ordinal)
           && IsEnabledArgument(arguments, "includeScreenshotDiagnostics");

    private static bool RequiresDestructiveGate(
        string toolName,
        IDictionary<string, JsonElement>? arguments)
    {
        if (!DestructiveTools.Contains(toolName))
        {
            return false;
        }

        return toolName is not "apply_ui_blueprint" and not "import_ui_block_pack"
               || IsComposerWrite(arguments);
    }

    private static bool IsComposerWrite(IDictionary<string, JsonElement>? arguments)
    {
        if (arguments?.TryGetValue("dryRun", out var dryRun) != true)
        {
            return false;
        }

        if (dryRun.ValueKind == JsonValueKind.False)
        {
            return true;
        }

        return dryRun.ValueKind == JsonValueKind.String
               && bool.TryParse(dryRun.GetString(), out var parsedDryRun)
               && !parsedDryRun;
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

    private static bool BatchMutateReturnsSensitiveRead(IDictionary<string, JsonElement>? arguments)
    {
        if (!TryGetArrayArgument(arguments, "mutations", out var mutations))
        {
            return false;
        }

        return mutations.EnumerateArray().Any(MutationStepReturnsSensitiveRead);
    }

    private static bool TriggerMutationUsesViewModel(IDictionary<string, JsonElement>? arguments)
        => TryGetObjectArgument(arguments, "triggerMutation", out var triggerMutation)
           && MutationStepUsesViewModel(triggerMutation);

    private static bool MutationStepUsesViewModel(JsonElement mutationStep)
        => mutationStep.ValueKind == JsonValueKind.Object
           && mutationStep.TryGetProperty("tool", out var tool)
           && tool.ValueKind == JsonValueKind.String
           && ViewModelInspectionTools.Contains(tool.GetString() ?? string.Empty);

    private static bool MutationStepReturnsSensitiveRead(JsonElement mutationStep)
        => mutationStep.ValueKind == JsonValueKind.Object
           && mutationStep.TryGetProperty("tool", out var tool)
           && tool.ValueKind == JsonValueKind.String
           && SensitiveReadTools.Contains(tool.GetString() ?? string.Empty);

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

    private static bool IsEnabledArgument(IDictionary<string, JsonElement>? arguments, string propertyName)
    {
        if (arguments?.TryGetValue(propertyName, out var value) != true)
        {
            return false;
        }

        return value.ValueKind == JsonValueKind.True
               || value.ValueKind == JsonValueKind.String
               && bool.TryParse(value.GetString(), out var parsed)
               && parsed;
    }

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
                ConfigurationError:
                $"Invalid value for {environmentVariable}. {EnvironmentVariableDiagnostics.AcceptedBooleanValues}");
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
