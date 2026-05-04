using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class InstallerTuiWideCharacterLayoutTests
{
    [Fact]
    public void TuiLayout_DisplayWidth_ShouldTreatCjkGlyphsAsDoubleWidth()
    {
        var command = string.Join(" ; ",
        [
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Layout.ps1")) + "'",
            "Write-Output (Get-TuiDisplayWidthCore -Text '遠端伺服器傳回一個錯誤: (404) 找不到。')"
        ]);

        var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

        result.ExitCode.Should().Be(0, result.Stderr);
        int.Parse(result.Stdout.Trim()).Should().Be(38);
    }

    [Fact]
    public void TuiLayout_WrappingWideGlyphText_ShouldKeepEachRenderedLineWithinViewportWidth()
    {
        var command = string.Join(" ; ",
        [
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Layout.ps1")) + "'",
            "$wrapped = @(ConvertTo-TuiWrappedLinesCore -Text '找不到更新頁面' -Width 10)",
            "$widths = @($wrapped | ForEach-Object { Get-TuiDisplayWidthCore -Text $_ })",
            "Write-Output ($wrapped.Count.ToString() + '|' + ($widths -join ','))"
        ]);

        var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

        result.ExitCode.Should().Be(0, result.Stderr);
        var segments = result.Stdout.Trim().Split('|');
        segments.Should().HaveCount(2);
        int.Parse(segments[0]).Should().BeGreaterThan(1);
        segments[1]
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(int.Parse)
            .Should()
            .OnlyContain(static width => width <= 10);
    }

    [Fact]
    public void TuiStatusBar_WideGlyphFailureMessage_ShouldStillFitWithinSingleInstallStatusLine()
    {
        var command = string.Join(" ; ",
        [
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Layout.ps1")) + "'",
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Sections.ps1")) + "'",
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.StatusBar.ps1")) + "'",
            "$state = [ordered]@{ CurrentScreen = 'InstallScreen'; StatusMessage = 'Installation failed: 遠端伺服器傳回一個錯誤: (404) 找不到。'; SelectionIndex = 0 }",
            "$lines = @(Build-TuiStatusBarLinesCore -State $state -Viewport ([ordered]@{ Width = 38; Height = 20; UseAnsi = $false }) -Accent (Get-TuiAccent))",
            "$widths = @($lines | ForEach-Object { Get-TuiDisplayWidthCore -Text $_ })",
            "Write-Output ($lines.Count.ToString() + '|' + ($widths -join ','))"
        ]);

        var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

        result.ExitCode.Should().Be(0, result.Stderr);
        var segments = result.Stdout.Trim().Split('|');
        segments.Should().HaveCount(2);
        int.Parse(segments[0]).Should().Be(1);
        segments[1]
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(int.Parse)
            .Should()
            .OnlyContain(static width => width <= 38);
    }

    private static string EscapeForPowerShell(string value)
        => value.Replace("'", "''");
}
