using System.Text.RegularExpressions;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class OnlineInstallerContractTests
{
    [Fact]
    public void OnlineInstallerScript_ShouldExistAsPublicEntryPoint()
    {
        var scriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1");

        File.Exists(scriptPath).Should().BeTrue(
            "users and maintainers should have a stable one-command installer entrypoint under scripts/");
    }

    [Fact]
    public void OnlineInstallerScript_ShouldBeTuiFirstWhileKeepingAutomationFlags()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().Contain("'plan'");
        content.Should().Contain("Get-InstallerPlan");
        content.Should().Contain("Start-TuiInstaller");
        content.Should().Contain("[switch]$NonInteractive");
        content.Should().Contain("[switch]$OutputJson");
        content.Should().Contain("Render-TuiScreen");
        content.Should().Contain("Read-TuiKey");
        content.Should().Contain("Read-Host",
            "the installer still needs a plain CLI fallback when the full-screen TUI cannot be used");
    }

    [Fact]
    public void OnlineInstallerScript_ShouldSupportDistinctArchitectureAndWindowsClientSelections()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().Contain("'x64'");
        content.Should().Contain("'x86'");
        content.Should().Contain("'arm64'");
        content.Should().Contain("'claude-code'");
        content.Should().Contain("'codex'");
        content.Should().Contain("'vscode'");
        content.Should().Contain("'visual-studio'");
    }

    [Fact]
    public void OnlineInstallerScript_ShouldResolveOnlineOfflineModesAndPersistInstallerState()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().Contain("Resolve-InstallerMode");
        content.Should().Contain("Resolve-InstallerStatePath");
        content.Should().Contain("installer-state.json");
        content.Should().Contain("Save-InstallerState");
        content.Should().Contain("Get-AvailableInstallerUpdates");
        content.Should().Contain("Invoke-TuiUpdateAllOperation");
    }

    [Fact]
    public void OnlineInstallerScript_ShouldDeclareTwoStepConfirmationAndFullUninstallContracts()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().Contain("ConfirmScreen");
        content.Should().Contain("ConfirmationStep");
        content.Should().Contain("Full Uninstall");
        content.Should().Contain("full-uninstall");
    }

    [Fact]
    public void OnlineInstallerScript_ShouldDownloadVersionedReleaseArchiveNames()
    {
        var content =
            File.ReadAllText(ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1")) +
            Environment.NewLine +
            File.ReadAllText(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/online-installer.release-assets.ps1"));

        content.Should().Contain("release_{0}_win-{1}.zip");
        content.Should().Contain("releases/latest/download");
        content.Should().Contain("releases/download/");
        content.Should().Contain("api.github.com/repos/Evanlau1798/wpf-devtools-mcp/releases");
    }

    [Fact]
    public void InstallerRegistrationCommands_ShouldUseInstallerSubdomain()
    {
        var content = File.ReadAllText(ReleaseScriptTestHarness.GetRepoFilePath(
            "scripts/installer/Installer.Registration.Commands.ps1"));

        content.Should().Contain("https://installer.wpf-mcptools.evanlau1798.com");
        content.Should().NotContain("https://wpf-mcptools.evanlau1798.com'",
            "the root custom domain is reserved for DocFX Pages");
    }

    [Fact]
    public void OnlineInstallerScript_ShouldLoadPinnedRemoteReleaseAssetModuleWhenStandalone()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var standaloneRoot = Path.Combine(tempRoot, "standalone");
            Directory.CreateDirectory(standaloneRoot);
            var standaloneScript = Path.Combine(standaloneRoot, "online-installer.ps1");
            File.Copy(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                standaloneScript);
            var modulePath = ReleaseScriptTestHarness.GetRepoFilePath(
                "scripts/installer/online-installer.release-assets.ps1");
            var probePath = Path.Combine(tempRoot, "probe.ps1");
            var command = $$"""
            $ErrorActionPreference = 'Stop'
            function Invoke-WebRequest {
                param([string]$Uri, $Headers, [int]$TimeoutSec)
                [pscustomobject]@{ Content = [System.IO.File]::ReadAllText('{{EscapePowerShellPath(modulePath)}}') }
            }

            $scriptPath = '{{EscapePowerShellPath(standaloneScript)}}'
            $scriptContent = Get-Content -LiteralPath $scriptPath -Raw
            $marker = '{{TestHelpers.OnlineInstallerDefinitionBoundaryMarker}}'
            $markerIndex = $scriptContent.IndexOf($marker, [System.StringComparison]::Ordinal)
            if ($markerIndex -lt 0) { throw 'Main script boundary marker not found.' }
            $definitionsPath = Join-Path '{{EscapePowerShellPath(tempRoot)}}' 'online-installer-definitions.ps1'
            Set-Content -LiteralPath $definitionsPath -Value $scriptContent.Substring(0, $markerIndex) -Encoding UTF8
            . $definitionsPath
            if (-not $script:OnlineInstallerReleaseAssetModuleLoaded) {
                throw 'release asset module was not loaded'
            }

            Get-ReleaseAssetName -ResolvedVersion '1.2.3' -ResolvedArchitecture 'x64'
            """;
            File.WriteAllText(probePath, command);

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                probePath,
                [],
                new Dictionary<string, string?>
                {
                    ["WPFDEVTOOLS_INSTALLER_TEST_MODE"] = "0"
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Trim().Should().EndWith("release_1.2.3_win-x64.zip");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstallerScript_ShouldUseMasterHelperBaseForStandaloneLatestPrerelease()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var standaloneRoot = Path.Combine(tempRoot, "standalone");
            Directory.CreateDirectory(standaloneRoot);
            var standaloneScript = Path.Combine(standaloneRoot, "online-installer.ps1");
            File.Copy(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"),
                standaloneScript);
            var modulePath = ReleaseScriptTestHarness.GetRepoFilePath(
                "scripts/installer/online-installer.release-assets.ps1");
            var probePath = Path.Combine(tempRoot, "probe.ps1");
            var command = $$"""
            $ErrorActionPreference = 'Stop'
            function Invoke-WebRequest {
                param([string]$Uri, $Headers, [int]$TimeoutSec)
                [pscustomobject]@{ Content = [System.IO.File]::ReadAllText('{{EscapePowerShellPath(modulePath)}}') }
            }

            $scriptPath = '{{EscapePowerShellPath(standaloneScript)}}'
            $scriptContent = Get-Content -LiteralPath $scriptPath -Raw
            $marker = '{{TestHelpers.OnlineInstallerDefinitionBoundaryMarker}}'
            $markerIndex = $scriptContent.IndexOf($marker, [System.StringComparison]::Ordinal)
            if ($markerIndex -lt 0) { throw 'Main script boundary marker not found.' }
            $definitionsPath = Join-Path '{{EscapePowerShellPath(tempRoot)}}' 'online-installer-definitions.ps1'
            Set-Content -LiteralPath $definitionsPath -Value $scriptContent.Substring(0, $markerIndex) -Encoding UTF8
            . $definitionsPath
            Resolve-TuiHelperDownloadBaseUri
            """;
            File.WriteAllText(probePath, command);

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                probePath,
                ["-Version", "latest", "-Prerelease"],
                new Dictionary<string, string?>
                {
                    ["WPFDEVTOOLS_INSTALLER_TEST_MODE"] = "0"
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Trim().Should().Be(
                "https://raw.githubusercontent.com/Evanlau1798/wpf-devtools-mcp/master/scripts/installer");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstallerScript_ShouldForceTls12BeforeNetworkCalls()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        var tlsIndex = content.IndexOf("[Net.ServicePointManager]::SecurityProtocol", StringComparison.Ordinal);
        var firstWebRequestIndex = content.IndexOf("Invoke-WebRequest", StringComparison.Ordinal);
        var firstRestMethodIndex = content.IndexOf("Invoke-RestMethod", StringComparison.Ordinal);

        tlsIndex.Should().BeGreaterThan(0);
        content.Should().Contain("[Net.SecurityProtocolType]::Tls12");
        tlsIndex.Should().BeLessThan(firstWebRequestIndex);
        tlsIndex.Should().BeLessThan(firstRestMethodIndex);
    }

    [Fact]
    public void InstallerScripts_ShouldUseCompatibilityWebRequestWrapperForDownloads()
    {
        foreach (var relativePath in new[]
                 {
                     "scripts/online-installer.ps1",
                     "scripts/installer/online-installer.release-assets.ps1",
                     "scripts/installer/Installer.Release.ps1"
                 })
        {
            var content = File.ReadAllText(ReleaseScriptTestHarness.GetRepoFilePath(relativePath));

            content.Should().Contain("function Invoke-InstallerWebRequest",
                $"{relativePath} should define the Windows PowerShell 5.1 web-request compatibility wrapper");
            Regex.Matches(content, @"(?<!Installer)Invoke-WebRequest\s+-Uri")
                .Should().BeEmpty(
                    $"{relativePath} should not call Invoke-WebRequest -Uri directly because Windows PowerShell 5.1 can require UseBasicParsing for GitHub raw/release responses");
        }
    }

    [Fact]
    public void OnlineInstallerScript_ShouldCompareInstallerModeFunctionResultsExplicitly()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        Regex.Matches(content, @"Resolve-InstallerMode\s+-(?:eq|ne)\b")
            .Should().BeEmpty(
                "PowerShell treats unparenthesized function comparisons as function arguments, making online mode enter offline branches");
    }

    [Fact]
    public void OnlineInstallerScript_ShouldAvoidLegacyDecorativeCliBranding()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().NotContain("WPF DEVTOOLS MCP");
        content.Should().NotContain("<Binding Path=\"{Binding}\" />");
        content.Should().NotContain("<DependencyProperty/>");
        content.Should().NotContain("Open docs homepage");
        content.Should().NotContain("WindowChrome.WindowChrome");
    }

    [Fact]
    public void OnlineInstallerScript_ShouldOffloadBootstrapUiHelpersIntoInstallerModules()
    {
        var scriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1");
        var content = File.ReadAllText(scriptPath);
        var manifestContent = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/installer-helpers.manifest.json"));

        content.Should().Contain("Installer.BootstrapUi.ps1");
        manifestContent.Should().Contain("Installer.BootstrapUi.ps1");
        manifestContent.Should().Contain("Installer.Actions.ps1");
        manifestContent.Should().Contain("Installer.Uninstall.ps1");
    }

    [Fact]
    public void OnlineInstallerScript_ShouldNotHardRequireSharedModulesBeforeStandaloneNonInteractiveRemoval()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().NotContain("Assert-InstallerHelperRuntimeAvailable -ResolvedAction $ResolvedAction\r\n    foreach ($helperPath in @(Get-InstallerSharedModulePaths))",
            "standalone noninteractive uninstall/full-uninstall must have a recovery path that does not hard-fail before it can inspect state or existing registration artifacts");
    }

    [Fact]
    public void OnlineInstallerScript_ShouldFindInstallerHelpersInWindowsCreatedArchives()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().Contain("\"bin\\installer\\$LeafName\"",
            "PowerShell Compress-Archive on Windows stores release entries with backslash separators");
        content.Should().Contain("\"installer\\$LeafName\"",
            "offline package helper lookup should accept both archive layouts supported by packaged releases");
    }

    [Fact]
    public void OnlineInstallerSupportedClients_ShouldMatchAgentInstallDocumentation()
    {
        var script = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));
        var functionStart = script.IndexOf("function Get-SupportedClients", StringComparison.Ordinal);
        var functionEnd = script.IndexOf("function Resolve-ClientBaseId", StringComparison.Ordinal);
        functionStart.Should().BeGreaterThanOrEqualTo(0);
        functionEnd.Should().BeGreaterThan(functionStart);

        var functionBody = script[functionStart..functionEnd];
        var supportedClients = Regex.Matches(functionBody, @"Id\s*=\s*'([^']+)'")
            .Select(match => match.Groups[1].Value)
            .ToArray();

        supportedClients.Should().Equal(
            "claude-code",
            "codex",
            "grok",
            "cursor",
            "vscode",
            "visual-studio",
            "claude-desktop",
            "other");

        foreach (var file in new[]
                 {
                     "AGENT_INSTALL.md",
                     "docfx/guides/agent-assisted-install.md",
                     "docfx/zh-tw/guides/agent-assisted-install.md"
                 })
        {
            var content = File.ReadAllText(ReleaseScriptTestHarness.GetRepoFilePath(file));

            foreach (var clientId in supportedClients)
            {
                content.Should().Contain($"`{clientId}`",
                    $"{file} should stay synchronized with the installer supported client list");
            }
        }
    }

    private static string EscapePowerShellPath(string path)
        => path.Replace("'", "''", StringComparison.Ordinal);
}
