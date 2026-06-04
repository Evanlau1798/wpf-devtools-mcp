using FluentAssertions;
using WpfDevTools.Tests.Unit.TestSupport;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class AgentInstallDocumentationTests
{
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
    public void AgentInstallDocs_ShouldExistAndBeLinkedFromPublicEntrypoints()
    {
        foreach (var file in AgentInstallFiles)
        {
            File.Exists(GetRepoFilePath(file)).Should().BeTrue($"{file} should be published as an agent-readable install contract");
        }

        File.ReadAllText(GetRepoFilePath("docfx/toc.yml"))
            .Should().Contain("guides/agent-assisted-install.md");
        File.ReadAllText(GetRepoFilePath("docfx/zh-tw/toc.yml"))
            .Should().Contain("guides/agent-assisted-install.md");

        File.ReadAllText(GetRepoFilePath("docfx/quickstart/ai-agent-clients.md"))
            .Should().Contain("guides/agent-assisted-install.md");
        File.ReadAllText(GetRepoFilePath("docfx/zh-tw/quickstart/ai-agent-clients.md"))
            .Should().Contain("guides/agent-assisted-install.md");
        File.ReadAllText(GetRepoFilePath("docfx/production/deployment.md"))
            .Should().Contain("guides/agent-assisted-install.md");
        File.ReadAllText(GetRepoFilePath("docfx/zh-tw/production/deployment.md"))
            .Should().Contain("guides/agent-assisted-install.md");

        File.ReadAllText(GetRepoFilePath("AGENT_INSTALL.md"))
            .Should().Contain("docfx/guides/agent-assisted-install.md");
    }

    [Fact]
    public void AgentInstallGuide_ShouldDefineSafeAgentContract()
    {
        var english = File.ReadAllText(GetRepoFilePath("docfx/guides/agent-assisted-install.md"));
        english.Should().Contain("## Agent contract");
        english.Should().Contain("## Required user confirmations");
        english.Should().Contain("## Discovery steps");
        english.Should().Contain("powershell -ExecutionPolicy Bypass -File .\\scripts\\online-installer.ps1 -Action plan -OutputJson");
        english.Should().Contain("## Release acquisition");
        english.Should().Contain("## Provenance verification");
        english.Should().Contain("## Client registration");
        english.Should().Contain("## Code signing boundaries");
        english.Should().Contain("## Troubleshooting");
        english.Should().Contain("## Copyable agent prompt");
        english.Should().Contain("Do not install yet");
        english.Should().Contain("requires user confirmation before mutation");
        english.Should().Contain("Never ask for private keys");
        english.Should().Contain("PFX password");
        english.Should().Contain("self-signed certificates are only for local/dev/test");

        var traditionalChinese = File.ReadAllText(GetRepoFilePath("docfx/zh-tw/guides/agent-assisted-install.md"));
        traditionalChinese.Should().Contain("## Agent 契約");
        traditionalChinese.Should().Contain("## 必要使用者確認");
        traditionalChinese.Should().Contain("## 偵測步驟");
        traditionalChinese.Should().Contain("powershell -ExecutionPolicy Bypass -File .\\scripts\\online-installer.ps1 -Action plan -OutputJson");
        traditionalChinese.Should().Contain("## Release 取得");
        traditionalChinese.Should().Contain("## 來源驗證");
        traditionalChinese.Should().Contain("## Client registration");
        traditionalChinese.Should().Contain("## Code signing 邊界");
        traditionalChinese.Should().Contain("## 疑難排解");
        traditionalChinese.Should().Contain("## 可複製 Agent prompt");
        traditionalChinese.Should().Contain("尚未安裝");
        traditionalChinese.Should().Contain("修改前必須取得使用者確認");
        traditionalChinese.Should().Contain("不可要求 private key");
        traditionalChinese.Should().Contain("PFX password");
        traditionalChinese.Should().Contain("self-signed certificate 只適用 local/dev/test");
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
            content.Should().Contain("release asset SBOM");
            content.Should().Contain("not a full package/dependency SBOM");
            content.Should().Contain("WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT");
            content.Should().Contain("WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT");
            content.Should().Contain("signer pin");
            content.Should().Contain("thumbprint");
            content.Should().Contain("subject");
            content.Should().Contain("additional constraint");
            content.Should().NotContain("WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT` or `WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT");
            content.Should().NotContain("WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT` 或 `WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT");
            content.Should().Contain("-Action plan");
            content.Should().Contain("-OutputJson");
        }
    }

    [Fact]
    public void AgentInstallDocs_ShouldShowPlanJsonShapeAndReadOnlyFlags()
    {
        foreach (var file in AgentInstallFiles)
        {
            var content = File.ReadAllText(GetRepoFilePath(file));
            content.Should().Contain("\"action\": \"plan\"");
            content.Should().Contain("\"contractVersion\": 1");
            content.Should().Contain("\"platform\": \"windows\"");
            content.Should().Contain("\"architecture\"");
            content.Should().Contain("\"client\"");
            content.Should().Contain("\"installRootDefault\"");
            content.Should().Contain("\"preferredInstallRoot\"");
            content.Should().Contain("\"fallbackInstallRoot\"");
            content.Should().Contain("\"installRootSource\"");
            content.Should().Contain("\"supportedClients\"");
            content.Should().Contain("\"detectedClients\"");
            content.Should().Contain("\"registrationStyle\"");
            content.Should().Contain("\"artifact-only\"");
            content.Should().Contain("\"requiresUserConfirmationBeforeMutation\": true");
            content.Should().Contain("\"mutatesFileSystem\": false");
            content.Should().Contain("\"downloadsReleaseAssets\": false");
            content.Should().Contain("\"runsClientRegistration\": false");
            content.Should().Contain("\"mutationBoundary\"");
        }
    }

    [Fact]
    public void AgentInstallDocs_ShouldShowPostConfirmationInstallCommand()
    {
        foreach (var file in AgentInstallFiles)
        {
            var content = File.ReadAllText(GetRepoFilePath(file));
            content.Should().Contain("-PackageArchivePath .\\release\\release_<version>_win-<arch>.zip");
            content.Should().Contain("-TrustedReleaseMetadataDirectory .\\release");
            content.Should().Contain("-Client <client-id>");
            content.Should().Contain("-NonInteractive");
            content.Should().Contain("-Force");
            content.Should().Contain("-OutputJson");
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
                "irm https://wpf-mcptools.evanlau1798.com | iex",
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
