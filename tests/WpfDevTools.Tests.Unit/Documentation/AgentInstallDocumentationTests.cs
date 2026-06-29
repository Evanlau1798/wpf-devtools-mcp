using FluentAssertions;
using WpfDevTools.Tests.Unit.TestSupport;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class AgentInstallDocumentationTests
{
    private const string StableLatestInstallerCommand =
        "irm https://installer.wpf-mcptools.evanlau1798.com | iex";
    private const string PreviewPrereleaseInstallerCommand =
        "& ([scriptblock]::Create((irm https://installer.wpf-mcptools.evanlau1798.com))) -Version latest -Prerelease";

    private static readonly string[] AgentInstallFiles =
    [
        "AGENT_INSTALL.md",
        "docfx/guides/agent-assisted-install.md",
        "docfx/zh-tw/guides/agent-assisted-install.md"
    ];

    private static readonly string[] SupportedClientIds =
    [
        "claude-code",
        "codex",
        "cursor",
        "vscode",
        "visual-studio",
        "claude-desktop",
        "other"
    ];

    [Fact]
    public void AgentInstallDocs_ShouldExistAndBeLinkedFromMaintainerNavigation()
    {
        foreach (var file in AgentInstallFiles)
        {
            File.Exists(GetRepoFilePath(file)).Should().BeTrue($"{file} should be published as an agent-readable install contract");
        }

        File.ReadAllText(GetRepoFilePath("docfx/contributors/toc.yml")).Should().Contain("../guides/agent-assisted-install.md");
        File.ReadAllText(GetRepoFilePath("docfx/zh-tw/toc.yml")).Should().Contain("guides/agent-assisted-install.md");
        File.ReadAllText(GetRepoFilePath("AGENT_INSTALL.md")).Should().Contain("docfx/guides/agent-assisted-install.md");
    }

    [Fact]
    public void AgentInstallGuides_ShouldDefineSafePlanThenConfirmContract()
    {
        foreach (var file in AgentInstallFiles)
        {
            var content = File.ReadAllText(GetRepoFilePath(file));
            content.Should().Contain("Do not install");
            content.Should().Contain("-Action plan");
            content.Should().Contain("-OutputJson");
            content.Should().Contain("mutation");
            content.Should().Contain("confirmation");
            content.Should().Contain("client-registration");
            content.Should().Contain("private keys");
            content.Should().Contain("auth secrets");
            content.Should().NotContain("-ExecutionPolicy Bypass");
        }
    }

    [Fact]
    public void AgentInstallDocs_ShouldProvideWindowsPowerShellFallback()
    {
        foreach (var file in AgentInstallFiles)
        {
            var content = File.ReadAllText(GetRepoFilePath(file));
            content.Should().Contain("pwsh -NoProfile -File");
            content.Should().Contain("powershell.exe -NoProfile -File",
                $"{file} should support hosts without PowerShell 7 installed");
        }
    }

    [Fact]
    public void AgentInstallDocs_ShouldRequireReleaseProvenanceAndSignerPinning()
    {
        foreach (var file in AgentInstallFiles)
        {
            var content = File.ReadAllText(GetRepoFilePath(file));
            content.Should().Contain("SHA256SUMS.txt");
            content.Should().Contain("release-assets.json");
            content.Should().Contain("release-sbom.spdx.json");
            content.Should().Contain("package-sbom.spdx.json");
            content.Should().Contain("WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT");
            content.Should().Contain("-PackageArchivePath");
            content.Should().Contain("-TrustedReleaseMetadataDirectory");
            content.Should().Contain("-NonInteractive");
            content.Should().Contain("-Force");
        }
    }

    [Theory]
    [InlineData(
        "AGENT_INSTALL.md",
        "default online installer path",
        "does not need the agent to download release archives or sidecars first",
        "reviewed local package")]
    [InlineData(
        "docfx/guides/agent-assisted-install.md",
        "default online installer path",
        "does not need the agent to download release archives or sidecars first",
        "reviewed local package")]
    [InlineData(
        "docfx/zh-tw/guides/agent-assisted-install.md",
        "預設 online installer path",
        "不需要 agent 先下載 release archive 或 sidecar",
        "已審查本機 package")]
    public void AgentInstallDocs_ShouldPreferOnlineInstallerForNormalSetup(
        string file,
        string defaultPathPhrase,
        string noPreDownloadPhrase,
        string localArchivePhrase)
    {
        var content = File.ReadAllText(GetRepoFilePath(file));

        content.Should().Contain(defaultPathPhrase,
            $"{file} should keep the low-friction installer alias as the normal agent path");
        content.Should().Contain(noPreDownloadPhrase,
            $"{file} should not make GitHub asset collection a prerequisite for the online installer path");

        var defaultPathIndex = content.IndexOf(defaultPathPhrase, StringComparison.OrdinalIgnoreCase);
        var localArchiveIndex = content.IndexOf(localArchivePhrase, StringComparison.OrdinalIgnoreCase);
        defaultPathIndex.Should().BeGreaterThanOrEqualTo(0);
        localArchiveIndex.Should().BeGreaterThan(defaultPathIndex,
            $"{file} should present local archive verification as a fallback, not the first path");
    }

    [Theory]
    [InlineData("docfx/quickstart/openai-codex.md", "normal Codex setup")]
    [InlineData("docfx/zh-tw/quickstart/openai-codex.md", "一般 Codex setup")]
    public void CodexQuickstarts_ShouldTellAgentsToAvoidPortableZipForNormalSetup(
        string file,
        string normalSetupPhrase)
    {
        var content = File.ReadAllText(GetRepoFilePath(file));

        content.Should().Contain("5-Minute Setup");
        content.Should().Contain("portable ZIP");
        content.Should().Contain(normalSetupPhrase,
            $"{file} should steer agents away from the friction-prone portable extraction path");
    }

    [Theory]
    [InlineData(
        "AGENT_INSTALL.md",
        "Do not use `bin\\install.ps1`, `bin/install.ps1`, or `run.bat` as the noninteractive prerelease/debug trust path.")]
    [InlineData(
        "docfx/guides/agent-assisted-install.md",
        "Do not use `bin\\install.ps1`, `bin/install.ps1`, or `run.bat` as the noninteractive prerelease/debug trust path.")]
    [InlineData(
        "docfx/zh-tw/guides/agent-assisted-install.md",
        "不要把 `bin\\install.ps1`、`bin/install.ps1` 或 `run.bat` 當作 noninteractive prerelease/debug trust path。")]
    public void AgentInstallDocs_ShouldClarifyPrereleaseDebugTrustPath(
        string file,
        string expectedBoundary)
    {
        var content = File.ReadAllText(GetRepoFilePath(file));

        content.Should().Contain(expectedBoundary);
        content.Should().Contain("-PackageArchivePath");
        content.Should().Contain("-TrustedReleaseMetadataDirectory");
        content.Should().Contain("DebugTrustedRootSkip");
    }

    [Fact]
    public void AgentInstallDocs_ShouldListTheInstallerSupportedClients()
    {
        foreach (var file in AgentInstallFiles)
        {
            var content = File.ReadAllText(GetRepoFilePath(file));

            foreach (var clientId in SupportedClientIds)
            {
                content.Should().Contain($"`{clientId}`", $"{file} should stay synchronized with installer client choices");
            }
        }
    }

    [Fact]
    public void AgentInstallDocs_ShouldAvoidUnsafeRemoteExecutionCommands()
    {
        foreach (var file in AgentInstallFiles)
        {
            var content = File.ReadAllText(GetRepoFilePath(file));
            var withoutReviewedHttpsAlias = content.Replace(
                PreviewPrereleaseInstallerCommand,
                string.Empty,
                StringComparison.Ordinal).Replace(
                StableLatestInstallerCommand,
                string.Empty,
                StringComparison.Ordinal);

            withoutReviewedHttpsAlias.Should().NotContain("Invoke-Expression");
            withoutReviewedHttpsAlias.Should().NotContain("iex ");
            withoutReviewedHttpsAlias.Should().NotContain("raw.githubusercontent.com");
            withoutReviewedHttpsAlias.Should().NotContain("releases/latest/download/install.ps1");
            withoutReviewedHttpsAlias.Should().NotContain("| powershell");
            withoutReviewedHttpsAlias.Should().NotContain("| pwsh");
        }
    }

    private static string GetRepoFilePath(string relativePath)
        => TestRepositoryPaths.GetRepoFilePath(relativePath);
}
