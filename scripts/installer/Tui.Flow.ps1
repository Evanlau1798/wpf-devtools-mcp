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

function Get-TuiStartupReadyTimeoutSecondsCore {
    $timeoutSeconds = 2
    if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_STARTUP_READY_TIMEOUT_SEC)) {
        [void][int]::TryParse($env:WPFDEVTOOLS_INSTALLER_STARTUP_READY_TIMEOUT_SEC, [ref]$timeoutSeconds)
    }

    return [Math]::Min(15, [Math]::Max(1, $timeoutSeconds))
}

function Initialize-TuiStartupStateCore {
    param([Parameter(Mandatory)] $State)

    if ($State.StartupInitialized) {
        return $State
    }

    $State.StartupInitialized = $true
    $State.StatusMessage = 'Loading installer data...'
    if ([string]::IsNullOrWhiteSpace([string]$State.LatestVersion)) {
        $State.StatusMessage = 'Checking cached release information...'
        $State.LatestVersion = Get-LatestInstallerVersion -UseCacheOnly
    }

    $State.UpdateBannerText = Get-TuiUpdateBannerText -State $State.InstallerState -LatestVersion ([string]$State.LatestVersion) -RegistrationMap $State.DetectedRegistrationMap
    $State = Start-TuiLatestVersionRefreshCore -State $State
    if ([bool]$State.LatestVersionRefreshPending) {
        $State.StatusMessage = 'Checking latest release metadata...'
    }
    else {
        $State.StatusMessage = 'Use Up/Down to choose an action.'
    }
    return $State
}

function Start-TuiLatestVersionRefreshCore {
    param([Parameter(Mandatory)] $State)

    if (-not [string]::IsNullOrWhiteSpace([string]$State.LatestVersion)) {
        return $State
    }

    if ($null -ne $State.LatestVersionRefreshHandle) {
        return $State
    }

    if ($State.DetectedRegistrationMap.Count -eq 0) {
        return $State
    }

    $State.LatestVersionRefreshHandle = Start-LatestInstallerVersionRefresh
    $State.LatestVersionRefreshPending = $true
    $State.HomeItems = @(Get-TuiHomeItemsCore -InstallRoot ([string]$State.InstallRoot) -InstallerState $State.InstallerState -LatestVersion ([string]$State.LatestVersion) -RegistrationMap $State.DetectedRegistrationMap -LatestVersionRefreshPending ([bool]$State.LatestVersionRefreshPending))
    return $State
}

function Update-TuiLatestVersionRefreshCore {
    param([Parameter(Mandatory)] $State)

    if ($null -eq $State.LatestVersionRefreshHandle) {
        return $State
    }

    $refreshResult = Receive-LatestInstallerVersionRefresh -RefreshHandle $State.LatestVersionRefreshHandle
    if (-not [bool]$refreshResult.IsCompleted) {
        return $State
    }

    $State.LatestVersionRefreshHandle = $null
    $State.LatestVersionRefreshPending = $false
    if ([string]::IsNullOrWhiteSpace([string]$refreshResult.Version)) {
        $State.UpdateBannerText = Get-TuiUpdateBannerText -State $State.InstallerState -LatestVersion ([string]$State.LatestVersion) -RegistrationMap $State.DetectedRegistrationMap
        $State.HomeItems = @(Get-TuiHomeItemsCore -InstallRoot ([string]$State.InstallRoot) -InstallerState $State.InstallerState -LatestVersion ([string]$State.LatestVersion) -RegistrationMap $State.DetectedRegistrationMap -LatestVersionRefreshPending ([bool]$State.LatestVersionRefreshPending))
        if ([string]$State.CurrentScreen -eq 'HomeScreen' -and ([string]$State.StatusMessage -in @('Checking latest release in the background...', 'Checking latest release metadata...'))) {
            if (-not [string]::IsNullOrWhiteSpace([string]$refreshResult.ErrorMessage)) {
                $State.StatusMessage = "Latest release metadata unavailable: $([string]$refreshResult.ErrorMessage)"
            }
            else {
                $State.StatusMessage = 'Use Up/Down to choose an action.'
            }
        }
        return $State
    }

    if (-not [string]::IsNullOrWhiteSpace([string]$refreshResult.Version)) {
        $State.LatestVersion = [string]$refreshResult.Version
        $State.UpdateBannerText = Get-TuiUpdateBannerText -State $State.InstallerState -LatestVersion ([string]$State.LatestVersion) -RegistrationMap $State.DetectedRegistrationMap
        $State.HomeItems = @(Get-TuiHomeItemsCore -InstallRoot ([string]$State.InstallRoot) -InstallerState $State.InstallerState -LatestVersion ([string]$State.LatestVersion) -RegistrationMap $State.DetectedRegistrationMap -LatestVersionRefreshPending ([bool]$State.LatestVersionRefreshPending))
        if ([string]$State.CurrentScreen -eq 'HomeScreen' -and ([string]$State.StatusMessage -in @('Checking latest release in the background...', 'Checking latest release metadata...'))) {
            $State.StatusMessage = 'Use Up/Down to choose an action.'
        }
    }

    return $State
}

function Update-TuiStartupProgressCore {
    param([Parameter(Mandatory)] $State)

    if (-not [bool]$State.StartupProgressActive) {
        return $State
    }

    if (-not [bool]$State.StartupInitialized) {
        return $State
    }

    if (-not [bool]$State.LatestVersionRefreshPending) {
        $State.StartupProgressActive = $false
        $State.StartupProgressTitle = ''
        $State.StartupReadyDeadlineUtc = $null
        if ([string]::IsNullOrWhiteSpace([string]$State.StatusMessage) -or
            ([string]$State.StatusMessage -eq 'Checking latest release metadata...')) {
            $State.StatusMessage = 'Use Up/Down to choose an action.'
        }

        return $State
    }

    $State.StartupProgressTitle = 'Checking latest release'
    $State.StatusMessage = 'Checking latest release metadata...'

    $deadlineUtc = [DateTimeOffset]::MinValue
    if (-not [DateTimeOffset]::TryParse([string]$State.StartupReadyDeadlineUtc, [ref]$deadlineUtc)) {
        $deadlineUtc = [DateTimeOffset]::UtcNow.AddSeconds((Get-TuiStartupReadyTimeoutSecondsCore))
        $State.StartupReadyDeadlineUtc = $deadlineUtc.ToString('o')
        return $State
    }

    if ([DateTimeOffset]::UtcNow -lt $deadlineUtc) {
        return $State
    }

    $State = Stop-TuiLatestVersionRefreshCore -State $State
    $State.UpdateBannerText = Get-TuiUpdateBannerText -State $State.InstallerState -LatestVersion ([string]$State.LatestVersion) -RegistrationMap $State.DetectedRegistrationMap
    $State.HomeItems = @(Get-TuiHomeItemsCore -InstallRoot ([string]$State.InstallRoot) -InstallerState $State.InstallerState -LatestVersion ([string]$State.LatestVersion) -RegistrationMap $State.DetectedRegistrationMap -LatestVersionRefreshPending ([bool]$State.LatestVersionRefreshPending))
    $State.StatusMessage = 'Latest release metadata is unavailable. Continuing with cached or offline data.'
    $State.StartupProgressActive = $false
    $State.StartupProgressTitle = ''
    $State.StartupReadyDeadlineUtc = $null
    return $State
}

function Stop-TuiLatestVersionRefreshCore {
    param([Parameter(Mandatory)] $State)

    if ($null -eq $State.LatestVersionRefreshHandle) {
        return $State
    }

    Stop-LatestInstallerVersionRefresh -RefreshHandle $State.LatestVersionRefreshHandle
    $State.LatestVersionRefreshHandle = $null
    $State.LatestVersionRefreshPending = $false
    return $State
}

function Get-TuiRenderSignatureCore {
    param([Parameter(Mandatory)] $State)

    $pathEditorBuffer = if ($null -ne $State.PathEditor) { [string]$State.PathEditor.Buffer } else { '' }
    $pathEditorStatus = if ($null -ne $State.PathEditor) { [string]$State.PathEditor.StatusMessage } else { '' }
    $browserDirectory = if ($null -ne $State.PathEditor) { [string]$State.PathEditor.BrowserCurrentDirectory } else { '' }
    $browserSelectionIndex = if ($null -ne $State.PathEditor) { [int]$State.PathEditor.BrowserSelectionIndex } else { 0 }
    $browserScrollOffset = if ($null -ne $State.PathEditor) { [int]$State.PathEditor.BrowserScrollOffset } else { 0 }
    $selectedParentDirectory = if ($null -ne $State.PathEditor) { [string]$State.PathEditor.SelectedParentDirectory } else { '' }
    $folderNameBuffer = if ($null -ne $State.PathEditor) { [string]$State.PathEditor.FolderNameBuffer } else { '' }
    return @(
        [string]$State.CurrentScreen
        [int]$State.SelectionIndex
        [int]$State.ScrollOffset
        [string]$State.SelectedArchitecture
        [string]$State.InstallRoot
        [string]$State.StatusMessage
        [string]$State.UpdateBannerText
        [string]$State.LatestVersion
        [bool]$State.LatestVersionRefreshPending
        [bool]$State.StartupProgressActive
        [string]$State.StartupProgressTitle
        [int]$State.ConfirmationStep
        [string]$State.ConfirmationMode
        $pathEditorBuffer
        $pathEditorStatus
        $browserDirectory
        $browserSelectionIndex
        $browserScrollOffset
        $selectedParentDirectory
        $folderNameBuffer
    ) -join '|'
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
    $state.StartupProgressActive = $true
    $state.StartupProgressTitle = 'Preparing installer UI'
    $state.StatusMessage = 'Loading installer runtime...'
    $terminalSession = Enter-TuiTerminalSessionCore -Viewport (Get-TuiViewportCore)
    $lastViewportKey = $null
    $lastRenderSignature = $null

    try {
        Render-TuiScreenCore -State $state | Out-Null
        $lastViewportKey = Get-TuiViewportCacheKeyCore -Viewport (Get-TuiViewportCore)
        $lastRenderSignature = Get-TuiRenderSignatureCore -State $state
        $state.StartupProgressTitle = 'Loading installer data'
        $state.StatusMessage = if ([string]::IsNullOrWhiteSpace([string]$state.LatestVersion)) {
            'Checking cached release information...'
        }
        else {
            'Loading installer data...'
        }
        Render-TuiScreenCore -State $state | Out-Null
        $lastViewportKey = Get-TuiViewportCacheKeyCore -Viewport (Get-TuiViewportCore)
        $lastRenderSignature = Get-TuiRenderSignatureCore -State $state
        $state = Initialize-TuiStartupStateCore -State $state
        $state = Update-TuiStartupProgressCore -State $state

        while (-not $state.ShouldExit) {
            $state = Update-TuiLatestVersionRefreshCore -State $state
            $state = Update-TuiStartupProgressCore -State $state
            $viewportKey = Get-TuiViewportCacheKeyCore -Viewport (Get-TuiViewportCore)
            $renderSignature = Get-TuiRenderSignatureCore -State $state
            if (($viewportKey -ne $lastViewportKey) -or ($renderSignature -ne $lastRenderSignature)) {
                Render-TuiScreenCore -State $state | Out-Null
                $lastViewportKey = $viewportKey
                $lastRenderSignature = $renderSignature
            }

            if ([bool]$state.StartupProgressActive) {
                Start-Sleep -Milliseconds 50
                continue
            }

            $keyInfo = Read-TuiKeyCore -TimeoutMilliseconds (Get-TuiInputPollTimeoutCore -State $state)
            if ($null -eq $keyInfo) {
                continue
            }
            $state = Update-TuiSelectionCore -State $state -KeyInfo $keyInfo

            switch ([string]$state.PendingAction) {
                'edit-root' { $state = Invoke-TuiInstallRootPromptCore -State $state; $state.PendingAction = $null }
                'install' { $state = Invoke-TuiInstallOperationCore -State $state }
                'uninstall' { $state = Invoke-TuiUninstallOperationCore -State $state }
                'full-uninstall' { $state = Invoke-TuiFullUninstallOperationCore -State $state }
                'update-all' { $state = Invoke-TuiUpdateAllOperationCore -State $state }
                'exit' { $state.PendingAction = $null; $state.ShouldExit = $true }
            }
        }
    }
    finally {
        $state = Stop-TuiLatestVersionRefreshCore -State $state
        Exit-TuiTerminalSessionCore -Session $terminalSession
    }

    return [ordered]@{
        Launched = $true
        Cancelled = [bool]$state.Cancelled
        Selection = $null
        HandledInWindow = $true
    }
}
