using FluentAssertions;
using WpfDevTools.Tests.Unit.TestSupport;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class AgentInstallDocumentationTests
{
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

        File.ReadAllText(GetRepoFilePath("docfx/toc.yml")).Should().Contain("guides/agent-assisted-install.md");
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
            content.Should().Contain("release-evidence.json");
            content.Should().Contain("WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT");
            content.Should().Contain("-PackageArchivePath");
            content.Should().Contain("-TrustedReleaseMetadataDirectory");
            content.Should().Contain("-NonInteractive");
            content.Should().Contain("-Force");
        }
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
