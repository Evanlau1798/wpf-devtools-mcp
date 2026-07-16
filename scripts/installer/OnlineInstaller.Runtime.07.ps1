function Invoke-StandaloneFullUninstallActionCore {
    param(
        [Parameter(Mandatory)] [ValidateSet('uninstall', 'full-uninstall')] [string]$ResolvedAction,
        [Parameter(Mandatory)] [string]$ResolvedArchitecture,
        [Parameter(Mandatory)] [string]$ResolvedClient,
        [Parameter(Mandatory)] [AllowEmptyString()] [AllowNull()] [string]$ResolvedInstallRoot,
        [Parameter(Mandatory)] [string]$RequestedVersion,
        [switch]$UseLatestRelease,
        [switch]$InstallRootWasSpecified
    )

    $state = Get-StandaloneInstallerState
    $scopedInstallRoot = if ($InstallRootWasSpecified) {
        Normalize-StandaloneInstallerPath -PathValue (Assert-InstallerLocalPathTrusted -Path $ResolvedInstallRoot)
    }
    else {
        $null
    }
    $effectiveInstallRoot = if ([string]::IsNullOrWhiteSpace($scopedInstallRoot)) {
        Resolve-StandaloneRemovalInstallRoot -ResolvedInstallRoot $ResolvedInstallRoot -State $state
    }
    else {
        $scopedInstallRoot
    }

    $detectedInstallations = @(Get-StandaloneDetectedInstallerInstallations -State $state -ExpectedInstallRoot $effectiveInstallRoot)
        $detectedRegistrations = @(Get-StandaloneDetectedInstallerRegistrations -State $state)
        if (-not [string]::IsNullOrWhiteSpace($scopedInstallRoot)) {
            $detectedInstallations = @($detectedInstallations | Where-Object {
                    Test-StandaloneInstallerPathEquals -Left ([string]$_.InstallRoot) -Right $scopedInstallRoot
                })
            $detectedRegistrations = @($detectedRegistrations | Where-Object {
                    Test-StandaloneRegistrationMatchesInstallRoot -RegistrationRecord $_ -ExpectedInstallRoot $scopedInstallRoot
                })
        }
        $registrationMap = [ordered]@{}
        foreach ($registration in $detectedRegistrations) {
            $stateKey = Resolve-ClientStateKey -ClientId ([string]$registration.ClientId) -RegistrationMode ([string]$registration.RegistrationMode)
            $registrationMap[$stateKey] = $registration
        }

        foreach ($installation in $detectedInstallations) {
            foreach ($registration in @(Get-StandaloneManagedRegistrationsFromInstall -ResolvedInstallRoot ([string]$installation.InstallRoot) -ResolvedArchitecture ([string]$installation.Architecture))) {
                $stateKey = Resolve-ClientStateKey -ClientId ([string]$registration.ClientId) -RegistrationMode ([string]$registration.RegistrationMode)
                if (-not $registrationMap.Contains($stateKey)) {
                    $registrationMap[$stateKey] = $registration
                }
            }
        }

        $detectedRegistrations = @($registrationMap.Values)
        $registrationOperations = @()
        $installationBackups = @()
        $removedInstallations = @()
        $removedInstallRoots = @()
        $stateRestoreRequired = $false
        $statePath = Resolve-StandaloneInstallerStatePath -CreateRoot
        $hadOriginalStateFile = Test-Path -LiteralPath $statePath
        $originalStateJson = if ($hadOriginalStateFile) { Get-Content -LiteralPath $statePath -Raw } else { $null }
        try {
            foreach ($record in $detectedRegistrations) {
                $clientId = [string]$record.ClientId
                $rawRegistrationMode = Get-StandaloneRecordStringValue -Record $record -PropertyNames @('RegistrationMode', 'mode', 'Mode')
                $registrationMode = Get-StandaloneNormalizedRegistrationMode -RegistrationMode $rawRegistrationMode
                $targetPath = Get-StandaloneTrustedRecordedTarget -SelectedClient $clientId -RegistrationRecord $record
                $clientBaseId = Resolve-ClientBaseId -ClientId $clientId
                $operation = [ordered]@{
                    ClientId = $clientId
                    RegistrationMode = $registrationMode
                    TargetPath = $targetPath
                    BackupPath = $null
                    Applied = $false
                    InstalledExecutable = (Get-StandaloneRecordStringValue -Record $record -PropertyNames @('InstalledExecutable', 'installedExecutable'))
                }

                if ([string]::IsNullOrWhiteSpace($targetPath) -and [string]::Equals($registrationMode, 'json-file', [System.StringComparison]::OrdinalIgnoreCase)) {
                    if ($clientBaseId -eq 'cursor') {
                        $targetPath = Get-StandaloneTrustedCursorManifestTarget -SelectedClient $clientId -RegistrationRecord $record
                    }
                    else {
                        $targetPath = Get-StandaloneTrustedManagedJsonRegistrationTarget -SelectedClient $clientId -RegistrationRecord $record
                    }

                    $operation.TargetPath = $targetPath
                }

                if ([string]::Equals($registrationMode, 'artifact-only', [System.StringComparison]::OrdinalIgnoreCase) -and [string]::IsNullOrWhiteSpace($targetPath)) {
                    $artifactTargets = @(Get-StandaloneTrustedOtherRegistrationArtifactTargets -RegistrationRecord $record)
                    if ($artifactTargets.Count -gt 0) {
                        $targetPath = [string]$artifactTargets[0]
                        $operation.TargetPath = $targetPath
                    }
                }

                if ([string]::Equals($registrationMode, 'json-file', [System.StringComparison]::OrdinalIgnoreCase)) {
                    $collectionName = switch ($clientBaseId) {
                        'vscode' { 'servers' }
                        'visual-studio' { 'servers' }
                        'claude-desktop' { 'mcpServers' }
                        'cursor' { 'mcpServers' }
                        default { $null }
                    }

                    if (-not [string]::IsNullOrWhiteSpace($collectionName) -and -not [string]::IsNullOrWhiteSpace($targetPath)) {
                        $removal = Remove-StandaloneJsonConfigRegistration -CollectionName $collectionName -ConfigPath $targetPath
                        $operation.BackupPath = [string]$removal.backupPath
                        $operation.Applied = [bool]$removal.applied
                    }
                }
                elseif ([string]::Equals($registrationMode, 'artifact-only', [System.StringComparison]::OrdinalIgnoreCase)) {
                    $artifactTargets = @(Get-StandaloneTrustedOtherRegistrationArtifactTargets -RegistrationRecord $record)
                    foreach ($candidateTarget in @($artifactTargets + @($targetPath))) {
                        if ([string]::IsNullOrWhiteSpace([string]$candidateTarget)) {
                            continue
                        }

                        try {
                            $trustedCandidateTarget = Assert-InstallerLocalPathTrusted -Path ([string]$candidateTarget)
                        }
                        catch {
                            continue
                        }

                        if (-not (Test-Path -LiteralPath $trustedCandidateTarget)) {
                            continue
                        }

                        if ([string]::IsNullOrWhiteSpace([string]$operation.BackupPath)) {
                            $targetPath = $trustedCandidateTarget
                            $operation.TargetPath = $targetPath
                            $operation.BackupPath = Assert-InstallerLocalPathTrusted -Path "$targetPath.bak-$([guid]::NewGuid().ToString('N'))"
                            Copy-Item -LiteralPath $targetPath -Destination ([string]$operation.BackupPath) -Force
                        }

                        Remove-PathIfExists -Path $trustedCandidateTarget
                        $operation.Applied = $true
                    }
                }
                elseif ([string]::Equals($registrationMode, 'cli', [System.StringComparison]::OrdinalIgnoreCase)) {
                    $commandName = Resolve-StandaloneCliCommandName -ClientBaseId $clientBaseId
                    $resolvedCommandPath = Resolve-StandaloneExecutableCommandPath -Command $commandName
                    if (-not [string]::IsNullOrWhiteSpace($resolvedCommandPath)) {
                        & $resolvedCommandPath @(Get-StandaloneCliRemoveArguments -ClientBaseId $clientBaseId) | Out-Null
                        $operation.Applied = ($LASTEXITCODE -eq 0)
                    }
                }

                $registrationOperations += $operation
                $verification = Invoke-StandaloneUninstallVerification -SelectedClient $clientId -RegistrationRecord $record -RegistrationChanges @($registrationOperations | Where-Object { [string]$_.ClientId -eq $clientId })
                if (-not $verification.Succeeded) {
                    throw $verification.VerificationMessage
                }
            }

            foreach ($installation in $detectedInstallations) {
                $architecture = [string]$installation.Architecture
                $installRoot = [string]$installation.InstallRoot
                $installBase = [string]$installation.InstallBase
                try {
                    $trustedInstallBase = Assert-InstallerLocalPathTrusted -Path $installBase
                }
                catch {
                    continue
                }

                if (Test-Path -LiteralPath $trustedInstallBase) {
                    $installBase = $trustedInstallBase
                    $rollbackPath = Assert-InstallerLocalPathTrusted -Path "$installBase.rollback-$([guid]::NewGuid().ToString('N'))"
                    Move-StandalonePathWithRetry -SourcePath $installBase -DestinationPath $rollbackPath
                    $installationBackups += [ordered]@{
                        InstallBase = $installBase
                        RollbackPath = $rollbackPath
                    }
                    $removedInstallations += [ordered]@{
                        InstallRoot = $installRoot
                        Architecture = $architecture
                        InstallBase = $installBase
                        InstalledExecutable = [string]$installation.InstalledExecutable
                        ResolvedVersion = [string]$installation.ResolvedVersion
                        InstallerOwned = $true
                    }
                }
            }

            foreach ($installation in $removedInstallations) {
                $trustedInstallBase = $null
                try {
                    $trustedInstallBase = Assert-InstallerLocalPathTrusted -Path ([string]$installation.InstallBase)
                }
                catch {
                    $trustedInstallBase = $null
                }

                if (-not [string]::IsNullOrWhiteSpace($trustedInstallBase) -and (Test-Path -LiteralPath $trustedInstallBase)) {
                    throw "Installation root still exists: $([string]$installation.InstallBase)"
                }
            }

            if ([string]::IsNullOrWhiteSpace($scopedInstallRoot)) {
                $state.registrations.Clear()
                $state.architectures.Clear()
                $state.lastInstallRoot = $null
            }
            else {
                foreach ($key in @($state.registrations.Keys)) {
                    if (Test-StandaloneRegistrationMatchesInstallRoot -RegistrationRecord $state.registrations[$key] -ExpectedInstallRoot $scopedInstallRoot) {
                        $null = $state.registrations.Remove($key)
                    }
                }
                foreach ($key in @($state.architectures.Keys)) {
                    if (Test-StandaloneRegistrationMatchesInstallRoot -RegistrationRecord $state.architectures[$key] -ExpectedInstallRoot $scopedInstallRoot) {
                        $null = $state.architectures.Remove($key)
                    }
                }
                if (Test-StandaloneInstallerPathEquals -Left ([string]$state.lastInstallRoot) -Right $scopedInstallRoot) {
                    $remainingRoot = @($state.architectures.Values) + @($state.registrations.Values) |
                        ForEach-Object { Get-StandaloneRecordStringValue -Record $_ -PropertyNames @('installRoot', 'InstallRoot') } |
                        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
                        Select-Object -First 1
                    $state.lastInstallRoot = $remainingRoot
                }
            }
            $statePath = Save-StandaloneInstallerState -State $state
            $stateRestoreRequired = $true
            foreach ($operation in $registrationOperations) {
                Remove-PathIfExists -Path ([string]$operation.BackupPath)
            }

            foreach ($backup in $installationBackups) {
                Remove-PathIfExists -Path ([string]$backup.RollbackPath)
            }
            $removedInstallRoots = @(Remove-StandaloneInstallerOwnedEmptyInstallRoots -Installations $removedInstallations)
            $summary = Get-StandaloneFullUninstallResultSummary -RemovedInstallations $removedInstallations -RequestedVersion $RequestedVersion

            return [ordered]@{
                action = 'full-uninstall'
                mode = 'offline'
                downloadSource = 'none'
                version = $summary.version
                resolvedVersion = $summary.resolvedVersion
                architecture = 'all'
                client = 'all'
                packageAssetName = $null
                downloadUri = $null
                installRoot = if ($InstallRootWasSpecified) { $scopedInstallRoot } else { $summary.installRoot }
                installedExecutable = $null
                selectedClients = @()
                statePath = $statePath
                removedInstallation = ($removedInstallations.Count -gt 0)
                removedInstallations = @($removedInstallations)
                removedInstallRoots = @($removedInstallRoots)
                registrations = @($registrationOperations | Where-Object { [bool]$_.Applied })
                cleanupScope = if ($InstallRootWasSpecified) { 'explicit-install-root-registrations-and-server-locations' } else { 'registrations-and-installer-owned-server-locations' }
                cleanupGuidance = Get-StandaloneFullUninstallCleanupGuidance -InstallRootWasSpecified:$InstallRootWasSpecified
                verificationMessage = "Verified removal of $($registrationOperations.Count) registration(s) and $($removedInstallations.Count) installer-owned server location(s)."
                releaseChannel = $summary.releaseChannel
            }
        }
        catch {
            $backupsInReverse = @($installationBackups)
            [array]::Reverse($backupsInReverse)
            foreach ($backup in $backupsInReverse) {
                if (-not [string]::IsNullOrWhiteSpace([string]$backup.RollbackPath)) {
                    try {
                        $trustedRollbackPath = Assert-InstallerLocalPathTrusted -Path ([string]$backup.RollbackPath)
                        $trustedInstallBase = Assert-InstallerLocalPathTrusted -Path ([string]$backup.InstallBase)
                    }
                    catch {
                        continue
                    }

                    if (Test-Path -LiteralPath $trustedRollbackPath) {
                        Move-StandalonePathWithRetry -SourcePath $trustedRollbackPath -DestinationPath $trustedInstallBase
                    }
                }
            }

            $operationsInReverse = @($registrationOperations)
            [array]::Reverse($operationsInReverse)
            foreach ($operation in $operationsInReverse) {
                if (-not [bool]$operation.Applied) {
                    continue
                }

                if ([string]::Equals([string]$operation.RegistrationMode, 'json-file', [System.StringComparison]::OrdinalIgnoreCase)) {
                    if (-not [string]::IsNullOrWhiteSpace([string]$operation.BackupPath)) {
                        try {
                            $trustedBackupPath = Assert-InstallerLocalPathTrusted -Path ([string]$operation.BackupPath)
                            $trustedTargetPath = Assert-InstallerLocalPathTrusted -Path ([string]$operation.TargetPath) -RejectHardLinks
                        }
                        catch {
                            continue
                        }

                        if (Test-Path -LiteralPath $trustedBackupPath) {
                            Copy-Item -LiteralPath $trustedBackupPath -Destination $trustedTargetPath -Force
                            Remove-PathIfExists -Path $trustedBackupPath
                        }
                    }
                    continue
                }

                if ([string]::Equals([string]$operation.RegistrationMode, 'artifact-only', [System.StringComparison]::OrdinalIgnoreCase)) {
                    if (-not [string]::IsNullOrWhiteSpace([string]$operation.BackupPath)) {
                        try {
                            $trustedBackupPath = Assert-InstallerLocalPathTrusted -Path ([string]$operation.BackupPath)
                            $trustedTargetPath = Assert-InstallerLocalPathTrusted -Path ([string]$operation.TargetPath) -RejectHardLinks
                        }
                        catch {
                            continue
                        }

                        if (Test-Path -LiteralPath $trustedBackupPath) {
                            Copy-Item -LiteralPath $trustedBackupPath -Destination $trustedTargetPath -Force
                            Remove-PathIfExists -Path $trustedBackupPath
                        }
                    }
                    continue
                }

                if ([string]::Equals([string]$operation.RegistrationMode, 'cli', [System.StringComparison]::OrdinalIgnoreCase) -and -not [string]::IsNullOrWhiteSpace([string]$operation.InstalledExecutable)) {
                    $clientBaseId = Resolve-ClientBaseId -ClientId ([string]$operation.ClientId)
                    $commandName = Resolve-StandaloneCliCommandName -ClientBaseId $clientBaseId
                    $resolvedCommandPath = Resolve-StandaloneExecutableCommandPath -Command $commandName
                    if (-not [string]::IsNullOrWhiteSpace($resolvedCommandPath)) {
                        & $resolvedCommandPath @(Get-StandaloneCliAddArguments -ClientBaseId $clientBaseId -InstalledExecutable ([string]$operation.InstalledExecutable)) | Out-Null
                    }
                }
            }

            if ($stateRestoreRequired) {
                try {
                    if ($hadOriginalStateFile) {
                        $originalStateJson | Set-Content -LiteralPath $statePath -Encoding UTF8
                    }
                    else {
                        Remove-PathIfExists -Path $statePath
                    }
                }
                catch {
                    throw ($_.Exception.Message + ' Failed to restore standalone installer state after rollback. ' + $_.Exception.Message)
                }
            }

            throw
        }
}
