using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class MobileReadableDocumentationTests
{
    [Theory]
    [InlineData("docfx/production/threat-model.md", "## Accepted-risk register", "## Security contact")]
    [InlineData("docfx/zh-tw/production/threat-model.md", "## Accepted-risk register", "## Security contact")]
    public void ThreatModelAcceptedRisks_ShouldUseMobileReadableSections(
        string relativePath,
        string startHeading,
        string endHeading)
    {
        var section = ReadSection(relativePath, startHeading, endHeading);

        section.Should().NotContain("| Risk |");
        section.Should().NotContain("| Status |");
        section.Should().Contain("### Same-user code remains inside the local trust boundary");
        section.Should().Contain("### Raw injection remains an emergency path");
        section.Should().Contain("### STDIO is single-session only");
    }

    [Theory]
    [InlineData("docfx/production/compatibility-matrix.md", "## Known unsupported or constrained scenarios", "## Practical guidance")]
    [InlineData("docfx/zh-tw/production/compatibility-matrix.md", "## 已知不支援或受限的情境", "## 實務建議")]
    public void CompatibilityConstrainedScenarios_ShouldUseMobileReadableSections(
        string relativePath,
        string startHeading,
        string endHeading)
    {
        var section = ReadSection(relativePath, startHeading, endHeading);

        section.Should().NotContain("| Scenario |");
        section.Should().NotContain("| Raw injection");
        section.Should().Contain("### Self-contained single-file WPF app");
        section.Should().Contain("### Native AOT");
        section.Should().Contain("### Trimmed app");
        section.Should().Contain("### Non-WPF desktop UI");
    }

    [Theory]
    [InlineData("docfx/reference/configuration.md", "## MCP server runtime variables", "## Installer and package variables", "Variable")]
    [InlineData("docfx/zh-tw/reference/configuration.md", "## MCP server runtime 變數", "## Installer 與 package 變數", "變數")]
    public void ConfigurationRuntimeVariables_ShouldUseMobileReadableSections(
        string relativePath,
        string startHeading,
        string endHeading,
        string headerText)
    {
        var section = ReadSection(relativePath, startHeading, endHeading);

        section.Should().NotContain($"| {headerText} |");
        section.Should().Contain("### WPFDEVTOOLS_AUTH_SECRET");
        section.Should().Contain("### WPFDEVTOOLS_MCP_ALLOWED_TARGETS");
        section.Should().Contain("### WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS");
    }

    [Theory]
    [InlineData("docfx/reference/configuration.md", "## Installer and package variables", "## Build modes", "Variable")]
    [InlineData("docfx/zh-tw/reference/configuration.md", "## Installer 與 package 變數", "## Build 模式", "變數")]
    public void ConfigurationInstallerVariables_ShouldUseMobileReadableSections(
        string relativePath,
        string startHeading,
        string endHeading,
        string headerText)
    {
        var section = ReadSection(relativePath, startHeading, endHeading);

        section.Should().NotContain($"| {headerText} |");
        section.Should().Contain("### WPFDEVTOOLS_SKIP_ELEVATION");
        section.Should().Contain("### WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT");
    }

    private static string ReadSection(string relativePath, string startHeading, string endHeading)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));
        var startIndex = content.IndexOf(startHeading, StringComparison.Ordinal);
        startIndex.Should().BeGreaterThanOrEqualTo(0, $"{relativePath} should contain {startHeading}");
        var endIndex = content.IndexOf(endHeading, startIndex + startHeading.Length, StringComparison.Ordinal);
        endIndex.Should().BeGreaterThan(startIndex, $"{relativePath} should contain {endHeading} after {startHeading}");
        return content[startIndex..endIndex];
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}
