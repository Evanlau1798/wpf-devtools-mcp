using FluentAssertions;
using WpfDevTools.Mcp.Server;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class SessionManagerPipeNameTests
{
    [Fact]
    public void CreateDetachedPipeClient_WithExplicitPipeName_ShouldUseInjectionPipeName()
    {
        using var sessionManager = new SessionManager();
        var processId = NextSyntheticProcessId();
        var pipeName = $"WpfDevTools_{processId}_{Guid.NewGuid():N}";

        using var client = sessionManager.CreateDetachedPipeClient(processId, pipeName);

        client.PipeName.Should().Be(pipeName,
            "injected sessions must attach to the randomized bootstrap pipe name rather than reconstructing a predictable PID-only pipe");
    }
}
