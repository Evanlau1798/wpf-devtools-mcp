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
    [Description("CATEGORY: Process\nReports session access and exact request inputs; never grants it.")]
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
            (_, _) => Task.FromResult<object>(BuildStatus(service, processId, projectRoot, packRef)),
            null,
            cancellationToken,
            toolName: "get_access_status");
    }

    [McpServerTool(Name = "request_session_access", Title = "Request Temporary Session Access", OpenWorld = false, Destructive = true, Idempotent = false, UseStructuredContent = true)]
    [Description("CATEGORY: Process\nRequests exact temporary access through server elicitation; Agent text is not authorization.")]
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

    private static object BuildStatus(
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
                return new
                {
                    capability,
                    status = status.Success ? status.Status : "scope-required",
                    status.ErrorCode,
                    status.Error
                };
            })
            .ToArray();
        var missing = capabilities
            .Where(item => item.status == "consent-required")
            .Select(item => item.capability)
            .ToArray();

        return new
        {
            success = true,
            capabilities,
            missingCapabilities = missing,
            suggestedRequest = missing.Length == 0 ? null : new
            {
                tool = "request_session_access",
                capabilities = missing,
                processId,
                projectRoot,
                packRef,
                reason = "Explain why this temporary access is needed.",
                lifetime = "session"
            }
        };
    }
}
