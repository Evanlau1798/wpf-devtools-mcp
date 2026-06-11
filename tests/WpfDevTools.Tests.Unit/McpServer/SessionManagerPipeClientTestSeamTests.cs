using FluentAssertions;
using WpfDevTools.Mcp.Server;
using static WpfDevTools.Tests.Unit.TestHelpers;

namespace WpfDevTools.Tests.Unit.McpServer;

public sealed class SessionManagerPipeClientTestSeamTests
{
    [Fact]
    public void ReplacePipeClientForTesting_WhenReplacementIsNull_ShouldThrow()
    {
        using var manager = new SessionManager();
        var processId = NextSyntheticProcessId();
        manager.AddSession(processId);

        var act = () => manager.ReplacePipeClientForTesting(processId, null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ReplacePipeClientForTesting_WhenSessionDoesNotExist_ShouldThrow()
    {
        using var manager = new SessionManager();
        var processId = NextSyntheticProcessId();
        using var replacement = new NamedPipeClient(processId, CreateUniquePipeName());

        var act = () => manager.ReplacePipeClientForTesting(processId, replacement);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"Session for process {processId} does not exist");
    }

    [Fact]
    public void ReplacePipeClientForTesting_WhenManagerIsDisposed_ShouldThrow()
    {
        var manager = new SessionManager();
        var processId = NextSyntheticProcessId();
        using var replacement = new NamedPipeClient(processId, CreateUniquePipeName());
        manager.Dispose();

        var act = () => manager.ReplacePipeClientForTesting(processId, replacement);

        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void ReplacePipeClientForTesting_ShouldReplaceStoredClient()
    {
        using var manager = new SessionManager();
        var processId = NextSyntheticProcessId();
        var replacement = new NamedPipeClient(processId, CreateUniquePipeName());
        manager.AddSession(processId);

        try
        {
            manager.ReplacePipeClientForTesting(processId, replacement);

            manager.GetPipeClient(processId).Should().BeSameAs(replacement);
        }
        finally
        {
            replacement.Dispose();
        }
    }
}