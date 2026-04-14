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

function Get-TuiRegistrationEntriesCore {
    param(
        [Parameter(Mandatory)] $RegistrationMap,
        [Parameter(Mandatory)] [string]$ClientId
    )

    if ($RegistrationMap.Contains($ClientId)) {
        return @([ordered]@{
                Key = $ClientId
                Registration = $RegistrationMap[$ClientId]
            })
    }

    if ($ClientId -ne 'cursor') {
        return @()
    }

    return @($RegistrationMap.GetEnumerator() |
            Where-Object { $_.Key -like 'cursor-*' } |
            Sort-Object Key |
            ForEach-Object {
                [ordered]@{
                    Key = [string]$_.Key
                    Registration = $_.Value
                }
            })
}

function Get-TuiInstalledVersion {
    param(
        [Parameter(Mandatory)] $RegistrationMap,
        [Parameter(Mandatory)] [string]$ClientId
    )

    $versions = @((Get-TuiRegistrationEntriesCore -RegistrationMap $RegistrationMap -ClientId $ClientId) |
        ForEach-Object {
            Get-InstallerRecordStringValueCore -Record $_.Registration -PropertyNames @('ResolvedVersion', 'resolvedVersion')
        } |
        Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } |
        Select-Object -Unique)
    if ($versions.Count -eq 1) {
        return [string]$versions[0]
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
        'cursor' {
            return $true
        }
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

    $registrationMap = if ($null -ne $RegistrationMap) { $RegistrationMap } else { Get-DetectedInstallerRegistrationMap -State $State }
    $items = @()
    if ($Mode -eq 'install') {
        foreach ($client in Get-SupportedClients) {
            $clientId = [string]$client.Id
            $registrations = @(Get-TuiRegistrationEntriesCore -RegistrationMap $registrationMap -ClientId $clientId)
            $installed = $registrations.Count -gt 0
            $available = Test-TuiClientAvailable -ClientId $clientId -State $State
            $version = Get-TuiInstalledVersion -RegistrationMap $registrationMap -ClientId $clientId

            if (-not ($available -or $installed)) {
                continue
            }

            $label = Resolve-ClientLabel -ClientId $clientId
            $statusBadge = ''
            if (-not [string]::IsNullOrWhiteSpace($version)) {
                $label = "$label (Installed v$version)"
                $statusBadge = "Installed v$version"
            }
            elseif ($installed -and $clientId -eq 'cursor' -and $registrations.Count -gt 1) {
                $label = "$label ($($registrations.Count) scopes installed)"
                $statusBadge = "$($registrations.Count) scopes installed"
            }
            elseif ($installed) {
                $label = "$label (Installed)"
                $statusBadge = 'Installed'
            }

            $secondaryText = ''
            $description = 'Press Enter to install or update this target.'
            $statusText = ''
            if ($clientId -eq 'other') {
                $secondaryText = 'Export JSON and CLI examples for manual MCP registration.'
                $description = 'Review other.mcpServers.json, claude-code.txt, and codex.txt under client-registration. See AI Agent Clients for registration examples.'
                $statusText = 'Registration examples: other.mcpServers.json, claude-code.txt, codex.txt.'
            }

            $items += [ordered]@{
                Id = $clientId
                Label = $label
                PrimaryText = (Resolve-ClientLabel -ClientId $clientId)
                SecondaryText = $secondaryText
                StatusBadge = $statusBadge
                IsPrimaryAction = $false
                Installed = $installed
                Available = $available
                Description = $description
                StatusText = $statusText
            }
        }
    }
    else {
        foreach ($entry in @($registrationMap.GetEnumerator() | Sort-Object Key)) {
            $clientId = [string]$entry.Key
            $version = Get-InstallerRecordStringValueCore -Record $entry.Value -PropertyNames @('ResolvedVersion', 'resolvedVersion')
            $label = Resolve-ClientLabel -ClientId $clientId
            $statusBadge = if ([string]::IsNullOrWhiteSpace($version)) { 'Installed' } else { "Installed v$version" }
            if (-not [string]::IsNullOrWhiteSpace($version)) {
                $label = "$label (Installed v$version)"
            }
            else {
                $label = "$label (Installed)"
            }

            $items += [ordered]@{
                Id = $clientId
                Label = $label
                PrimaryText = (Resolve-ClientLabel -ClientId $clientId)
                SecondaryText = ''
                StatusBadge = $statusBadge
                IsPrimaryAction = $false
                Installed = $true
                Available = (Test-TuiClientAvailable -ClientId (Resolve-ClientBaseId -ClientId $clientId) -State $State)
                Description = 'Press Enter to remove this registration.'
            }
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

function Get-TuiCurrentItems {
    param([Parameter(Mandatory)] $State)

    switch ([string]$State.CurrentScreen) {
        'HomeScreen' { return @($State.HomeItems) }
        'InstallScreen' { return @($State.InstallItems) }
        'UninstallScreen' { return @($State.UninstallItems) }
        'ConfirmScreen' { return @() }
        'PathEditorScreen' { return @() }
        'DirectoryPickerScreen' { return @() }
        'FolderNamePromptScreen' { return @() }
        default { return @() }
    }
}

function Get-TuiDirectionalScrollOffsetCore {
    param(
        [Parameter(Mandatory)] [int]$SelectionIndex,
        [Parameter(Mandatory)] [int]$ItemCount,
        [Parameter(Mandatory)] [int]$WindowSize,
        [Parameter(Mandatory)] [int]$CurrentOffset,
        [int]$Direction = 0
    )

    $maxOffset = [Math]::Max(0, $ItemCount - $WindowSize)
    if ($ItemCount -le $WindowSize) {
        return 0
    }

    switch ($Direction) {
        1 {
            if ($SelectionIndex -lt ($ItemCount - 1)) {
                $bottomAnchor = [Math]::Max(0, $WindowSize - 2)
                return [Math]::Min($maxOffset, [Math]::Max(0, $SelectionIndex - $bottomAnchor))
            }
        }
        -1 {
            if ($SelectionIndex -gt 0) {
                $topAnchor = if ($WindowSize -gt 1) { 1 } else { 0 }
                return [Math]::Min($maxOffset, [Math]::Max(0, $SelectionIndex - $topAnchor))
            }
        }
    }

    $offset = [Math]::Max(0, $CurrentOffset)
    if ($SelectionIndex -lt $offset) {
        $offset = $SelectionIndex
    }
    elseif ($SelectionIndex -ge ($offset + $WindowSize)) {
        $offset = $SelectionIndex - $WindowSize + 1
    }

    return [Math]::Min($maxOffset, [Math]::Max(0, $offset))
}

function Update-TuiScrollCore {
    param(
        [Parameter(Mandatory)] $State,
        [int]$Direction = 0
    )

    $items = @(Get-TuiCurrentItems -State $State)
    $maxIndex = [Math]::Max(0, $items.Count - 1)
    $State.SelectionIndex = [Math]::Min($maxIndex, [Math]::Max(0, [int]$State.SelectionIndex))

    $windowSize = [Math]::Max(1, [int]$State.VisibleWindowSize)
    if ([string]$State.CurrentScreen -in @('InstallScreen', 'UninstallScreen')) {
        $State.ScrollOffset = Get-TuiDirectionalScrollOffsetCore `
            -SelectionIndex ([int]$State.SelectionIndex) `
            -ItemCount $items.Count `
            -WindowSize $windowSize `
            -CurrentOffset ([int]$State.ScrollOffset) `
            -Direction $Direction
    }
    else {
        if ([int]$State.SelectionIndex -lt [int]$State.ScrollOffset) {
            $State.ScrollOffset = [int]$State.SelectionIndex
        }
        elseif ([int]$State.SelectionIndex -ge ([int]$State.ScrollOffset + $windowSize)) {
            $State.ScrollOffset = [int]$State.SelectionIndex - $windowSize + 1
        }

        $maxOffset = [Math]::Max(0, $items.Count - $windowSize)
        $State.ScrollOffset = [Math]::Min($maxOffset, [Math]::Max(0, [int]$State.ScrollOffset))
    }

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

    if ([string]$State.CurrentScreen -in @('PathEditorScreen', 'DirectoryPickerScreen', 'FolderNamePromptScreen')) {
        return (Handle-TuiPathEditorKeyCore -State $State -KeyInfo $KeyInfo)
    }

    $items = @(Get-TuiCurrentItems -State $State)
    $maxIndex = [Math]::Max(0, $items.Count - 1)
    $scrollDirection = 0

    switch ($KeyInfo.Key) {
        ([ConsoleKey]::UpArrow) {
            $State.SelectionIndex = [Math]::Max(0, [int]$State.SelectionIndex - 1)
            $scrollDirection = -1
        }
        ([ConsoleKey]::DownArrow) {
            $State.SelectionIndex = [Math]::Min($maxIndex, [int]$State.SelectionIndex + 1)
            $scrollDirection = 1
        }
        ([ConsoleKey]::LeftArrow) { $State.SelectedArchitecture = Get-NextArchitecture -Current $State.SelectedArchitecture -Direction -1 }
        ([ConsoleKey]::RightArrow) { $State.SelectedArchitecture = Get-NextArchitecture -Current $State.SelectedArchitecture -Direction 1 }
        ([ConsoleKey]::Escape) {
            if ($State.CurrentScreen -eq 'HomeScreen') {
                $State = Enter-TuiConfirmationCore -State $State -ConfirmationMode 'close-app'
            }
            else {
                $State.CurrentScreen = 'HomeScreen'
                $State.SelectionIndex = 0
                $State.ScrollOffset = 0
            }
        }
        ([ConsoleKey]::Backspace) {
            if ($State.CurrentScreen -eq 'HomeScreen') {
                $State = Enter-TuiConfirmationCore -State $State -ConfirmationMode 'close-app'
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
                        default { $State.PendingAction = $null }
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

    return (Update-TuiScrollCore -State $State -Direction $scrollDirection)
}
