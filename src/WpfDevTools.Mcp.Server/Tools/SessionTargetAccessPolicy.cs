using WpfDevTools.Injector.Discovery;
using WpfDevTools.Mcp.Server.McpTools;

namespace WpfDevTools.Mcp.Server.Tools;

internal static class SessionTargetAccessPolicy
{
    internal static McpTargetAuthorization AuthorizeTarget(
        WpfProcessInfo processInfo,
        SessionAccessRequestService access,
        string? configuredAllowedTargets,
        Func<string, string?> resolvePhysicalPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredAllowedTargets))
        {
            return McpTargetPolicy.Authorize(
                processInfo,
                configuredAllowedTargets,
                resolvePhysicalPath);
        }

        return IsGranted(access, SessionAccessCapabilities.TargetConnect, processInfo, SessionAccessLifetime.Session)
            ? new McpTargetAuthorization(true, null, null)
            : new McpTargetAuthorization(
                false,
                "Connecting to this target requires temporary user-approved access.",
                $"Call request_session_access for target-connect with processId {processInfo.ProcessId}, then retry connect in this session.",
                McpTargetAuthorizationFailureKind.InteractiveConsentRequired);
    }

    internal static RawInjectionAuthorization AuthorizeRawInjection(
        WpfProcessInfo processInfo,
        SessionAccessRequestService access,
        string? configuredAllowedTargets,
        Func<string, string?> resolvePhysicalPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredAllowedTargets))
        {
            return RawInjectionTargetPolicy.Authorize(
                processInfo,
                AppContext.BaseDirectory,
                configuredAllowedTargets,
                resolvePhysicalPath);
        }

        return IsGranted(access, SessionAccessCapabilities.RawInjection, processInfo, SessionAccessLifetime.Once)
            ? new RawInjectionAuthorization(true, null, null)
            : new RawInjectionAuthorization(
                false,
                "Raw injection requires one-time user-approved access for this exact process identity.",
                $"Call request_session_access for raw-injection with processId {processInfo.ProcessId} and lifetime once, then retry connect.",
                RawInjectionAuthorizationFailureKind.InteractiveConsentRequired);
    }

    internal static bool IsRawInjectionAllowed(
        WpfProcessInfo processInfo,
        SessionAccessRequestService access,
        string? configuredAllowedTargets,
        Func<string, string?> resolvePhysicalPath)
        => AuthorizeRawInjection(
            processInfo,
            access,
            configuredAllowedTargets,
            resolvePhysicalPath).IsAllowed;

    internal static bool ConsumeRawInjection(
        WpfProcessInfo processInfo,
        SessionAccessRequestService access,
        string? configuredAllowedTargets,
        Func<string, string?> resolvePhysicalPath)
    {
        if (!string.IsNullOrWhiteSpace(configuredAllowedTargets))
        {
            return RawInjectionTargetPolicy.Authorize(
                processInfo,
                AppContext.BaseDirectory,
                configuredAllowedTargets,
                resolvePhysicalPath).IsAllowed;
        }

        return access.TryConsume(Request(
            SessionAccessCapabilities.RawInjection,
            processInfo.ProcessId,
            SessionAccessLifetime.Once));
    }

    private static bool IsGranted(
        SessionAccessRequestService access,
        string capability,
        WpfProcessInfo processInfo,
        SessionAccessLifetime lifetime)
        => access.GetStatus(Request(capability, processInfo.ProcessId, lifetime))
            is { Success: true, Status: "granted" };

    private static SessionAccessRequest Request(
        string capability,
        int processId,
        SessionAccessLifetime lifetime)
        => new([capability], processId, null, null, null, lifetime);
}
