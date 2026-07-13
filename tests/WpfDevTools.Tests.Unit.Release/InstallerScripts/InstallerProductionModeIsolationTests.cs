using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Tests.Unit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class InstallerProductionModeIsolationTests
{
    [Fact]
    public void TestOnlyEnvironmentControls_ShouldBeInertWithoutHarnessAuthority()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var appData = Path.Combine(tempRoot, "AppData", "Roaming");
            Directory.CreateDirectory(appData);
            var environmentNames = new[]
            {
                "WPFDEVTOOLS_INSTALLER_TEST_RESPONSES",
                "WPFDEVTOOLS_INSTALLER_TEST_FAIL_SAVE_STANDALONE_STATE",
                "WPFDEVTOOLS_INSTALLER_TEST_LATEST_VERSION",
                "WPFDEVTOOLS_INSTALLER_TEST_LATEST_PRERELEASE_VERSION",
                "WPFDEVTOOLS_INSTALLER_TEST_REMOTE_LATEST_VERSION",
                "WPFDEVTOOLS_INSTALLER_TEST_REMOTE_LATEST_PRERELEASE_VERSION"
            };
            var command = string.Join(" ; ",
            [
                "$env:APPDATA='" + Escape(appData) + "'",
                OnlineInstallerScriptTestHarness.BuildDefinitionOnlyPrelude(
                    "-Action install -Architecture x64 -Client other -InstallRoot '" +
                    Escape(Path.Combine(tempRoot, "install-root")) + "' -NonInteractive",
                    enableInternalTestMode: false),
                "$env:WPFDEVTOOLS_INSTALLER_TEST_RESPONSES='ambient-production-value'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_FAIL_SAVE_STANDALONE_STATE='1'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_LATEST_VERSION='ambient-latest'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_LATEST_PRERELEASE_VERSION='ambient-prerelease'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_REMOTE_LATEST_VERSION='ambient-remote-latest'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_REMOTE_LATEST_PRERELEASE_VERSION='ambient-remote-prerelease'",
                "function Read-Host { param([string]$Prompt) return 'interactive-production-value' }",
                "function Get-CachedLatestInstallerVersion { param([string]$ReleaseChannel) return 'cached-production-version' }",
                "$inputValue = Read-InstallerInput -Prompt 'Choice' -DefaultValue 'default'",
                "$latestVersion = Get-LatestInstallerVersion -UseCacheOnly",
                "$statePath = Save-StandaloneInstallerState -State (Get-StandaloneEmptyInstallerState)",
                "$activeTestValueCount = @(@('" + string.Join("','", environmentNames) + "') | ForEach-Object { Get-InstallerTestEnvironmentValue -Name $_ } | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) }).Count",
                "@{ mode = [bool]$script:WpfDevToolsInstallerTestModeEnabled; inputValue = $inputValue; latestVersion = $latestVersion; stateExists = (Test-Path -LiteralPath $statePath); activeTestValueCount = $activeTestValueCount } | ConvertTo-Json -Compress"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(
                command,
                new Dictionary<string, string?> { ["WPFDEVTOOLS_INSTALLER_TEST_MODE"] = "0" });

            result.ExitCode.Should().Be(0, result.Stderr);
            using var payload = JsonDocument.Parse(result.Stdout);
            payload.RootElement.GetProperty("mode").GetBoolean().Should().BeFalse();
            payload.RootElement.GetProperty("inputValue").GetString().Should().Be("interactive-production-value");
            payload.RootElement.GetProperty("latestVersion").GetString().Should().Be("cached-production-version");
            payload.RootElement.GetProperty("stateExists").GetBoolean().Should().BeTrue();
            payload.RootElement.GetProperty("activeTestValueCount").GetInt32().Should().Be(0);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void AmbientTuiTestControls_ShouldBeRejectedWithoutHarnessAuthority()
    {
        var command = string.Join(" ; ",
        [
            "$env:WPFDEVTOOLS_INSTALLER_TEST_TUI_KEYS='Enter||Enter'",
            "$env:WPFDEVTOOLS_INSTALLER_TEST_CONSOLE_WIDTH='40'",
            OnlineInstallerScriptTestHarness.BuildDefinitionOnlyPrelude(
                "-Action install -Architecture x64 -Client other -NonInteractive",
                enableInternalTestMode: false)
        ]);

        var result = ReleaseScriptTestHarness.RunPowerShellCommand(
            command,
            new Dictionary<string, string?> { ["WPFDEVTOOLS_INSTALLER_TEST_MODE"] = "0" });

        result.ExitCode.Should().NotBe(0);
        result.Stderr.Should().Contain("WPFDEVTOOLS_INSTALLER_TEST_TUI_KEYS");
        result.Stderr.Should().Contain("WPFDEVTOOLS_INSTALLER_TEST_MODE=1");
    }

    private static string Escape(string value)
        => OnlineInstallerScriptTestHarness.EscapePowerShellSingleQuotedString(value);
}
