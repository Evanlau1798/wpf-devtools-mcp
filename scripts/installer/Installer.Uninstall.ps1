function Invoke-InstallerVerifiedRemoval {
    param(
        [Parameter(Mandatory)] [string]$RegistrationMode,
        [string]$InstalledExecutable,
        [Parameter(Mandatory)] [bool]$InstallerOwned
    )

    return [ordered]@{
        RegistrationMode = $RegistrationMode
        InstalledExecutable = $InstalledExecutable
        InstallerOwned = $InstallerOwned
    }
}

function Invoke-InstallerFullUninstallCore {
    param([Parameter(Mandatory)] $State)

    $detectedRegistrations = @(Get-DetectedInstallerRegistrations -State $State)
    $detectedInstallations = @(Get-DetectedInstallerInstallations -State $State)
    $unregistrationResults = @()
    $unregistrationOperations = @()
    $registrationBackups = @()
    $installationBackups = @()
    $removedInstallations = @()
    $stateRestoreRequired = $false

    try {
        foreach ($registration in $detectedRegistrations) {
            $trustedBackupTarget = $null
            $clientId = [string]$registration.ClientId
            $clientBaseId = Resolve-ClientBaseId -ClientId $clientId
            if ([string]::Equals([string]$registration.RegistrationMode, 'json-file', [System.StringComparison]::OrdinalIgnoreCase)) {
                if ($clientBaseId -eq 'cursor') {
                    $trustedBackupTarget = [string](Resolve-CursorRegistrationProfile -SelectedClient $clientId -RegistrationRecord $registration).ConfigPath
                }
                else {
                    $trustedBackupTarget = Get-TrustedRecordedRegistrationTarget -ClientBaseId $clientBaseId -RegistrationRecord $registration
                    if ([string]::IsNullOrWhiteSpace($trustedBackupTarget)) {
                        $trustedBackupTarget = switch ($clientBaseId) {
                            'vscode' { Resolve-VsCodeConfigPath }
                            'visual-studio' { Resolve-VisualStudioConfigPath }
                            'claude-desktop' { Resolve-ClaudeDesktopConfigPath }
                            default { $null }
                        }
                    }
                }
            }
            elseif ([string]::Equals([string]$registration.RegistrationMode, 'artifact-only', [System.StringComparison]::OrdinalIgnoreCase)) {
                $trustedBackupTarget = Resolve-TrustedOtherRegistrationArtifactPath -RegistrationRecord $registration
            }

            if (-not [string]::IsNullOrWhiteSpace($trustedBackupTarget) -and (Test-Path -LiteralPath $trustedBackupTarget)) {
                $backupPath = "{0}.rollback-{1}" -f $trustedBackupTarget, ([guid]::NewGuid().ToString('N'))
                Copy-Item -LiteralPath $trustedBackupTarget -Destination $backupPath -Force
                $registrationBackups += [ordered]@{
                    TargetPath = $trustedBackupTarget
                    BackupPath = $backupPath
                }
            }

            $results = @(Invoke-ClientUnregistration -SelectedClient ([string]$registration.ClientId) -RegistrationRecord $registration)
            $unregistrationOperations += [pscustomobject][ordered]@{
                ClientId = [string]$registration.ClientId
                RegistrationRecord = $registration
                Registrations = @($results)
            }
            $unregistrationResults += @($results)
        }

        $verificationFailures = @()
        foreach ($registration in $detectedRegistrations) {
            $registrationChanges = @($unregistrationOperations | Where-Object { [string]$_.ClientId -eq [string]$registration.ClientId } | Select-Object -ExpandProperty Registrations)
            $verification = Invoke-UninstallVerification -SelectedClient ([string]$registration.ClientId) -RegistrationRecord $registration -RegistrationChanges $registrationChanges
            if (-not $verification.Succeeded) {
                $verificationFailures += [string]$verification.VerificationMessage
            }
        }

        if ($verificationFailures.Count -gt 0) {
            throw ($verificationFailures -join ' ')
        }

        foreach ($installation in $detectedInstallations) {
            if (-not [bool]$installation.InstallerOwned) {
                continue
            }

            $installBase = [string]$installation.InstallBase
            if ([string]::IsNullOrWhiteSpace($installBase) -or -not (Test-Path -LiteralPath $installBase)) {
                continue
            }

            $rollbackPath = "$installBase.rollback-$([guid]::NewGuid().ToString('N'))"
            Move-Item -LiteralPath $installBase -Destination $rollbackPath -Force
            $installationBackups += [ordered]@{
                Installation = $installation
                RollbackPath = $rollbackPath
            }
            $removedInstallations += $installation
        }

        $verificationFailures = @()
        foreach ($installation in $removedInstallations) {
            if (Test-Path -LiteralPath ([string]$installation.InstallBase)) {
                $verificationFailures += "Installation root still exists: $([string]$installation.InstallBase)"
            }
            if (-not [string]::IsNullOrWhiteSpace([string]$installation.InstalledExecutable) -and (Test-Path -LiteralPath ([string]$installation.InstalledExecutable))) {
                $verificationFailures += "Executable still exists: $([string]$installation.InstalledExecutable)"
            }
        }

        if ($verificationFailures.Count -gt 0) {
            throw ($verificationFailures -join ' ')
        }

        $newState = Get-EmptyInstallerState
        $newState.lastInstallRoot = $State.lastInstallRoot
        $statePath = Save-InstallerState -State $newState
        $stateRestoreRequired = $true
        foreach ($registrationBackup in $registrationBackups) {
            Remove-PathIfExists -Path ([string]$registrationBackup.BackupPath)
        }

        foreach ($backup in $installationBackups) {
            Remove-PathIfExists -Path ([string]$backup.RollbackPath)
        }
        return [ordered]@{
            action = 'full-uninstall'
            client = 'all'
            statePath = $statePath
            removedInstallation = ($removedInstallations.Count -gt 0)
            removedInstallations = @($removedInstallations)
            registrations = @($unregistrationResults)
            verificationMessage = "Verified removal of $($detectedRegistrations.Count) registration(s) and $($removedInstallations.Count) installer-owned server location(s)."
        }
    }
    catch {
        $rollbackErrors = New-Object System.Collections.Generic.List[string]
        $registrationBackupsInReverse = @($registrationBackups)
        [array]::Reverse($registrationBackupsInReverse)
        foreach ($registrationBackup in $registrationBackupsInReverse) {
            $targetPath = [string]$registrationBackup.TargetPath
            $backupPath = [string]$registrationBackup.BackupPath
            if ([string]::IsNullOrWhiteSpace($targetPath) -or [string]::IsNullOrWhiteSpace($backupPath)) {
                continue
            }

            if (Test-Path -LiteralPath $backupPath) {
                try {
                    if (Test-Path -LiteralPath $targetPath) {
                        (Get-Item -LiteralPath $targetPath).Attributes = [System.IO.FileAttributes]::Normal
                    }

                    Copy-Item -LiteralPath $backupPath -Destination $targetPath -Force
                    Remove-PathIfExists -Path $backupPath
                }
                catch {
                    $rollbackErrors.Add("Failed to restore registration backup '$targetPath'. $($_.Exception.Message)")
                }
            }
        }

        $backupsInReverse = @($installationBackups)
        [array]::Reverse($backupsInReverse)
        foreach ($backup in $backupsInReverse) {
            $rollbackPath = [string]$backup.RollbackPath
            $installBase = [string]$backup.Installation.InstallBase
            if ([string]::IsNullOrWhiteSpace($rollbackPath) -or [string]::IsNullOrWhiteSpace($installBase)) {
                continue
            }

            if (Test-Path -LiteralPath $rollbackPath) {
                try {
                    Move-Item -LiteralPath $rollbackPath -Destination $installBase -Force
                }
                catch {
                    $rollbackErrors.Add("Failed to restore installation root '$installBase'. $($_.Exception.Message)")
                }
            }
        }

        $operationsInReverse = @($unregistrationOperations)
        [array]::Reverse($operationsInReverse)
        foreach ($operation in $operationsInReverse) {
            $registrationRecord = $operation.RegistrationRecord
            $registrationRollbackItems = @($operation.Registrations)
            [array]::Reverse($registrationRollbackItems)
            foreach ($registration in $registrationRollbackItems) {
                if ($null -eq $registration) {
                    continue
                }

                if ([string]::Equals([string]$registration.mode, 'json-file', [System.StringComparison]::OrdinalIgnoreCase)) {
                    $backupPath = [string]$registration.backupPath
                    $targetPath = [string]$registration.target
                    if (-not [string]::IsNullOrWhiteSpace($backupPath) -and -not [string]::IsNullOrWhiteSpace($targetPath) -and (Test-Path -LiteralPath $backupPath)) {
                        try {
                            Copy-Item -LiteralPath $backupPath -Destination $targetPath -Force
                            Remove-PathIfExists -Path $backupPath
                        }
                        catch {
                            $rollbackErrors.Add("Failed to restore client registration artifact '$targetPath'. $($_.Exception.Message)")
                        }
                    }
                }
            }

            $installBase = $null
            if (-not [string]::IsNullOrWhiteSpace([string]$registrationRecord.InstallRoot) -and -not [string]::IsNullOrWhiteSpace([string]$registrationRecord.Architecture)) {
                $installBase = Resolve-InstallBasePath -ResolvedInstallRoot ([string]$registrationRecord.InstallRoot) -ResolvedArchitecture ([string]$registrationRecord.Architecture)
            }

            try {
                Undo-ClientRegistrationChanges `
                    -SelectedClient ([string]$operation.ClientId) `
                    -Registrations @($operation.Registrations) `
                    -RollbackMode 'uninstall' `
                    -InstalledExecutable ([string]$registrationRecord.InstalledExecutable) `
                    -InstallBase $installBase `
                    -RegistrationRecord $registrationRecord
            }
            catch {
                $rollbackErrors.Add("Failed to restore registration command state for '$([string]$operation.ClientId)'. $($_.Exception.Message)")
            }
        }

        if ($stateRestoreRequired) {
            try {
                [void](Save-InstallerState -State $State)
            }
            catch {
                $rollbackErrors.Add("Failed to restore installer state after full-uninstall rollback. $($_.Exception.Message)")
            }
        }

        if ($rollbackErrors.Count -gt 0) {
            throw ($_.Exception.Message + ' Rollback issues: ' + ($rollbackErrors -join ' '))
        }

        throw
    }
}

function Get-StandaloneDetectedInstallerRegistrations {
    param([Parameter(Mandatory)] $State)

    $registrationMap = [ordered]@{}
    foreach ($entry in $State.registrations.GetEnumerator()) {
        $record = $entry.Value
        $installedExecutable = if ($record.Contains('installedExecutable')) { [string]$record.installedExecutable } else { [string]$record.InstalledExecutable }
        $installRoot = if ($record.Contains('installRoot')) { [string]$record.installRoot } else { [string]$record.InstallRoot }
        $architecture = if ($record.Contains('architecture')) { [string]$record.architecture } else { [string]$record.Architecture }
        $resolvedVersion = if ($record.Contains('resolvedVersion')) { [string]$record.resolvedVersion } else { [string]$record.ResolvedVersion }
        $registrationMode = if ($record.Contains('mode')) { [string]$record.mode } else { [string]$record.Mode }
        $registrationTarget = if ($record.Contains('target')) { [string]$record.target } else { [string]$record.Target }
        $installerOwned = $false

        if (-not [string]::IsNullOrWhiteSpace($installedExecutable)) {
            $ownership = Resolve-InstallerOwnershipFromExecutable -InstalledExecutable $installedExecutable
            $installerOwned = [bool]$ownership.InstallerOwned
            if ([string]::IsNullOrWhiteSpace($installRoot)) {
                $installRoot = [string]$ownership.InstallRoot
            }

            if ([string]::IsNullOrWhiteSpace($architecture)) {
                $architecture = [string]$ownership.Architecture
            }

            if ([string]::IsNullOrWhiteSpace($resolvedVersion)) {
                $resolvedVersion = [string]$ownership.ResolvedVersion
            }
        }

        $registrationMap[[string]$entry.Key] = [ordered]@{
                ClientId = [string]$entry.Key
                RegistrationMode = $registrationMode
                RegistrationTarget = $registrationTarget
                InstalledExecutable = $installedExecutable
                InstallRoot = $installRoot
                Architecture = $architecture
                InstallerOwned = $installerOwned
                ResolvedVersion = $resolvedVersion
            }
    }

    foreach ($registration in @(Get-StandaloneDetectedConfigRegistrations)) {
        $stateKey = Resolve-ClientStateKey `
            -ClientId ([string]$registration.ClientId) `
            -RegistrationMode ([string]$registration.RegistrationMode)
        if ($registrationMap.Contains($stateKey)) {
            $existing = $registrationMap[$stateKey]
            $clientBaseId = Resolve-ClientBaseId -ClientId ([string]$registration.ClientId)
            $collectionName = switch ($clientBaseId) {
                'vscode' { 'servers' }
                'visual-studio' { 'servers' }
                'claude-desktop' { 'mcpServers' }
                'cursor' { 'mcpServers' }
                default { $null }
            }
            $existingTarget = [string]$existing.RegistrationTarget
            $liveTarget = [string]$registration.RegistrationTarget
            $existingTargetHasRegistration = $false
            $liveTargetHasRegistration = $false
            if (-not [string]::IsNullOrWhiteSpace($collectionName)) {
                if (-not [string]::IsNullOrWhiteSpace($existingTarget)) {
                    $existingTargetHasRegistration = Test-JsonConfigRegistration -CollectionName $collectionName -ConfigPath $existingTarget
                }

                if (-not [string]::IsNullOrWhiteSpace($liveTarget)) {
                    $liveTargetHasRegistration = Test-JsonConfigRegistration -CollectionName $collectionName -ConfigPath $liveTarget
                }
            }

            if ([string]::IsNullOrWhiteSpace([string]$existing.RegistrationTarget)) {
                $existing.RegistrationTarget = [string]$registration.RegistrationTarget
            }
            elseif ($liveTargetHasRegistration -and -not $existingTargetHasRegistration) {
                $existing.RegistrationTarget = $liveTarget
                $existing.RegistrationMode = [string]$registration.RegistrationMode
                $existing.InstalledExecutable = [string]$registration.InstalledExecutable
            }
            if ([string]::IsNullOrWhiteSpace([string]$existing.InstalledExecutable)) {
                $existing.InstalledExecutable = [string]$registration.InstalledExecutable
            }
            if (-not [bool]$existing.InstallerOwned -and [bool]$registration.InstallerOwned) {
                $existing.InstallerOwned = $true
                $existing.InstallRoot = [string]$registration.InstallRoot
                $existing.Architecture = [string]$registration.Architecture
                $existing.ResolvedVersion = [string]$registration.ResolvedVersion
            }
            continue
        }

        $registrationMap[$stateKey] = $registration
    }

    return @($registrationMap.Values)
}

function Get-JsonConfigRegisteredExecutable {
    param(
        [Parameter(Mandatory)] [string]$CollectionName,
        [AllowEmptyString()] [string]$ConfigPath
    )

    if ([string]::IsNullOrWhiteSpace($ConfigPath) -or -not (Test-Path $ConfigPath)) {
        return $null
    }

    $root = Get-ExistingConfigMap -Path $ConfigPath
    $servers = Get-ConfigCollectionMap -Root $root -CollectionName $CollectionName
    if (-not $servers.Contains('wpf-devtools')) {
        return $null
    }

    return [string]$servers['wpf-devtools'].command
}

function Get-StandaloneDetectedConfigRegistrations {
    $registrations = @()
    foreach ($candidate in @(
            [ordered]@{
                ClientId = 'vscode'
                RegistrationMode = 'json-file'
                RegistrationTarget = (Resolve-VsCodeConfigPath)
                CollectionName = 'servers'
            }
            [ordered]@{
                ClientId = 'visual-studio'
                RegistrationMode = 'json-file'
                RegistrationTarget = (Resolve-VisualStudioConfigPath)
                CollectionName = 'servers'
            }
            [ordered]@{
                ClientId = 'claude-desktop'
                RegistrationMode = 'json-file'
                RegistrationTarget = (Resolve-ClaudeDesktopConfigPath)
                CollectionName = 'mcpServers'
            }
            [ordered]@{
                ClientId = 'cursor-global'
                RegistrationMode = 'cursor-global'
                RegistrationTarget = (Resolve-CursorGlobalConfigPath)
                CollectionName = 'mcpServers'
            }
            [ordered]@{
                ClientId = 'cursor-project'
                RegistrationMode = 'cursor-project'
                RegistrationTarget = (Resolve-CursorProjectConfigPath)
                CollectionName = 'mcpServers'
            }
        )) {
        $registrationTarget = [string]$candidate.RegistrationTarget
        $installedExecutable = Get-JsonConfigRegisteredExecutable `
            -CollectionName ([string]$candidate.CollectionName) `
            -ConfigPath $registrationTarget
        if ([string]::IsNullOrWhiteSpace($installedExecutable)) {
            continue
        }

        $ownership = Resolve-InstallerOwnershipFromExecutable -InstalledExecutable $installedExecutable
        $registrations += ,([ordered]@{
                ClientId = [string]$candidate.ClientId
                RegistrationMode = [string]$candidate.RegistrationMode
                RegistrationTarget = $registrationTarget
                InstalledExecutable = $installedExecutable
                InstallRoot = [string]$ownership.InstallRoot
                Architecture = [string]$ownership.Architecture
                InstallerOwned = [bool]$ownership.InstallerOwned
                ResolvedVersion = [string]$ownership.ResolvedVersion
            })
    }

    return $registrations
}

function Get-StandaloneDetectedInstallerRegistrationMap {
    param([Parameter(Mandatory)] $State)

    $registrationMap = [ordered]@{}
    foreach ($registration in @(Get-StandaloneDetectedInstallerRegistrations -State $State)) {
        $stateKey = Resolve-ClientStateKey `
            -ClientId ([string]$registration.ClientId) `
            -RegistrationMode ([string]$registration.RegistrationMode)
        $registrationMap[$stateKey] = $registration
        if (-not $registrationMap.Contains([string]$registration.ClientId)) {
            $registrationMap[[string]$registration.ClientId] = $registration
        }
    }

    return $registrationMap
}

function Get-StandaloneDetectedInstallerInstallations {
    param([Parameter(Mandatory)] $State)

    $installations = [ordered]@{}
    foreach ($registration in @(Get-StandaloneDetectedInstallerRegistrations -State $State)) {
        if (-not [bool]$registration.InstallerOwned) {
            continue
        }

        if ([string]::IsNullOrWhiteSpace([string]$registration.InstallRoot) -or [string]::IsNullOrWhiteSpace([string]$registration.Architecture)) {
            continue
        }

        $key = "{0}|{1}" -f ([string]$registration.InstallRoot).ToLowerInvariant(), ([string]$registration.Architecture).ToLowerInvariant()
        $installations[$key] = [ordered]@{
            InstallRoot = [string]$registration.InstallRoot
            Architecture = [string]$registration.Architecture
            InstallBase = Resolve-InstallBasePath -ResolvedInstallRoot ([string]$registration.InstallRoot) -ResolvedArchitecture ([string]$registration.Architecture)
            InstalledExecutable = [string]$registration.InstalledExecutable
            ResolvedVersion = [string]$registration.ResolvedVersion
            InstallerOwned = $true
        }
    }

    foreach ($architectureEntry in $State.architectures.GetEnumerator()) {
        $arch = [string]$architectureEntry.Key
        $record = $architectureEntry.Value
        $executable = [string]$record.executable
        if ([string]::IsNullOrWhiteSpace($executable)) {
            continue
        }

        $ownership = Resolve-InstallerOwnershipFromExecutable -InstalledExecutable $executable
        if (-not [bool]$ownership.InstallerOwned) {
            continue
        }

        $installRoot = [string]$record.installRoot
        if ([string]::IsNullOrWhiteSpace($installRoot)) {
            $installRoot = [string]$ownership.InstallRoot
        }

        if ([string]::IsNullOrWhiteSpace($installRoot)) {
            continue
        }

        $key = "{0}|{1}" -f $installRoot.ToLowerInvariant(), $arch.ToLowerInvariant()
        $installations[$key] = [ordered]@{
            InstallRoot = $installRoot
            Architecture = $arch
            InstallBase = Resolve-InstallBasePath -ResolvedInstallRoot $installRoot -ResolvedArchitecture $arch
            InstalledExecutable = $executable
            ResolvedVersion = [string]$record.version
            InstallerOwned = $true
        }
    }

    return @($installations.Values)
}

function Invoke-StandaloneFullUninstall {
    param([Parameter(Mandatory)] $State)

    $detectedRegistrations = @(Get-StandaloneDetectedInstallerRegistrations -State $State)
    $detectedInstallations = @(Get-StandaloneDetectedInstallerInstallations -State $State)
    $unregistrationResults = @()
    $unregistrationOperations = @()

    foreach ($registration in $detectedRegistrations) {
        $results = @(Invoke-ClientUnregistration -SelectedClient ([string]$registration.ClientId) -RegistrationRecord $registration)
        $unregistrationResults += @($results)
        $unregistrationOperations += [pscustomobject][ordered]@{
            ClientId = [string]$registration.ClientId
            Registrations = @($results)
        }
    }

    $verificationFailures = @()
    foreach ($registration in $detectedRegistrations) {
        $registrationChanges = @($unregistrationOperations | Where-Object { [string]$_.ClientId -eq [string]$registration.ClientId } | Select-Object -ExpandProperty Registrations)
        $verification = Invoke-UninstallVerification -SelectedClient ([string]$registration.ClientId) -RegistrationRecord $registration -RegistrationChanges $registrationChanges
        if (-not $verification.Succeeded) {
            $verificationFailures += [string]$verification.VerificationMessage
        }
    }

    $removedInstallations = @()
    foreach ($installation in $detectedInstallations) {
        if (-not [bool]$installation.InstallerOwned) {
            continue
        }

        Remove-PathIfExists -Path ([string]$installation.InstallBase)
        $removedInstallations += $installation
    }

    foreach ($installation in $removedInstallations) {
        if (Test-Path ([string]$installation.InstallBase)) {
            $verificationFailures += "Installation root still exists: $([string]$installation.InstallBase)"
        }
        if (-not [string]::IsNullOrWhiteSpace([string]$installation.InstalledExecutable) -and (Test-Path ([string]$installation.InstalledExecutable))) {
            $verificationFailures += "Executable still exists: $([string]$installation.InstalledExecutable)"
        }
    }

    if ($verificationFailures.Count -gt 0) {
        throw ($verificationFailures -join ' ')
    }

    $newState = Get-EmptyInstallerState
    $newState.lastInstallRoot = $State.lastInstallRoot
    $statePath = Save-InstallerState -State $newState
    return [ordered]@{
        statePath = $statePath
        removedInstallation = ($removedInstallations.Count -gt 0)
        removedInstallations = @($removedInstallations)
        registrations = @($unregistrationResults)
        verificationMessage = "Verified removal of $($detectedRegistrations.Count) registration(s) and $($removedInstallations.Count) installer-owned server location(s)."
    }
}
