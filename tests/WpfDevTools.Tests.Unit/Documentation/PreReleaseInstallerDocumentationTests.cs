using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class PreReleaseInstallerDocumentationTests
{
    private const string HttpsInstallerCommand =
        "irm https://installer.wpf-mcptools.evanlau1798.com | iex";

    [Theory]
    [InlineData("README.md")]
    [InlineData("docfx/index.md")]
    [InlineData("docfx/quickstart/index.md")]
    [InlineData("docfx/guides/agent-assisted-install.md")]
    [InlineData("docfx/production/deployment.md")]
    [InlineData("docfx/zh-tw/index.md")]
    [InlineData("docfx/zh-tw/quickstart/index.md")]
    [InlineData("docfx/zh-tw/guides/agent-assisted-install.md")]
    [InlineData("docfx/zh-tw/production/deployment.md")]
    public void PublicInstallerDocs_ShouldPublishHttpsAliasButKeepFirstReleaseGate(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain(HttpsInstallerCommand);
        content.Should().NotContain("irm http://wpf-mcptools.evanlau1798.com | iex",
            "the public installer alias is HTTPS-only");
        content.Should().Contain("GitHub Release assets",
            $"{relativePath} should make release asset availability the promotion gate");
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("docfx/quickstart/index.md")]
    [InlineData("docfx/guides/agent-assisted-install.md")]
    [InlineData("docfx/zh-tw/quickstart/index.md")]
    [InlineData("docfx/zh-tw/guides/agent-assisted-install.md")]
    public void PreReleaseE2eDocs_ShouldDescribeGitHubPrereleaseOnlineInstallerInstall(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().NotContain("git clone https://github.com/Evanlau1798/wpf-devtools-mcp.git",
            $"{relativePath} should model external E2E as an installer flow, not a source checkout");
        content.Should().Contain("https://installer.wpf-mcptools.evanlau1798.com/",
            $"{relativePath} should download the reviewed installer through the public installer alias");
        content.Should().Contain("$installerPath",
            $"{relativePath} should run the downloaded installer script explicitly so prerelease parameters can be passed");
        content.Should().Contain("Invoke-WebRequest @installerDownload",
            $"{relativePath} should keep the installer download compatible with Windows PowerShell 5.1 and PowerShell 7");
        content.Should().Contain("online-installer.ps1",
            $"{relativePath} should keep the reviewed online installer as the pre-release E2E entrypoint");
        content.Should().Contain("-Version latest -Prerelease",
            $"{relativePath} should let E2E agents select the latest GitHub pre-release explicitly");
        content.Should().NotContain("Publish-Release.ps1 -Configuration Debug",
            $"{relativePath} should not make local Debug package publishing the default pre-release E2E path");
        content.Should().NotContain("-PackageArchivePath $package.FullName",
            $"{relativePath} should not install a locally generated archive for the GitHub-sourced pre-release E2E path");
        content.Should().NotContain("-TrustedReleaseMetadataDirectory .\\artifacts\\release-e2e-debug",
            $"{relativePath} should not require local sidecars for the GitHub-sourced pre-release E2E path");
        content.Should().NotContain("dotnet tool install --tool-path ./.tools --add-source ./artifacts/package <PackageId>",
            $"{relativePath} should not present the MCP server release archive as a dotnet tool package");
        content.Should().Contain("pre-release E2E",
            $"{relativePath} should separate source-checkout E2E from published release install");
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("AGENT_INSTALL.md")]
    [InlineData("docfx/quickstart/index.md")]
    [InlineData("docfx/guides/agent-assisted-install.md")]
    [InlineData("docfx/production/deployment.md")]
    [InlineData("docfx/zh-tw/quickstart/index.md")]
    [InlineData("docfx/zh-tw/guides/agent-assisted-install.md")]
    [InlineData("docfx/zh-tw/production/deployment.md")]
    public void PreReleaseE2eDocs_ShouldShowIsolatedInstallAndWorkingRoots(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain("$installRoot",
            $"{relativePath} should give external E2E agents an isolated install root instead of reusing installer state");
        content.Should().Contain("$workingRoot",
            $"{relativePath} should keep installer work files inside the E2E workspace");
        content.Should().Contain("-InstallRoot $installRoot",
            $"{relativePath} should make the isolated install root copy-pasteable");
        content.Should().Contain("-WorkingRoot $workingRoot",
            $"{relativePath} should make installer temporary work isolation copy-pasteable");
    }

    [Theory]
    [InlineData("README.md")]
    [InlineData("AGENT_INSTALL.md")]
    [InlineData("docfx/quickstart/index.md")]
    [InlineData("docfx/guides/agent-assisted-install.md")]
    [InlineData("docfx/production/deployment.md")]
    [InlineData("docfx/zh-tw/quickstart/index.md")]
    [InlineData("docfx/zh-tw/guides/agent-assisted-install.md")]
    [InlineData("docfx/zh-tw/production/deployment.md")]
    public void PreReleaseE2eDocs_ShouldUseArtifactOnlyClientForValidationSnippets(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain("-Client other",
            $"{relativePath} should keep noninteractive validation snippets from mutating real MCP client registrations");
    }

    [Theory]
    [InlineData("AGENT_INSTALL.md")]
    [InlineData("docfx/quickstart/index.md")]
    [InlineData("docfx/guides/agent-assisted-install.md")]
    [InlineData("docfx/production/deployment.md")]
    [InlineData("docfx/zh-tw/quickstart/index.md")]
    [InlineData("docfx/zh-tw/guides/agent-assisted-install.md")]
    [InlineData("docfx/zh-tw/production/deployment.md")]
    public void PreReleaseDocs_ShouldDocumentGitHubPrereleaseAssetsAreRequired(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain("-Version latest -Prerelease",
            $"{relativePath} should keep prerelease E2E on the explicit GitHub prerelease channel");
        content.Should().Contain("GitHub pre-release",
            $"{relativePath} should make the source of prerelease assets explicit");
        content.Should().Contain("release-assets.json",
            $"{relativePath} should keep prerelease installation tied to release sidecar metadata");
        content.Should().Contain("WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT",
            $"{relativePath} should still document the signer pin required for signed Release packaging");
    }

    [Theory]
    [InlineData("AGENT_INSTALL.md")]
    [InlineData("docfx/quickstart/index.md")]
    [InlineData("docfx/guides/agent-assisted-install.md")]
    [InlineData("docfx/zh-tw/quickstart/index.md")]
    [InlineData("docfx/zh-tw/guides/agent-assisted-install.md")]
    public void PreReleaseE2eDocs_ShouldDocumentNdjsonStdioSmokeContract(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain("newline-delimited JSON",
            $"{relativePath} should keep direct MCP STDIO smoke tests on the server transport used by the product");
        content.Should().Contain("NDJSON",
            $"{relativePath} should give agents a searchable shorthand for the STDIO transport");
        content.Should().Contain("Content-Length",
            $"{relativePath} should explicitly warn agents not to use header-framed MCP messages for this server");
    }

    [Theory]
    [InlineData("AGENT_INSTALL.md")]
    [InlineData("docfx/quickstart/index.md")]
    [InlineData("docfx/guides/agent-assisted-install.md")]
    [InlineData("docfx/zh-tw/quickstart/index.md")]
    [InlineData("docfx/zh-tw/guides/agent-assisted-install.md")]
    public void PreReleaseE2eDocs_ShouldProvideDirectMcpJsonRpcSmokeSequence(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain("\"method\":\"initialize\"",
            $"{relativePath} should show a raw JSON-RPC initialize request for direct STDIO validation");
        content.Should().Contain("\"method\":\"notifications/initialized\"",
            $"{relativePath} should show the initialized notification before tool calls");
        content.Should().Contain("\"method\":\"tools/list\"",
            $"{relativePath} should show tool discovery in the direct STDIO smoke path");
        content.Should().Contain("\"name\":\"connect\"",
            $"{relativePath} should show connect() as the first workflow tool call");
        content.Should().Contain("\"name\":\"get_ui_summary\"",
            $"{relativePath} should show scene-first validation after connect");
        content.Should().Contain("large `tools/list`",
            $"{relativePath} should warn raw harness authors that the full schema payload is large");
    }

    [Theory]
    [InlineData("docfx/quickstart/sdk-hosted-inspector.md")]
    [InlineData("docfx/zh-tw/quickstart/sdk-hosted-inspector.md")]
    public void SdkHostedDocs_ShouldExplainLocalPackageSourceMappingAndCentralPackageManagement(string relativePath)
    {
        var content = File.ReadAllText(GetRepoFilePath(relativePath));

        content.Should().Contain("PackageSourceMapping",
            $"{relativePath} should explain local SDK package restore when NuGet package source mapping is enabled");
        content.Should().Contain("Central Package Management",
            $"{relativePath} should explain local SDK package restore when Directory.Packages.props controls versions");
        content.Should().Contain("NuGet.config",
            $"{relativePath} should show the app-local restore configuration boundary");
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}
