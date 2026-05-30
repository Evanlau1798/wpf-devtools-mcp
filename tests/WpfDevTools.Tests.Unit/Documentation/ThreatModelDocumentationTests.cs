using FluentAssertions;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class ThreatModelDocumentationTests
{
    [Theory]
    [InlineData("docfx/production/threat-model.md")]
    [InlineData("docfx/zh-tw/production/threat-model.md")]
    public void ProductionThreatModel_ShouldCoverExternalReviewThreats(string relativePath)
    {
        var content = File.ReadAllText(TestRepositoryPaths.GetRepoFilePath(relativePath));

        content.Should().Contain("MCP client");
        content.Should().Contain("prompt-injected");
        content.Should().Contain("same-user");
        content.Should().Contain("malicious target process");
        content.Should().Contain("fake named-pipe");
        content.Should().Contain("MITM");
        content.Should().Contain("raw injection");
        content.Should().Contain("screenshot");
        content.Should().Contain("ViewModel");
        content.Should().Contain("supply-chain");
        content.Should().Contain("release tampering");
        content.Should().Contain("HTTP/SSE");
        content.Should().Contain("multi-session");
        content.Should().Contain("Out of scope");
        content.Should().Contain("Mitigations");
    }
}
