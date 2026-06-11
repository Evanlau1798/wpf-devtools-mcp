function Get-TuiHomeItemsCore {
    param(
        [Parameter(Mandatory)] [string]$InstallRoot,
        [Parameter(Mandatory)] $InstallerState,
        [string]$LatestVersion,
        $RegistrationMap,
        [bool]$LatestVersionRefreshPending = $false
    )

    if ($RegistrationMap.Count -eq 0) {
        $updateSummary = 'All detected targets are up to date.'
    }
    elseif ($LatestVersionRefreshPending) {
        $updateSummary = 'Checking latest release...'
    }
    else {
        $updateSummary = 'Unable to check the latest release right now.'
    }

    if (-not [string]::IsNullOrWhiteSpace([string]$LatestVersion)) {
        $updates = @(Get-AvailableInstallerUpdates -State $InstallerState -LatestVersion $LatestVersion -RegistrationMap $RegistrationMap)
        if ($updates.Count -gt 0) {
            $updateSummary = "$($updates.Count) target(s) can move to v$LatestVersion."
        }
        else {
            $updateSummary = 'All detected targets are up to date.'
        }
    }

    return @(
        [ordered]@{ Id = 'install'; Label = 'Install'; PrimaryText = 'Install'; SecondaryText = 'Select the target AI tool and install the MCP server.'; StatusBadge = ''; IsPrimaryAction = $true; Description = 'Install the selected release into a client.' }
        [ordered]@{ Id = 'uninstall'; Label = 'Uninstall'; PrimaryText = 'Uninstall'; SecondaryText = 'Remove a registered target or run a full uninstall.'; StatusBadge = ''; IsPrimaryAction = $true; Description = 'Remove an installed registration.' }
        [ordered]@{ Id = 'update-all'; Label = 'Update All'; PrimaryText = 'Update All'; SecondaryText = $updateSummary; StatusBadge = ''; IsPrimaryAction = $false; Description = 'Update every installed registration to the latest release.' }
        [ordered]@{ Id = 'edit-root'; Label = 'Install location'; PrimaryText = 'Install location'; SecondaryText = $InstallRoot; StatusBadge = ''; IsPrimaryAction = $false; Description = 'Change the shared MCP server install root.' }
    )
}

function New-TuiState {
    param(
        [Parameter(Mandatory)] [string]$DefaultAction,
        [Parameter(Mandatory)] [string]$DefaultArchitecture,
        [Parameter(Mandatory)] [string]$DefaultClient,
        [Parameter(Mandatory)] [string]$DefaultInstallRoot,
        [Parameter(Mandatory)] $InstallerState,
        [string]$VersionHint,
        [string]$LatestVersion
    )

    $state = [ordered]@{
        CurrentScreen = 'HomeScreen'
        SelectionIndex = 0
        ScrollOffset = 0
        SelectedAction = $DefaultAction
        SelectedArchitecture = $DefaultArchitecture
        SelectedClient = $DefaultClient
        InstallRoot = $DefaultInstallRoot
        VersionHint = $VersionHint
        LatestVersion = $LatestVersion
        StatusMessage = ''
        ShouldExit = $false
        Cancelled = $false
        PendingAction = $null
        PendingClient = $null
        ConfirmationMode = $null
        ConfirmationStep = 0
        LatestVersionRefreshPending = $false
        InstallerState = $InstallerState
        VisibleWindowSize = 4
        HomeItems = @()
        PathEditor = $null
    }

    $detectedRegistrationMap = Get-DetectedInstallerRegistrationMap -State $state.InstallerState
    $state.DetectedRegistrationMap = $detectedRegistrationMap
    $state.InstallItems = @(Get-TuiClientItems -State $state.InstallerState -Mode 'install' -RegistrationMap $detectedRegistrationMap)
    $state.UninstallItems = @(Get-TuiClientItems -State $state.InstallerState -Mode 'uninstall' -RegistrationMap $detectedRegistrationMap)
    $state.UpdateBannerText = Get-TuiUpdateBannerText -State $state.InstallerState -LatestVersion $LatestVersion -RegistrationMap $detectedRegistrationMap
    $state.HomeItems = @(Get-TuiHomeItemsCore -InstallRoot ([string]$state.InstallRoot) -InstallerState $state.InstallerState -LatestVersion $LatestVersion -RegistrationMap $detectedRegistrationMap -LatestVersionRefreshPending ([bool]$state.LatestVersionRefreshPending))
    return $state
}

function Sync-TuiStateFromInstallerState {
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)] $InstallerState
    )

    $State.InstallerState = $InstallerState
    $detectedRegistrationMap = Get-DetectedInstallerRegistrationMap -State $InstallerState
    $State.DetectedRegistrationMap = $detectedRegistrationMap
    $State.InstallItems = @(Get-TuiClientItems -State $InstallerState -Mode 'install' -RegistrationMap $detectedRegistrationMap)
    $State.UninstallItems = @(Get-TuiClientItems -State $InstallerState -Mode 'uninstall' -RegistrationMap $detectedRegistrationMap)
    $State.UpdateBannerText = Get-TuiUpdateBannerText -State $InstallerState -LatestVersion ([string]$State.LatestVersion) -RegistrationMap $detectedRegistrationMap
    $State.HomeItems = @(Get-TuiHomeItemsCore -InstallRoot ([string]$State.InstallRoot) -InstallerState $InstallerState -LatestVersion ([string]$State.LatestVersion) -RegistrationMap $detectedRegistrationMap -LatestVersionRefreshPending ([bool]$State.LatestVersionRefreshPending))
    return $State
}
