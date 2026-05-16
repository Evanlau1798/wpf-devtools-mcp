using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Diagnostics;
using FluentAssertions;
using WpfDevTools.Tests.Unit;
using Xunit;
using Xunit.Sdk;

namespace WpfDevTools.Tests.Unit.Release;

public sealed partial class InstallerBootstrapTests
{
    [Fact]
    public void OnlineInstallerScript_ShouldDeferLatestVersionLookupUntilAfterTuiStartup()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().Contain("Initialize-TuiStartupState");
        content.Should().Contain("Get-LatestInstallerVersion -UseCacheOnly");
    }

    [Fact]
    public void TuiFlow_ShouldEnterTerminalSessionBeforeRunningStartupInitialization()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Flow.ps1"));

        var startFunctionIndex = content.IndexOf("function Start-TuiInstallerCore", StringComparison.Ordinal);
        startFunctionIndex.Should().BeGreaterThanOrEqualTo(0);

        var startFunctionBody = content[startFunctionIndex..];
        var enterSessionIndex = startFunctionBody.IndexOf("Enter-TuiTerminalSessionCore", StringComparison.Ordinal);
        var initializeIndex = startFunctionBody.IndexOf("Initialize-TuiStartupStateCore -State $state", StringComparison.Ordinal);

        enterSessionIndex.Should().BeGreaterThanOrEqualTo(0);
        initializeIndex.Should().BeGreaterThanOrEqualTo(0);
        enterSessionIndex.Should().BeLessThan(initializeIndex);
    }

    [Fact]
    public void OnlineInstallerScript_ShouldDeclareBootstrapProgressAndCliFallbackMessages()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().Contain("Write-TuiBootstrapScreen");
        content.Should().Contain("Preparing installer UI...");
        content.Should().Contain("Installer UI bootstrap failed. Falling back to plain CLI.");
    }

    [Fact]
    public void OnlineInstallerScript_ShouldUseManifestBackedHelperCacheKeys()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().Contain("installer-helpers.manifest.json");
        content.Should().Contain("Get-InstallerHelperRuntimeCacheKey");
        content.Should().NotContain("WPFDEVTOOLS_INSTALLER_HELPER_CACHE_KEY:v1");
    }

    [Fact]
    public void OnlineInstallerScript_ShouldKeepDefinitionBoundaryMarkerImmediatelyAboveMainEntrypoint()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));
        var marker = TestHelpers.OnlineInstallerDefinitionBoundaryMarker;

        var markerIndex = content.IndexOf(marker, StringComparison.Ordinal);
        markerIndex.Should().BeGreaterThanOrEqualTo(0);
        content.LastIndexOf(marker, StringComparison.Ordinal).Should().Be(markerIndex,
            "definition-only loading must rely on a single unambiguous boundary marker");

        var entrypointIndex = content.IndexOf(
            "$selectionContext = Resolve-Selection",
            markerIndex + marker.Length,
            StringComparison.Ordinal);
        entrypointIndex.Should().BeGreaterThan(markerIndex);

        var separator = content.Substring(markerIndex + marker.Length, entrypointIndex - (markerIndex + marker.Length));
        separator.Should().MatchRegex("^\\r?\\n$",
            "the boundary marker should stay immediately above the main entrypoint");
    }

    [Fact]
    public void OnlineInstallerDefinitionLoader_ShouldPreserveScriptRootContext()
    {
        var scriptsRoot = ReleaseScriptTestHarness.GetRepoFilePath("scripts");
        var command = $$"""
{{OnlineInstallerScriptTestHarness.BuildDefinitionOnlyPrelude("-Action install -Architecture x64 -Client other -NonInteractive")}}
Resolve-InstallerScriptRoot
""";

        var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

        result.ExitCode.Should().Be(0, result.Stderr);
        result.Stdout.Trim().Should().Be(scriptsRoot);
    }

    [Fact]
    public void OnlineInstallerDefinitionLoader_ShouldSupportWildcardCharactersInScriptPath()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var wildcardRepoRoot = Path.Combine(tempRoot, "repo[1]");
            var wildcardScriptsRoot = Path.Combine(wildcardRepoRoot, "scripts");
            Directory.CreateDirectory(wildcardScriptsRoot);

            var sourceScriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1");
            var wildcardScriptPath = Path.Combine(wildcardScriptsRoot, "online-installer.ps1");
            File.Copy(sourceScriptPath, wildcardScriptPath, overwrite: true);

            var command = $$"""
{{OnlineInstallerScriptTestHarness.BuildDefinitionOnlyPrelude("-Action install -Architecture x64 -Client other -NonInteractive", wildcardScriptPath)}}
Resolve-InstallerScriptRoot
""";

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Trim().Should().Be(wildcardScriptsRoot);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstallerScript_ShouldRejectHelperOverrideOutsideTestMode()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var helperDirectory = ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer");
            var command = string.Join(" ; ",
            [
                "$env:WPFDEVTOOLS_INSTALLER_HELPER_DIRECTORY='" + helperDirectory.Replace("'", "''") + "'",
                ". ([scriptblock]::Create((Get-Content -LiteralPath '" + ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1").Replace("'", "''") + "' -Raw))) -Action install -Architecture x64 -Client other -NonInteractive",
                "Get-TuiHelperOverrideDirectory"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(
                command,
                new Dictionary<string, string?>
                {
                    ["APPDATA"] = Path.Combine(tempRoot, "AppData", "Roaming"),
                    ["LOCALAPPDATA"] = Path.Combine(tempRoot, "AppData", "Local"),
                    ["USERPROFILE"] = Path.Combine(tempRoot, "UserProfile"),
                    ["WPFDEVTOOLS_INSTALLER_TEST_MODE"] = "0"
                });

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("WPFDEVTOOLS_INSTALLER_HELPER_DIRECTORY is supported only when WPFDEVTOOLS_INSTALLER_TEST_MODE=1");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstallerScript_ShouldRejectHelperBaseUriOverrideOutsideTestMode()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var command = string.Join(" ; ",
            [
                "$env:WPFDEVTOOLS_INSTALLER_HELPER_BASE_URI='http://127.0.0.1:9/installer'",
                OnlineInstallerScriptTestHarness.BuildDefinitionOnlyPrelude(
                    "-Action install -Architecture x64 -Client other -NonInteractive",
                    enableInternalTestMode: false),
                "Resolve-TuiHelperDownloadBaseUri"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(
                command,
                new Dictionary<string, string?>
                {
                    ["APPDATA"] = Path.Combine(tempRoot, "AppData", "Roaming"),
                    ["LOCALAPPDATA"] = Path.Combine(tempRoot, "AppData", "Local"),
                    ["USERPROFILE"] = Path.Combine(tempRoot, "UserProfile"),
                    ["WPFDEVTOOLS_INSTALLER_TEST_MODE"] = "0"
                });

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("WPFDEVTOOLS_INSTALLER_HELPER_BASE_URI is supported only when WPFDEVTOOLS_INSTALLER_TEST_MODE=1");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void TrustedReleaseMetadataDirectory_ShouldRejectUncPathBeforeProbing()
    {
        var command = string.Join(" ; ",
        [
            OnlineInstallerScriptTestHarness.BuildDefinitionOnlyPrelude(
                "-Action install -Architecture x64 -Client other -NonInteractive",
                enableInternalTestMode: false),
            ". '" + ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Installer.PackageIntegrity.ps1").Replace("'", "''") + "'",
            "$env:WPFDEVTOOLS_TRUSTED_RELEASE_METADATA_DIRECTORY='\\\\server\\share\\metadata'",
            "function Test-Path { param([string]$LiteralPath, [string]$Path) throw 'untrusted Test-Path probe' }",
            "Get-ExplicitTrustedReleaseMetadataDirectory"
        ]);

        var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

        result.ExitCode.Should().NotBe(0);
        result.Stderr.Should().Contain("local path");
        result.Stderr.Should().NotContain("untrusted Test-Path probe");
    }

    [Fact]
    public void InstallerTestMode_ShouldIgnoreAmbientEnvironmentWithoutHarnessAuthority()
    {
        var command = string.Join(" ; ",
        [
            "$env:WPFDEVTOOLS_INSTALLER_TEST_MODE='1'",
            "$global:WpfDevToolsInstallerTestModeEnabled=$true",
            OnlineInstallerScriptTestHarness.BuildDefinitionOnlyPrelude(
                "-Action install -Architecture x64 -Client other -NonInteractive",
                enableInternalTestMode: false),
            ". '" + ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Installer.PackageIntegrity.ps1").Replace("'", "''") + "'",
            "[string](Test-InstallerTestModeEnabled)"
        ]);

        var result = ReleaseScriptTestHarness.RunPowerShellCommand(
            command,
            new Dictionary<string, string?>
            {
                ["WPFDEVTOOLS_INSTALLER_TEST_MODE"] = "0"
            });

        result.ExitCode.Should().Be(0, result.Stderr);
        result.Stdout.Trim().Should().Be("False");
    }

    [Fact]
    public void OnlineInstallerScript_InlineIexExecution_WhenTuiBootstrapFails_ShouldExplainFallbackAndContinueWithCli()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var appData = Path.Combine(tempRoot, "AppData", "Roaming");
            var localAppData = Path.Combine(tempRoot, "AppData", "Local");
            var userProfile = Path.Combine(tempRoot, "UserProfile");
            var installRootResponse = Path.Combine(appData, "WpfDevToolsMcp");
            Directory.CreateDirectory(appData);
            Directory.CreateDirectory(localAppData);
            Directory.CreateDirectory(userProfile);

            var repoScriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1");
            var command = string.Join(" ; ",
            [
                "$env:APPDATA='" + appData.Replace("'", "''") + "'",
                "$env:LOCALAPPDATA='" + localAppData.Replace("'", "''") + "'",
                "$env:USERPROFILE='" + userProfile.Replace("'", "''") + "'",
                "$env:WPFDEVTOOLS_INSTALLER_HELPER_BASE_URI='http://127.0.0.1:9'",
                "$env:WPFDEVTOOLS_INSTALLER_HELPER_TIMEOUT_SEC='1'",
                "$env:WPFDEVTOOLS_INSTALLER_HELPER_BOOTSTRAP_TIMEOUT_SEC='3'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_LATEST_VERSION='1.2.3'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_RESPONSES='uninstall||x64||other||" + installRootResponse.Replace("'", "''") + "'",
                "Set-Location '" + tempRoot.Replace("'", "''") + "'",
                "& ([scriptblock]::Create((Get-Content -LiteralPath '" + repoScriptPath.Replace("'", "''") + "' -Raw))) -Action uninstall -Architecture x64 -Client other"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("The installer runtime required for uninstall is unavailable.");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstallerScript_ShouldDeclareBootstrapPseudoWindowBeforeHelperDownloadCompletes()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().Contain("Write-TuiBootstrapScreen");
        content.Should().Contain("Enter-TuiBootstrapTerminalSession");
        content.Should().Contain("Exit-TuiBootstrapTerminalSession");
        content.Should().Contain("Preparing installer UI... (archive)");
        content.Should().Contain("Preparing installer UI... (fallback)");
        content.Should().NotContain("('+' + ('-' * $innerWidth) + '+')");
    }

    [Fact]
    public void OnlineInstallerScript_ShouldBootstrapHelperRuntimeFromReleaseArchives()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().Contain("Get-TuiHelperArchiveDownloadDetails");
        content.Should().Contain("Assert-TuiHelperArchiveIntegrity");
        content.Should().Contain("Copy-InstallerHelperBundleFromArchive");
    }

    [Fact]
    public void OnlineInstallerScript_InlineIexExecution_ShouldRenderFullscreenBootstrapLoadingScreen()
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
                "$env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_ANSI='1'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_CONSOLE_WIDTH='96'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_CONSOLE_HEIGHT='28'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_LATEST_VERSION='1.2.3'",
                "Set-Location '" + tempRoot.Replace("'", "''") + "'",
                "$scriptContent = (Get-Content -LiteralPath '" + repoScriptPath.Replace("'", "''") + "' -Raw).Replace('$script:WpfDevToolsInstallerTestModeEnabled = [bool]$script:WpfDevToolsInstallerTestModeEnabled -and [bool]$script:WpfDevToolsInstallerTestModeHarnessEnabled', '$script:WpfDevToolsInstallerTestModeEnabled = $true')",
                "& ([scriptblock]::Create($scriptContent)) -Action install -Architecture x64 -Client other"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Should().Contain("Preparing installer UI");
            result.Stdout.Should().Contain("[Status] Preparing installer UI...");
            result.Stdout.Should().NotContain("Status: Preparing installer UI...");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

}
