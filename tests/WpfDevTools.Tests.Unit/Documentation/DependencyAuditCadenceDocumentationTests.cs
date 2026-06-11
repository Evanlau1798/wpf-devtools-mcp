using FluentAssertions;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class DependencyAuditCadenceDocumentationTests
{
    [Theory]
    [InlineData("SECURITY.md")]
    [InlineData("RELEASING.md")]
    [InlineData("docfx/production/deployment.md")]
    public void ReleaseDocs_ShouldDocumentDependencyAuditCadence(string relativePath)
    {
        var content = File.ReadAllText(TestRepositoryPaths.GetRepoFilePath(relativePath));

        content.Should().Contain("dotnet list package --vulnerable");
        content.Should().Contain("NuGet audit");
        content.Should().Contain("dotnet restore --locked-mode");
        content.Should().Contain("ModelContextProtocol");
        content.Should().Contain("System.Text.Json");
        content.Should().Contain("GitHub Actions");
        content.Should().Contain("verified advisories");
        content.Should().Contain("speculative CVE claims");
    }
}
