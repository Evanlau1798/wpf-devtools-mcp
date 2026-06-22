function Write-InstallerActionProgress {
    param([Parameter(Mandatory)] [string]$Message)

    if (Get-Command Write-InstallerMessage -ErrorAction SilentlyContinue) {
        Write-InstallerMessage $Message
    }
}

function Get-InstallerUninstallCleanupGuidance {
    param([Parameter(Mandatory)] [string]$ResolvedClient)

    $otherClause = if ((Resolve-ClientBaseId -ClientId $ResolvedClient) -eq 'other') {
        ' For -Client other, other.mcpServers.json is the selected artifact-only registration target.'
    }
    else {
        ''
    }

    return "uninstall removes or verifies only the selected registration and leaves installer-owned server locations in place.$otherClause Use -Action full-uninstall to remove all detected registrations, generated client-registration artifacts, and installer-owned server locations."
}

function Get-InstallerFullUninstallCleanupGuidance {
    return 'full-uninstall removes all detected registrations, generated client-registration artifacts, and installer-owned server locations. Persisted auth secrets and certificate stores remain manual cleanup items.'
}

function Invoke-InstallerActionCore {
    param(
        [Parameter(Mandatory)] [ValidateSet('install', 'uninstall', 'full-uninstall')] [string]$ResolvedAction,
        [Parameter(Mandatory)] [string]$ResolvedArchitecture,
        [Parameter(Mandatory)] [string]$ResolvedClient,
        [Parameter(Mandatory)] [AllowEmptyString()] [AllowNull()] [string]$ResolvedInstallRoot,
        [Parameter(Mandatory)] [string]$RequestedVersion,
        [switch]$UseLatestRelease
    )

    if ($ResolvedAction -eq 'full-uninstall') {
        $state = Get-InstallerState
        $result = Invoke-InstallerFullUninstallCore -State $state
        return [ordered]@{
            action = 'full-uninstall'
            mode = 'offline'
            downloadSource = 'none'
            version = $RequestedVersion
            resolvedVersion = $null
            architecture = 'all'
            client = 'all'
            packageAssetName = $null
            downloadUri = $null
            installRoot = $null
            installedExecutable = $null
            selectedClients = @()
            statePath = [string]$result.statePath
            removedInstallation = [bool]$result.removedInstallation
            removedInstallations = @($result.removedInstallations)
            registrations = @($result.registrations)
            cleanupScope = 'registrations-and-installer-owned-server-locations'
            cleanupGuidance = Get-InstallerFullUninstallCleanupGuidance
            verificationMessage = [string]$result.verificationMessage
        }
    }

    if ($ResolvedAction -eq 'uninstall') {
        $state = Get-InstallerState
        $effectiveInstallRoot = Resolve-StandaloneRemovalInstallRoot -ResolvedInstallRoot $ResolvedInstallRoot -State $state
        $detectedRegistrations = if ($NonInteractive -or $OutputJson) {
            Get-StandaloneDetectedInstallerRegistrationMap -State $state
        }
        else {
            Invoke-WithTuiHelpers -ScriptBlock { Get-DetectedInstallerRegistrationMap -State $state }
        }
        $cursorRegistrationMode = $null
        if ((Resolve-ClientBaseId -ClientId $ResolvedClient) -eq 'cursor') {
            $cursorProfile = Resolve-CursorRegistrationProfile -SelectedClient $ResolvedClient
            $cursorRegistrationMode = "cursor-$([string]$cursorProfile.Mode)"
        }

        $requestedStateKey = Resolve-ClientStateKey -ClientId $ResolvedClient -RegistrationMode $cursorRegistrationMode
        $detectedRegistration = if ($detectedRegistrations.Contains($requestedStateKey)) {
            $detectedRegistrations[$requestedStateKey]
        }
        elseif ($detectedRegistrations.Contains($ResolvedClient)) {
            $detectedRegistrations[$ResolvedClient]
        }
        elseif ($ResolvedClient -eq 'cursor') {
            ($detectedRegistrations.GetEnumerator() | Where-Object { $_.Key -like 'cursor-*' } | Select-Object -ExpandProperty Value | Select-Object -First 1)
        }
        else {
            $null
        }

        $stateRegistrationKey = if ($state.registrations.Contains($requestedStateKey)) {
            $requestedStateKey
        }
        elseif ($state.registrations.Contains($ResolvedClient)) {
            $ResolvedClient
        }
        elseif ($null -ne $detectedRegistration) {
            Resolve-ClientStateKey -ClientId ([string]$detectedRegistration.ClientId) -RegistrationMode ([string]$detectedRegistration.RegistrationMode)
        }
        else {
            $null
        }

        $stateRegistrationRecord = if (-not [string]::IsNullOrWhiteSpace($stateRegistrationKey) -and $state.registrations.Contains($stateRegistrationKey)) {
            $state.registrations[$stateRegistrationKey]
        }
        else {
            $null
        }

        if ($script:InstallRootWasSpecified) {
            if ($null -ne $detectedRegistration -and -not (Test-InstallerRegistrationMatchesInstallRoot -RegistrationRecord $detectedRegistration -ExpectedInstallRoot $effectiveInstallRoot)) {
                $detectedRegistration = $null
            }

            if ($null -ne $stateRegistrationRecord -and -not (Test-InstallerRegistrationMatchesInstallRoot -RegistrationRecord $stateRegistrationRecord -ExpectedInstallRoot $effectiveInstallRoot)) {
                $stateRegistrationRecord = $null
                $stateRegistrationKey = $null
            }
        }

        $registrationRecord = if ($null -ne $detectedRegistration) {
            $detectedRegistration
        }
        elseif ($null -ne $stateRegistrationRecord) {
            $stateRegistrationRecord
        }
        else {
            Get-StandaloneFallbackRegistrationRecord -SelectedClient $ResolvedClient -ResolvedInstallRoot $effectiveInstallRoot -ResolvedArchitecture $ResolvedArchitecture
        }
        $registrationRecord = Merge-RegistrationRecordWithStateFallback -RegistrationRecord $registrationRecord -StateRecord $stateRegistrationRecord -SelectedClient $ResolvedClient
        if ($null -ne $registrationRecord) {
            if ([string]::IsNullOrWhiteSpace($ResolvedArchitecture)) {
                $ResolvedArchitecture = if ($registrationRecord.Contains('architecture')) { [string]$registrationRecord.architecture } else { [string]$registrationRecord.Architecture }
            }
            if (-not $script:InstallRootWasSpecified -and [string]::IsNullOrWhiteSpace($ResolvedInstallRoot)) {
                $ResolvedInstallRoot = if ($registrationRecord.Contains('installRoot')) { [string]$registrationRecord.installRoot } else { [string]$registrationRecord.InstallRoot }
            }
        }

        if ([string]::IsNullOrWhiteSpace($ResolvedArchitecture)) {
            $ResolvedArchitecture = Get-SystemDefaultArchitecture
        }
        if ([string]::IsNullOrWhiteSpace($ResolvedInstallRoot)) {
            $ResolvedInstallRoot = $effectiveInstallRoot
        }

        $ResolvedInstallRoot = Resolve-AbsolutePath -Path $ResolvedInstallRoot
        $installedExecutable = if ($null -ne $detectedRegistration) { [string]$detectedRegistration.InstalledExecutable } else { $null }
        $installBase = if (-not [string]::IsNullOrWhiteSpace($ResolvedArchitecture) -and -not [string]::IsNullOrWhiteSpace($ResolvedInstallRoot)) {
            Join-Path $ResolvedInstallRoot $ResolvedArchitecture
        }
        else {
            $null
        }
        $registrations = @()
        $skipSelectedUninstall = Test-InstallerExplicitRootCliUninstallNoOp -RegistrationRecord $registrationRecord -InstallRootWasSpecified:([bool]$script:InstallRootWasSpecified)
        try {
            if ($skipSelectedUninstall) {
                $verification = [ordered]@{
                    Succeeded = $true
                    VerificationMessage = "Verified no matching $ResolvedClient registration under $ResolvedInstallRoot."
                }
            }
            else {
                $registrations = @(Invoke-ClientUnregistration -SelectedClient $ResolvedClient -RegistrationRecord $registrationRecord)
                $verification = Invoke-UninstallVerification -SelectedClient $ResolvedClient -RegistrationRecord $registrationRecord -RegistrationChanges $registrations
                if (-not $verification.Succeeded) {
                    throw $verification.VerificationMessage
                }
            }

            $removedRuntimeScreenshotCache = Remove-InstallerRuntimeScreenshotCache
            if (-not [string]::IsNullOrWhiteSpace($stateRegistrationKey) -and $state.registrations.Contains($stateRegistrationKey)) {
                [void]$state.registrations.Remove($stateRegistrationKey)
            }

            $statePath = Resolve-InstallerStatePath
            if ((Test-Path $statePath) -or (Test-InstallerStateHasData -State $state)) {
                $statePath = Save-InstallerState -State $state
            }
            else {
                $statePath = $null
            }
        }
        catch {
            Undo-ClientRegistrationChanges -SelectedClient $ResolvedClient -Registrations $registrations -RollbackMode 'uninstall' -InstalledExecutable $installedExecutable -InstallBase $installBase -RegistrationRecord $registrationRecord
            throw
        }
        return [ordered]@{
            action = 'uninstall'
            mode = 'offline'
            downloadSource = 'none'
            version = $RequestedVersion
            resolvedVersion = $null
            architecture = $ResolvedArchitecture
            client = $ResolvedClient
            packageAssetName = $null
            downloadUri = $null
            installRoot = $ResolvedInstallRoot
            installedExecutable = $installedExecutable
            selectedClients = @($ResolvedClient)
            statePath = $statePath
            removedInstallation = $false
            removedRuntimeScreenshotCache = $removedRuntimeScreenshotCache
            registrations = @($registrations)
            cleanupScope = 'selected-registration-only'
            cleanupGuidance = Get-InstallerUninstallCleanupGuidance -ResolvedClient $ResolvedClient
            verificationMessage = [string]$verification.VerificationMessage
        }
    }

    if ([string]::IsNullOrWhiteSpace($ResolvedInstallRoot)) {
        $ResolvedInstallRoot = Resolve-PreferredInstallRoot
    }

    $mode = if ($UseLatestRelease) { 'online' } else { Resolve-InstallerMode }
    Write-InstallerActionProgress "[1/4] Resolving package for $ResolvedArchitecture."
    $session = Resolve-PackageSession -Mode $mode -ResolvedVersion $RequestedVersion -ResolvedArchitecture $ResolvedArchitecture
    $installResult = $null
    $registrations = @()
    $allowPayloadRollback = $true
    $stateRestoreRequired = $false
    $statePathForRollback = $null
    $stateFileExistedBeforeInstall = $false
    $originalStateContent = $null
    $statePathResolver = Get-Command Resolve-InstallerStatePath -ErrorAction SilentlyContinue
    if ($null -ne $statePathResolver) {
        $statePathForRollback = Resolve-InstallerStatePath
        $stateFileExistedBeforeInstall = Test-Path -LiteralPath $statePathForRollback
        if ($stateFileExistedBeforeInstall) {
            $originalStateContent = Get-Content -LiteralPath $statePathForRollback -Raw
        }
    }
    try {
        $packageManifest = Get-Content -Path (Resolve-PackageManifestPath -PackageDirectory $session.PackageDirectory) -Raw | ConvertFrom-Json
        $resolvedArchitecture = if ([string]::IsNullOrWhiteSpace([string]$packageManifest.architecture)) { $ResolvedArchitecture } else { [string]$packageManifest.architecture }
        $resolvedVersion = if ([string]::IsNullOrWhiteSpace([string]$packageManifest.version)) { [string]$session.ResolvedVersion } else { [string]$packageManifest.version }
        $packageAssetIdentity = Get-ReleaseAssetIdentity -AssetName ([string]$session.PackageAssetName)
        $packageAssetName = if ($null -ne $packageAssetIdentity) {
            [string]$packageAssetIdentity.AssetName
        }
        elseif ($session.DownloadSource -eq 'local-package' -and -not [string]::IsNullOrWhiteSpace([string]$session.PackageAssetName) -and -not [string]::IsNullOrWhiteSpace($resolvedVersion)) {
            Get-ReleaseAssetName -ResolvedVersion $resolvedVersion -ResolvedArchitecture $resolvedArchitecture
        }
        else {
            [string]$session.PackageAssetName
        }
        $downloadUri = if (-not [string]::IsNullOrWhiteSpace($packageAssetName) -and -not [string]::IsNullOrWhiteSpace($resolvedVersion)) {
            Get-ReleaseDownloadUri -ResolvedVersion $resolvedVersion -ResolvedArchitecture $resolvedArchitecture
        }
        else {
            [string]$session.DownloadUri
        }

        Write-InstallerActionProgress "[2/4] Installing payload into $ResolvedInstallRoot."
        $installResult = Install-PackagePayload `
            -PackageDirectory $session.PackageDirectory `
            -PackageManifest $packageManifest `
            -ResolvedArchitecture $resolvedArchitecture `
            -ResolvedInstallRoot $ResolvedInstallRoot `
            -ResolvedVersion $resolvedVersion `
            -TrustedSignerThumbprint ([string]$session.TrustedSignerThumbprint) `
            -TrustedSignerSubject ([string]$session.TrustedSignerSubject) `
            -TrustedArchiveManifestPolicy:([bool]$session.TrustedArchiveManifestPolicy)
        Write-InstallerActionProgress "[3/4] Registering client $ResolvedClient."
        $registrations = @(Invoke-ClientRegistration -SelectedClient $ResolvedClient -InstalledExecutable $installResult.installedExecutable -InstallBase $installResult.installBase)
        Write-InstallerActionProgress '[4/4] Verifying installation.'
        $verification = Invoke-InstallVerification -SelectedClient $ResolvedClient -ResolvedVersion $resolvedVersion -InstalledExecutable $installResult.installedExecutable -Registration $registrations[0]
        if (-not $verification.Succeeded) {
            throw $verification.VerificationMessage
        }

        Update-InstalledManifestManagedRegistrationTarget -InstallBase $installResult.installBase -SelectedClient $ResolvedClient -Registration $registrations[0]

        $state = Get-InstallerState
        Update-InstallerStateAfterInstall -State $state -ResolvedInstallRoot $installResult.installRoot -ResolvedArchitecture $resolvedArchitecture -ResolvedVersion ([string]$verification.InstalledVersion) -InstalledExecutable $installResult.installedExecutable -SelectedClient $ResolvedClient -Registration $registrations[0] -LastVerifiedUtc ([string]$verification.LastVerifiedUtc)
        $statePath = Save-InstallerState -State $state
        $stateRestoreRequired = $true
        Complete-InstalledPayloadCommit -InstallResult $installResult
        $allowPayloadRollback = $false
        return [ordered]@{
            action = 'install'
            mode = $mode
            downloadSource = [string]$session.DownloadSource
            version = $RequestedVersion
            resolvedVersion = [string]$verification.InstalledVersion
            architecture = $resolvedArchitecture
            client = $ResolvedClient
            packageAssetName = $packageAssetName
            downloadUri = $downloadUri
            installRoot = $installResult.installRoot
            installedExecutable = $installResult.installedExecutable
            selectedClients = @($ResolvedClient)
            statePath = $statePath
            reusedExistingBinary = [bool]$installResult.reusedExistingBinary
            registrations = @($registrations)
            verificationMessage = [string]$verification.VerificationMessage
            lastVerifiedUtc = [string]$verification.LastVerifiedUtc
        }
    }
    catch {
        $rollbackErrors = New-Object System.Collections.Generic.List[string]
        $rollbackInstalledExecutable = if ($null -ne $installResult) { [string]$installResult.installedExecutable } else { $null }
        $rollbackInstallBase = if ($null -ne $installResult) { [string]$installResult.installBase } else { $null }
        if ($stateRestoreRequired -and -not [string]::IsNullOrWhiteSpace($statePathForRollback)) {
            try {
                if ($stateFileExistedBeforeInstall) {
                    $stateDirectory = Split-Path -Parent $statePathForRollback
                    if (-not [string]::IsNullOrWhiteSpace($stateDirectory)) {
                        New-Item -ItemType Directory -Force -Path $stateDirectory | Out-Null
                    }

                    Write-InstallerUtf8NoBomFile -Path $statePathForRollback -Content ([string]$originalStateContent)
                }
                else {
                    Remove-PathIfExists -Path $statePathForRollback
                }
            }
            catch {
                $rollbackErrors.Add("Failed to restore installer state after install rollback. $($_.Exception.Message)")
            }
        }

        if ($allowPayloadRollback) {
            try {
                Undo-ClientRegistrationChanges -SelectedClient $ResolvedClient -Registrations $registrations -RollbackMode 'install' -InstalledExecutable $rollbackInstalledExecutable -InstallBase $rollbackInstallBase
            }
            catch {
                $rollbackErrors.Add("Failed to restore client registrations after install rollback. $($_.Exception.Message)")
            }

            try {
                Undo-InstalledPayload -InstallResult $installResult
            }
            catch {
                $rollbackErrors.Add("Failed to restore installed payload after install rollback. $($_.Exception.Message)")
            }
        }

        if ($rollbackErrors.Count -gt 0) {
            throw ($_.Exception.Message + ' Rollback issues: ' + ($rollbackErrors -join ' '))
        }

        throw
    }
    finally {
        if ($session.CleanupSession) {
            Remove-PathIfExists -Path $session.SessionRoot -BestEffort
        }
    }
}
