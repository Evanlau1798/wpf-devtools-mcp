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
        content.Should().Contain("DirectoryPickerScreen");
        content.Should().Contain("FolderNamePromptScreen");
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

        content.Should().Contain("scripts/installer/Tui.Window.ps1");
        content.Should().Contain("scripts/installer/Tui.Presenters.ps1");
        content.Should().Contain("scripts/installer/Tui.PathEditor.ps1");
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
    public void TuiConfirm_ShouldDeclareCloseAppConfirmationMode()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Confirm.ps1"));

        content.Should().Contain("'close-app'");
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
        content.Should().Contain("Release version");
        content.Should().Contain("Action (install/uninstall)");
        content.Should().Contain("Architecture (x64/x86/arm64)");
        content.Should().NotContain("<VisualTree/>");
        content.Should().NotContain("PresentationFramework");
    }

    [Fact]
    public void OnlineInstallerScript_CliFallback_ShouldPromptForReleaseVersion()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var repoScriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1");
            var installRoot = Path.Combine(tempRoot, "install-root");
            var escapedRepoScriptPath = repoScriptPath.Replace("'", "''");
            var escapedInstallRoot = installRoot.Replace("'", "''");
            var escapedMarker = TestHelpers.OnlineInstallerDefinitionBoundaryMarker.Replace("'", "''");
            var command = string.Join(" ; ",
            [
                "$env:WPFDEVTOOLS_INSTALLER_TEST_RESPONSES='install||1.2.3||x64||other||" + escapedInstallRoot + "'",
                "$content = Get-Content '" + escapedRepoScriptPath + "' -Raw",
                "$marker = '" + escapedMarker + "'",
                "$prefix = $content.Substring(0, $content.IndexOf($marker))",
                ". ([scriptblock]::Create($prefix))",
                "$selection = Get-CliSelection",
                "$selection | ConvertTo-Json -Compress"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            using var payload = JsonDocument.Parse(result.Stdout);
            var root = payload.RootElement;
            root.GetProperty("Action").GetString().Should().Be("install");
            root.GetProperty("Version").GetString().Should().Be("1.2.3");
            root.GetProperty("Architecture").GetString().Should().Be("x64");
            root.GetProperty("Client").GetString().Should().Be("other");
            root.GetProperty("InstallRoot").GetString().Should().Be(installRoot);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
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
                "$env:WPFDEVTOOLS_INSTALLER_TEST_TUI_KEYS='Escape||Enter'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_CLEAR='1'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_LATEST_VERSION='1.2.3'",
                "Set-Location '" + tempRoot.Replace("'", "''") + "'",
                "& ([scriptblock]::Create((Get-Content '" + repoScriptPath.Replace("'", "''") + "' -Raw))) -Action install -Architecture x64 -Client other"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Should().Contain("Installation Manager");
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
                "$env:WPFDEVTOOLS_INSTALLER_TEST_TUI_KEYS='DownArrow||Enter||Escape||Escape||Enter'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_CLEAR='1'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_LATEST_VERSION='1.2.3'",
                "Set-Location '" + tempRoot.Replace("'", "''") + "'",
                "& ([scriptblock]::Create((Get-Content '" + repoScriptPath.Replace("'", "''") + "' -Raw))) -Action install -Architecture x64 -Client other"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Should().Contain("Select what to uninstall");
            result.Stdout.Should().Contain("VS Code");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstallerScript_TuiUninstallScreen_ShouldListCliRegisteredTargetsEvenWhenInstallerStateIsMissing()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var appData = Path.Combine(tempRoot, "AppData", "Roaming");
            var localAppData = Path.Combine(tempRoot, "AppData", "Local");
            var userProfile = Path.Combine(tempRoot, "UserProfile");
            var fakeBin = Path.Combine(tempRoot, "bin");
            Directory.CreateDirectory(appData);
            Directory.CreateDirectory(localAppData);
            Directory.CreateDirectory(userProfile);
            Directory.CreateDirectory(fakeBin);
            var detectedExecutable = Path.Combine(tempRoot, "external", "wpf-devtools-x64.exe");

            File.WriteAllText(
                Path.Combine(fakeBin, "claude.cmd"),
                "@echo off" + Environment.NewLine +
                "if \"%1 %2\"==\"mcp list\" echo wpf-devtools " + detectedExecutable + Environment.NewLine +
                "exit /b 0" + Environment.NewLine);
            File.WriteAllText(
                Path.Combine(fakeBin, "codex.cmd"),
                "@echo off" + Environment.NewLine +
                "if \"%1 %2\"==\"mcp list\" echo wpf-devtools " + detectedExecutable + Environment.NewLine +
                "exit /b 0" + Environment.NewLine);

            var repoScriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1");
            var helperDirectory = ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer");
            var command = string.Join(" ; ",
            [
                "$env:APPDATA='" + appData.Replace("'", "''") + "'",
                "$env:LOCALAPPDATA='" + localAppData.Replace("'", "''") + "'",
                "$env:USERPROFILE='" + userProfile.Replace("'", "''") + "'",
                "$env:PATH='" + fakeBin.Replace("'", "''") + ";" + Environment.GetEnvironmentVariable("PATH")!.Replace("'", "''") + "'",
                "$env:WPFDEVTOOLS_INSTALLER_HELPER_DIRECTORY='" + helperDirectory.Replace("'", "''") + "'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_TUI_KEYS='DownArrow||Enter||Escape||Escape||Enter'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_CLEAR='1'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_LATEST_VERSION='1.2.3'",
                "Set-Location '" + tempRoot.Replace("'", "''") + "'",
                "& ([scriptblock]::Create((Get-Content '" + repoScriptPath.Replace("'", "''") + "' -Raw))) -Action install -Architecture x64 -Client other"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Should().Contain("Select what to uninstall");
            result.Stdout.Should().Contain("Claude Code");
            result.Stdout.Should().Contain("Codex");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void TuiRenderer_UninstallScreen_ShouldRenderFullUninstallAfterDivider()
    {
        var helperDirectory = ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer");
        var command = string.Join(" ; ",
        [
            "$env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_ANSI='1'",
            "$env:WPFDEVTOOLS_INSTALLER_TEST_CONSOLE_WIDTH='100'",
            "$env:WPFDEVTOOLS_INSTALLER_TEST_CONSOLE_HEIGHT='32'",
            ". '" + Path.Combine(helperDirectory, "Tui.Terminal.ps1").Replace("'", "''") + "'",
            ". '" + Path.Combine(helperDirectory, "Tui.Layout.ps1").Replace("'", "''") + "'",
            ". '" + Path.Combine(helperDirectory, "Tui.Sections.ps1").Replace("'", "''") + "'",
            ". '" + Path.Combine(helperDirectory, "Tui.Window.ps1").Replace("'", "''") + "'",
            ". '" + Path.Combine(helperDirectory, "Tui.ScreenModel.ps1").Replace("'", "''") + "'",
            ". '" + Path.Combine(helperDirectory, "Tui.Presenters.ps1").Replace("'", "''") + "'",
            ". '" + Path.Combine(helperDirectory, "Tui.Renderer.ps1").Replace("'", "''") + "'",
            "$state = [ordered]@{ CurrentScreen='UninstallScreen'; SelectedArchitecture='x64'; InstallRoot='C:\\WpfDevTools'; UpdateBannerText=''; SelectionIndex=1; ScrollOffset=0; VisibleWindowSize=4; HomeItems=@(); InstallItems=@(); UninstallItems=@(",
            "[ordered]@{ Id='vscode'; Label='VS Code (Installed)'; PrimaryText='VS Code'; SecondaryText=''; StatusBadge='Installed'; IsPrimaryAction=$false; Installed=$true; Available=$true; Description='Press Enter to remove this registration.' }",
            "[ordered]@{ Id='full-uninstall'; Label='Full Uninstall'; PrimaryText='Full Uninstall'; SecondaryText='Remove every detected registration and every installer-owned server location.'; StatusBadge=''; IsPrimaryAction=$false; ShowDividerBefore=$true; Installed=$true; Available=$true; Description='Press Enter to remove all detected registrations and installer-owned server files.' }",
            ") }",
            "Render-TuiScreenCore -State $state -AsString"
        ]);

        var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

        result.ExitCode.Should().Be(0, result.Stderr);
        result.Stdout.Should().Contain("Select what to uninstall");
        result.Stdout.Should().Contain("├");
        result.Stdout.Should().Contain("Full Uninstall");
    }

    [Fact]
    public void OnlineInstallerScript_TuiUninstall_ShouldRequireTwoStepConfirmationBeforeRemovingRegistration()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var visualStudioConfigPath = Path.Combine(tempRoot, "config", "VisualStudio", ".mcp.json");

            ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                ["-PackageArchivePath", archivePath, "-InstallRoot", installRoot, "-Client", "visual-studio", "-VisualStudioConfigPath", visualStudioConfigPath, "-NonInteractive", "-Force", "-OutputJson"],
                new Dictionary<string, string?>
                {
                    ["APPDATA"] = Path.Combine(tempRoot, "AppData", "Roaming"),
                    ["LOCALAPPDATA"] = Path.Combine(tempRoot, "AppData", "Local"),
                    ["USERPROFILE"] = Path.Combine(tempRoot, "UserProfile")
                }).ExitCode.Should().Be(0);

            var repoScriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1");
            var helperDirectory = ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer");
            var fakeBin = Path.Combine(tempRoot, "bin");
            Directory.CreateDirectory(fakeBin);
            var command = string.Join(" ; ",
            [
                "$env:APPDATA='" + Path.Combine(tempRoot, "AppData", "Roaming").Replace("'", "''") + "'",
                "$env:LOCALAPPDATA='" + Path.Combine(tempRoot, "AppData", "Local").Replace("'", "''") + "'",
                "$env:USERPROFILE='" + Path.Combine(tempRoot, "UserProfile").Replace("'", "''") + "'",
                "$env:PATH='" + fakeBin.Replace("'", "''") + "'",
                "$env:WPFDEVTOOLS_INSTALLER_HELPER_DIRECTORY='" + helperDirectory.Replace("'", "''") + "'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_TUI_KEYS='DownArrow||Enter||Enter||Escape||Escape||Escape||Enter'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_CLEAR='1'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_LATEST_VERSION='1.2.3'",
                "Set-Location '" + tempRoot.Replace("'", "''") + "'",
                "& ([scriptblock]::Create((Get-Content '" + repoScriptPath.Replace("'", "''") + "' -Raw))) -Action install -Architecture x64 -Client other -VisualStudioConfigPath '" + visualStudioConfigPath.Replace("'", "''") + "'"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Should().Contain("Confirm action");
            result.Stdout.Should().Contain("Step 1 of 2");
            File.ReadAllText(visualStudioConfigPath).Should().Contain("wpf-devtools");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstallerScript_TuiFullUninstall_ShouldEnterTwoStepConfirmationBeforeExecution()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var appData = Path.Combine(tempRoot, "AppData", "Roaming");
            var localAppData = Path.Combine(tempRoot, "AppData", "Local");
            var userProfile = Path.Combine(tempRoot, "UserProfile");
            var fakeBin = Path.Combine(tempRoot, "bin");
            Directory.CreateDirectory(appData);
            Directory.CreateDirectory(localAppData);
            Directory.CreateDirectory(userProfile);
            Directory.CreateDirectory(fakeBin);

            var repoScriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1");
            var helperDirectory = ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer");
            var command = string.Join(" ; ",
            [
                "$env:APPDATA='" + appData.Replace("'", "''") + "'",
                "$env:LOCALAPPDATA='" + localAppData.Replace("'", "''") + "'",
                "$env:USERPROFILE='" + userProfile.Replace("'", "''") + "'",
                "$env:PATH='" + fakeBin.Replace("'", "''") + "'",
                "$env:WPFDEVTOOLS_INSTALLER_HELPER_DIRECTORY='" + helperDirectory.Replace("'", "''") + "'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_TUI_KEYS='DownArrow||Enter||Enter||Escape||Escape||Escape||Enter'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_CLEAR='1'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_LATEST_VERSION='1.2.3'",
                "Set-Location '" + tempRoot.Replace("'", "''") + "'",
                "& ([scriptblock]::Create((Get-Content '" + repoScriptPath.Replace("'", "''") + "' -Raw))) -Action install -Architecture x64 -Client other"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Should().Contain("Confirm action");
            result.Stdout.Should().Contain("Full Uninstall");
            result.Stdout.Should().Contain("Step 1 of 2");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }
}
