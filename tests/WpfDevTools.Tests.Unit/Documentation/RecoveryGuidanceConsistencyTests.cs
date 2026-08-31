using FluentAssertions;
using WpfDevTools.Mcp.Server;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class RecoveryGuidanceConsistencyTests
{
    [Fact]
    public void RuntimeAndBilingualToolIndexes_ShouldUseNextStepsFirst()
    {
        var english = File.ReadAllText(TestSupport.TestRepositoryPaths.GetRepoFilePath(
            "docfx/reference/tools/index.md"));
        var traditionalChinese = File.ReadAllText(TestSupport.TestRepositoryPaths.GetRepoFilePath(
            "docfx/zh-tw/reference/tools/index.md"));

        ServerInstructions.Value.Should().Contain(
            "follow non-empty tool-specific nextSteps first; otherwise use navigation.recommended");
        english.Should().Contain(
            "Follow non-empty tool-specific `nextSteps` first; otherwise use `navigation.recommended`");
        traditionalChinese.Should().Contain(
            "先採用非空的 tool-specific `nextSteps`；否則使用 `navigation.recommended`");
    }
}
