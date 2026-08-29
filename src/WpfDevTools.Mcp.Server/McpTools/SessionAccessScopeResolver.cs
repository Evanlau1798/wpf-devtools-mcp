using System.Diagnostics;
using WpfDevTools.Injector.Discovery;
using WpfDevTools.Mcp.Server.Composer.Apply;

namespace WpfDevTools.Mcp.Server.McpTools;

internal readonly record struct TargetProcessIdentity(
    int ProcessId,
    long StartTimeUtcTicks,
    string ExecutablePath);

internal sealed class SessionAccessScopeResolver
{
    private static readonly HashSet<string> ProcessCapabilities =
    [
        SessionAccessCapabilities.TargetConnect,
        SessionAccessCapabilities.RawInjection,
        SessionAccessCapabilities.SensitiveRead,
        SessionAccessCapabilities.Screenshot,
        SessionAccessCapabilities.RuntimeMutation,
        SessionAccessCapabilities.ViewModelInspection
    ];

    private readonly Func<int, TargetProcessIdentity?> _processIdentityResolver;
    private readonly Func<int?> _activeProcessResolver;

    internal SessionAccessScopeResolver(
        Func<int, TargetProcessIdentity?> processIdentityResolver,
        Func<int?> activeProcessResolver)
    {
        _processIdentityResolver = processIdentityResolver
            ?? throw new ArgumentNullException(nameof(processIdentityResolver));
        _activeProcessResolver = activeProcessResolver
            ?? throw new ArgumentNullException(nameof(activeProcessResolver));
    }

    internal static SessionAccessScopeResolver Create(SessionManager sessionManager)
    {
        ArgumentNullException.ThrowIfNull(sessionManager);
        var detector = new WpfProcessDetector();
        return new SessionAccessScopeResolver(
            processId => ResolveProcessIdentity(detector, processId),
            () => sessionManager.TryGetActiveProcessId(out var processId) ? processId : null);
    }

    internal SessionAccessScopeResolution Resolve(SessionAccessRequest request)
    {
        if (request.Capabilities is not { Count: > 0 })
        {
            return SessionAccessScopeResolution.Invalid(
                "MissingRequiredParameter",
                "At least one access capability is required.");
        }

        var capabilities = request.Capabilities
            .Where(capability => !string.IsNullOrWhiteSpace(capability))
            .Select(capability => capability.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (capabilities.Length != request.Capabilities.Count
            || capabilities.Any(capability => !SessionAccessCapabilities.All.Contains(capability)))
        {
            return SessionAccessScopeResolution.Invalid(
                "InvalidAccessCapability",
                "capabilities contains an unknown or duplicate value.");
        }

        if (capabilities.Contains(SessionAccessCapabilities.RawInjection)
            && request.Lifetime != SessionAccessLifetime.Once)
        {
            return SessionAccessScopeResolution.Invalid(
                "InvalidAccessLifetime",
                "raw-injection access is limited to one use.");
        }

        var scopes = new List<SessionAccessScope>(capabilities.Length);
        TargetProcessIdentity? processIdentity = null;
        foreach (var capability in capabilities)
        {
            if (ProcessCapabilities.Contains(capability))
            {
                var allowsPreviewScope = (capability is SessionAccessCapabilities.Screenshot
                    or SessionAccessCapabilities.SensitiveRead)
                    && capabilities.Contains(SessionAccessCapabilities.ComposerPreview);
                processIdentity ??= ResolveRequestedProcess(request.ProcessId);
                if (processIdentity is null)
                {
                    if (allowsPreviewScope)
                    {
                        scopes.Add(SessionAccessScope.Unscoped(capability));
                        continue;
                    }

                    return SessionAccessScopeResolution.Invalid(
                        "ProcessScopeRequired",
                        $"{capability} requires processId or an active connected process.");
                }

                scopes.Add(SessionAccessScope.ForProcess(
                    capability,
                    processIdentity.Value.ProcessId,
                    processIdentity.Value.StartTimeUtcTicks,
                    processIdentity.Value.ExecutablePath));
                continue;
            }

            if (capability == SessionAccessCapabilities.ProjectWrite)
            {
                if (!TryNormalizeProjectRoot(request.ProjectRoot, out var projectRoot, out var error))
                {
                    return SessionAccessScopeResolution.Invalid("InvalidProjectRoot", error!);
                }

                scopes.Add(SessionAccessScope.ForProject(capability, projectRoot!));
                continue;
            }

            if (capability == SessionAccessCapabilities.ComposerRuntimeApproval)
            {
                if (string.IsNullOrWhiteSpace(request.PackRef))
                {
                    return SessionAccessScopeResolution.Invalid(
                        "PackScopeRequired",
                        "composer-runtime-approval requires packRef.");
                }

                scopes.Add(SessionAccessScope.ForPack(capability, request.PackRef));
                continue;
            }

            scopes.Add(SessionAccessScope.Unscoped(capability));
        }

        return SessionAccessScopeResolution.Valid(scopes);
    }

    private TargetProcessIdentity? ResolveRequestedProcess(int? requestedProcessId)
    {
        var processId = requestedProcessId ?? _activeProcessResolver();
        return processId is > 0 ? _processIdentityResolver(processId.Value) : null;
    }

    private static TargetProcessIdentity? ResolveProcessIdentity(
        WpfProcessDetector detector,
        int processId)
    {
        try
        {
            var processInfo = detector.GetProcessInfo(processId);
            if (processInfo?.ExecutablePath is not { Length: > 0 } executablePath)
            {
                return null;
            }

            using var process = Process.GetProcessById(processId);
            return new TargetProcessIdentity(
                processId,
                process.StartTime.ToUniversalTime().Ticks,
                executablePath);
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException
                                   or System.ComponentModel.Win32Exception
                                   or NotSupportedException)
        {
            return null;
        }
    }

    private static bool TryNormalizeProjectRoot(
        string? value,
        out string? projectRoot,
        out string? error)
    {
        projectRoot = null;
        error = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            error = "projectRoot is required for project-write access.";
            return false;
        }

        try
        {
            projectRoot = Path.GetFullPath(value)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!ProjectWritePolicy.IsLocalAbsolutePath(projectRoot)
                || ProjectWritePolicy.IsSystemDirectoryPath(projectRoot))
            {
                error = "projectRoot must be a non-system local absolute path.";
                return false;
            }

            if (ProjectWritePolicy.FindReparsePoint(projectRoot, projectRoot) is not null)
            {
                error = "projectRoot must not use a reparse point.";
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            error = "projectRoot is not a valid local absolute path.";
            return false;
        }
    }
}

internal sealed record SessionAccessScopeResolution(
    bool Success,
    IReadOnlyList<SessionAccessScope> Scopes,
    string? ErrorCode,
    string? Error)
{
    internal static SessionAccessScopeResolution Valid(IReadOnlyList<SessionAccessScope> scopes)
        => new(true, scopes, null, null);

    internal static SessionAccessScopeResolution Invalid(string errorCode, string error)
        => new(false, [], errorCode, error);
}
