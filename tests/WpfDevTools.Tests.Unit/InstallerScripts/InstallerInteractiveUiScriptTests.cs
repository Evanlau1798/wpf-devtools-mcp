using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class InstallerInteractiveUiScriptTests
{
    [Fact]
    public void OnlineInstallerScript_ShouldDeclareCliFirstTuiContracts()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().Contain("Start-TuiInstaller");
        content.Should().Contain("Render-TuiScreen");
        content.Should().Contain("Read-TuiKey");
        content.Should().Contain("Update-TuiSelection");
        content.Should().Contain("Invoke-TuiInstallOperation");
        content.Should().Contain("Invoke-TuiUninstallOperation");
        content.Should().Contain("Invoke-TuiUpdateAllOperation");
        content.Should().Contain("HomeScreen");
        content.Should().Contain("InstallScreen");
        content.Should().Contain("UninstallScreen");
        content.Should().Contain("ProgressScreen");
        content.Should().Contain("Installed v");
        content.Should().Contain("Update available");
    }

    [Fact]
    public void OnlineInstallerScript_ShouldDeclareKeyboardNavigationAndTuiSettingsRows()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().Contain("Architecture");
        content.Should().Contain("Install location");
        content.Should().Contain("Update All");
        content.Should().Contain("Escape");
        content.Should().Contain("Backspace");
        content.Should().Contain("ConsoleKey.UpArrow");
        content.Should().Contain("ConsoleKey.DownArrow");
        content.Should().Contain("ConsoleKey.Enter");
    }

    [Fact]
    public void OnlineInstallerScript_ShouldDotSourceTuiHelpersAndAvoidPrimaryWpfMarkers()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().Contain("scripts/installer/Tui.ScreenModel.ps1");
        content.Should().Contain("scripts/installer/Tui.Renderer.ps1");
        content.Should().Contain("scripts/installer/Tui.Input.ps1");
        content.Should().Contain("scripts/installer/Tui.Flow.ps1");
        content.Should().NotContain("Show-InstallerWindow");
        content.Should().NotContain("WindowChrome.WindowChrome");
        content.Should().NotContain("DwmMicaHelper");
    }

    [Fact]
    public void TuiScreenModel_ShouldSupportScrollableListsAvailabilityFilteringAndVersionLabels()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.ScreenModel.ps1"));

        content.Should().Contain("VisibleWindowSize");
        content.Should().Contain("ScrollOffset");
        content.Should().Contain("Test-TuiClientAvailable");
        content.Should().Contain("Get-TuiClientItems");
        content.Should().Contain("Get-TuiUpdateBannerText");
        content.Should().Contain("Installed v");
    }

    [Fact]
    public void TuiFlow_ShouldKeepOperationsInSameSessionAndReuseInstallerCoreActions()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Flow.ps1"));

        content.Should().Contain("CurrentScreen = 'ProgressScreen'");
        content.Should().Contain("Invoke-InstallerAction -ResolvedAction 'install'");
        content.Should().Contain("Invoke-InstallerAction -ResolvedAction 'uninstall'");
        content.Should().Contain("Invoke-InstallerAction -ResolvedAction 'install' -ResolvedArchitecture ([string]$update.Architecture)");
        content.Should().Contain("CurrentScreen = 'HomeScreen'");
    }

    [Fact]
    public void OnlineInstallerScript_ShouldKeepCliFallbackPlainWithoutLegacyDecorativeBanners()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().Contain("Read-Host");
        content.Should().Contain("Action (install/uninstall)");
        content.Should().Contain("Architecture (x64/x86/arm64)");
        content.Should().NotContain("<VisualTree/>");
        content.Should().NotContain("PresentationFramework");
    }

    [Fact]
    public void OnlineInstallerScript_NonInteractiveJsonFlow_ShouldEmitModeStateAndBypassTuiRendering()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot, "x64");
            var installRoot = Path.Combine(tempRoot, "install-root");

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
                    ["USERPROFILE"] = Path.Combine(tempRoot, "UserProfile")
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            using var json = JsonDocument.Parse(result.Stdout);
            json.RootElement.GetProperty("mode").GetString().Should().NotBeNullOrWhiteSpace();
            json.RootElement.GetProperty("statePath").GetString().Should().NotBeNullOrWhiteSpace();
            json.RootElement.GetProperty("selectedClients").EnumerateArray().Select(x => x.GetString())
                .Should().Contain("visual-studio");
            result.Stdout.Should().NotContain("HomeScreen");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }
}
