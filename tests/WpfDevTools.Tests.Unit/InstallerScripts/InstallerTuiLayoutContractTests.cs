using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class InstallerTuiLayoutContractTests
{
    [Fact]
    public void OnlineInstallerScript_ShouldIncludeDedicatedTerminalAndLayoutHelpers()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        content.Should().Contain("scripts/installer/Tui.Terminal.ps1");
        content.Should().Contain("scripts/installer/Tui.Layout.ps1");
    }

    [Fact]
    public void TuiRenderer_ShouldDeclareFrameBasedWpfInspiredLayoutPrimitives()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Renderer.ps1"));

        content.Should().Contain("Get-TuiViewportCore");
        content.Should().Contain("Get-TuiContentColumnWidthCore");
        content.Should().Contain("Build-TuiTitleBarLinesCore");
        content.Should().Contain("Build-TuiHomeHeroLinesCore");
        content.Should().Contain("Build-TuiPageHeaderLinesCore");
        content.Should().Contain("Build-TuiStatusPanelLinesCore");
        content.Should().Contain("Build-TuiFooterLinesCore");
        content.Should().Contain("Format-TuiBadgeCore");
        content.Should().Contain("New-TuiFrameLinesCore");
    }

    [Fact]
    public void TuiScreenModel_ShouldSeparatePrimaryTextFromInstalledBadges()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.ScreenModel.ps1"));

        content.Should().Contain("PrimaryText");
        content.Should().Contain("SecondaryText");
        content.Should().Contain("StatusBadge");
        content.Should().Contain("IsPrimaryAction");
    }

    [Fact]
    public void InstallerHelperManifest_ShouldListTerminalAndLayoutHelpers()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/installer-helpers.manifest.json"));

        content.Should().Contain("Tui.Terminal.ps1");
        content.Should().Contain("Tui.Layout.ps1");
    }
}
