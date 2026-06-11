using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Documentation;

public class NamedPipeClientLockOrderingDocumentationTests
{
    [Fact]
    public void NamedPipeClient_ShouldDocumentPipeSemaphoreAndLockOrdering()
    {
        var content = File.ReadAllText(GetRepoFilePath(
            "src/WpfDevTools.Mcp.Server/NamedPipeClient.cs"));

        content.Should().Contain("_pipeSemaphore is the outer async serialization primitive");
        content.Should().Contain("_lock protects local pipe state");
        content.Should().Contain("Do not wait on _pipeSemaphore while holding _lock");
        content.Should().Contain("acquire _pipeSemaphore first");
    }

    private static string GetRepoFilePath(string relativePath)
        => TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}