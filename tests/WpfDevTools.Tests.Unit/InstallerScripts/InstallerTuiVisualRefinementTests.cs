using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class InstallerTuiVisualRefinementTests
{
    [Fact]
    public void OnlineInstaller_HomeScreen_ShouldRenderPseudoCaptionControlsAndBoxedUtilityRowsWithoutLegacyExitCard()
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

            var result = RunInteractiveInstaller(tempRoot, appData, localAppData, userProfile, new[]
            {
                "$env:WPFDEVTOOLS_INSTALLER_TEST_TUI_KEYS='Escape||Enter'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_CLEAR='1'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_ANSI='1'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_CONSOLE_WIDTH='96'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_CONSOLE_HEIGHT='28'"
            });

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Should().Contain("[_]").And.Contain("[ ]").And.Contain("[X]");
            result.Stdout.Should().Contain("Update All");
            result.Stdout.Should().Contain("Install location");
            result.Stdout.Should().NotContain("│ Exit");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_HomeScreen_ShouldWrapLongInstallLocationWithoutEllipsis()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var appData = Path.Combine(tempRoot, "AppData", "Roaming");
            var localAppData = Path.Combine(tempRoot, "AppData", "Local");
            var userProfile = Path.Combine(tempRoot, "UserProfile");
            var installRoot = @"C:\Very\Long\Nested\Path\For\WpfDevTools\Server\Install\That\Should\Wrap\Without\Ellipsis\Current\Location";
            Directory.CreateDirectory(appData);
            Directory.CreateDirectory(localAppData);
            Directory.CreateDirectory(userProfile);

            var result = RunInteractiveInstaller(tempRoot, appData, localAppData, userProfile, new[]
            {
                "$env:WPFDEVTOOLS_INSTALLER_TEST_TUI_KEYS='Escape||Enter'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_CLEAR='1'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_ANSI='1'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_CONSOLE_WIDTH='96'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_CONSOLE_HEIGHT='28'"
            }, installRoot);

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Should().Contain(@"C:\Very\Long\Nested\Path\For\WpfDevTools");
            result.Stdout.Should().Contain(@"Wrap\Without\Ellipsis\Current\Location");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_InstallScreen_ShouldRenderTargetsAsIndividualItemButtons()
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
            ReleaseScriptTestHarness.CreateFakeCommand(fakeBin, "code", Path.Combine(tempRoot, "code.log"));

            var result = RunInteractiveInstaller(tempRoot, appData, localAppData, userProfile, new[]
            {
                "$env:PATH='" + (fakeBin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH")!).Replace("'", "''") + "'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_TUI_KEYS='Enter||Escape||Escape||Enter'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_CLEAR='1'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_ANSI='1'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_CONSOLE_WIDTH='96'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_CONSOLE_HEIGHT='28'"
            });

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Should().Contain("Where would you like to install?");
            result.Stdout.Should().Contain("┌");
            result.Stdout.Should().Contain("VS Code");
            result.Stdout.Should().Contain("└");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_HomeScreen_ShouldRefreshUpdateAllSummaryAfterBackgroundLatestVersionFetch()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var appData = Path.Combine(tempRoot, "AppData", "Roaming");
            var localAppData = Path.Combine(tempRoot, "AppData", "Local");
            var userProfile = Path.Combine(tempRoot, "UserProfile");
            var installRoot = Path.Combine(tempRoot, "install-root");
            var executablePath = Path.Combine(installRoot, "x64", "current", "bin", "wpf-devtools-x64.exe");
            var installBase = Path.Combine(installRoot, "x64");
            var configPath = Path.Combine(appData, "Code", "User", "mcp.json");

            Directory.CreateDirectory(Path.GetDirectoryName(executablePath)!);
            Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
            Directory.CreateDirectory(localAppData);
            Directory.CreateDirectory(userProfile);

            File.WriteAllText(executablePath, "stub");
            File.WriteAllText(
                Path.Combine(installBase, "install-manifest.json"),
                """
                {
                  "name": "wpf-devtools",
                  "architecture": "x64",
                  "version": "1.0.0",
                  "installRoot": "__INSTALL_ROOT__",
                  "installDir": "__INSTALL_DIR__",
                  "executable": "__EXECUTABLE__"
                }
                """
                .Replace("__INSTALL_ROOT__", installRoot.Replace("\\", "\\\\"))
                .Replace("__INSTALL_DIR__", Path.Combine(installBase, "current").Replace("\\", "\\\\"))
                .Replace("__EXECUTABLE__", executablePath.Replace("\\", "\\\\")));
            File.WriteAllText(
                configPath,
                """
                {
                  "servers": {
                    "wpf-devtools": {
                      "command": "__EXECUTABLE__",
                      "args": []
                    }
                  }
                }
                """
                .Replace("__EXECUTABLE__", executablePath.Replace("\\", "\\\\")));

            var result = RunInteractiveInstaller(tempRoot, appData, localAppData, userProfile, new[]
            {
                "$env:WPFDEVTOOLS_INSTALLER_TEST_TUI_KEYS='Tick||Tick||Tick||Escape||Enter'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_CLEAR='1'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_ANSI='1'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_CONSOLE_WIDTH='96'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_CONSOLE_HEIGHT='28'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_REMOTE_LATEST_VERSION='1.2.3'"
            });

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Should().Contain("1 target(s) can move to v1.2.3.");
            result.Stdout.Should().NotContain("All detected targets are up to date.");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstaller_HomeScreen_ShouldOpenCloseConfirmationInsteadOfExitingImmediatelyOnEscape()
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

            var result = RunInteractiveInstaller(tempRoot, appData, localAppData, userProfile, new[]
            {
                "$env:WPFDEVTOOLS_INSTALLER_TEST_TUI_KEYS='Escape||Enter'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_CLEAR='1'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_ANSI='1'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_CONSOLE_WIDTH='96'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_CONSOLE_HEIGHT='28'"
            });

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Should().Contain("Confirm close");
            result.Stdout.Should().Contain("Press Enter once to close the installer");
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
        IEnumerable<string> extraCommands,
        string? installRoot = null)
    {
        var repoScriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1");
        var helperDirectory = ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer");
        var commandParts = new List<string>
        {
            "$env:APPDATA='" + appData.Replace("'", "''") + "'",
            "$env:LOCALAPPDATA='" + localAppData.Replace("'", "''") + "'",
            "$env:USERPROFILE='" + userProfile.Replace("'", "''") + "'",
            "$env:WPFDEVTOOLS_INSTALLER_HELPER_DIRECTORY='" + helperDirectory.Replace("'", "''") + "'",
            "Set-Location '" + tempRoot.Replace("'", "''") + "'"
        };

        commandParts.AddRange(extraCommands);

        var installRootArgument = string.IsNullOrWhiteSpace(installRoot)
            ? string.Empty
            : " -InstallRoot '" + installRoot.Replace("'", "''") + "'";
        commandParts.Add(
            "& ([scriptblock]::Create((Get-Content '" + repoScriptPath.Replace("'", "''") + "' -Raw))) -Action install -Architecture x64 -Client other" +
            installRootArgument);

        return ReleaseScriptTestHarness.RunPowerShellCommand(string.Join(" ; ", commandParts));
    }
}
