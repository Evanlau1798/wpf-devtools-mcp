using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class InstallerTuiInstallLocationEditorTests
{
    [Fact]
    public void TuiInput_ShouldDeclareInlineInstallLocationEditorContracts()
    {
        var pathEditorContent = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.PathEditor.ps1"));
        var presenterContent = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Presenters.ps1"));

        pathEditorContent.Should().Contain("PathEditorScreen");
        pathEditorContent.Should().Contain("DirectoryPickerScreen");
        pathEditorContent.Should().Contain("FolderNamePromptScreen");
        pathEditorContent.Should().Contain("wpf-devtools-mcp");
        pathEditorContent.Should().Contain("'..'");
        pathEditorContent.Should().Contain("ConsoleKey]::Tab");
        pathEditorContent.Should().Contain("Install location unchanged.");
        pathEditorContent.Should().Contain("Install location updated to");
        pathEditorContent.Should().NotContain("Type a full path, or press Tab to browse folders.");
        pathEditorContent.Should().NotContain("Continue editing the full install path or press Tab to browse folders.");
        presenterContent.Should().Contain("Select install parent directory");
    }

    [Fact]
    public void TuiInstallRootPrompt_ShouldOpenDirectoryPickerImmediately()
    {
        var command = string.Join(" ; ",
        [
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.PathEditor.ps1")) + "'",
            "$state = [ordered]@{ InstallRoot = 'C:\\Users\\tester\\AppData\\Roaming\\WpfDevToolsMcp'; CurrentScreen = 'HomeScreen'; HomeItems = @(); SelectionIndex = 0; ScrollOffset = 0; StatusMessage = '' }",
            "$state = Enter-TuiPathEditorCore -State $state",
            "Write-Output $state.CurrentScreen"
        ]);

        var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

        result.ExitCode.Should().Be(0, result.Stderr);
        result.Stdout.Trim().Should().Be("DirectoryPickerScreen");
    }

    [Fact]
    public void OnlineInstaller_EditInstallLocation_ShouldCancelWithEscapeWithoutChangingRoot()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var appData = Path.Combine(tempRoot, "AppData", "Roaming");
            var localAppData = Path.Combine(tempRoot, "AppData", "Local");
            var userProfile = Path.Combine(tempRoot, "UserProfile");
            var installRoot = Path.Combine(tempRoot, "CustomRoots");
            Directory.CreateDirectory(appData);
            Directory.CreateDirectory(localAppData);
            Directory.CreateDirectory(userProfile);
            Directory.CreateDirectory(installRoot);

            var result = RunInteractiveInstaller(
                tempRoot,
                appData,
                localAppData,
                userProfile,
                installRoot,
                "DownArrow||DownArrow||DownArrow||Enter||Escape||Escape||Enter",
                timeout: TimeSpan.FromSeconds(30));

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Should().Contain("Select install parent directory");
            result.Stdout.Should().Contain("Install location unchanged.");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_EditInstallLocation_ShouldBrowseDirectoriesAndNameFolderWithinTui()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var appData = Path.Combine(tempRoot, "AppData", "Roaming");
            var localAppData = Path.Combine(tempRoot, "AppData", "Local");
            var userProfile = Path.Combine(tempRoot, "UserProfile");
            var installRoot = Path.Combine(tempRoot, "CustomRoots");
            var alphaPath = Path.Combine(installRoot, "Alpha");
            var betaPath = Path.Combine(alphaPath, "Beta");
            Directory.CreateDirectory(appData);
            Directory.CreateDirectory(localAppData);
            Directory.CreateDirectory(userProfile);
            Directory.CreateDirectory(betaPath);

            var result = RunInteractiveInstaller(
                tempRoot,
                appData,
                localAppData,
                userProfile,
                installRoot,
                "DownArrow||DownArrow||DownArrow||Enter||DownArrow||Enter||DownArrow||Enter||Tab||Text:-team||Enter||Escape||Enter",
                timeout: TimeSpan.FromSeconds(90));

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Should().Contain("Select install parent directory");
            result.Stdout.Should().Contain("Name install folder");
            result.Stdout.Should().Contain("wpf-devtools-mcp-team");
            result.Stdout.Should().Contain("Beta");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static (int ExitCode, string Stdout, string Stderr) RunInteractiveInstaller(
        string tempRoot,
        string appData,
        string localAppData,
        string userProfile,
        string installRoot,
        string keySequence,
        TimeSpan timeout)
    {
        var repoScriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1");
        var helperDirectory = ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer");
        var command = string.Join(" ; ",
        [
            "$env:APPDATA='" + EscapeForPowerShell(appData) + "'",
            "$env:LOCALAPPDATA='" + EscapeForPowerShell(localAppData) + "'",
            "$env:USERPROFILE='" + EscapeForPowerShell(userProfile) + "'",
            "$env:WPFDEVTOOLS_INSTALLER_HELPER_DIRECTORY='" + EscapeForPowerShell(helperDirectory) + "'",
            "$env:WPFDEVTOOLS_INSTALLER_TEST_TUI_KEYS='" + EscapeForPowerShell(keySequence) + "'",
            "$env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_CLEAR='1'",
            "$env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_ANSI='1'",
            "$env:WPFDEVTOOLS_INSTALLER_TEST_CONSOLE_WIDTH='96'",
            "$env:WPFDEVTOOLS_INSTALLER_TEST_CONSOLE_HEIGHT='28'",
            "$env:WPFDEVTOOLS_INSTALLER_TEST_LATEST_VERSION='1.2.3'",
            "Set-Location '" + EscapeForPowerShell(tempRoot) + "'",
            "& ([scriptblock]::Create((Get-Content '" + EscapeForPowerShell(repoScriptPath) + "' -Raw))) -Action install -Architecture x64 -Client other -InstallRoot '" + EscapeForPowerShell(installRoot) + "'"
        ]);

        return ReleaseScriptTestHarness.RunPowerShellCommand(command, timeout: timeout);
    }

    private static string EscapeForPowerShell(string value)
        => value.Replace("'", "''");
}
