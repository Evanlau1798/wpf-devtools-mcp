using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class CodeSigningDocumentationTests
{
    [Fact]
    public void CodeSigningGuide_ShouldNotOverclaimEvSmartScreenBypass()
    {
        var content = File.ReadAllText(GetRepoFilePath("CODE_SIGNING.md"));

        content.Should().NotContain("Immediate SmartScreen reputation");
        content.Should().NotContain("No warnings on first download");
        content.Should().NotContain("eliminates SmartScreen warnings");
        content.Should().NotContain("✅ Immediate");
        content.Should().Contain("EV certificates no longer bypass SmartScreen");
        content.Should().Contain("Azure Artifact Signing");
        content.Should().Contain("reputation builds over time");
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}
