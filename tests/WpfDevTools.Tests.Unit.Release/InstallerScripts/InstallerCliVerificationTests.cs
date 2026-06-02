using FluentAssertions;
using Xunit;
using static WpfDevTools.Tests.Unit.Release.InstallerScriptTestSupport;

namespace WpfDevTools.Tests.Unit.Release;

public sealed partial class InstallerScriptTests
{
    [Fact]
    public void OnlineInstallerScript_ShouldDeclareVerificationCommandHelper()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));
        var manifestContent = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/installer-helpers.manifest.json"));

        content.Should().Contain("scripts/installer/Installer.Verification.Commands.ps1");
        manifestContent.Should().Contain("Installer.Verification.Commands.ps1");
        content.IndexOf(
                "scripts/installer/Installer.Verification.Commands.ps1",
                StringComparison.Ordinal)
            .Should()
            .BeLessThan(content.IndexOf(
                "scripts/installer/Installer.Verification.ps1",
                StringComparison.Ordinal));
        content.IndexOf(
                "'Installer.Verification.Commands.ps1'",
                StringComparison.Ordinal)
            .Should()
            .BeLessThan(content.IndexOf(
                "'Installer.Verification.ps1'",
                StringComparison.Ordinal));
    }

    [Fact]
    public void OnlineInstaller_ShouldFailCliRegistrationWhenListDoesNotExposeExecutablePath()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install-root");
            var fakeBin = Path.Combine(tempRoot, "bin");
            var claudeLog = Path.Combine(tempRoot, "claude.log");
            Directory.CreateDirectory(fakeBin);
            File.WriteAllText(
                Path.Combine(fakeBin, "claude.cmd"),
                string.Join(
                    Environment.NewLine,
                    [
                        "@echo off",
                        "echo %*>>\"" + claudeLog + "\"",
                        "if /I \"%1 %2\"==\"mcp list\" echo wpf-devtools",
                        "exit /b 0"
                    ]));

            var result = RunInstaller(
                tempRoot,
                [
                    "-PackageArchivePath", archivePath,
                    "-InstallRoot", installRoot,
                    "-Client", "claude-code",
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ],
                new Dictionary<string, string?>
                {
                    ["PATH"] = fakeBin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH")
                });

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("could not verify").And.Contain("mcp list");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void VerificationCommand_ShouldTerminateTimedOutBatchProcessTree()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var fakeBin = Path.Combine(tempRoot, "bin");
            var timeoutMarker = Path.Combine(tempRoot, "claude-timeout-marker.log");
            Directory.CreateDirectory(fakeBin);
            File.WriteAllText(
                Path.Combine(fakeBin, "claude.cmd"),
                "@echo off" + Environment.NewLine +
                "if \"%1 %2\"==\"mcp list\" (" + Environment.NewLine +
                "  powershell -NoProfile -Command \"Start-Sleep -Seconds 3; Set-Content -Path '" + timeoutMarker.Replace("'", "''") + "' -Value done\"" + Environment.NewLine +
                "  echo wpf-devtools" + Environment.NewLine +
                "  exit /b 0" + Environment.NewLine +
                ")" + Environment.NewLine +
                "exit /b 0" + Environment.NewLine);

            var command = string.Join(" ; ",
            [
                "function Get-InstallerTimeoutSeconds { param([string]$EnvironmentVariable, [int]$DefaultValue, [int]$MinimumValue = 1, [int]$MaximumValue = 120) $rawValue = [Environment]::GetEnvironmentVariable($EnvironmentVariable); if ([string]::IsNullOrWhiteSpace($rawValue)) { return $DefaultValue }; $parsedValue = 0; if (-not [int]::TryParse($rawValue, [ref]$parsedValue)) { return $DefaultValue }; return [Math]::Min($MaximumValue, [Math]::Max($MinimumValue, $parsedValue)) }",
                "function Get-InstallerVerificationTimeoutSeconds { return (Get-InstallerTimeoutSeconds -EnvironmentVariable 'WPFDEVTOOLS_INSTALLER_VERIFICATION_TIMEOUT_SEC' -DefaultValue 2 -MinimumValue 1 -MaximumValue 30) }",
                ". '" + ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Installer.Verification.Commands.ps1").Replace("'", "''") + "'",
                "$env:PATH='" + BuildShimOnlyPath(fakeBin).Replace("'", "''") + "'",
                "$env:WPFDEVTOOLS_INSTALLER_VERIFICATION_TIMEOUT_SEC='1'",
                "$verification = Invoke-VerificationCommand -Command 'claude' -Arguments @('mcp', 'list') -ExpectedToken 'wpf-devtools' -ExpectPresent $true",
                "if ($verification.ExitCode -ne -2) { throw ('Expected timeout exit code -2, got ' + $verification.ExitCode + ': ' + $verification.Output) }"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command, timeout: TimeSpan.FromSeconds(30));

            result.ExitCode.Should().Be(0, result.Stderr);
            System.Threading.Thread.Sleep(TimeSpan.FromMilliseconds(3500));
            File.Exists(timeoutMarker).Should().BeFalse("timed out batch CLI verification must stop descendants before marker writes");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

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
