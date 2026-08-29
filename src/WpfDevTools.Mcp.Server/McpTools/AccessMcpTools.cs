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
    [Description("CATEGORY: access. Reports temporary session capabilities, missing grants, and exact request_session_access inputs. This tool never grants access.")]
    public static Task<CallToolResult> GetAccessStatus(
        ModelContextProtocol.Server.McpServer server,
        [Range(1, int.MaxValue)]
        [Description("Optional target process ID. Omit to use the active connected process when available.")] int? processId = null,
        [Description("Optional exact local project root for project-write status.")] string? projectRoot = null,
        [Description("Optional exact pack id@version#fingerprint reference for composer-runtime-approval status.")] string? packRef = null,
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
    [Description("CATEGORY: access. Requests a server-authored MCP elicitation prompt. Access is granted only when the user explicitly confirms the exact temporary scope; Agent text is never authorization.")]
    public static Task<CallToolResult> RequestSessionAccess(
        ModelContextProtocol.Server.McpServer server,
        [MinLength(1), MaxLength(9)]
        [Description("One or more capabilities returned by get_access_status: target-connect, raw-injection, sensitive-read, screenshot, project-write, runtime-mutation, viewmodel-inspection, composer-preview, or composer-runtime-approval.")] string[] capabilities,
        [Required, MaxLength(256)]
        [Description("Short Agent-provided reason shown as untrusted text in the server-authored consent prompt.")] string reason,
        [Range(1, int.MaxValue)]
        [Description("Optional exact target process ID. Omit to use the active connected process when available.")] int? processId = null,
        [Description("Optional exact local project root required by project-write.")] string? projectRoot = null,
        [Description("Optional exact pack id@version#fingerprint reference required by composer-runtime-approval.")] string? packRef = null,
        [AllowedValues("session", "once")]
        [Description("Temporary grant lifetime: 'session' (30 minutes or MCP disconnect) or 'once'. raw-injection requires 'once'.")] string lifetime = "session",
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
