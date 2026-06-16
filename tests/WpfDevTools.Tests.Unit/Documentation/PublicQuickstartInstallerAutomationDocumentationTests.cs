using FluentAssertions;
using WpfDevTools.Tests.Unit.TestSupport;
using Xunit;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class PublicQuickstartInstallerAutomationDocumentationTests
{
    [Fact]
    public void CanonicalInstallerExamples_ShouldUseNonInteractiveAndOutputJson()
    {
        string[] files =
        [
            "AGENT_INSTALL.md",
            "docfx/guides/agent-assisted-install.md",
            "docfx/production/deployment.md",
            "docfx/zh-tw/guides/agent-assisted-install.md",
            "docfx/zh-tw/production/deployment.md"
        ];

        foreach (var file in files)
        {
            var content = File.ReadAllText(GetRepoFilePath(file));
            content.Should().Contain("-NonInteractive",
                $"{file} should show explicit non-interactive installer usage for automation-safe examples");
            content.Should().Contain("-OutputJson",
                $"{file} should show machine-readable installer output for automation-safe examples");
        }
    }

    [Fact]
    public void InstallerExamples_ShouldNotPresentX64AsTheDefaultArchitecture()
    {
        foreach (var file in EnumeratePublicInstallDocs())
        {
            var content = File.ReadAllText(GetRepoFilePath(file));
            content.Should().NotContain("-Version latest -Architecture x64",
                $"{file} should not imply that x64 is the installer default");
            content.Should().NotContain("release_latest_win-x64.zip",
                $"{file} should use the architecture placeholder when discussing release assets");
        }
    }

    [Fact]
    public void ClientQuickstarts_ShouldDescribeFallbackExecutablePath_NotFixedDefaultRoot()
    {
        foreach (var file in new[]
        {
            "docfx/quickstart/claude-code.md",
            "docfx/quickstart/openai-codex.md",
            "docfx/zh-tw/quickstart/claude-code.md",
            "docfx/zh-tw/quickstart/openai-codex.md"
        })
        {
            var content = File.ReadAllText(GetRepoFilePath(file));
            content.Should().Contain("<InstallRoot>\\<arch>");
            content.Should().NotContain("%APPDATA%\\WpfDevToolsMcp\\x64");
        }
    }

    private static IEnumerable<string> EnumeratePublicInstallDocs()
    {
        yield return "README.md";
        yield return "docfx/index.md";
        yield return "docfx/quickstart/index.md";
        yield return "docfx/production/deployment.md";
        yield return "docfx/zh-tw/index.md";
        yield return "docfx/zh-tw/quickstart/index.md";
        yield return "docfx/zh-tw/production/deployment.md";
    }

    private static string GetRepoFilePath(string relativePath)
        => TestRepositoryPaths.GetRepoFilePath(relativePath);
}
