using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace WpfDevTools.Mcp.Server.McpTools;

internal sealed record SessionAccessRequest(
    IReadOnlyList<string> Capabilities,
    int? ProcessId,
    string? ProjectRoot,
    string? PackRef,
    string? Reason,
    SessionAccessLifetime Lifetime);

internal sealed record SessionAccessRequestResult(
    bool Success,
    string Status,
    IReadOnlyList<SessionAccessScope> Scopes,
    string? ErrorCode = null,
    string? Error = null,
    string? Hint = null,
    DateTimeOffset? ExpiresAt = null);

internal sealed class SessionAccessRequestService
{
    private const int MaximumReasonLength = 256;
    private const int MaximumPromptsPerMinute = 3;

    private readonly SessionAccessGrantStore _grantStore;
    private readonly SessionAccessScopeResolver _scopeResolver;
    private readonly Func<DateTimeOffset> _utcNow;
    private readonly SemaphoreSlim _promptGate = new(1, 1);
    private readonly Queue<DateTimeOffset> _recentPrompts = new();

    internal SessionAccessRequestService(
        SessionAccessGrantStore grantStore,
        SessionAccessScopeResolver scopeResolver)
        : this(grantStore, scopeResolver, () => DateTimeOffset.UtcNow)
    {
    }

    internal SessionAccessRequestService(
        SessionAccessGrantStore grantStore,
        SessionAccessScopeResolver scopeResolver,
        Func<DateTimeOffset> utcNow)
    {
        _grantStore = grantStore ?? throw new ArgumentNullException(nameof(grantStore));
        _scopeResolver = scopeResolver ?? throw new ArgumentNullException(nameof(scopeResolver));
        _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));
    }

    internal SessionAccessRequestResult GetStatus(SessionAccessRequest request)
    {
        var resolution = _scopeResolver.Resolve(request);
        if (!resolution.Success)
        {
            return Invalid(resolution);
        }

        var granted = resolution.Scopes.Where(_grantStore.HasGrant).ToArray();
        return new SessionAccessRequestResult(
            Success: true,
            Status: granted.Length == resolution.Scopes.Count ? "granted" : "consent-required",
            Scopes: resolution.Scopes);
    }

    internal bool TryConsume(SessionAccessRequest request)
    {
        var resolution = _scopeResolver.Resolve(request);
        return resolution.Success
               && resolution.Scopes.Count == 1
               && _grantStore.TryConsume(resolution.Scopes[0]);
    }

    internal async Task<SessionAccessRequestResult> RequestAsync(
        SessionAccessRequest request,
        bool supportsElicitation,
        Func<ElicitRequestParams, CancellationToken, Task<ElicitResult>> elicitAsync,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(elicitAsync);
        var resolution = _scopeResolver.Resolve(request);
        if (!resolution.Success)
        {
            return Invalid(resolution);
        }

        if (resolution.Scopes.All(_grantStore.HasGrant))
        {
            return new SessionAccessRequestResult(true, "already-granted", resolution.Scopes);
        }

        if (!supportsElicitation)
        {
            return new SessionAccessRequestResult(
                false,
                "unsupported",
                resolution.Scopes,
                "InteractiveConsentUnavailable",
                "The connected MCP client did not advertise elicitation support.",
                "Use a client with MCP elicitation support or an explicit operator environment policy.");
        }

        // ponytail: STDIO has one client; serialize prompts until concurrent-client transport exists.
        await _promptGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (resolution.Scopes.All(_grantStore.HasGrant))
            {
                return new SessionAccessRequestResult(true, "already-granted", resolution.Scopes);
            }

            if (!TryRecordPrompt())
            {
                return new SessionAccessRequestResult(
                    false,
                    "rate-limited",
                    resolution.Scopes,
                    "ConsentPromptRateLimited",
                    "Too many interactive access requests were made in one minute.",
                    "Wait before requesting access again, or continue with a read-only alternative.");
            }

            ElicitResult elicitation;
            try
            {
                elicitation = await elicitAsync(
                    CreatePrompt(request, resolution.Scopes),
                    cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                return new SessionAccessRequestResult(
                    false,
                    "unsupported",
                    resolution.Scopes,
                    "InteractiveConsentUnavailable",
                    "The MCP client could not process the elicitation request.",
                    "Use an explicit operator environment policy or a compatible MCP client.");
            }

            var action = elicitation.Action is "accept" or "decline" or "cancel"
                ? elicitation.Action
                : "cancel";
            if (action != "accept" || !HasExplicitConfirmation(elicitation))
            {
                return new SessionAccessRequestResult(false, action, resolution.Scopes);
            }

            if (resolution.Scopes.Any(scope => !_grantStore.Grant(scope, request.Lifetime)))
            {
                return new SessionAccessRequestResult(
                    false,
                    "rejected",
                    resolution.Scopes,
                    "InvalidAccessLifetime",
                    "The requested lifetime is not valid for every capability.");
            }

            return new SessionAccessRequestResult(
                true,
                "granted",
                resolution.Scopes,
                ExpiresAt: _utcNow() + SessionAccessGrantStore.SessionGrantDuration);
        }
        finally
        {
            _promptGate.Release();
        }
    }

    private bool TryRecordPrompt()
    {
        var now = _utcNow();
        while (_recentPrompts.TryPeek(out var timestamp)
               && now - timestamp >= TimeSpan.FromMinutes(1))
        {
            _recentPrompts.Dequeue();
        }

        if (_recentPrompts.Count >= MaximumPromptsPerMinute)
        {
            return false;
        }

        _recentPrompts.Enqueue(now);
        return true;
    }

    private static ElicitRequestParams CreatePrompt(
        SessionAccessRequest request,
        IReadOnlyList<SessionAccessScope> scopes)
    {
        var capabilities = string.Join(", ", scopes.Select(scope => scope.Capability));
        var resources = string.Join(", ", scopes.Select(DescribeScope).Distinct(StringComparer.Ordinal));
        var reason = NormalizeReason(request.Reason);
        var lifetime = request.Lifetime == SessionAccessLifetime.Once
            ? "Access expires after one use, 30 minutes, or when this MCP session ends. "
            : "Access expires after 30 minutes or when this MCP session ends. ";
        var message =
            $"WPF DevTools requests temporary access. Capabilities: {capabilities}. " +
            $"Scope: {resources}. {lifetime}" +
            $"Agent-provided reason (untrusted): {reason}";
        return new ElicitRequestParams
        {
            Mode = "form",
            Message = message,
            RequestedSchema = new ElicitRequestParams.RequestSchema
            {
                Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition>
                {
                    ["confirm"] = new ElicitRequestParams.BooleanSchema
                    {
                        Title = "Approve temporary access",
                        Description = "Confirm only after reviewing the capabilities and exact scope above.",
                        Default = false
                    }
                },
                Required = ["confirm"]
            }
        };
    }

    private static string DescribeScope(SessionAccessScope scope)
        => scope.ProcessId is int processId
            ? $"process {processId} ({scope.Resource})"
            : string.IsNullOrEmpty(scope.Resource)
                ? "current MCP session"
                : scope.Resource;

    private static string NormalizeReason(string? reason)
    {
        var flattened = string.IsNullOrWhiteSpace(reason)
            ? "No reason supplied."
            : reason.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return flattened.Length <= MaximumReasonLength
            ? flattened
            : flattened[..MaximumReasonLength];
    }

    private static bool HasExplicitConfirmation(ElicitResult result)
        => result.Content?.TryGetValue("confirm", out var confirm) == true
           && confirm.ValueKind == JsonValueKind.True;

    private static SessionAccessRequestResult Invalid(SessionAccessScopeResolution resolution)
        => new(
            false,
            "invalid",
            resolution.Scopes,
            resolution.ErrorCode,
            resolution.Error);
}
