function Test-TuiPathChildExists {
    param(
        [string]$BasePath,
        [Parameter(Mandatory)] [string]$ChildPath
    )

    if ([string]::IsNullOrWhiteSpace($BasePath)) {
        return $false
    }

    return (Test-Path (Join-Path $BasePath $ChildPath))
}

function Get-TuiInstalledVersion {
    param(
        [Parameter(Mandatory)] $RegistrationMap,
        [Parameter(Mandatory)] [string]$ClientId
    )

    if ($RegistrationMap.Contains($ClientId)) {
        return [string]$RegistrationMap[$ClientId].ResolvedVersion
    }

    return $null
}

function Test-TuiClientAvailable {
    param(
        [Parameter(Mandatory)] [string]$ClientId,
        [Parameter(Mandatory)] $State
    )

    if ($State.registrations.Contains($ClientId) -or $ClientId -eq 'other') {
        return $true
    }

    switch ($ClientId) {
        'claude-code' { return ($null -ne (Get-Command 'claude' -ErrorAction SilentlyContinue)) }
        'codex' { return ($null -ne (Get-Command 'codex' -ErrorAction SilentlyContinue)) }
        'vscode' {
            return ($null -ne (Get-Command 'code' -ErrorAction SilentlyContinue)) -or
                (Test-TuiPathChildExists -BasePath $env:APPDATA -ChildPath 'Code')
        }
        'visual-studio' {
            $programFilesX86 = [Environment]::GetEnvironmentVariable('ProgramFiles(x86)', 'Process')
            return ($null -ne (Get-Command 'devenv' -ErrorAction SilentlyContinue)) -or
                (Test-TuiPathChildExists -BasePath $programFilesX86 -ChildPath 'Microsoft Visual Studio') -or
                (Test-TuiPathChildExists -BasePath $env:ProgramFiles -ChildPath 'Microsoft Visual Studio')
        }
        'claude-desktop' {
            return (Test-TuiPathChildExists -BasePath $env:APPDATA -ChildPath 'Claude') -or
                (Test-TuiPathChildExists -BasePath $env:LOCALAPPDATA -ChildPath 'Programs\Claude')
        }
        default { return $true }
    }
}

function Get-TuiClientItems {
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)] [ValidateSet('install', 'uninstall')] [string]$Mode,
        $RegistrationMap
    )

    $items = @()
    $registrationMap = if ($null -ne $RegistrationMap) { $RegistrationMap } else { Get-DetectedInstallerRegistrationMap -State $State }
    foreach ($client in Get-SupportedClients) {
        $clientId = [string]$client.Id
        $installed = $registrationMap.Contains($clientId)
        $available = Test-TuiClientAvailable -ClientId $clientId -State $State
        $version = Get-TuiInstalledVersion -RegistrationMap $registrationMap -ClientId $clientId

        if ($Mode -eq 'install' -and -not ($available -or $installed)) {
            continue
        }

        if ($Mode -eq 'uninstall' -and -not $installed) {
            continue
        }

        $label = Resolve-ClientLabel -ClientId $clientId
        $statusBadge = ''
        if (-not [string]::IsNullOrWhiteSpace($version)) {
            $label = "$label (Installed v$version)"
            $statusBadge = "Installed v$version"
        }
        elseif ($installed) {
            $label = "$label (Installed)"
            $statusBadge = 'Installed'
        }

        $secondaryText = ''

        $items += [ordered]@{
            Id = $clientId
            Label = $label
            PrimaryText = (Resolve-ClientLabel -ClientId $clientId)
            SecondaryText = $secondaryText
            StatusBadge = $statusBadge
            IsPrimaryAction = $false
            Installed = $installed
            Available = $available
            Description = if ($Mode -eq 'install') { 'Press Enter to install or update this target.' } else { 'Press Enter to remove this registration.' }
        }
    }

    if ($Mode -eq 'uninstall') {
        $items += [ordered]@{
            Id = 'full-uninstall'
            Label = (Get-TuiFullUninstallLabel)
            PrimaryText = (Get-TuiFullUninstallLabel)
            SecondaryText = 'Remove every detected registration and every installer-owned server location.'
            StatusBadge = ''
            IsPrimaryAction = $false
            ShowDividerBefore = $true
            Installed = $true
            Available = $true
            Description = 'Press Enter to remove all detected registrations and installer-owned server files.'
        }
    }
    elseif ($items.Count -eq 0) {
        $emptyLabel = if ($Mode -eq 'install') { 'No install targets available.' } else { 'No installed targets found.' }
        $items += [ordered]@{
            Id = ''
            Label = $emptyLabel
            PrimaryText = $emptyLabel
            SecondaryText = 'Press Escape to go back.'
            StatusBadge = ''
            IsPrimaryAction = $false
            Installed = $false
            Available = $false
            Description = 'Press Escape to go back.'
        }
    }

    if ($Mode -eq 'uninstall' -and $items.Count -eq 1) {
        $items[0].Description = 'Press Enter to remove all detected registrations and installer-owned server files.'
    }

    return @($items)
}

function Get-TuiUpdateBannerText {
    param(
        [Parameter(Mandatory)] $State,
        [string]$LatestVersion,
        $RegistrationMap
    )

    $updates = @(Get-AvailableInstallerUpdates -State $State -LatestVersion $LatestVersion -RegistrationMap $RegistrationMap)
    if ($updates.Count -eq 0) {
        return ''
    }

    if ([string]::IsNullOrWhiteSpace($LatestVersion)) {
        return ''
    }

    return "Update available: $($updates.Count) target(s) can move to v$LatestVersion."
}

function Get-TuiFullUninstallDivider {
    return '------------------------------'
}

function Get-TuiFullUninstallLabel {
    return 'Full Uninstall'
}

function Get-TuiHomeItemsCore {
    param(
        [Parameter(Mandatory)] [string]$InstallRoot,
        [Parameter(Mandatory)] $InstallerState,
        [string]$LatestVersion,
        $RegistrationMap
    )

    $updateSummary = 'All detected targets are up to date.'
    if (-not [string]::IsNullOrWhiteSpace($LatestVersion)) {
        $updates = @(Get-AvailableInstallerUpdates -State $InstallerState -LatestVersion $LatestVersion -RegistrationMap $RegistrationMap)
        if ($updates.Count -gt 0) {
            $updateSummary = "$($updates.Count) target(s) can move to v$LatestVersion."
        }
    }

    return @(
        [ordered]@{ Id = 'install'; Label = 'Install'; PrimaryText = 'Install'; SecondaryText = 'Select the target AI tool and install the MCP server.'; StatusBadge = ''; IsPrimaryAction = $true; Description = 'Install the selected release into a client.' }
        [ordered]@{ Id = 'uninstall'; Label = 'Uninstall'; PrimaryText = 'Uninstall'; SecondaryText = 'Remove a registered target or run a full uninstall.'; StatusBadge = ''; IsPrimaryAction = $true; Description = 'Remove an installed registration.' }
        [ordered]@{ Id = 'update-all'; Label = 'Update All'; PrimaryText = 'Update All'; SecondaryText = $updateSummary; StatusBadge = ''; IsPrimaryAction = $false; Description = 'Update every installed registration to the latest release.' }
        [ordered]@{ Id = 'edit-root'; Label = 'Install location'; PrimaryText = 'Install location'; SecondaryText = $InstallRoot; StatusBadge = ''; IsPrimaryAction = $false; Description = 'Change the shared MCP server install root.' }
        [ordered]@{ Id = 'exit'; Label = 'Exit'; PrimaryText = 'Exit'; SecondaryText = 'Close the installer.'; StatusBadge = ''; IsPrimaryAction = $false; Description = 'Close the installer.' }
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
        InstallerState = $InstallerState
        VisibleWindowSize = 6
        HomeItems = @()
    }

    $detectedRegistrationMap = Get-DetectedInstallerRegistrationMap -State $state.InstallerState
    $state.DetectedRegistrationMap = $detectedRegistrationMap
    $state.InstallItems = @(Get-TuiClientItems -State $state.InstallerState -Mode 'install' -RegistrationMap $detectedRegistrationMap)
    $state.UninstallItems = @(Get-TuiClientItems -State $state.InstallerState -Mode 'uninstall' -RegistrationMap $detectedRegistrationMap)
    $state.UpdateBannerText = Get-TuiUpdateBannerText -State $state.InstallerState -LatestVersion $LatestVersion -RegistrationMap $detectedRegistrationMap
    $state.HomeItems = @(Get-TuiHomeItemsCore -InstallRoot ([string]$state.InstallRoot) -InstallerState $state.InstallerState -LatestVersion $LatestVersion -RegistrationMap $detectedRegistrationMap)
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
    $State.HomeItems = @(Get-TuiHomeItemsCore -InstallRoot ([string]$State.InstallRoot) -InstallerState $InstallerState -LatestVersion ([string]$State.LatestVersion) -RegistrationMap $detectedRegistrationMap)
    return $State
}

function Get-TuiCurrentItems {
    param([Parameter(Mandatory)] $State)

    switch ([string]$State.CurrentScreen) {
        'HomeScreen' { return @($State.HomeItems) }
        'InstallScreen' { return @($State.InstallItems) }
        'UninstallScreen' { return @($State.UninstallItems) }
        'ConfirmScreen' { return @() }
        default { return @() }
    }
}

function Update-TuiScrollCore {
    param([Parameter(Mandatory)] $State)

    $items = @(Get-TuiCurrentItems -State $State)
    $maxIndex = [Math]::Max(0, $items.Count - 1)
    $State.SelectionIndex = [Math]::Min($maxIndex, [Math]::Max(0, [int]$State.SelectionIndex))

    $windowSize = [Math]::Max(1, [int]$State.VisibleWindowSize)
    if ([int]$State.SelectionIndex -lt [int]$State.ScrollOffset) {
        $State.ScrollOffset = [int]$State.SelectionIndex
    }
    elseif ([int]$State.SelectionIndex -ge ([int]$State.ScrollOffset + $windowSize)) {
        $State.ScrollOffset = [int]$State.SelectionIndex - $windowSize + 1
    }

    $maxOffset = [Math]::Max(0, $items.Count - $windowSize)
    $State.ScrollOffset = [Math]::Min($maxOffset, [Math]::Max(0, [int]$State.ScrollOffset))
    return $State
}

function Update-TuiSelectionCore {
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)] $KeyInfo
    )

    if ([string]$State.CurrentScreen -eq 'ConfirmScreen') {
        return (Handle-TuiConfirmationKeyCore -State $State -KeyInfo $KeyInfo)
    }

    $items = @(Get-TuiCurrentItems -State $State)
    $maxIndex = [Math]::Max(0, $items.Count - 1)

    switch ($KeyInfo.Key) {
        ([ConsoleKey]::UpArrow) { $State.SelectionIndex = [Math]::Max(0, [int]$State.SelectionIndex - 1) }
        ([ConsoleKey]::DownArrow) { $State.SelectionIndex = [Math]::Min($maxIndex, [int]$State.SelectionIndex + 1) }
        ([ConsoleKey]::LeftArrow) { $State.SelectedArchitecture = Get-NextArchitecture -Current $State.SelectedArchitecture -Direction -1 }
        ([ConsoleKey]::RightArrow) { $State.SelectedArchitecture = Get-NextArchitecture -Current $State.SelectedArchitecture -Direction 1 }
        ([ConsoleKey]::Escape) {
            if ($State.CurrentScreen -eq 'HomeScreen') {
                $State.Cancelled = $true
                $State.ShouldExit = $true
            }
            else {
                $State.CurrentScreen = 'HomeScreen'
                $State.SelectionIndex = 0
                $State.ScrollOffset = 0
            }
        }
        ([ConsoleKey]::Backspace) {
            if ($State.CurrentScreen -eq 'HomeScreen') {
                $State.Cancelled = $true
                $State.ShouldExit = $true
            }
            else {
                $State.CurrentScreen = 'HomeScreen'
                $State.SelectionIndex = 0
                $State.ScrollOffset = 0
            }
        }
        ([ConsoleKey]::Enter) {
            switch ([string]$State.CurrentScreen) {
                'HomeScreen' {
                    $selected = $State.HomeItems[[int]$State.SelectionIndex]
                    switch ([string]$selected.Id) {
                        'install' { $State.CurrentScreen = 'InstallScreen'; $State.SelectionIndex = 0; $State.ScrollOffset = 0 }
                        'uninstall' { $State.CurrentScreen = 'UninstallScreen'; $State.SelectionIndex = 0; $State.ScrollOffset = 0 }
                        'update-all' { $State.PendingAction = 'update-all' }
                        'edit-root' { $State.PendingAction = 'edit-root' }
                        default { $State.ShouldExit = $true }
                    }
                }
                'InstallScreen' {
                    if ($items.Count -gt 0 -and -not [string]::IsNullOrWhiteSpace([string]$items[[int]$State.SelectionIndex].Id)) {
                        $State.PendingAction = 'install'
                        $State.PendingClient = [string]$items[[int]$State.SelectionIndex].Id
                    }
                }
                'UninstallScreen' {
                    if ($items.Count -gt 0 -and -not [string]::IsNullOrWhiteSpace([string]$items[[int]$State.SelectionIndex].Id)) {
                        $selectedId = [string]$items[[int]$State.SelectionIndex].Id
                        if ($selectedId -eq 'full-uninstall') {
                            $State = Enter-TuiConfirmationCore -State $State -ConfirmationMode 'full-uninstall'
                        }
                        else {
                            $State = Enter-TuiConfirmationCore -State $State -ConfirmationMode 'unregister' -ClientId $selectedId
                        }
                    }
                }
            }
        }
    }

    return (Update-TuiScrollCore -State $State)
}
