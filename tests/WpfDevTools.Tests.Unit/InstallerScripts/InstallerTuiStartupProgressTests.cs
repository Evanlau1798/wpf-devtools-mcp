using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class InstallerTuiStartupProgressTests
{
    [Fact]
    public void OnlineInstallerScript_ResolveSelection_ShouldCheckTuiSupportBeforeLoadingInstallerState()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        var startIndex = content.IndexOf("function Resolve-Selection", StringComparison.Ordinal);
        startIndex.Should().BeGreaterThanOrEqualTo(0);

        var body = content[startIndex..];
        var tuiSupportIndex = body.IndexOf("if (Test-TuiSupport)", StringComparison.Ordinal);
        var installerStateIndex = body.IndexOf("$installerState = Get-InstallerState", StringComparison.Ordinal);

        tuiSupportIndex.Should().BeGreaterThanOrEqualTo(0);
        installerStateIndex.Should().BeGreaterThanOrEqualTo(0);
        tuiSupportIndex.Should().BeLessThan(installerStateIndex);
    }

    [Fact]
    public void OnlineInstallerScript_TestTuiSupport_ShouldEmitBootstrapProgressBeforeHelperResolution()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"));

        var startIndex = content.IndexOf("function Test-TuiSupport", StringComparison.Ordinal);
        startIndex.Should().BeGreaterThanOrEqualTo(0);

        var body = content[startIndex..];
        var bootstrapIndex = body.IndexOf("Write-TuiBootstrapScreen 'Preparing installer UI...'", StringComparison.Ordinal);
        var ensureIndex = body.IndexOf("$null = Ensure-TuiHelpersAvailable", StringComparison.Ordinal);

        bootstrapIndex.Should().BeGreaterThanOrEqualTo(0);
        ensureIndex.Should().BeGreaterThanOrEqualTo(0);
        bootstrapIndex.Should().BeLessThan(ensureIndex);
    }

    [Fact]
    public void TuiRenderer_StartupProgress_ShouldRenderCenteredPhaseTitleBeforeShowingHomeActions()
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
            "$state = [ordered]@{ CurrentScreen = 'HomeScreen'; SelectionIndex = 0; ScrollOffset = 0; StatusMessage = 'Checking cached release information...'; UpdateBannerText = ''; VersionHint = ''; StartupProgressActive = $true; StartupProgressTitle = 'Loading installer data'; HomeItems = @([ordered]@{ Id = 'install'; PrimaryText = 'Install'; SecondaryText = 'desc'; StatusBadge = ''; Description = 'desc' }, [ordered]@{ Id = 'update-all'; PrimaryText = 'Update All'; SecondaryText = 'desc'; StatusBadge = ''; Description = 'desc' }) }",
            "$rendered = Render-TuiScreenCore -State $state -AsString",
            "Write-Output $rendered"
        ]);

        var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

        result.ExitCode.Should().Be(0, result.Stderr);
        result.Stdout.Should().Contain("Loading installer data");
        result.Stdout.Should().Contain("Checking cached release information...");
        result.Stdout.Should().NotContain("> Install");
        result.Stdout.Should().NotContain("Update All");
    }

    [Fact]
    public void TuiFlow_StartupProgress_ShouldStayActiveUntilLatestRefreshSettlesOrTimesOut()
    {
        var command = string.Join(" ; ",
        [
            ". '" + EscapeForPowerShell(ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer/Tui.Flow.ps1")) + "'",
            "function Stop-LatestInstallerVersionRefresh { param($RefreshHandle) $script:refreshStopped = $true }",
            "function Get-TuiUpdateBannerText { param($State, $LatestVersion, $RegistrationMap) return '' }",
            "function Get-TuiHomeItemsCore { param($InstallRoot, $InstallerState, $LatestVersion, $RegistrationMap, $LatestVersionRefreshPending) return @([ordered]@{ Id = 'install'; PrimaryText = 'Install'; SecondaryText = ''; StatusBadge = ''; Description = 'Install target' }) }",
            "$state = [ordered]@{ StartupInitialized = $true; StartupProgressActive = $true; StartupProgressTitle = 'Loading installer data'; LatestVersionRefreshPending = $true; LatestVersionRefreshHandle = [ordered]@{ Mode = 'process' }; StartupReadyDeadlineUtc = [DateTime]::UtcNow.AddSeconds(-1).ToString('o'); StatusMessage = 'Checking latest release metadata...'; InstallRoot = 'C:\\Temp\\WpfDevTools'; InstallerState = [ordered]@{ registrations = @{} }; DetectedRegistrationMap = @{}; HomeItems = @(); LatestVersion = '' }",
            "$state = Update-TuiStartupProgressCore -State $state",
            "Write-Output ([string]$state.StartupProgressActive)",
            "Write-Output ([string]$state.LatestVersionRefreshPending)",
            "Write-Output ([string]$state.StatusMessage)",
            "Write-Output ([string]$script:refreshStopped)"
        ]);

        var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

        result.ExitCode.Should().Be(0, result.Stderr);
        result.Stdout.Should().Contain("False");
        result.Stdout.Should().Contain("Latest release metadata is unavailable. Continuing with cached or offline data.");
        result.Stdout.Should().Contain("True");
    }

    private static string EscapeForPowerShell(string value)
        => value.Replace("'", "''");
}
