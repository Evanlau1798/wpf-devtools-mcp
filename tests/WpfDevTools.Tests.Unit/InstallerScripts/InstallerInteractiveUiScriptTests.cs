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
    public void TuiFlow_ShouldDeclareConfirmScreenAndDualUninstallActions()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().Contain("ConfirmScreen");
        content.Should().Contain("UnregisterTarget");
        content.Should().Contain("FullUninstall");
    }

    [Fact]
    public void TuiScreenModel_ShouldDeclareFullUninstallRowSeparatedByDivider()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.ScreenModel.ps1"));

        content.Should().Contain("------------------------------");
        content.Should().Contain("Full Uninstall");
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

    [Fact]
    public void OnlineInstallerScript_InlineIexExecution_ShouldStillBootstrapTuiWhenHelpersAreNotBesideTheScript()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var appData = Path.Combine(tempRoot, "AppData", "Roaming");
            var localAppData = Path.Combine(tempRoot, "AppData", "Local");
            var userProfile = Path.Combine(tempRoot, "UserProfile");
            Directory.CreateDirectory(appData);
            Directory.CreateDirectory(localAppData);
            Directory.CreateDirectory(userProfile);

            var repoScriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1");
            var helperDirectory = ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer");
            var command = string.Join(" ; ",
            [
                "$env:APPDATA='" + appData.Replace("'", "''") + "'",
                "$env:LOCALAPPDATA='" + localAppData.Replace("'", "''") + "'",
                "$env:USERPROFILE='" + userProfile.Replace("'", "''") + "'",
                "$env:WPFDEVTOOLS_INSTALLER_HELPER_DIRECTORY='" + helperDirectory.Replace("'", "''") + "'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_TUI_KEYS='Escape'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_CLEAR='1'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_LATEST_VERSION='1.2.3'",
                "Set-Location '" + tempRoot.Replace("'", "''") + "'",
                "& ([scriptblock]::Create((Get-Content '" + repoScriptPath.Replace("'", "''") + "' -Raw))) -Action install -Architecture x64 -Client other"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Should().Contain("HomeScreen");
            result.Stdout.Should().Contain("Install location");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstallerScript_TuiUninstallScreen_ShouldListJsonRegisteredTargetsEvenWhenInstallerStateIsMissing()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var appData = Path.Combine(tempRoot, "AppData", "Roaming");
            var localAppData = Path.Combine(tempRoot, "AppData", "Local");
            var userProfile = Path.Combine(tempRoot, "UserProfile");
            var configPath = Path.Combine(appData, "Code", "User", "mcp.json");
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            Directory.CreateDirectory(localAppData);
            Directory.CreateDirectory(userProfile);
            File.WriteAllText(
                configPath,
                "{\"servers\":{\"wpf-devtools\":{\"command\":\"C:\\\\tools\\\\wpf-devtools-x64.exe\",\"args\":[]}}}");

            var repoScriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1");
            var helperDirectory = ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer");
            var command = string.Join(" ; ",
            [
                "$env:APPDATA='" + appData.Replace("'", "''") + "'",
                "$env:LOCALAPPDATA='" + localAppData.Replace("'", "''") + "'",
                "$env:USERPROFILE='" + userProfile.Replace("'", "''") + "'",
                "$env:WPFDEVTOOLS_INSTALLER_HELPER_DIRECTORY='" + helperDirectory.Replace("'", "''") + "'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_TUI_KEYS='DownArrow||Enter||Escape||Escape'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_CLEAR='1'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_LATEST_VERSION='1.2.3'",
                "Set-Location '" + tempRoot.Replace("'", "''") + "'",
                "& ([scriptblock]::Create((Get-Content '" + repoScriptPath.Replace("'", "''") + "' -Raw))) -Action install -Architecture x64 -Client other"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Should().Contain("UninstallScreen");
            result.Stdout.Should().Contain("VS Code");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

}
