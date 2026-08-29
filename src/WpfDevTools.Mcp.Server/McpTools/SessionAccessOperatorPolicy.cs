using WpfDevTools.Injector.Discovery;
using WpfDevTools.Mcp.Server.Composer.Apply;
using WpfDevTools.Mcp.Server.Tools;
using WpfDevTools.Shared.Enums;

namespace WpfDevTools.Mcp.Server.McpTools;

internal static class SessionAccessOperatorPolicy
{
    internal static SessionAccessRequestResult Apply(
        SessionAccessRequest request,
        SessionAccessRequestResult sessionStatus,
        Func<string, string?> getEnvironmentValue)
    {
        var allPreauthorized = true;
        foreach (var capability in request.Capabilities)
        {
            var result = ApplyCapability(
                capability,
                sessionStatus,
                request.ProjectRoot,
                getEnvironmentValue);
            if (result.Status is "hard-denied" or "invalid-policy")
            {
                return result;
            }

            allPreauthorized &= result.Status == "preauthorized";
        }

        return allPreauthorized
            ? sessionStatus with { Success = true, Status = "preauthorized" }
            : sessionStatus;
    }

    private static SessionAccessRequestResult ApplyCapability(
        string capability,
        SessionAccessRequestResult sessionStatus,
        string? projectRoot,
        Func<string, string?> getEnvironmentValue)
    {
        var environmentVariable = capability switch
        {
            SessionAccessCapabilities.SensitiveRead => McpServerConfiguration.AllowSensitiveReadsEnvVar,
            SessionAccessCapabilities.Screenshot => McpServerConfiguration.AllowScreenshotsEnvVar,
            SessionAccessCapabilities.RuntimeMutation or SessionAccessCapabilities.ComposerPreview
                => McpServerConfiguration.AllowDestructiveToolsEnvVar,
            SessionAccessCapabilities.ViewModelInspection => McpServerConfiguration.AllowViewModelInspectionEnvVar,
            SessionAccessCapabilities.ComposerRuntimeApproval => McpServerConfiguration.AllowComposerRuntimeApprovalsEnvVar,
            SessionAccessCapabilities.ProjectWrite => McpServerConfiguration.AllowProjectWritesEnvVar,
            SessionAccessCapabilities.TargetConnect => McpServerConfiguration.AllowedTargetsEnvVar,
            SessionAccessCapabilities.RawInjection => McpServerConfiguration.RawInjectionAllowedTargetsEnvVar,
            _ => null
        };
        if (environmentVariable is null)
        {
            return sessionStatus;
        }

        var configuredValue = getEnvironmentValue(environmentVariable);
        if (capability == SessionAccessCapabilities.ProjectWrite)
        {
            return ApplyProjectPolicy(sessionStatus, projectRoot, configuredValue, getEnvironmentValue);
        }

        if (string.IsNullOrWhiteSpace(configuredValue))
        {
            return sessionStatus;
        }

        if (capability is SessionAccessCapabilities.TargetConnect or SessionAccessCapabilities.RawInjection)
        {
            return ApplyTargetPolicy(capability, sessionStatus, configuredValue);
        }

        var gate = PolicyGate.Parse(environmentVariable, configuredValue);
        return gate.State switch
        {
            PolicyGateState.Allowed => sessionStatus with { Success = true, Status = "preauthorized" },
            PolicyGateState.Invalid => Denied(sessionStatus, "invalid-policy", "InvalidPolicyConfiguration", gate.ConfigurationError),
            _ => Denied(sessionStatus, "hard-denied", "SecurityError", $"{environmentVariable} explicitly disables this capability.")
        };
    }

    private static SessionAccessRequestResult ApplyProjectPolicy(
        SessionAccessRequestResult sessionStatus,
        string? projectRoot,
        string? configuredWrites,
        Func<string, string?> getEnvironmentValue)
    {
        var gate = PolicyGate.Parse(McpServerConfiguration.AllowProjectWritesEnvVar, configuredWrites);
        if (gate.State == PolicyGateState.Invalid)
        {
            return Denied(sessionStatus, "invalid-policy", "InvalidPolicyConfiguration", gate.ConfigurationError);
        }

        if (gate.State == PolicyGateState.Denied)
        {
            return Denied(sessionStatus, "hard-denied", "SecurityError", "Project writes are explicitly disabled.");
        }

        if (projectRoot is null || sessionStatus.Scopes.Count == 0)
        {
            return sessionStatus;
        }

        var authorization = ProjectWritePolicy.AuthorizeSession(
            projectRoot,
            _ => false,
            configuredWrites,
            getEnvironmentValue(McpServerConfiguration.AllowedProjectRootsEnvVar));
        if (authorization.Allowed)
        {
            return sessionStatus with { Success = true, Status = "preauthorized" };
        }

        return authorization.Code == "InteractiveConsentRequired"
            ? sessionStatus
            : Denied(sessionStatus, "hard-denied", authorization.Code, authorization.Message);
    }

    private static SessionAccessRequestResult ApplyTargetPolicy(
        string capability,
        SessionAccessRequestResult sessionStatus,
        string configuredValue)
    {
        var scope = sessionStatus.Scopes.SingleOrDefault();
        if (scope?.ProcessId is not int processId || string.IsNullOrWhiteSpace(scope.Resource))
        {
            return sessionStatus;
        }

        var process = new WpfProcessInfo
        {
            ProcessId = processId,
            ProcessName = "redacted",
            Architecture = ProcessArchitecture.Unknown,
            IsWpfApplication = true,
            ExecutablePath = scope.Resource
        };
        if (capability == SessionAccessCapabilities.TargetConnect)
        {
            var target = McpTargetPolicy.Authorize(
                process,
                configuredValue,
                RawInjectionTargetPolicy.TryResolvePhysicalPath);
            return target.IsAllowed
                ? sessionStatus with { Success = true, Status = "preauthorized" }
                : Denied(sessionStatus,
                    target.ErrorCode == "InvalidPolicyConfiguration" ? "invalid-policy" : "hard-denied",
                    target.ErrorCode,
                    target.Error);
        }

        var raw = RawInjectionTargetPolicy.Authorize(
            process,
            AppContext.BaseDirectory,
            configuredValue,
            RawInjectionTargetPolicy.TryResolvePhysicalPath);
        return raw.IsAllowed
            ? sessionStatus with { Success = true, Status = "preauthorized" }
            : Denied(sessionStatus,
                raw.ErrorCode == "InvalidPolicyConfiguration" ? "invalid-policy" : "hard-denied",
                raw.ErrorCode,
                raw.Error);
    }

    private static SessionAccessRequestResult Denied(
        SessionAccessRequestResult status,
        string state,
        string errorCode,
        string? error)
        => status with { Success = false, Status = state, ErrorCode = errorCode, Error = error };
}
