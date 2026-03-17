using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class InstallerInteractiveUiScriptTests
{
    private const string DocsHomepageUrl = "https://evanlau1798.github.io/wpf-devtools-mcp/index.html";

    [Fact]
    public void OnlineInstallerScript_ShouldRenderMenuDrivenFlow_AndOfferDocsHomepageAction()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot, "x64");
            var installRoot = Path.Combine(tempRoot, "install-root");
            var fakeBin = Path.Combine(tempRoot, "bin");
            var browserLog = Path.Combine(tempRoot, "browser.log");
            var browserCommand = ReleaseScriptTestHarness.CreateFakeCommand(fakeBin, "open-docs", browserLog);
            var userProfile = Path.Combine(tempRoot, "UserProfile");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                new[]
                {
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Force"
                },
                new Dictionary<string, string?>
                {
                    ["APPDATA"] = Path.Combine(tempRoot, "AppData", "Roaming"),
                    ["LOCALAPPDATA"] = Path.Combine(tempRoot, "AppData", "Local"),
                    ["USERPROFILE"] = userProfile,
                    ["PATH"] = fakeBin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
                    ["WPFDEVTOOLS_INSTALLER_TEST_RESPONSES"] = string.Join("||", "", "", "3", "", "1"),
                    ["WPFDEVTOOLS_INSTALLER_OPEN_BROWSER_COMMAND"] = browserCommand
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Should().Contain("WPF DEVTOOLS MCP");
            result.Stdout.Should().Contain("Open docs homepage");
            File.ReadAllText(browserLog).Should().Contain(DocsHomepageUrl);
            File.ReadAllText(Path.Combine(userProfile, ".mcp.json")).Should().Contain("wpf-devtools");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void SetupScript_ShouldRenderOfflineMenuDrivenFlow_AndOfferDocsHomepageAction()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var packageDir = ReleaseScriptTestHarness.CreatePackageDirectory(tempRoot, "x64");
            var installRoot = Path.Combine(tempRoot, "install-root");
            var fakeBin = Path.Combine(tempRoot, "bin");
            var browserLog = Path.Combine(tempRoot, "browser.log");
            var browserCommand = ReleaseScriptTestHarness.CreateFakeCommand(fakeBin, "open-docs", browserLog);
            var userProfile = Path.Combine(tempRoot, "UserProfile");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/release/Setup-WpfDevTools.ps1"),
                new[]
                {
                    "-PackagePath", packageDir,
                    "-InstallRoot", installRoot,
                    "-Force"
                },
                new Dictionary<string, string?>
                {
                    ["APPDATA"] = Path.Combine(tempRoot, "AppData", "Roaming"),
                    ["LOCALAPPDATA"] = Path.Combine(tempRoot, "AppData", "Local"),
                    ["USERPROFILE"] = userProfile,
                    ["PATH"] = fakeBin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
                    ["WPFDEVTOOLS_INSTALLER_TEST_RESPONSES"] = string.Join("||", "3", "", "1"),
                    ["WPFDEVTOOLS_INSTALLER_OPEN_BROWSER_COMMAND"] = browserCommand
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Should().Contain("WPF DEVTOOLS MCP");
            result.Stdout.Should().Contain("VisualTree");
            result.Stdout.Should().Contain("Open docs homepage");
            File.ReadAllText(browserLog).Should().Contain(DocsHomepageUrl);
            File.ReadAllText(Path.Combine(userProfile, ".mcp.json")).Should().Contain("wpf-devtools");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void SetupScript_NonInteractiveJsonFlow_ShouldNotAttemptToOpenDocsHomepage()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var packageDir = ReleaseScriptTestHarness.CreatePackageDirectory(tempRoot, "x64");
            var installRoot = Path.Combine(tempRoot, "install-root");
            var fakeBin = Path.Combine(tempRoot, "bin");
            var browserLog = Path.Combine(tempRoot, "browser.log");
            var browserCommand = ReleaseScriptTestHarness.CreateFakeCommand(fakeBin, "open-docs", browserLog);

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/release/Setup-WpfDevTools.ps1"),
                new[]
                {
                    "-PackagePath", packageDir,
                    "-InstallRoot", installRoot,
                    "-Clients", "none",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                },
                new Dictionary<string, string?>
                {
                    ["APPDATA"] = Path.Combine(tempRoot, "AppData", "Roaming"),
                    ["LOCALAPPDATA"] = Path.Combine(tempRoot, "AppData", "Local"),
                    ["USERPROFILE"] = Path.Combine(tempRoot, "UserProfile"),
                    ["PATH"] = fakeBin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH"),
                    ["WPFDEVTOOLS_INSTALLER_OPEN_BROWSER_COMMAND"] = browserCommand
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            using var json = JsonDocument.Parse(result.Stdout);
            json.RootElement.GetProperty("selectedClients").EnumerateArray().Should().BeEmpty();
            File.Exists(browserLog).Should().BeFalse();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }
}
