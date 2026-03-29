using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class InstallerUninstallBehaviorTests
{
    [Fact]
    public void OnlineInstallerScript_ShouldDeclareSharedDiscoveryAndUninstallHelpers()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().Contain("scripts/installer/Installer.Discovery.ps1");
        content.Should().Contain("scripts/installer/Installer.Uninstall.ps1");
        content.Should().Contain("scripts/installer/Tui.Confirm.ps1");
    }

    [Fact]
    public void OnlineInstallerScript_ShouldDeclareHelperCacheKeyAndVerifiedRemovalContracts()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().Contain("installer-helpers.manifest.json");
        content.Should().Contain("Get-InstallerHelperRuntimeCacheKey");
        content.Should().Contain("helper-cache-key.txt");
        content.Should().Contain("Remove-PathIfExists -Path $runtimeRoot");
        content.Should().Contain("InstallerOwned");
        content.Should().Contain("RegistrationMode");
        content.Should().Contain("InstalledExecutable");
    }

    [Fact]
    public void OnlineInstallerScript_ShouldDeclareDualUninstallModes()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().Contain("unregister");
        content.Should().Contain("full-uninstall");
        content.Should().Contain("Full Uninstall");
    }

    [Fact]
    public void InstallerDiscoveryMerge_ShouldPreferExternalEvidenceForMutableInstallationFields()
    {
        var discoveryScriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Installer.Discovery.ps1");
        var command = string.Join(" ; ",
        [
            ". '" + discoveryScriptPath.Replace("'", "''") + "'",
            "$primary = [ordered]@{ ClientId='vscode'; RegistrationMode='state'; RegistrationTarget='state.json'; InstalledExecutable='C:\\old-root\\wpf-devtools-x64.exe'; InstallRoot='C:\\old-root'; Architecture='x64'; InstallerOwned=$true; EvidenceSource='state'; ResolvedVersion='1.0.0'; LastVerifiedUtc='2026-03-30T00:00:00Z' }",
            "$secondary = [ordered]@{ ClientId='vscode'; RegistrationMode='json-file'; RegistrationTarget='C:\\config\\mcp.json'; InstalledExecutable='C:\\new-root\\wpf-devtools-arm64.exe'; InstallRoot='C:\\new-root'; Architecture='arm64'; InstallerOwned=$true; EvidenceSource='json-file'; ResolvedVersion='1.2.3'; LastVerifiedUtc=$null }",
            "$merged = Merge-DetectedInstallerRegistration -Primary $primary -Secondary $secondary",
            "$merged | ConvertTo-Json -Compress"
        ]);

        var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

        result.ExitCode.Should().Be(0, result.Stderr);
        using var json = JsonDocument.Parse(result.Stdout);
        var root = json.RootElement;
        root.GetProperty("RegistrationMode").GetString().Should().Be("json-file");
        root.GetProperty("RegistrationTarget").GetString().Should().Be(@"C:\config\mcp.json");
        root.GetProperty("InstalledExecutable").GetString().Should().Be(@"C:\new-root\wpf-devtools-arm64.exe");
        root.GetProperty("InstallRoot").GetString().Should().Be(@"C:\new-root");
        root.GetProperty("Architecture").GetString().Should().Be("arm64");
        root.GetProperty("EvidenceSource").GetString().Should().Be("json-file");
        root.GetProperty("ResolvedVersion").GetString().Should().Be("1.2.3");
        root.GetProperty("LastVerifiedUtc").GetString().Should().Be("2026-03-30T00:00:00Z");
    }
}
