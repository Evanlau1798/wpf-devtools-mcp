using WpfDevTools.Injector.Discovery;

namespace WpfDevTools.Mcp.Server.Tools;

public readonly record struct McpTargetAuthorization(
    bool IsAllowed,
    string? Error,
    string? Hint);

internal static class McpTargetPolicy
{
    public static McpTargetAuthorization Authorize(WpfProcessInfo processInfo)
        => Authorize(
            processInfo,
            Environment.GetEnvironmentVariable(McpServerConfiguration.AllowedTargetsEnvVar),
            RawInjectionTargetPolicy.ResolvePhysicalPathForPolicy);

    internal static McpTargetAuthorization Authorize(
        WpfProcessInfo processInfo,
        string? configuredAllowedTargets,
        Func<string, string?> tryResolvePhysicalPath)
        => Authorize(
            processInfo,
            configuredAllowedTargets,
            path =>
            {
                var resolvedPath = tryResolvePhysicalPath(path);
                return resolvedPath is null
                    ? PhysicalPathResolution.Unresolved()
                    : PhysicalPathResolution.Resolved(resolvedPath);
            });

    private static McpTargetAuthorization Authorize(
        WpfProcessInfo processInfo,
        string? configuredAllowedTargets,
        Func<string, PhysicalPathResolution> resolvePhysicalPath)
    {
        if (string.IsNullOrWhiteSpace(configuredAllowedTargets))
        {
            return new McpTargetAuthorization(
                IsAllowed: false,
                Error: "MCP target allowlist is not configured.",
                Hint: $"Set {McpServerConfiguration.AllowedTargetsEnvVar} to a semicolon-separated list of exact absolute executable paths, then retry connect(processId).");
        }

        var configuredTargetEntries = configuredAllowedTargets.Split(
            ';',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (configuredTargetEntries.Length == 0)
        {
            return CreateInvalidConfigurationAuthorization();
        }

        var configuredTargets = new HashSet<string>(RawInjectionTargetPolicy.PathComparer);
        foreach (var configuredTargetEntry in configuredTargetEntries)
        {
            if (!RawInjectionTargetPolicy.TryNormalizeAbsolutePath(
                    configuredTargetEntry,
                    resolvePhysicalPath,
                    out var normalizedConfiguredTarget))
            {
                return CreateInvalidConfigurationAuthorization();
            }

            configuredTargets.Add(normalizedConfiguredTarget);
        }

        if (!RawInjectionTargetPolicy.TryNormalizeAbsolutePath(
            processInfo.ExecutablePath,
            resolvePhysicalPath,
            out var normalizedTargetPath))
        {
            return new McpTargetAuthorization(
                IsAllowed: false,
                Error: "MCP target allowlisting is enabled, but the target executable path is missing or not a local absolute path.",
                Hint: $"Set {McpServerConfiguration.AllowedTargetsEnvVar} to a semicolon-separated list of exact absolute executable paths, then retry connect(processId).");
        }

        if (configuredTargets.Contains(normalizedTargetPath, RawInjectionTargetPolicy.PathComparer))
        {
            return new McpTargetAuthorization(IsAllowed: true, Error: null, Hint: null);
        }

        return new McpTargetAuthorization(
            IsAllowed: false,
            Error: "Target is blocked by the MCP target allowlist.",
            Hint: $"Add the exact absolute executable path to {McpServerConfiguration.AllowedTargetsEnvVar} only after reviewing the target process. The full denied path is written only to server diagnostics.");
    }

    private static McpTargetAuthorization CreateInvalidConfigurationAuthorization()
        => new(
            IsAllowed: false,
            Error: "Invalid MCP target allowlist configuration. Every configured entry must be an exact local absolute executable path.",
            Hint: $"Fix {McpServerConfiguration.AllowedTargetsEnvVar} to a semicolon-separated list of exact local absolute executable paths, then restart the MCP server.");
}
