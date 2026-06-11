using FluentAssertions;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class ExternalReviewSecurityPackageTests
{
    [Theory]
    [InlineData("docfx/production/threat-model.md")]
    [InlineData("docfx/zh-tw/production/threat-model.md")]
    public void ThreatModel_ShouldPublishAuditableAcceptedRiskRegister(string relativePath)
    {
        var content = Read(relativePath);

        content.Should().Contain("Owner");
        content.Should().Contain("Accepted date");
        content.Should().Contain("Revisit date");
        content.Should().Contain("Release owner");
        content.Should().Contain("2026-06-01");
    }

    [Theory]
    [InlineData("SECURITY.md")]
    [InlineData("docfx/production/threat-model.md")]
    [InlineData("docfx/zh-tw/production/threat-model.md")]
    public void SecurityDocs_ShouldPublishVulnerabilityHandlingProcess(string relativePath)
    {
        var content = Read(relativePath);

        content.Should().Contain("Security contact");
        content.Should().Contain("vulnerability");
        content.Should().Contain("GitHub Security Advisory");
        content.ToLowerInvariant().Should().Contain("do not publish exploit details");
    }

    private static string Read(string relativePath)
        => File.ReadAllText(TestRepositoryPaths.GetRepoFilePath(relativePath));
}
