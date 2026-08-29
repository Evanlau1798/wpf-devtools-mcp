using ModelContextProtocol.Protocol;
using WpfDevTools.Shared.Utilities;

namespace WpfDevTools.Mcp.Server.McpTools;

internal readonly record struct PolicyGate(
    string EnvironmentVariable,
    PolicyGateState State,
    string? ConfigurationError)
{
    internal bool IsAllowed => State == PolicyGateState.Allowed;
    internal bool CanRequest => State == PolicyGateState.Unset;

    internal static PolicyGate Parse(string environmentVariable, string? configuredValue)
    {
        if (string.IsNullOrWhiteSpace(configuredValue))
        {
            return new PolicyGate(environmentVariable, PolicyGateState.Unset, null);
        }

        if (IsEnabledValue(configuredValue))
        {
            return new PolicyGate(environmentVariable, PolicyGateState.Allowed, null);
        }

        if (IsDisabledValue(configuredValue))
        {
            return new PolicyGate(environmentVariable, PolicyGateState.Denied, null);
        }

        return new PolicyGate(
            environmentVariable,
            PolicyGateState.Invalid,
            $"Invalid value for {environmentVariable}. {EnvironmentVariableDiagnostics.AcceptedBooleanValues}");
    }

    private static bool IsEnabledValue(string value)
        => value.Equals("true", StringComparison.OrdinalIgnoreCase)
           || value.Equals("1", StringComparison.OrdinalIgnoreCase)
           || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
           || value.Equals("on", StringComparison.OrdinalIgnoreCase);

    private static bool IsDisabledValue(string value)
        => value.Equals("false", StringComparison.OrdinalIgnoreCase)
           || value.Equals("0", StringComparison.OrdinalIgnoreCase)
           || value.Equals("no", StringComparison.OrdinalIgnoreCase)
           || value.Equals("off", StringComparison.OrdinalIgnoreCase);
}

internal enum PolicyGateState
{
    Unset,
    Allowed,
    Denied,
    Invalid
}

internal readonly record struct McpToolPolicyDecision(
    bool IsAllowed,
    string? Error,
    string? ErrorCode,
    string? Hint,
    string? SuggestedAction,
    string? PolicyCategory)
{
    internal static McpToolPolicyDecision Allowed { get; } = new(true, null, null, null, null, null);

    internal static McpToolPolicyDecision Denied(
        string error,
        string errorCode,
        string hint,
        string suggestedAction,
        string policyCategory)
        => new(false, error, errorCode, hint, suggestedAction, policyCategory);

    internal CallToolResult ToCallToolResult()
        => ToolCallHelper.CreateStructuredErrorResult(
            Error ?? "MCP tool call blocked by policy.",
            ErrorCode ?? "SecurityError",
            Hint,
            SuggestedAction);
}
