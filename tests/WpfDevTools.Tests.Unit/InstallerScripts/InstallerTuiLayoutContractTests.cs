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
    public void TuiRenderer_ShouldComposeFrameFromDedicatedSectionHelpers()
    {
        var rendererContent = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Renderer.ps1"));
        var sectionsContent = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Sections.ps1"));

        rendererContent.Should().Contain("Get-TuiViewportCore");
        rendererContent.Should().Contain("Get-TuiContentColumnWidthCore");
        rendererContent.Should().Contain("Build-TuiTitleBarLinesCore");
        rendererContent.Should().Contain("Build-TuiHomeHeroLinesCore");
        rendererContent.Should().Contain("Build-TuiPageHeaderLinesCore");
        rendererContent.Should().Contain("Build-TuiStatusPanelLinesCore");
        rendererContent.Should().Contain("Build-TuiFooterLinesCore");
        rendererContent.Should().Contain("New-TuiFrameLinesCore");
        sectionsContent.Should().Contain("Format-TuiBadgeCore");
        sectionsContent.Should().Contain("Get-TuiCaptionControlsTextCore");
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
