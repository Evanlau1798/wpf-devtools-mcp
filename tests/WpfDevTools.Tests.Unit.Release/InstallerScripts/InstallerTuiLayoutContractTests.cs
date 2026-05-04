using System.Text.RegularExpressions;
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
        rendererContent.Should().Contain("Build-TuiFooterLinesCore");
        rendererContent.Should().Contain("New-TuiFrameLinesCore");
        rendererContent.Should().NotContain("Build-TuiStatusPanelLinesCore");
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
    public void TuiScreenModel_ShouldStayWithinSingleFileLimitAfterHelperSplits()
    {
        var lineCount = File.ReadLines(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.ScreenModel.ps1")).Count();

        lineCount.Should().BeLessThanOrEqualTo(500,
            "screen model responsibilities should be split into helper modules before the file grows past the repository limit");
    }

    [Fact]
    public void InstallerHelperManifest_ShouldListTerminalAndLayoutHelpers()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/installer-helpers.manifest.json"));

        content.Should().Contain("Tui.Terminal.ps1");
        content.Should().Contain("Tui.Layout.ps1");
        content.Should().Contain("Tui.State.ps1");
    }

    [Fact]
    public void TuiPresenterFunctionNames_ShouldNotOverlapAcrossSplitHelperFiles()
    {
        var sectionsContent = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Sections.ps1"));
        var presentersContent = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Presenters.ps1"));
        var titleBarContent = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.TitleBar.ps1"));
        var statusBarContent = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.StatusBar.ps1"));

        var sectionsFunctions = GetFunctionNames(sectionsContent);
        var splitPresenterFunctions = GetFunctionNames(presentersContent)
            .Concat(GetFunctionNames(titleBarContent))
            .Concat(GetFunctionNames(statusBarContent))
            .ToHashSet(StringComparer.Ordinal);

        sectionsFunctions.Intersect(splitPresenterFunctions, StringComparer.Ordinal)
            .Should().BeEmpty("split TUI helpers should not redefine presenter functions from Tui.Sections.ps1");
    }

    private static IReadOnlyCollection<string> GetFunctionNames(string content)
        => Regex.Matches(content, @"^function\s+([A-Za-z0-9\.-]+)", RegexOptions.Multiline)
            .Select(static match => match.Groups[1].Value)
            .ToArray();
}
