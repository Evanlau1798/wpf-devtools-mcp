using System.Diagnostics;
using WpfDevTools.Injector;
using WpfDevTools.Injector.Discovery;

namespace WpfDevTools.Mcp.Server.Tools;

public sealed partial class ConnectTool
{
    private bool TryRefreshRawInjectionTargetBeforeInjection(
        int processId,
        ref ConnectTargetContext context,
        out object? failure)
        => TryRefreshConnectTarget(
            processId,
            ref context,
            requireRawInjectionTargetAllowed: true,
            out failure);

    private bool TryRefreshConnectTargetBeforeExistingHostProbe(
        int processId,
        ref ConnectTargetContext context,
        out object? failure)
        => TryRefreshConnectTarget(
            processId,
            ref context,
            requireRawInjectionTargetAllowed: false,
            out failure);

    private bool TryRefreshConnectTarget(
        int processId,
        ref ConnectTargetContext context,
        bool requireRawInjectionTargetAllowed,
        out object? failure)
    {
        failure = null;

        var currentProcessInfo = _processDetector.GetProcessInfo(processId);
        if (currentProcessInfo == null)
        {
            failure = new
            {
                success = false,
                error = $"Could not detect process info for {processId} before injection",
                errorCode = "ProcessNotFound"
            };
            return false;
        }

        if (RawInjectionTargetPolicy.TryCompareExecutablePath(
                context.ProcessInfo.ExecutablePath,
                currentProcessInfo.ExecutablePath,
                out var areSameExecutablePath)
            && !areSameExecutablePath)
        {
            Trace.WriteLine(
                "ConnectTool raw injection target changed before injection for process " +
                $"{processId}: original={context.ProcessInfo.ExecutablePath}; current={currentProcessInfo.ExecutablePath}");
            failure = CreateRawInjectionIdentityChangedFailure();
            return false;
        }

        var authorizationFailure = AuthorizeTarget(processId, currentProcessInfo);
        if (authorizationFailure != null)
        {
            failure = authorizationFailure;
            return false;
        }

        var access = ProcessConnectionAccessEvaluator.Evaluate(
            processId,
            currentProcessInfo.IsElevated,
            _isCurrentProcessElevated());
        if (access.RequiresElevationToConnect)
        {
            failure = CreateElevationRequiredFailure(currentProcessInfo, access);
            return false;
        }

        var isRawInjectionTargetAllowed = _isRawInjectionTargetAllowed(currentProcessInfo);
        if (requireRawInjectionTargetAllowed && !isRawInjectionTargetAllowed)
        {
            failure = CreateRawInjectionDeniedFailure(processId, currentProcessInfo);
            return false;
        }

        context = new ConnectTargetContext(
            currentProcessInfo,
            access,
            IsLikelySdkOnlyPackaging(currentProcessInfo),
            isRawInjectionTargetAllowed);
        return true;
    }

    private static object CreateRawInjectionIdentityChangedFailure()
    {
        return new
        {
            success = false,
            error = "Raw injection is blocked because the target executable changed before injection could start. Re-run process discovery and explicitly allowlist the current target executable before retrying.",
            errorCode = "SecurityError",
            hint = $"Refresh get_processes() or connect() discovery, then set {McpServerConfiguration.RawInjectionAllowedTargetsEnvVar} only for the exact current executable path.",
            requiresExplicitTargetOptIn = true,
            allowlistEnvVar = McpServerConfiguration.RawInjectionAllowedTargetsEnvVar
        };
    }
}
