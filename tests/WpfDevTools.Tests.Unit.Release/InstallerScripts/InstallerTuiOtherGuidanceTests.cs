using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class InstallerTuiOtherGuidanceTests
{
    [Fact]
    public void TuiScreenModel_ShouldDescribeOtherTargetWithArtifactAndDocumentationGuidance()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.ScreenModel.ps1"));

        content.Should().Contain("other.mcpServers.json");
        content.Should().Contain("claude-code.txt");
        content.Should().Contain("codex.txt");
        content.Should().Contain("AI Agent Clients");
    }

    [Fact]
    public void OnlineInstaller_InstallScreen_ShouldExplainOtherArtifactsInStatusPanel()
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
                "$env:APPDATA='" + EscapeForPowerShell(appData) + "'",
                "$env:LOCALAPPDATA='" + EscapeForPowerShell(localAppData) + "'",
                "$env:USERPROFILE='" + EscapeForPowerShell(userProfile) + "'",
                "$env:PATH='" + EscapeForPowerShell(BuildShimOnlyPath(fakeBin)) + "'",
                "$env:WPFDEVTOOLS_INSTALLER_HELPER_DIRECTORY='" + EscapeForPowerShell(helperDirectory) + "'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_TUI_KEYS='Enter||DownArrow||DownArrow||DownArrow||DownArrow||DownArrow||DownArrow||Escape||Escape||Enter'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_CLEAR='1'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_ANSI='1'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_CONSOLE_WIDTH='96'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_CONSOLE_HEIGHT='28'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_LATEST_VERSION='1.2.3'",
                "Set-Location '" + EscapeForPowerShell(tempRoot) + "'",
                "& ([scriptblock]::Create((Get-Content '" + EscapeForPowerShell(repoScriptPath) + "' -Raw))) -Action install -Architecture x64 -Client other"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Should().Contain("Where would you like to install?");
            result.Stdout.Should().Contain("other.mcpServers.json");
            result.Stdout.Should().Contain("claude-code.txt");
            result.Stdout.Should().Contain("codex.txt");
            result.Stdout.Should().Contain("Registration examples");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static string EscapeForPowerShell(string value)
        => value.Replace("'", "''");

    private static string BuildShimOnlyPath(string fakeBin)
        => string.Join(
            Path.PathSeparator,
            [
                fakeBin,
                Environment.SystemDirectory,
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "System32", "WindowsPowerShell", "v1.0")
            ]);
}
