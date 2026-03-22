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

        content.Should().Contain("WPFDEVTOOLS_INSTALLER_HELPER_CACHE_KEY");
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
}
