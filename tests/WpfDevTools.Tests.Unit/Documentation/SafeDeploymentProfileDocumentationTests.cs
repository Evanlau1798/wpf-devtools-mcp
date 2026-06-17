using FluentAssertions;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class SafeDeploymentProfileDocumentationTests
{
    [Theory]
    [InlineData("docfx/production/security.md")]
    [InlineData("docfx/zh-tw/production/security.md")]
    public void SecurityDocs_ShouldPublishSafeDeploymentProfileMatrix(string relativePath)
    {
        var content = File.ReadAllText(TestRepositoryPaths.GetRepoFilePath(relativePath));

        content.Should().Contain("Safe deployment profiles");
        content.Should().Contain("Read-only diagnostics");
        content.Should().Contain("Screenshot-enabled diagnostics");
        content.Should().Contain("ViewModel-enabled diagnostics");
        content.Should().Contain("Mutation-enabled diagnostics");
        content.Should().Contain("Raw-injection emergency diagnostics");
    }

    [Theory]
    [InlineData("docfx/production/security.md")]
    [InlineData("docfx/zh-tw/production/security.md")]
    public void SecurityDocs_ShouldUseMobileReadableSafeDeploymentProfileSections(string relativePath)
    {
        var content = File.ReadAllText(TestRepositoryPaths.GetRepoFilePath(relativePath));

        content.Should().NotContain("| Profile |");
        content.Should().NotContain("| 適用情境 |");
        content.Should().NotContain("|---|---|---|---|");
        content.Should().Contain("### Read-only diagnostics");
        content.Should().Contain("### Screenshot-enabled diagnostics");
        content.Should().Contain("### ViewModel-enabled diagnostics");
        content.Should().Contain("### Mutation-enabled diagnostics");
        content.Should().Contain("### Raw-injection emergency diagnostics");
    }

    [Theory]
    [InlineData("docfx/production/security.md")]
    [InlineData("docfx/zh-tw/production/security.md")]
    public void SecurityDocs_ShouldListExactGatesForSafeDeploymentProfiles(string relativePath)
    {
        var content = File.ReadAllText(TestRepositoryPaths.GetRepoFilePath(relativePath));

        content.Should().Contain("WPFDEVTOOLS_MCP_ALLOWED_TARGETS");
        content.Should().Contain("WPFDEVTOOLS_MCP_ALLOW_SENSITIVE_READS");
        content.Should().Contain("WPFDEVTOOLS_MCP_ALLOW_SCREENSHOTS");
        content.Should().Contain("WPFDEVTOOLS_MCP_ALLOW_VIEWMODEL_INSPECTION");
        content.Should().Contain("WPFDEVTOOLS_MCP_ALLOW_DESTRUCTIVE_TOOLS");
        content.Should().Contain("WPFDEVTOOLS_INJECTION_ALLOWED_TARGETS");
    }

    [Theory]
    [InlineData("docfx/production/security.md")]
    [InlineData("docfx/zh-tw/production/security.md")]
    public void SecurityDocs_ShouldDescribeExpectedBlockedToolsForEachProfile(string relativePath)
    {
        var content = File.ReadAllText(TestRepositoryPaths.GetRepoFilePath(relativePath));

        content.Should().Contain("get_ui_summary");
        content.Should().Contain("element_screenshot");
        content.Should().Contain("get_viewmodel");
        content.Should().Contain("modify_viewmodel");
        content.Should().Contain("set_dp_value");
        content.Should().Contain("click_element");
        content.Should().Contain("batch_mutate");
        content.Should().Contain("blocked");
    }
}
