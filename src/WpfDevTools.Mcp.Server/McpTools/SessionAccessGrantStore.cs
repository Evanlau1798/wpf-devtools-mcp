namespace WpfDevTools.Mcp.Server.McpTools;

internal static class SessionAccessCapabilities
{
    internal const string TargetConnect = "target-connect";
    internal const string RawInjection = "raw-injection";
    internal const string SensitiveRead = "sensitive-read";
    internal const string Screenshot = "screenshot";
    internal const string ProjectWrite = "project-write";
    internal const string RuntimeMutation = "runtime-mutation";
    internal const string ViewModelInspection = "viewmodel-inspection";
    internal const string ComposerPreview = "composer-preview";
    internal const string ComposerRuntimeApproval = "composer-runtime-approval";

    internal static readonly HashSet<string> All =
    [
        TargetConnect,
        RawInjection,
        SensitiveRead,
        Screenshot,
        ProjectWrite,
        RuntimeMutation,
        ViewModelInspection,
        ComposerPreview,
        ComposerRuntimeApproval
    ];
}

internal enum SessionAccessLifetime
{
    Once,
    Session
}

internal sealed record SessionAccessScope
{
    private SessionAccessScope(
        string capability,
        int? processId,
        long? processStartTimeUtcTicks,
        string resource)
    {
        if (!SessionAccessCapabilities.All.Contains(capability))
        {
            throw new ArgumentOutOfRangeException(nameof(capability));
        }

        Capability = capability;
        ProcessId = processId;
        ProcessStartTimeUtcTicks = processStartTimeUtcTicks;
        Resource = resource;
        Key = $"{capability}|{processId?.ToString() ?? "-"}|{processStartTimeUtcTicks?.ToString() ?? "-"}|{resource}";
    }

    internal string Capability { get; }
    internal int? ProcessId { get; }
    internal long? ProcessStartTimeUtcTicks { get; }
    internal string Resource { get; }
    internal string Key { get; }

    internal static SessionAccessScope ForProcess(
        string capability,
        int processId,
        long processStartTimeUtcTicks,
        string executablePath)
    {
        if (processId <= 0 || processStartTimeUtcTicks <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(processId));
        }

        return new SessionAccessScope(
            capability,
            processId,
            processStartTimeUtcTicks,
            NormalizePath(executablePath));
    }

    internal static SessionAccessScope ForProject(string capability, string projectRoot)
        => new(capability, null, null, NormalizePath(projectRoot));

    internal static SessionAccessScope ForPack(string capability, string packRef)
    {
        if (string.IsNullOrWhiteSpace(packRef))
        {
            throw new ArgumentException("packRef is required.", nameof(packRef));
        }

        return new SessionAccessScope(capability, null, null, packRef.Trim());
    }

    internal static SessionAccessScope Unscoped(string capability)
        => new(capability, null, null, string.Empty);

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A local absolute path is required.", nameof(path));
        }

        var normalized = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return normalized.ToUpperInvariant();
    }
}

internal sealed class SessionAccessGrantStore : IDisposable
{
    internal static readonly TimeSpan SessionGrantDuration = TimeSpan.FromMinutes(30);

    private readonly object _gate = new();
    private readonly Dictionary<string, GrantEntry> _grants = new(StringComparer.Ordinal);
    private readonly Func<DateTimeOffset> _utcNow;
    private bool _disposed;

    internal SessionAccessGrantStore()
        : this(() => DateTimeOffset.UtcNow)
    {
    }

    internal SessionAccessGrantStore(Func<DateTimeOffset> utcNow)
    {
        _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
    }

    internal bool Grant(SessionAccessScope scope, SessionAccessLifetime lifetime)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.Capability == SessionAccessCapabilities.RawInjection
            && lifetime != SessionAccessLifetime.Once)
        {
            return false;
        }

        lock (_gate)
        {
            if (_disposed)
            {
                return false;
            }

            var now = _utcNow();
            _grants[scope.Key] = new GrantEntry(
                ExpiresAt: now + SessionGrantDuration,
                RemainingUses: lifetime == SessionAccessLifetime.Once ? 1 : null);
            return true;
        }
    }

    internal bool HasGrant(SessionAccessScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        lock (_gate)
        {
            return TryGetActiveGrant(scope.Key, out _);
        }
    }

    internal bool TryConsume(SessionAccessScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        lock (_gate)
        {
            if (!TryGetActiveGrant(scope.Key, out var grant))
            {
                return false;
            }

            if (grant.RemainingUses is null)
            {
                return true;
            }

            _grants.Remove(scope.Key);
            return grant.RemainingUses > 0;
        }
    }

    private bool TryGetActiveGrant(string key, out GrantEntry grant)
    {
        grant = default;
        if (_disposed || !_grants.TryGetValue(key, out var candidate))
        {
            return false;
        }

        if (_utcNow() >= candidate.ExpiresAt || candidate.RemainingUses is <= 0)
        {
            _grants.Remove(key);
            return false;
        }

        grant = candidate;
        return true;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _grants.Clear();
        }
    }

    private readonly record struct GrantEntry(DateTimeOffset ExpiresAt, int? RemainingUses);
}
