using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class InstallerInteractiveUiScriptTests
{
    [Fact]
    public void OnlineInstallerScript_ShouldDeclareGuiArchitectureSelectorInstallRootAndSharedPages()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().Contain("ComboBox");
        content.Should().Contain("Architecture");
        content.Should().Contain("Install root");
        content.Should().Contain("PageInstall");
        content.Should().Contain("PageUninstall");
        content.Should().Contain("%APPDATA%");
    }

    [Fact]
    public void OnlineInstallerScript_ShouldDeclareGuiTargetsForVsCodeAndVisualStudio()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().Contain("VS Code");
        content.Should().Contain("Visual Studio");
        content.Should().Contain("vscode");
        content.Should().Contain("visual-studio");
    }

    [Fact]
    public void OnlineInstallerScript_NonInteractiveJsonFlow_ShouldEmitModeStateAndAvoidBrowserActions()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot, "x64");
            var installRoot = Path.Combine(tempRoot, "install-root");
            var fakeBin = Path.Combine(tempRoot, "bin");
            var browserLog = Path.Combine(tempRoot, "browser.log");
            var browserCommand = ReleaseScriptTestHarness.CreateFakeCommand(fakeBin, "open-docs", browserLog);

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                new[]
                {
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "visual-studio",
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
            json.RootElement.GetProperty("mode").GetString().Should().NotBeNullOrWhiteSpace();
            json.RootElement.GetProperty("statePath").GetString().Should().NotBeNullOrWhiteSpace();
            json.RootElement.GetProperty("selectedClients").EnumerateArray().Select(x => x.GetString())
                .Should().Contain("visual-studio");
            File.Exists(browserLog).Should().BeFalse();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstallerScript_ShouldKeepCliFallbackPlainWithoutDecorativeBannerStrings()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().Contain("Read-Host");
        content.Should().Contain("Action (install/uninstall)");
        content.Should().Contain("Architecture (x64/x86/arm64)");
        content.Should().NotContain("+==================================================================+");
        content.Should().NotContain("<VisualTree/>");
    }
}
