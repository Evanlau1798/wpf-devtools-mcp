using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class InstallerInteractiveUiScriptTests
{
    [Fact]
    public void OnlineInstallerScript_ShouldDeclareUiTestWindowChromeThemeAndAnimatedShell()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().Contain("WindowStyle=\"None\"");
        content.Should().Contain("WindowChrome.WindowChrome");
        content.Should().Contain("Style x:Key=\"CaptionBtn\"");
        content.Should().Contain("Style x:Key=\"CloseBtn\"");
        content.Should().Contain("Style x:Key=\"MainBtn\"");
        content.Should().Contain("Style x:Key=\"ItemBtn\"");
        content.Should().Contain("Style x:Key=\"NavBtn\"");
        content.Should().Contain("BtnMin");
        content.Should().Contain("BtnClose");
        content.Should().Contain("DwmMicaHelper");
        content.Should().Contain("function Switch-Page");
        content.Should().Contain("TranslateTransform");
        content.Should().Contain("Opacity=\"0\"");
        content.Should().Contain("PageMain");
        content.Should().Contain("PageInstall");
        content.Should().Contain("PageUninstall");
    }

    [Fact]
    public void OnlineInstallerScript_ShouldKeepArchitectureInstallRootAndCurrentTargetsInsideGuiShell()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().Contain("ArchitectureSelector");
        content.Should().Contain("InstallRootTextBox");
        content.Should().Contain("VS Code");
        content.Should().Contain("Visual Studio");
        content.Should().Contain("Claude Desktop");
        content.Should().Contain("Other");
        content.Should().Contain("vscode");
        content.Should().Contain("visual-studio");
    }

    [Fact]
    public void OnlineInstallerScript_ShouldDeclareUiTestPageStatusTextAndInstallerSpecificSecondaryActions()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().Contain("TxtVersion");
        content.Should().Contain("TxtInstMsg");
        content.Should().Contain("TxtUninstMsg");
        content.Should().Contain("Generate Standard Install JSON");
        content.Should().Contain("GenerateStandardInstallJsonButton");
        content.Should().Contain("System.Windows.Clipboard");
        content.Should().Contain("Install location");
        content.Should().Contain("HorizontalAlignment=\"Right\"");
    }

    [Fact]
    public void OnlineInstallerScript_ShouldUseCustomUiTestStyledCompletionWindowInsteadOfSystemMessageBox()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().Contain("SummaryMessageText");
        content.Should().Contain("OpenDocsButton");
        content.Should().Contain("CloseSummaryButton");
        content.Should().Contain("Open documentation homepage");
        content.Should().NotContain("System.Windows.MessageBox");
    }

    [Fact]
    public void OnlineInstallerScript_ShouldDeclareAnimatedPageTransitionsAndStatusRefreshHelpers()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().Contain("function Update-AllStatus");
        content.Should().Contain("DispatcherTimer");
        content.Should().Contain("CubicEase");
        content.Should().Contain("TxtInstMsg.Text");
        content.Should().Contain("TxtUninstMsg.Text");
        content.Should().Contain("$installed");
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
