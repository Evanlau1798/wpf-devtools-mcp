function Invoke-TuiInstallOperationCore {
    param([Parameter(Mandatory)] $State)

    $selectedClient = [string]$State.PendingClient
    $selectedArchitecture = [string]$State.SelectedArchitecture
    $selectedRoot = [string]$State.InstallRoot

    $previousScreen = [string]$State.CurrentScreen
    $State.CurrentScreen = 'ProgressScreen'
    $State.StatusMessage = "Installing $(Resolve-ClientLabel -ClientId $selectedClient)..."
    Render-TuiScreenCore -State $State | Out-Null

    try {
        $result = Invoke-InstallerAction -ResolvedAction 'install' -ResolvedArchitecture $selectedArchitecture -ResolvedClient $selectedClient -ResolvedInstallRoot $selectedRoot -RequestedVersion $Version
        $State = Sync-TuiStateFromInstallerState -State $State -InstallerState (Get-InstallerState)
        $State.StatusMessage = "Installed $(Resolve-ClientLabel -ClientId $selectedClient) v$($result.resolvedVersion). $($result.verificationMessage)"
    }
    catch {
        $State.StatusMessage = "Installation failed: $($_.Exception.Message)"
    }
    finally {
        $State.PendingAction = $null
        $State.PendingClient = $null
        $State.CurrentScreen = 'InstallScreen'
        if ($previousScreen -eq 'HomeScreen') {
            $State.CurrentScreen = 'HomeScreen'
        }
    }

    return $State
}

function Invoke-TuiUninstallOperationCore {
    param([Parameter(Mandatory)] $State)

    $selectedClient = [string]$State.PendingClient
    $registration = if ($State.InstallerState.registrations.Contains($selectedClient)) { $State.InstallerState.registrations[$selectedClient] } else { $null }
    $selectedArchitecture = if ($null -ne $registration) { [string]$registration.architecture } else { [string]$State.SelectedArchitecture }
    $selectedRoot = if ($null -ne $registration) { [string]$registration.installRoot } else { [string]$State.InstallRoot }

    $State.CurrentScreen = 'ProgressScreen'
    $State.StatusMessage = "Uninstalling $(Resolve-ClientLabel -ClientId $selectedClient)..."
    Render-TuiScreenCore -State $State | Out-Null

    try {
        $result = Invoke-InstallerAction -ResolvedAction 'uninstall' -ResolvedArchitecture $selectedArchitecture -ResolvedClient $selectedClient -ResolvedInstallRoot $selectedRoot -RequestedVersion $Version
        $State = Sync-TuiStateFromInstallerState -State $State -InstallerState (Get-InstallerState)
        $State.StatusMessage = "Removed $(Resolve-ClientLabel -ClientId $selectedClient). $($result.verificationMessage)"
    }
    catch {
        $State.StatusMessage = "Uninstall failed: $($_.Exception.Message)"
    }
    finally {
        $State = Reset-TuiConfirmationCore -State $State
        $State.PendingAction = $null
        $State.PendingClient = $null
        $State.CurrentScreen = 'UninstallScreen'
    }

    return $State
}

function Invoke-TuiFullUninstallOperationCore {
    param([Parameter(Mandatory)] $State)

    $State.CurrentScreen = 'ProgressScreen'
    $State.StatusMessage = 'Removing all detected registrations and installer-owned server files...'
    Render-TuiScreenCore -State $State | Out-Null

    try {
        $result = Invoke-InstallerAction -ResolvedAction 'full-uninstall' -ResolvedArchitecture ([string]$State.SelectedArchitecture) -ResolvedClient 'all' -ResolvedInstallRoot ([string]$State.InstallRoot) -RequestedVersion $Version
        $State = Sync-TuiStateFromInstallerState -State $State -InstallerState (Get-InstallerState)
        $State.StatusMessage = "Full Uninstall completed. $($result.verificationMessage)"
    }
    catch {
        $State.StatusMessage = "Full Uninstall failed: $($_.Exception.Message)"
    }
    finally {
        $State = Reset-TuiConfirmationCore -State $State
        $State.PendingAction = $null
        $State.PendingClient = $null
        $State.CurrentScreen = 'UninstallScreen'
    }

    return $State
}

function Invoke-TuiUpdateAllOperationCore {
    param([Parameter(Mandatory)] $State)

    $State.CurrentScreen = 'ProgressScreen'
    $State.StatusMessage = 'Checking latest release...'
    Render-TuiScreenCore -State $State | Out-Null
    $State.LatestVersion = Get-LatestInstallerVersion
    $State = Sync-TuiStateFromInstallerState -State $State -InstallerState $State.InstallerState
    if ([string]::IsNullOrWhiteSpace([string]$State.LatestVersion)) {
        $State.StatusMessage = 'Update All failed: latest release metadata is unavailable.'
        $State.PendingAction = $null
        $State.CurrentScreen = 'HomeScreen'
        return $State
    }

    $updates = @(Get-AvailableInstallerUpdates -State $State.InstallerState -LatestVersion ([string]$State.LatestVersion) -RegistrationMap $State.DetectedRegistrationMap)
    if ($updates.Count -eq 0) {
        $State.StatusMessage = 'All installed targets are already on the latest release.'
        $State.PendingAction = $null
        $State.CurrentScreen = 'HomeScreen'
        return $State
    }

    $State.StatusMessage = "Updating $($updates.Count) target(s)..."
    Render-TuiScreenCore -State $State | Out-Null

    try {
        foreach ($update in $updates) {
            $State.StatusMessage = "Updating $(Resolve-ClientLabel -ClientId ([string]$update.Client)) to v$($State.LatestVersion)..."
            Render-TuiScreenCore -State $State | Out-Null
            $null = Invoke-InstallerAction -ResolvedAction 'install' -ResolvedArchitecture ([string]$update.Architecture) -ResolvedClient ([string]$update.Client) -ResolvedInstallRoot ([string]$update.InstallRoot) -RequestedVersion 'latest' -UseLatestRelease
        }

        $State = Sync-TuiStateFromInstallerState -State $State -InstallerState (Get-InstallerState)
        $State.StatusMessage = "Updated $($updates.Count) target(s) to v$($State.LatestVersion)."
    }
    catch {
        $State.StatusMessage = "Update All failed: $($_.Exception.Message)"
    }
    finally {
        $State.PendingAction = $null
        $State.CurrentScreen = 'HomeScreen'
        $State.SelectionIndex = 0
        $State.ScrollOffset = 0
    }

    return $State
}

function Initialize-TuiStartupStateCore {
    param([Parameter(Mandatory)] $State)

    if ($State.StartupInitialized) {
        return $State
    }

    $State.StartupInitialized = $true
    if ([string]::IsNullOrWhiteSpace([string]$State.LatestVersion)) {
        $State.LatestVersion = Get-LatestInstallerVersion -UseCacheOnly
    }

    $State.UpdateBannerText = Get-TuiUpdateBannerText -State $State.InstallerState -LatestVersion ([string]$State.LatestVersion) -RegistrationMap $State.DetectedRegistrationMap
    $State.StatusMessage = ''
    return $State
}

function Start-TuiInstallerCore {
    param(
        [Parameter(Mandatory)] [string]$DefaultAction,
        [Parameter(Mandatory)] [string]$DefaultArchitecture,
        [Parameter(Mandatory)] [string]$DefaultClient,
        [Parameter(Mandatory)] [string]$DefaultInstallRoot,
        [Parameter(Mandatory)] $InstallerState,
        [string]$VersionHint,
        [string]$LatestVersion
    )

    $state = New-TuiState -DefaultAction $DefaultAction -DefaultArchitecture $DefaultArchitecture -DefaultClient $DefaultClient -DefaultInstallRoot $DefaultInstallRoot -InstallerState $InstallerState -VersionHint $VersionHint -LatestVersion $LatestVersion
    Render-TuiScreenCore -State $state | Out-Null
    $state = Initialize-TuiStartupStateCore -State $state

    while (-not $state.ShouldExit) {
        Render-TuiScreenCore -State $state | Out-Null
        $keyInfo = Read-TuiKeyCore
        $state = Update-TuiSelectionCore -State $state -KeyInfo $keyInfo

        switch ([string]$state.PendingAction) {
            'edit-root' { $state = Invoke-TuiInstallRootPromptCore -State $state; $state.PendingAction = $null }
            'install' { $state = Invoke-TuiInstallOperationCore -State $state }
            'uninstall' { $state = Invoke-TuiUninstallOperationCore -State $state }
            'full-uninstall' { $state = Invoke-TuiFullUninstallOperationCore -State $state }
            'update-all' { $state = Invoke-TuiUpdateAllOperationCore -State $state }
        }
    }

    return [ordered]@{
        Launched = $true
        Cancelled = [bool]$state.Cancelled
        Selection = $null
        HandledInWindow = $true
    }
}
