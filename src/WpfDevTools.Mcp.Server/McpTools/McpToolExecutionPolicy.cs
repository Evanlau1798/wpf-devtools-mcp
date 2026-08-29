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
    private readonly Func<SessionAccessRequest, bool> _sessionGrantChecker;

    private McpToolExecutionPolicy(
        PolicyGate destructiveTools,
        PolicyGate screenshots,
        PolicyGate sensitiveReads,
        PolicyGate viewModelInspection,
        PolicyGate composerRuntimeApprovals,
        Func<SessionAccessRequest, bool>? sessionGrantChecker)
    {
        _destructiveTools = destructiveTools;
        _screenshots = screenshots;
        _sensitiveReads = sensitiveReads;
        _viewModelInspection = viewModelInspection;
        _composerRuntimeApprovals = composerRuntimeApprovals;
        _sessionGrantChecker = sessionGrantChecker ?? (_ => false);
    }

    internal static McpToolExecutionPolicy FromEnvironment(
        Func<SessionAccessRequest, bool>? sessionGrantChecker = null)
        => FromConfiguredValues(
            allowDestructiveTools: Environment.GetEnvironmentVariable(McpServerConfiguration.AllowDestructiveToolsEnvVar),
            allowScreenshots: Environment.GetEnvironmentVariable(McpServerConfiguration.AllowScreenshotsEnvVar),
            allowViewModelInspection: Environment.GetEnvironmentVariable(McpServerConfiguration.AllowViewModelInspectionEnvVar),
            allowSensitiveReads: Environment.GetEnvironmentVariable(McpServerConfiguration.AllowSensitiveReadsEnvVar),
            allowComposerRuntimeApprovals: Environment.GetEnvironmentVariable(McpServerConfiguration.AllowComposerRuntimeApprovalsEnvVar),
            sessionGrantChecker: sessionGrantChecker);

    internal static McpToolExecutionPolicy FromConfiguredValues(
        string? allowDestructiveTools,
        string? allowScreenshots,
        string? allowViewModelInspection,
        string? allowSensitiveReads = null,
        string? allowComposerRuntimeApprovals = null,
        Func<SessionAccessRequest, bool>? sessionGrantChecker = null)
        => new(
            PolicyGate.Parse(McpServerConfiguration.AllowDestructiveToolsEnvVar, allowDestructiveTools),
            PolicyGate.Parse(McpServerConfiguration.AllowScreenshotsEnvVar, allowScreenshots),
            PolicyGate.Parse(McpServerConfiguration.AllowSensitiveReadsEnvVar, allowSensitiveReads),
            PolicyGate.Parse(McpServerConfiguration.AllowViewModelInspectionEnvVar, allowViewModelInspection),
            PolicyGate.Parse(McpServerConfiguration.AllowComposerRuntimeApprovalsEnvVar, allowComposerRuntimeApprovals),
            sessionGrantChecker);

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
                capabilityDescription: "capture or return target UI screenshots",
                request: BuildAccessRequest(SessionAccessCapabilities.Screenshot, toolName, arguments));
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
                capabilityDescription: "inspect or modify target ViewModel state",
                request: BuildAccessRequest(SessionAccessCapabilities.ViewModelInspection, toolName, arguments));
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
                capabilityDescription: "mutate the running target application or persist generated project files",
                request: BuildAccessRequest(ResolveMutationCapability(toolName), toolName, arguments));
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
                capabilityDescription: "read target UI text, dependency property values, bindings, runtime event data, or state snapshots",
                request: BuildAccessRequest(SessionAccessCapabilities.SensitiveRead, toolName, arguments));
            if (!decision.IsAllowed)
            {
                return decision;
            }
        }

        if (string.Equals(toolName, "preview_ui_blueprint", StringComparison.Ordinal)
            && ContainsNonEmptyArray(arguments, "runtimePackApprovalTokens"))
        {
            foreach (var packRef in GetStringArray(arguments, "runtimePackApprovalTokens"))
            {
                var decision = EvaluateGate(
                    _composerRuntimeApprovals,
                    toolName,
                    policyCategory: "composer-runtime-approvals",
                    capabilityDescription: "approve reviewed content-bound third-party runtime packs for this preview request",
                    request: BuildAccessRequest(SessionAccessCapabilities.ComposerRuntimeApproval, toolName, arguments, packRef));
                if (!decision.IsAllowed)
                {
                    return decision;
                }
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
                || IsEnabledArgument(arguments, "includeScreenshotDiagnostics")
                || ContainsNonEmptyString(arguments, "visualLayoutContractJson"));
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

    private static bool ContainsNonEmptyString(
        IDictionary<string, JsonElement>? arguments,
        string propertyName)
        => arguments?.TryGetValue(propertyName, out var value) == true
           && value.ValueKind == JsonValueKind.String
           && !string.IsNullOrWhiteSpace(value.GetString());

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

    private McpToolPolicyDecision EvaluateGate(
        PolicyGate gate,
        string toolName,
        string policyCategory,
        string capabilityDescription,
        SessionAccessRequest request)
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

        if (gate.CanRequest && _sessionGrantChecker(request))
        {
            return McpToolPolicyDecision.Allowed;
        }

        if (gate.CanRequest)
        {
            var requestJson = JsonSerializer.Serialize(new
            {
                capabilities = request.Capabilities,
                processId = request.ProcessId,
                projectRoot = request.ProjectRoot,
                packRef = request.PackRef,
                reason = $"Allow {toolName} to {capabilityDescription}.",
                lifetime = "session"
            });
            return McpToolPolicyDecision.Denied(
                error: $"'{toolName}' requires temporary user-approved access.",
                errorCode: "InteractiveConsentRequired",
                hint: "Ask the user to review the exact scope, then call request_session_access. Chat text alone is not authorization.",
                suggestedAction: $"Call request_session_access with {requestJson}, then retry '{toolName}' in this session.",
                policyCategory: policyCategory);
        }

        return McpToolPolicyDecision.Denied(
            error: $"MCP policy blocks '{toolName}' because {policyCategory} are disabled.",
            errorCode: "SecurityError",
            hint: $"Set {gate.EnvironmentVariable}=true only for trusted local MCP sessions that are allowed to {capabilityDescription}.",
            suggestedAction: $"Review the request, then enable {gate.EnvironmentVariable} only when this MCP client and target process are trusted.",
            policyCategory: policyCategory);
    }

    private static SessionAccessRequest BuildAccessRequest(
        string capability,
        string toolName,
        IDictionary<string, JsonElement>? arguments,
        string? packRef = null)
    {
        var capabilities = toolName == "preview_ui_blueprint"
                           && capability is SessionAccessCapabilities.Screenshot or SessionAccessCapabilities.SensitiveRead
            ? new[] { capability, SessionAccessCapabilities.ComposerPreview }
            : new[] { capability };
        return new SessionAccessRequest(
            capabilities,
            TryGetIntArgument(arguments, "processId"),
            TryGetStringArgument(arguments, "projectRoot"),
            packRef,
            null,
            SessionAccessLifetime.Session);
    }

    private static string ResolveMutationCapability(string toolName)
        => toolName switch
        {
            "preview_ui_blueprint" => SessionAccessCapabilities.ComposerPreview,
            "apply_ui_blueprint" or "import_ui_block_pack" => SessionAccessCapabilities.ProjectWrite,
            _ => SessionAccessCapabilities.RuntimeMutation
        };

    private static int? TryGetIntArgument(IDictionary<string, JsonElement>? arguments, string name)
        => arguments?.TryGetValue(name, out var value) == true && value.TryGetInt32(out var result)
            ? result
            : null;

    private static string? TryGetStringArgument(IDictionary<string, JsonElement>? arguments, string name)
        => arguments?.TryGetValue(name, out var value) == true && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static IEnumerable<string> GetStringArray(
        IDictionary<string, JsonElement>? arguments,
        string name)
        => TryGetArrayArgument(arguments, name, out var values)
            ? values.EnumerateArray()
                .Where(value => value.ValueKind == JsonValueKind.String)
                .Select(value => value.GetString()!)
                .Where(value => !string.IsNullOrWhiteSpace(value))
            : [];
}
