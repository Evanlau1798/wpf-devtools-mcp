using Microsoft.Extensions.DependencyInjection;
using WpfDevTools.Mcp.Server.Composer.Apply;

namespace WpfDevTools.Mcp.Server.McpTools;

public static partial class UiComposerMcpTools
{
    private static Func<string, ProjectWriteAuthorization>? CreateProjectWriteAuthorizer(
        ModelContextProtocol.Server.McpServer? server)
    {
        var access = server?.Services?.GetService<SessionAccessRequestService>();
        return access is null
            ? null
            : projectRoot => ProjectWritePolicy.AuthorizeSession(
                projectRoot,
                normalizedRoot => access.TryConsume(new SessionAccessRequest(
                    [SessionAccessCapabilities.ProjectWrite],
                    null,
                    normalizedRoot,
                    null,
                    null,
                    SessionAccessLifetime.Session)));
    }
}
