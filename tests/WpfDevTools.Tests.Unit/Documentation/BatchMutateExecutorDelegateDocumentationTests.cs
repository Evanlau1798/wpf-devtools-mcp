using FluentAssertions;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class BatchMutateExecutorDelegateDocumentationTests
{
    [Fact]
    public void BatchMutateTool_ShouldUseNamedExecutorDelegates()
    {
        var content = File.ReadAllText(TestRepositoryPaths.GetRepoFilePath(
            "src/WpfDevTools.Mcp.Server/Tools/BatchMutateTool.cs"));

        content.Should().Contain("internal delegate Task<object> BatchMutationExecutor");
        content.Should().Contain("internal delegate Task<object> BatchJsonExecutor");
        content.Should().Contain("private readonly BatchMutationExecutor _mutationExecutor");
        content.Should().NotContain("Func<string, JsonElement, CancellationToken, Task<object>>");
    }
}