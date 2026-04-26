using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

[Collection("InstallerScripts")]
public sealed class InstallerTerminalRenderingTests
{
    [Fact]
    public void TuiTerminal_ShouldDeclareAlternateScreenAndCursorVisibilityContracts()
    {
        var terminalContent = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Terminal.ps1"));
        var flowContent = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Flow.ps1"));

        terminalContent.Should().Contain("?1049h");
        terminalContent.Should().Contain("?1049l");
        terminalContent.Should().Contain("?25l");
        terminalContent.Should().Contain("?25h");
        flowContent.Should().Contain("Enter-TuiTerminalSessionCore");
        flowContent.Should().Contain("Exit-TuiTerminalSessionCore");
    }

    [Fact]
    public void TuiTerminal_ShouldManageConsoleBufferAndCursorDuringWindowedRendering()
    {
        var terminalContent = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Terminal.ps1"));

        terminalContent.Should().Contain("Sync-TuiViewportConsoleBufferCore");
        terminalContent.Should().Contain("BufferSize");
        terminalContent.Should().Contain("CursorVisible");
        terminalContent.Should().Contain("SetCursorPosition");
    }

    [Fact]
    public void TuiTerminal_ShrinkingViewport_ShouldTrimNormalizedFrameToCurrentViewport()
    {
        var command = string.Join(" ; ",
        [
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Layout.ps1")) + "'",
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Terminal.ps1")) + "'",
            "$script:TuiLastRenderedFrameHeight = 0",
            "$script:TuiLastRenderedFrameWidth = 0",
            "$first = @(Get-TuiNormalizedFrameLinesCore -Lines @('frame') -Viewport ([ordered]@{ Width = 96; Height = 20; UseAnsi = $false }))",
            "$second = @(Get-TuiNormalizedFrameLinesCore -Lines @('frame') -Viewport ([ordered]@{ Width = 72; Height = 20; UseAnsi = $false }))",
            "Write-Output ($first[0].Length.ToString() + ',' + $second[0].Length.ToString())"
        ]);

        var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

        result.ExitCode.Should().Be(0, result.Stderr);
        var lengths = result.Stdout.Trim().Split(',');
        lengths.Should().HaveCount(2);
        int.Parse(lengths[0]).Should().Be(96);
        int.Parse(lengths[1]).Should().Be(72);
    }

    private static string EscapeForPowerShell(string value)
        => value.Replace("'", "''");
}
