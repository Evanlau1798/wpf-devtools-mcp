using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class InstallerTuiPathBrowserTests
{
    [Fact]
    public void TuiPathEditor_ShouldOpenScrollableDirectoryPickerWithParentEntry()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var browseRoot = Path.Combine(tempRoot, "BrowseRoot");
            Directory.CreateDirectory(browseRoot);
            foreach (var name in new[] { "Alpha", "Bravo", "Charlie", "Delta", "Echo", "Foxtrot", "Golf" })
            {
                Directory.CreateDirectory(Path.Combine(browseRoot, name));
            }

            var command = string.Join(" ; ",
            [
                ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Input.ps1")) + "'",
                ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.PathEditor.ps1")) + "'",
                "$state = [ordered]@{ InstallRoot = '" + EscapeForPowerShell(browseRoot) + "'; CurrentScreen = 'HomeScreen'; HomeItems = @(); SelectionIndex = 0; ScrollOffset = 0; StatusMessage = '' }",
                "$state = Enter-TuiPathEditorCore -State $state",
                "1..6 | ForEach-Object { $state = Handle-TuiPathEditorKeyCore -State $state -KeyInfo ([ordered]@{ Key = [ConsoleKey]::DownArrow; Character = '' }) }",
                "Write-Output ($state.CurrentScreen + '|' + [string]$state.PathEditor.BrowserEntries[0].Label + '|' + [int]$state.PathEditor.BrowserScrollOffset)"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            var tokens = result.Stdout.Trim().Split('|');
            tokens.Should().HaveCount(3);
            tokens[0].Should().Be("DirectoryPickerScreen");
            tokens[1].Should().Be("..");
            int.Parse(tokens[2]).Should().BeGreaterThan(0);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void TuiPathEditor_ShouldConfirmCurrentDirectoryAndAppendCustomFolderName()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var browseRoot = Path.Combine(tempRoot, "BrowseRoot");
            var alphaPath = Path.Combine(browseRoot, "Alpha");
            var betaPath = Path.Combine(alphaPath, "Beta");
            Directory.CreateDirectory(betaPath);

            var command = string.Join(" ; ",
            [
                ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Input.ps1")) + "'",
                ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.PathEditor.ps1")) + "'",
                "$state = [ordered]@{ InstallRoot = '" + EscapeForPowerShell(browseRoot) + "'; CurrentScreen = 'HomeScreen'; HomeItems = @([ordered]@{ Id = 'install' }, [ordered]@{ Id = 'uninstall' }, [ordered]@{ Id = 'update-all' }, [ordered]@{ Id = 'edit-root' }); SelectionIndex = 0; ScrollOffset = 0; StatusMessage = '' }",
                "$state = Enter-TuiPathEditorCore -State $state",
                "$state = Handle-TuiPathEditorKeyCore -State $state -KeyInfo ([ordered]@{ Key = [ConsoleKey]::DownArrow; Character = '' })",
                "$state = Handle-TuiPathEditorKeyCore -State $state -KeyInfo ([ordered]@{ Key = [ConsoleKey]::Enter; Character = '' })",
                "$state = Handle-TuiPathEditorKeyCore -State $state -KeyInfo ([ordered]@{ Key = [ConsoleKey]::DownArrow; Character = '' })",
                "$state = Handle-TuiPathEditorKeyCore -State $state -KeyInfo ([ordered]@{ Key = [ConsoleKey]::Enter; Character = '' })",
                "$state = Handle-TuiPathEditorKeyCore -State $state -KeyInfo ([ordered]@{ Key = [ConsoleKey]::Tab; Character = '' })",
                "foreach ($character in '-team'.ToCharArray()) { $state = Handle-TuiPathEditorKeyCore -State $state -KeyInfo ([ordered]@{ Key = [ConsoleKey]::NoName; Character = [string]$character }) }",
                "$state = Handle-TuiPathEditorKeyCore -State $state -KeyInfo ([ordered]@{ Key = [ConsoleKey]::Enter; Character = '' })",
                "Write-Output $state.InstallRoot"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Trim().Should().Be(Path.Combine(betaPath, "wpf-devtools-mcp-team"));
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void TuiDirectoryPicker_WhenTypingCharacters_ShouldNotMutateHiddenPathBuffer()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var browseRoot = Path.Combine(tempRoot, "BrowseRoot");
            Directory.CreateDirectory(Path.Combine(browseRoot, "Alpha"));

            var command = string.Join(" ; ",
            [
                ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Input.ps1")) + "'",
                ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.PathEditor.ps1")) + "'",
                "$state = [ordered]@{ InstallRoot = '" + EscapeForPowerShell(browseRoot) + "'; CurrentScreen = 'HomeScreen'; HomeItems = @(); SelectionIndex = 0; ScrollOffset = 0; StatusMessage = '' }",
                "$state = Enter-TuiPathEditorCore -State $state",
                "$bufferBefore = [string]$state.PathEditor.Buffer",
                "$state = Handle-TuiPathEditorKeyCore -State $state -KeyInfo ([ordered]@{ Key = [ConsoleKey]::NoName; Character = 'z' })",
                "Write-Output ($bufferBefore + '|' + [string]$state.PathEditor.Buffer + '|' + [string]$state.CurrentScreen)"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            var values = result.Stdout.Trim().Split('|');
            values.Should().HaveCount(3);
            values[2].Should().Be("DirectoryPickerScreen");
            values[1].Should().Be(values[0]);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void TuiDirectoryPickerVisibleWindowSize_ShouldShrinkWhenCurrentDirectoryWrapsAcrossMultipleLines()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var shortRoot = Path.Combine(tempRoot, "BrowseRoot");
            var longRoot = Path.Combine(
                tempRoot,
                "VeryLongDirectoryNameForViewportTesting",
                "NestedProjectFolder",
                "AnotherNestedFolder",
                "DeepInstallRoot");
            Directory.CreateDirectory(shortRoot);
            Directory.CreateDirectory(longRoot);

            var command = string.Join(" ; ",
            [
                ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Layout.ps1")) + "'",
                ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.PathEditor.ps1")) + "'",
                "$viewport = [ordered]@{ Width = 72; Height = 24; UseAnsi = $false }",
                "$shortState = [ordered]@{ UpdateBannerText = ''; PathEditor = [ordered]@{ BrowserCurrentDirectory = '" + EscapeForPowerShell(shortRoot) + "'; BrowserEntries = @(1..12); BrowserScrollOffset = 0; BrowserVisibleWindowSize = 0 } }",
                "$longState = [ordered]@{ UpdateBannerText = ''; PathEditor = [ordered]@{ BrowserCurrentDirectory = '" + EscapeForPowerShell(longRoot) + "'; BrowserEntries = @(1..12); BrowserScrollOffset = 0; BrowserVisibleWindowSize = 0 } }",
                "$shortSize = Get-TuiDirectoryPickerVisibleWindowSizeCore -State $shortState -Viewport $viewport",
                "$longSize = Get-TuiDirectoryPickerVisibleWindowSizeCore -State $longState -Viewport $viewport",
                "Write-Output ($shortSize.ToString() + '|' + $longSize.ToString())"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            var sizes = result.Stdout.Trim().Split('|');
            sizes.Should().HaveCount(2);
            int.Parse(sizes[0]).Should().BeGreaterThan(int.Parse(sizes[1]));
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void TuiDirectoryPickerView_ShouldAvoidArrayIndexOfLookupsForVisibleEntries()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.PathEditor.Views.ps1"));

        content.Should().NotContain("[Array]::IndexOf(@($State.PathEditor.BrowserEntries), $entry)");
    }

    [Fact]
    public void TuiDirectoryPicker_DeepPathViewport_ShouldStillRenderStatusBarAndHelpFooter()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var deepRoot = Path.Combine(
                tempRoot,
                "VeryLongDirectoryNameForViewportTesting",
                "NestedProjectFolder",
                "AnotherNestedFolder",
                "DeepInstallRoot",
                "EvenMoreSegments",
                "AndMoreSegments");
            Directory.CreateDirectory(deepRoot);
            foreach (var name in Enumerable.Range(1, 12).Select(static index => "Dir" + index))
            {
                Directory.CreateDirectory(Path.Combine(deepRoot, name));
            }

            var command = string.Join(" ; ",
            [
                ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Terminal.ps1")) + "'",
                ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Layout.ps1")) + "'",
                ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.ScreenModel.ps1")) + "'",
                ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Sections.ps1")) + "'",
                ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Window.ps1")) + "'",
                ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Presenters.ps1")) + "'",
                ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.PathEditor.ps1")) + "'",
                ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.PathEditor.Views.ps1")) + "'",
                ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Renderer.ps1")) + "'",
                "$state = [ordered]@{ CurrentScreen = 'DirectoryPickerScreen'; SelectionIndex = 0; ScrollOffset = 0; SelectedArchitecture = 'x64'; InstallRoot = 'C:\\Users\\tester\\AppData\\Roaming\\WpfDevToolsMcp'; StatusMessage = 'Browsing install directories.'; UpdateBannerText = ''; HomeItems = @(); InstallItems = @(); UninstallItems = @(); ConfirmationMode = $null; ConfirmationStep = 0; PathEditor = [ordered]@{ BrowserCurrentDirectory = '" + EscapeForPowerShell(deepRoot) + "'; BrowserEntries = @(); BrowserSelectionIndex = 0; BrowserScrollOffset = 0; BrowserVisibleWindowSize = 0; StatusMessage = 'Browsing path'; SelectedParentDirectory = ''; FolderNameBuffer = 'wpf-devtools-mcp'; Buffer = ''; OriginalValue = '' } }",
                "$state.PathEditor.BrowserEntries = @(Get-TuiDirectoryPickerEntriesCore -CurrentDirectory '" + EscapeForPowerShell(deepRoot) + "')",
                "$viewport = Get-TuiWindowViewportCore -Viewport ([ordered]@{ Width = 72; Height = 24; UseAnsi = $false })",
                "$state = Update-TuiVisibleWindowSizeCore -State $state -Viewport $viewport",
                "$state = Update-TuiDirectoryPickerScrollCore -State $state",
                "$rendered = Render-TuiScreenCore -State $state -AsString",
                "Write-Output ('HasStatus=' + ($rendered -match '\\[Status\\]'))",
                "Write-Output ('HasHelp=' + ($rendered -match 'Up/Down move'))",
                "Write-Output ('Visible=' + [string]$state.PathEditor.BrowserVisibleWindowSize)"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Should().Contain("HasStatus=True");
            result.Stdout.Should().Contain("HasHelp=True");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void TuiDirectoryPicker_DeepPathViewport_ShouldNotGenerateMoreBodyLinesThanViewportCanDisplay()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var deepRoot = Path.Combine(
                tempRoot,
                "VeryLongDirectoryNameForViewportTesting",
                "NestedProjectFolder",
                "AnotherNestedFolder",
                "DeepInstallRoot",
                "EvenMoreSegments",
                "AndMoreSegments");
            Directory.CreateDirectory(deepRoot);
            foreach (var name in Enumerable.Range(1, 12).Select(static index => "Dir" + index))
            {
                Directory.CreateDirectory(Path.Combine(deepRoot, name));
            }

            var command = string.Join(" ; ",
            [
                ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Terminal.ps1")) + "'",
                ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Layout.ps1")) + "'",
                ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.ScreenModel.ps1")) + "'",
                ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Sections.ps1")) + "'",
                ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Window.ps1")) + "'",
                ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Presenters.ps1")) + "'",
                ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.PathEditor.ps1")) + "'",
                ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.PathEditor.Views.ps1")) + "'",
                "$state = [ordered]@{ CurrentScreen = 'DirectoryPickerScreen'; SelectionIndex = 0; ScrollOffset = 0; SelectedArchitecture = 'x64'; InstallRoot = 'C:\\Users\\tester\\AppData\\Roaming\\WpfDevToolsMcp'; StatusMessage = 'Browsing install directories.'; UpdateBannerText = ''; HomeItems = @(); InstallItems = @(); UninstallItems = @(); ConfirmationMode = $null; ConfirmationStep = 0; PathEditor = [ordered]@{ BrowserCurrentDirectory = '" + EscapeForPowerShell(deepRoot) + "'; BrowserEntries = @(); BrowserSelectionIndex = 0; BrowserScrollOffset = 0; BrowserVisibleWindowSize = 0; StatusMessage = 'Browsing path'; SelectedParentDirectory = ''; FolderNameBuffer = 'wpf-devtools-mcp'; Buffer = ''; OriginalValue = '' } }",
                "$state.PathEditor.BrowserEntries = @(Get-TuiDirectoryPickerEntriesCore -CurrentDirectory '" + EscapeForPowerShell(deepRoot) + "')",
                "$viewport = Get-TuiWindowViewportCore -Viewport ([ordered]@{ Width = 72; Height = 24; UseAnsi = $false })",
                "$accent = Get-TuiAccent",
                "$windowSize = Get-TuiDirectoryPickerVisibleWindowSizeCore -State $state -Viewport $viewport",
                "$state.PathEditor.BrowserVisibleWindowSize = $windowSize",
                "$bodyLines = @(Build-TuiDirectoryPickerLinesCore -State $state -Viewport $viewport -Accent $accent).Count",
                "$availableLines = [int]$viewport.Height - (@(Build-TuiTitleBarLinesCore -State $state -Viewport $viewport -Accent $accent).Count + 1) - @(Build-TuiPageHeaderLinesCore -State $state -Viewport $viewport -Accent $accent).Count - @(Build-TuiFooterLinesCore -State $state -Viewport $viewport -Accent $accent).Count",
                "Write-Output ($bodyLines.ToString() + '|' + $availableLines.ToString() + '|' + $windowSize.ToString())"
            ]);

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            var values = result.Stdout.Trim().Split('|');
            values.Should().HaveCount(3);
            int.Parse(values[0]).Should().BeLessOrEqualTo(int.Parse(values[1]));
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static string EscapeForPowerShell(string value)
        => value.Replace("'", "''");
}
