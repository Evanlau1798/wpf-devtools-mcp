using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class ManualPackageVerificationDocumentationTests
{
    [Theory]
    [InlineData("docfx/quickstart/index.md", "run.bat")]
    [InlineData("docfx/production/deployment.md", "run.bat")]
    [InlineData("docfx/zh-tw/quickstart/index.md", "run.bat")]
    [InlineData("docfx/zh-tw/production/deployment.md", "run.bat")]
    public void CanonicalManualPackageDocs_ShouldVerifySidecarsBeforePackageLauncher(
        string relativePath,
        string runStep)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));
        var verificationIndex = content.IndexOf("SHA256SUMS.txt", StringComparison.Ordinal);
        var runIndex = content.IndexOf(runStep, StringComparison.Ordinal);

        verificationIndex.Should().BeGreaterThanOrEqualTo(0,
            $"{relativePath} should keep release provenance verification explicit");
        runIndex.Should().BeGreaterThanOrEqualTo(0,
            $"{relativePath} should document the package-local launcher");
        verificationIndex.Should().BeLessThan(runIndex,
            $"{relativePath} should verify sidecars before run.bat launch");
    }

    [Theory]
    [InlineData("docfx/quickstart/index.md")]
    [InlineData("docfx/production/deployment.md")]
    [InlineData("docfx/production/release-layout.md")]
    [InlineData("docfx/zh-tw/quickstart/index.md")]
    [InlineData("docfx/zh-tw/production/deployment.md")]
    [InlineData("docfx/zh-tw/production/release-layout.md")]
    public void CanonicalManualPackageDocs_ShouldDescribeReleaseAndPackageSboms(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain("SHA256SUMS.txt");
        content.Should().Contain("release-assets.json");
        content.Should().Contain("release-sbom.spdx.json");
        content.Should().Contain("package-sbom.spdx.json");
        content.Should().Contain("WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT");
    }

    [Theory]
    [InlineData("docfx/quickstart/ai-agent-clients.md")]
    [InlineData("docfx/quickstart/claude-code.md")]
    [InlineData("docfx/quickstart/openai-codex.md")]
    [InlineData("docfx/quickstart/claude-desktop.md")]
    [InlineData("docfx/quickstart/cursor-vscode.md")]
    [InlineData("docfx/zh-tw/quickstart/ai-agent-clients.md")]
    [InlineData("docfx/zh-tw/quickstart/claude-code.md")]
    [InlineData("docfx/zh-tw/quickstart/openai-codex.md")]
    [InlineData("docfx/zh-tw/quickstart/claude-desktop.md")]
    [InlineData("docfx/zh-tw/quickstart/cursor-vscode.md")]
    public void ClientQuickstarts_ShouldDelegateInstallProcedureToCanonicalQuickstart(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain("index.md");
        content.Should().Contain("client-registration");
        content.Should().NotContain("SHA256SUMS.txt",
            $"{relativePath} should avoid duplicating release integrity procedure from the canonical quickstart");
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}
