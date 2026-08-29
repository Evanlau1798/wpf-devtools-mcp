using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace WpfDevTools.Mcp.Server.McpTools;

[McpServerToolType]
public static class AccessMcpTools
{
    [McpServerTool(Name = "get_access_status", Title = "Get Session Access Status", OpenWorld = false, ReadOnly = true, Idempotent = true, UseStructuredContent = true)]
    [Description(AccessMcpToolDescriptions.GetStatus)]
    public static Task<CallToolResult> GetAccessStatus(
        ModelContextProtocol.Server.McpServer server,
        [Range(1, int.MaxValue)]
        [Description("Target PID; omit to use the active process.")] int? processId = null,
        [Description("Exact project root for project-write.")] string? projectRoot = null,
        [Description("Exact pack reference for runtime approval.")] string? packRef = null,
        CancellationToken cancellationToken = default)
    {
        var service = (server.Services ?? throw new InvalidOperationException("MCP services are unavailable."))
            .GetRequiredService<SessionAccessRequestService>();
        return ToolCallHelper.ExecuteAndWrapAsync(
            (_, _) => Task.FromResult<object>(BuildStatus(
                service,
                processId,
                projectRoot,
                packRef)),
            null,
            cancellationToken,
            toolName: "get_access_status");
    }

    [McpServerTool(Name = "request_session_access", Title = "Request Temporary Session Access", OpenWorld = false, Destructive = true, Idempotent = false, UseStructuredContent = true)]
    [Description(AccessMcpToolDescriptions.Request)]
    public static Task<CallToolResult> RequestSessionAccess(
        ModelContextProtocol.Server.McpServer server,
        [MinLength(1), MaxLength(9)]
        [Description("Capabilities returned by get_access_status.")] string[] capabilities,
        [Required, MaxLength(256)]
        [Description("Short untrusted reason shown in the consent prompt.")] string reason,
        [Range(1, int.MaxValue)]
        [Description("Target PID; omit to use the active process.")] int? processId = null,
        [Description("Exact root required by project-write.")] string? projectRoot = null,
        [Description("Exact pack reference required by runtime approval.")] string? packRef = null,
        [AllowedValues("session", "once")]
        [Description("'session' (30 minutes/disconnect) or 'once'; raw injection requires once.")] string lifetime = "session",
        CancellationToken cancellationToken = default)
    {
        var service = (server.Services ?? throw new InvalidOperationException("MCP services are unavailable."))
            .GetRequiredService<SessionAccessRequestService>();
        var request = new SessionAccessRequest(
            capabilities,
            processId,
            projectRoot,
            packRef,
            reason,
            string.Equals(lifetime, "once", StringComparison.Ordinal)
                ? SessionAccessLifetime.Once
                : SessionAccessLifetime.Session);

        return ToolCallHelper.ExecuteAndWrapAsync(
            async (_, ct) => await service.RequestAsync(
                request,
                supportsElicitation: server.ClientCapabilities?.Elicitation is not null,
                async (prompt, token) => await server.ElicitAsync(prompt, token).ConfigureAwait(false),
                ct).ConfigureAwait(false),
            null,
            cancellationToken,
            toolName: "request_session_access");
    }

    internal static object BuildStatus(
        SessionAccessRequestService service,
        int? processId,
        string? projectRoot,
        string? packRef)
    {
        var capabilities = SessionAccessCapabilities.All
            .Order(StringComparer.Ordinal)
            .Select(capability =>
            {
                var request = new SessionAccessRequest(
                    [capability],
                    processId,
                    projectRoot,
                    packRef,
                    null,
                    capability == SessionAccessCapabilities.RawInjection
                        ? SessionAccessLifetime.Once
                        : SessionAccessLifetime.Session);
                var status = service.GetStatus(request);
                var reportedStatus = status.Status == "invalid"
                    && status.ErrorCode is "ProcessScopeRequired" or "PackScopeRequired" or "InvalidProjectRoot"
                        ? "scope-required"
                        : status.Status;
                return new
                {
                    capability,
                    status = reportedStatus,
                    status.ErrorCode,
                    status.Error
                };
            })
            .ToArray();
        var missing = capabilities
            .Where(item => item.status is not "granted" and not "preauthorized")
            .Select(item => item.capability)
            .ToArray();
        var requestable = capabilities
            .Where(item => item.status == "consent-required")
            .Select(item => item.capability)
            .ToArray();
        var unavailable = capabilities
            .Where(item => item.status is "hard-denied" or "invalid-policy" or "scope-required")
            .Select(item => item.capability)
            .ToArray();
        var sessionCapabilities = requestable
            .Where(capability => capability != SessionAccessCapabilities.RawInjection)
            .ToArray();
        var suggestedRequests = new List<object>(2);
        if (sessionCapabilities.Length > 0)
        {
            suggestedRequests.Add(CreateSuggestedRequest(
                sessionCapabilities, processId, projectRoot, packRef, "session"));
        }

        if (requestable.Contains(SessionAccessCapabilities.RawInjection, StringComparer.Ordinal))
        {
            suggestedRequests.Add(CreateSuggestedRequest(
                [SessionAccessCapabilities.RawInjection], processId, projectRoot, packRef, "once"));
        }

        return new
        {
            success = true,
            capabilities,
            missingCapabilities = missing,
            requestableCapabilities = requestable,
            unavailableCapabilities = unavailable,
            suggestedRequests
        };
    }

    private static object CreateSuggestedRequest(
        string[] capabilities,
        int? processId,
        string? projectRoot,
        string? packRef,
        string lifetime)
        => new
        {
            tool = "request_session_access",
            capabilities,
            processId,
            projectRoot,
            packRef,
            reason = "Explain why this temporary access is needed.",
            lifetime
        };
}
