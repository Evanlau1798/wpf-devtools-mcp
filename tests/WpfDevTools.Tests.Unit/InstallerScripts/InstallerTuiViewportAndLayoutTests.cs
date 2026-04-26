using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

[Collection("InstallerScripts")]
public sealed class InstallerTuiViewportAndLayoutTests
{
    [Fact]
    public void TuiTerminal_AndFlow_ShouldDeclareViewportResizePollingContracts()
    {
        var terminalContent = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Terminal.ps1"));
        var flowContent = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Flow.ps1"));

        terminalContent.Should().Contain("WPFDEVTOOLS_INSTALLER_TEST_VIEWPORT_PATH");
        terminalContent.Should().Contain("Get-TuiViewportCacheKeyCore");
        flowContent.Should().Contain("Get-TuiViewportCacheKeyCore");
        flowContent.Should().Contain("Get-TuiInputPollTimeoutCore");
    }

    [Fact]
    public void TuiPresenters_StandardInstallViewport_ShouldExposeFourVisibleTargets()
    {
        var command = string.Join(" ; ",
        [
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Terminal.ps1")) + "'",
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Layout.ps1")) + "'",
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.ScreenModel.ps1")) + "'",
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Sections.ps1")) + "'",
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Window.ps1")) + "'",
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Presenters.ps1")) + "'",
            "$state = [ordered]@{ CurrentScreen = 'InstallScreen'; SelectedArchitecture = 'x64'; InstallRoot = 'C:\\Users\\tester\\AppData\\Roaming\\WpfDevTools\\Installer\\Nested\\LongLongLongLongLongLongLongFolderName'; ScrollOffset = 0; SelectionIndex = 0; StatusMessage = ''; UpdateBannerText = ''; HomeItems = @(); UninstallItems = @(); InstallItems = @([ordered]@{ Id = 'claude-code'; PrimaryText = 'Claude Code'; SecondaryText = ''; StatusBadge = ''; Description = 'desc' }, [ordered]@{ Id = 'codex'; PrimaryText = 'Codex/Codex CLI'; SecondaryText = ''; StatusBadge = ''; Description = 'desc' }, [ordered]@{ Id = 'cursor'; PrimaryText = 'Cursor'; SecondaryText = ''; StatusBadge = ''; Description = 'desc' }, [ordered]@{ Id = 'vscode'; PrimaryText = 'VS Code'; SecondaryText = ''; StatusBadge = ''; Description = 'desc' }, [ordered]@{ Id = 'visual-studio'; PrimaryText = 'Visual Studio'; SecondaryText = ''; StatusBadge = ''; Description = 'desc' }, [ordered]@{ Id = 'other'; PrimaryText = 'Other'; SecondaryText = 'Export JSON and CLI examples for manual MCP registration.'; StatusBadge = ''; Description = 'desc' }) }",
            "$viewport = Get-TuiWindowViewportCore -Viewport ([ordered]@{ Width = 96; Height = 28; UseAnsi = $false })",
            "$visible = Get-TuiVisibleWindowSizeCore -State $state -Viewport $viewport",
            "Write-Output $visible"
        ]);

        var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

        result.ExitCode.Should().Be(0, result.Stderr);
        int.Parse(result.Stdout.Trim()).Should().Be(4);
    }

    [Fact]
    public void TuiPresenters_TallInstallViewport_ShouldExposeMoreThanFourVisibleTargets()
    {
        var command = string.Join(" ; ",
        [
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Terminal.ps1")) + "'",
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Layout.ps1")) + "'",
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.ScreenModel.ps1")) + "'",
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Sections.ps1")) + "'",
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Window.ps1")) + "'",
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Presenters.ps1")) + "'",
            "$state = [ordered]@{ CurrentScreen = 'InstallScreen'; SelectedArchitecture = 'x64'; InstallRoot = 'C:\\Users\\tester\\AppData\\Roaming\\WpfDevTools\\Installer\\Nested\\LongLongLongLongLongLongLongFolderName'; ScrollOffset = 0; SelectionIndex = 0; StatusMessage = ''; UpdateBannerText = ''; HomeItems = @(); UninstallItems = @(); InstallItems = @([ordered]@{ Id = 'claude-code'; PrimaryText = 'Claude Code'; SecondaryText = ''; StatusBadge = ''; Description = 'desc' }, [ordered]@{ Id = 'codex'; PrimaryText = 'Codex/Codex CLI'; SecondaryText = ''; StatusBadge = ''; Description = 'desc' }, [ordered]@{ Id = 'cursor'; PrimaryText = 'Cursor'; SecondaryText = ''; StatusBadge = ''; Description = 'desc' }, [ordered]@{ Id = 'vscode'; PrimaryText = 'VS Code'; SecondaryText = ''; StatusBadge = ''; Description = 'desc' }, [ordered]@{ Id = 'visual-studio'; PrimaryText = 'Visual Studio'; SecondaryText = ''; StatusBadge = ''; Description = 'desc' }, [ordered]@{ Id = 'other'; PrimaryText = 'Other'; SecondaryText = 'Export JSON and CLI examples for manual MCP registration.'; StatusBadge = ''; Description = 'desc' }) }",
            "$viewport = Get-TuiWindowViewportCore -Viewport ([ordered]@{ Width = 96; Height = 40; UseAnsi = $false })",
            "$visible = Get-TuiVisibleWindowSizeCore -State $state -Viewport $viewport",
            "Write-Output $visible"
        ]);

        var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

        result.ExitCode.Should().Be(0, result.Stderr);
        int.Parse(result.Stdout.Trim()).Should().BeGreaterThan(4);
    }

    [Fact]
    public void TuiPresenters_LongInstallLocation_ShouldKeepVisibleTargetsByCompactingHeaderPath()
    {
        var command = string.Join(" ; ",
        [
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Terminal.ps1")) + "'",
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Layout.ps1")) + "'",
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.ScreenModel.ps1")) + "'",
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Sections.ps1")) + "'",
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Window.ps1")) + "'",
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Presenters.ps1")) + "'",
            "$viewport = Get-TuiWindowViewportCore -Viewport ([ordered]@{ Width = 96; Height = 28; UseAnsi = $false })",
            "$shortState = [ordered]@{ CurrentScreen = 'InstallScreen'; InstallRoot = 'C:\\Mcp'; SelectedArchitecture = 'x64'; ScrollOffset = 0; SelectionIndex = 0; StatusMessage = ''; UpdateBannerText = ''; HomeItems = @(); UninstallItems = @(); InstallItems = @([ordered]@{ Id = 'claude-code'; PrimaryText = 'Claude Code'; SecondaryText = ''; StatusBadge = ''; Description = 'desc' }, [ordered]@{ Id = 'codex'; PrimaryText = 'Codex/Codex CLI'; SecondaryText = ''; StatusBadge = ''; Description = 'desc' }, [ordered]@{ Id = 'cursor'; PrimaryText = 'Cursor'; SecondaryText = ''; StatusBadge = ''; Description = 'desc' }, [ordered]@{ Id = 'vscode'; PrimaryText = 'VS Code'; SecondaryText = ''; StatusBadge = ''; Description = 'desc' }, [ordered]@{ Id = 'visual-studio'; PrimaryText = 'Visual Studio'; SecondaryText = ''; StatusBadge = ''; Description = 'desc' }, [ordered]@{ Id = 'other'; PrimaryText = 'Other'; SecondaryText = 'Export JSON and CLI examples for manual MCP registration.'; StatusBadge = ''; Description = 'desc' }) }",
            "$longState = [ordered]@{ CurrentScreen = 'InstallScreen'; InstallRoot = 'C:\\Users\\tester\\AppData\\Roaming\\WpfDevTools\\Installer\\Nested\\LongLongLongLongLongLongLongFolderName'; SelectedArchitecture = 'x64'; ScrollOffset = 0; SelectionIndex = 0; StatusMessage = ''; UpdateBannerText = ''; HomeItems = @(); UninstallItems = @(); InstallItems = @([ordered]@{ Id = 'claude-code'; PrimaryText = 'Claude Code'; SecondaryText = ''; StatusBadge = ''; Description = 'desc' }, [ordered]@{ Id = 'codex'; PrimaryText = 'Codex/Codex CLI'; SecondaryText = ''; StatusBadge = ''; Description = 'desc' }, [ordered]@{ Id = 'cursor'; PrimaryText = 'Cursor'; SecondaryText = ''; StatusBadge = ''; Description = 'desc' }, [ordered]@{ Id = 'vscode'; PrimaryText = 'VS Code'; SecondaryText = ''; StatusBadge = ''; Description = 'desc' }, [ordered]@{ Id = 'visual-studio'; PrimaryText = 'Visual Studio'; SecondaryText = ''; StatusBadge = ''; Description = 'desc' }, [ordered]@{ Id = 'other'; PrimaryText = 'Other'; SecondaryText = 'Export JSON and CLI examples for manual MCP registration.'; StatusBadge = ''; Description = 'desc' }) }",
            "$shortVisible = Get-TuiVisibleWindowSizeCore -State $shortState -Viewport $viewport",
            "$longVisible = Get-TuiVisibleWindowSizeCore -State $longState -Viewport $viewport",
            "Write-Output ($shortVisible.ToString() + ',' + $longVisible.ToString())"
        ]);

        var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

        result.ExitCode.Should().Be(0, result.Stderr);
        var values = result.Stdout.Trim().Split(',');
        values.Should().HaveCount(2);
        int.Parse(values[1]).Should().Be(int.Parse(values[0]));
    }

    [Fact]
    public void TuiPresenters_StandardInstallViewport_ShouldKeepFourVisibleTargetsWhenOtherIsFocused()
    {
        var command = string.Join(" ; ",
        [
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Terminal.ps1")) + "'",
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Layout.ps1")) + "'",
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.ScreenModel.ps1")) + "'",
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Sections.ps1")) + "'",
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Window.ps1")) + "'",
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Presenters.ps1")) + "'",
            "$state = [ordered]@{ CurrentScreen = 'InstallScreen'; SelectedArchitecture = 'x64'; InstallRoot = 'C:\\Users\\tester\\AppData\\Roaming\\WpfDevToolsMcp'; ScrollOffset = 0; SelectionIndex = 5; StatusMessage = ''; UpdateBannerText = ''; HomeItems = @(); UninstallItems = @(); InstallItems = @([ordered]@{ Id = 'claude-code'; PrimaryText = 'Claude Code'; SecondaryText = ''; StatusBadge = ''; Description = 'Press Enter to install or update this target.' }, [ordered]@{ Id = 'codex'; PrimaryText = 'Codex/Codex CLI'; SecondaryText = ''; StatusBadge = ''; Description = 'Press Enter to install or update this target.' }, [ordered]@{ Id = 'cursor'; PrimaryText = 'Cursor'; SecondaryText = ''; StatusBadge = ''; Description = 'Press Enter to install or update this target.' }, [ordered]@{ Id = 'vscode'; PrimaryText = 'VS Code'; SecondaryText = ''; StatusBadge = ''; Description = 'Press Enter to install or update this target.' }, [ordered]@{ Id = 'visual-studio'; PrimaryText = 'Visual Studio'; SecondaryText = ''; StatusBadge = ''; Description = 'Press Enter to install or update this target.' }, [ordered]@{ Id = 'other'; PrimaryText = 'Other'; SecondaryText = 'Export JSON and CLI examples for manual MCP registration.'; StatusBadge = ''; Description = 'Review other.mcpServers.json, claude-code.txt, and codex.txt under client-registration. See AI Agent Clients for registration examples.' }) }",
            "$viewport = Get-TuiWindowViewportCore -Viewport ([ordered]@{ Width = 96; Height = 28; UseAnsi = $false })",
            "$visible = Get-TuiVisibleWindowSizeCore -State $state -Viewport $viewport",
            "Write-Output $visible"
        ]);

        var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

        result.ExitCode.Should().Be(0, result.Stderr);
        int.Parse(result.Stdout.Trim()).Should().Be(4);
    }

    [Fact]
    public void TuiScreenModel_InstallScreenDownArrow_ShouldKeepFocusOneCardAboveBottomUntilLastItem()
    {
        var command = string.Join(" ; ",
        [
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.ScreenModel.ps1")) + "'",
            "$state = [ordered]@{ CurrentScreen = 'InstallScreen'; SelectionIndex = 0; ScrollOffset = 0; VisibleWindowSize = 4; SelectedArchitecture = 'x64'; InstallRoot = 'C:\\\\Mcp'; PathEditor = $null; HomeItems = @(); UninstallItems = @(); InstallItems = @(" +
                "[ordered]@{ Id = 'claude-code'; PrimaryText = 'Claude Code'; SecondaryText = ''; StatusBadge = ''; Description = 'desc' }," +
                "[ordered]@{ Id = 'codex'; PrimaryText = 'Codex/Codex CLI'; SecondaryText = ''; StatusBadge = ''; Description = 'desc' }," +
                "[ordered]@{ Id = 'cursor'; PrimaryText = 'Cursor'; SecondaryText = ''; StatusBadge = ''; Description = 'desc' }," +
                "[ordered]@{ Id = 'vscode'; PrimaryText = 'VS Code'; SecondaryText = ''; StatusBadge = ''; Description = 'desc' }," +
                "[ordered]@{ Id = 'visual-studio'; PrimaryText = 'Visual Studio'; SecondaryText = ''; StatusBadge = ''; Description = 'desc' }," +
                "[ordered]@{ Id = 'other'; PrimaryText = 'Other'; SecondaryText = ''; StatusBadge = ''; Description = 'desc' }) }",
            "$snapshots = New-Object System.Collections.Generic.List[string]",
            "foreach ($ignored in 1..5) {",
            "  $state = Update-TuiSelectionCore -State $state -KeyInfo ([ordered]@{ Key = [ConsoleKey]::DownArrow; Character = '' })",
            "  $snapshots.Add(($state.SelectionIndex.ToString() + ':' + $state.ScrollOffset.ToString()))",
            "}",
            "Write-Output ($snapshots -join ',')"
        ]);

        var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

        result.ExitCode.Should().Be(0, result.Stderr);
        result.Stdout.Trim().Should().Be("1:0,2:0,3:1,4:2,5:2");
    }

    [Fact]
    public void TuiRenderer_InstallScreen_ShouldHonorViewportFileUpdatesAcrossRenders()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var viewportPath = Path.Combine(tempRoot, "viewport.txt");
            File.WriteAllText(viewportPath, "96x36");

            var command = string.Join(" ; ",
            [
                "$env:WPFDEVTOOLS_INSTALLER_TEST_VIEWPORT_PATH='" + EscapeForPowerShell(viewportPath) + "'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_CLEAR='1'",
                "$env:WPFDEVTOOLS_INSTALLER_TEST_DISABLE_ANSI='1'",
                ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Terminal.ps1")) + "'",
                ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Layout.ps1")) + "'",
                ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.ScreenModel.ps1")) + "'",
                ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Sections.ps1")) + "'",
                ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Window.ps1")) + "'",
                ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Presenters.ps1")) + "'",
                ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Renderer.ps1")) + "'",
                "$state = [ordered]@{ CurrentScreen = 'InstallScreen'; SelectedArchitecture = 'x64'; InstallRoot = 'C:\\Mcp'; SelectionIndex = 0; ScrollOffset = 0; VisibleWindowSize = 4; StatusMessage = ''; UpdateBannerText = ''; LatestVersionRefreshHandle = $null; registrations = [ordered]@{}; HomeItems = @(); UninstallItems = @(); InstallItems = @([ordered]@{ Id = 'claude-code'; PrimaryText = 'Claude Code'; SecondaryText = ''; StatusBadge = ''; Description = 'desc' }, [ordered]@{ Id = 'codex'; PrimaryText = 'Codex/Codex CLI'; SecondaryText = ''; StatusBadge = ''; Description = 'desc' }, [ordered]@{ Id = 'cursor'; PrimaryText = 'Cursor'; SecondaryText = ''; StatusBadge = ''; Description = 'desc' }, [ordered]@{ Id = 'other'; PrimaryText = 'Other'; SecondaryText = 'Export JSON and CLI examples for manual MCP registration.'; StatusBadge = ''; Description = 'desc' }) }",
                "$first = Render-TuiScreenCore -State $state -AsString",
                "Set-Content -Path '" + EscapeForPowerShell(viewportPath) + "' -Value '72x20'",
                "$second = Render-TuiScreenCore -State $state -AsString",
                "$firstMax = (($first -split \"`r?`n\") | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Measure-Object -Property Length -Maximum).Maximum",
                "$secondMax = (($second -split \"`r?`n\") | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Measure-Object -Property Length -Maximum).Maximum",
                "Write-Output ($firstMax.ToString() + ',' + $secondMax.ToString())"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            var widths = result.Stdout.Trim().Split(',');
            widths.Should().HaveCount(2, result.Stdout);
            int.Parse(widths[0]).Should().BeGreaterThan(72, result.Stdout);
            int.Parse(widths[1]).Should().BeLessOrEqualTo(72, result.Stdout);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static string EscapeForPowerShell(string value)
        => value.Replace("'", "''");
}
