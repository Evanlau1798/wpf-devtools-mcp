function Invoke-InstallerAction {
    param(
        [Parameter(Mandatory)] [ValidateSet('install', 'uninstall', 'full-uninstall')] [string]$ResolvedAction,
        [Parameter(Mandatory)] [string]$ResolvedArchitecture,
        [Parameter(Mandatory)] [string]$ResolvedClient,
        [Parameter(Mandatory)] [AllowEmptyString()] [AllowNull()] [string]$ResolvedInstallRoot,
        [Parameter(Mandatory)] [string]$RequestedVersion,
        [switch]$UseLatestRelease
    )

    Assert-InstallerHelperRuntimeAvailable -ResolvedAction $ResolvedAction
    $includeInstalledHelperRoots = $ResolvedAction -eq 'uninstall' -or $ResolvedAction -eq 'full-uninstall'
    $sharedModulePaths = if ($ResolvedAction -eq 'install') {
        @(Get-InstallerSharedModulePaths)
    }
    else {
        @(Get-InstallerSharedModulePaths -AllowMissing -IncludeInstalledRoots:$includeInstalledHelperRoots)
    }
    $shouldUseStandaloneFallback = ($ResolvedAction -ne 'install' -and $sharedModulePaths.Count -eq 0)

    foreach ($helperPath in $sharedModulePaths) {
        . $helperPath
    }

    if ($shouldUseStandaloneFallback) {
        $standaloneResult = Invoke-StandaloneInstallerActionCore `
                -ResolvedAction $ResolvedAction `
                -ResolvedArchitecture $ResolvedArchitecture `
                -ResolvedClient $ResolvedClient `
                -ResolvedInstallRoot $ResolvedInstallRoot `
                -RequestedVersion $RequestedVersion `
                -UseLatestRelease:$UseLatestRelease
        return (Add-InstallerReleaseChannelToResult -Result $standaloneResult)
    }

    $actionResult = Invoke-InstallerActionCore `
            -ResolvedAction $ResolvedAction `
            -ResolvedArchitecture $ResolvedArchitecture `
            -ResolvedClient $ResolvedClient `
            -ResolvedInstallRoot $ResolvedInstallRoot `
            -RequestedVersion $RequestedVersion `
            -UseLatestRelease:$UseLatestRelease
    return (Add-InstallerReleaseChannelToResult -Result $actionResult)
}
function Start-TuiInstaller {
    param(
        [Parameter(Mandatory)] [string]$DefaultAction,
        [Parameter(Mandatory)] [string]$DefaultArchitecture,
        [Parameter(Mandatory)] [string]$DefaultClient,
        [Parameter(Mandatory)] [string]$DefaultInstallRoot,
        [Parameter(Mandatory)] $InstallerState,
        [string]$VersionHint,
        [string]$LatestVersion
    )

    $global:WpfDevToolsInstallerBootstrapSession = $script:TuiBootstrapTerminalSession
    try {
        return (Invoke-WithTuiHelpers -ScriptBlock { Start-TuiInstallerCore `
                -DefaultAction $DefaultAction `
                -DefaultArchitecture $DefaultArchitecture `
                -DefaultClient $DefaultClient `
                -DefaultInstallRoot $DefaultInstallRoot `
                -InstallerState $InstallerState `
                -VersionHint $VersionHint `
                -LatestVersion $LatestVersion })
    }
    finally {
        if ($null -ne $global:WpfDevToolsInstallerBootstrapSession) {
            $script:TuiBootstrapTerminalSession = $global:WpfDevToolsInstallerBootstrapSession
            Close-TuiBootstrapScreen
        }

        $script:TuiBootstrapTerminalSession = $null
        Remove-Variable -Name WpfDevToolsInstallerBootstrapSession -Scope Global -ErrorAction SilentlyContinue
    }
}
function Resolve-Selection {
    if ($Action -eq 'plan') {
        return [ordered]@{
            Cancelled = $false
            Selection = (Get-InstallerPlan)
            VersionHint = $null
            HandledInWindow = $false
            IsPlan = $true
        }
    }

    $defaultArchitecture = if ([string]::IsNullOrWhiteSpace($Architecture)) { Get-SystemDefaultArchitecture } else { $Architecture }
    $defaultClient = if ([string]::IsNullOrWhiteSpace($Client)) { Get-DefaultClient } else { $Client }

    $script:InteractiveReleaseVersionWasPrompted = $false
    if (-not $NonInteractive -and -not $OutputJson -and $Action -eq 'install') {
        $script:Version = Read-ValidatedVersion -Prompt 'Release version' -DefaultValue $Version
        $script:InteractiveReleaseVersionWasPrompted = $true
    }

    if (Test-TuiSupport) {
        foreach ($helperPath in @(Get-InstallerSharedModulePaths)) {
            . $helperPath
        }

        $installerState = Get-InstallerState
        $defaultInstallRoot = Resolve-PreferredInstallRoot
        $mode = Resolve-InstallerMode
        $versionHint = Get-OfflineVersionHint -Mode $mode
        $latestVersion = Get-LatestInstallerVersion -UseCacheOnly

        $tuiResult = Start-TuiInstaller `
            -DefaultAction $Action `
            -DefaultArchitecture $defaultArchitecture `
            -DefaultClient $defaultClient `
            -DefaultInstallRoot $defaultInstallRoot `
            -InstallerState $installerState `
            -VersionHint $versionHint `
            -LatestVersion $latestVersion

        return [ordered]@{
            Cancelled = [bool]$tuiResult.Cancelled
            Selection = $tuiResult.Selection
            VersionHint = $versionHint
            HandledInWindow = [bool]$tuiResult.HandledInWindow
        }
    }

    Close-TuiBootstrapScreen
    $mode = Resolve-InstallerMode
    $versionHint = $null
    $includeInstalledHelperRoots = $Action -eq 'uninstall' -or $Action -eq 'full-uninstall'
    try {
        foreach ($helperPath in @(Get-InstallerSharedModulePaths -IncludeInstalledRoots:$includeInstalledHelperRoots)) {
            . $helperPath
        }

        $versionHint = Get-OfflineVersionHint -Mode $mode
    }
    catch {
    }

    return [ordered]@{
        Cancelled = $false
        Selection = (Get-CliSelection)
        VersionHint = $versionHint
        HandledInWindow = $false
    }
}
