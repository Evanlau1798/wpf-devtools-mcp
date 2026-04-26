using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

[Collection("InstallerScripts")]
public sealed class InstallerTuiStatusBarTests
{
    [Fact]
    public void TuiTitleBar_HomeScreen_ShouldKeepSubtitleAndInstallationManagerOutOfCaptionRow()
    {
        var command = string.Join(" ; ",
        [
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Terminal.ps1")) + "'",
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Layout.ps1")) + "'",
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Sections.ps1")) + "'",
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Window.ps1")) + "'",
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Presenters.ps1")) + "'",
            "$state = [ordered]@{ CurrentScreen = 'HomeScreen' }",
            "$lines = @(Build-TuiTitleBarLinesCore -State $state -Viewport ([ordered]@{ Width = 96; Height = 28; UseAnsi = $false }) -Accent (Get-TuiAccent))",
            "Write-Output ($lines -join [Environment]::NewLine)"
        ]);

        var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

        result.ExitCode.Should().Be(0, result.Stderr);
        result.Stdout.Should().Contain("WPF DevTools MCP");
        result.Stdout.Should().NotContain("Model Context Protocol Server");
        result.Stdout.Should().NotContain("Installation Manager");
    }

    [Fact]
    public void TuiRenderer_HomeScreen_ShouldRenderSubtitleAndInstallationManagerInsideContentArea()
    {
        var command = string.Join(" ; ",
        [
            "$env:WPFDEVTOOLS_INSTALLER_TEST_CONSOLE_WIDTH='96'",
            "$env:WPFDEVTOOLS_INSTALLER_TEST_CONSOLE_HEIGHT='28'",
            "$env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_CLEAR='1'",
            "$env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_ANSI='1'",
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Terminal.ps1")) + "'",
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Layout.ps1")) + "'",
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.ScreenModel.ps1")) + "'",
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Sections.ps1")) + "'",
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Window.ps1")) + "'",
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Presenters.ps1")) + "'",
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Renderer.ps1")) + "'",
            "$state = [ordered]@{ CurrentScreen = 'HomeScreen'; SelectionIndex = 0; ScrollOffset = 0; StatusMessage = ''; UpdateBannerText = ''; VersionHint = ''; HomeItems = @([ordered]@{ Id = 'install'; PrimaryText = 'Install'; SecondaryText = 'desc'; StatusBadge = ''; Description = 'desc' }, [ordered]@{ Id = 'update-all'; PrimaryText = 'Update All'; SecondaryText = 'desc'; StatusBadge = ''; Description = 'desc' }) }",
            "$rendered = Render-TuiScreenCore -State $state -AsString",
            "Write-Output $rendered"
        ]);

        var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

        result.ExitCode.Should().Be(0, result.Stderr);
        result.Stdout.Should().Contain("WPF DevTools MCP");
        result.Stdout.Should().Contain("Model Context Protocol Server");
        result.Stdout.Should().Contain("Installation Manager");
    }

    [Fact]
    public void TuiHomeHero_HomeScreen_ShouldCenterTitleSubtitleAndInstallationManagerLines()
    {
        var command = string.Join(" ; ",
        [
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Terminal.ps1")) + "'",
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Layout.ps1")) + "'",
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Sections.ps1")) + "'",
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Window.ps1")) + "'",
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Presenters.ps1")) + "'",
            "$state = [ordered]@{ CurrentScreen = 'HomeScreen'; SelectionIndex = 0; ScrollOffset = 0; VersionHint = ''; HomeItems = @([ordered]@{ Id = 'install'; PrimaryText = 'Install'; SecondaryText = 'desc'; StatusBadge = ''; Description = 'desc' }) }",
            "$lines = @(Build-TuiHomeHeroLinesCore -State $state -Viewport ([ordered]@{ Width = 94; Height = 24; UseAnsi = $false }) -Accent (Get-TuiAccent))",
            "$header = @($lines | Where-Object { $_ -match 'WPF DevTools MCP|Model Context Protocol Server|Installation Manager' })",
            "Write-Output ($header -join \"`n\")"
        ]);

        var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

        result.ExitCode.Should().Be(0, result.Stderr);
        var headerLines = result.Stdout
            .Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries)
            .ToArray();
        headerLines.Should().HaveCount(3);
        headerLines[0].Should().Contain("WPF DevTools MCP");
        headerLines[1].Should().Contain("Model Context Protocol Server");
        headerLines[2].Should().Contain("Installation Manager");
        headerLines.All(static line => line.StartsWith("          ")).Should().BeTrue(result.Stdout);
    }

    [Fact]
    public void TuiRenderer_InstallScreen_ShouldRenderBottomStatusBarInsteadOfCurrentStatusPanel()
    {
        var command = string.Join(" ; ",
        [
            "$env:WPFDEVTOOLS_INSTALLER_TEST_CONSOLE_WIDTH='96'",
            "$env:WPFDEVTOOLS_INSTALLER_TEST_CONSOLE_HEIGHT='28'",
            "$env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_CLEAR='1'",
            "$env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_ANSI='1'",
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Terminal.ps1")) + "'",
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Layout.ps1")) + "'",
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.ScreenModel.ps1")) + "'",
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Sections.ps1")) + "'",
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Window.ps1")) + "'",
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Presenters.ps1")) + "'",
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Renderer.ps1")) + "'",
            "$state = [ordered]@{ CurrentScreen = 'InstallScreen'; SelectedArchitecture = 'x64'; InstallRoot = 'C:\\Users\\tester\\AppData\\Roaming\\WpfDevToolsMcp'; SelectionIndex = 0; ScrollOffset = 0; VisibleWindowSize = 4; StatusMessage = 'Checking latest release...'; UpdateBannerText = ''; LatestVersionRefreshHandle = $null; HomeItems = @(); UninstallItems = @(); InstallItems = @([ordered]@{ Id = 'claude-code'; PrimaryText = 'Claude Code'; SecondaryText = ''; StatusBadge = ''; Description = 'desc' }, [ordered]@{ Id = 'codex'; PrimaryText = 'Codex/Codex CLI'; SecondaryText = ''; StatusBadge = ''; Description = 'desc' }, [ordered]@{ Id = 'cursor'; PrimaryText = 'Cursor'; SecondaryText = ''; StatusBadge = ''; Description = 'desc' }, [ordered]@{ Id = 'other'; PrimaryText = 'Other'; SecondaryText = 'Export JSON and CLI examples for manual MCP registration.'; StatusBadge = ''; Description = 'desc' }) }",
            "$rendered = Render-TuiScreenCore -State $state -AsString",
            "Write-Output $rendered"
        ]);

        var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

        result.ExitCode.Should().Be(0, result.Stderr);
        result.Stdout.Should().Contain("Checking latest release...");
        result.Stdout.Should().NotContain("Current status");
    }

    private static string EscapeForPowerShell(string value)
        => value.Replace("'", "''");
}
