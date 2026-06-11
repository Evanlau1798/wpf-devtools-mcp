if ($null -eq (Get-Command Invoke-StandaloneFullUninstall -ErrorAction SilentlyContinue)) {
    . (Join-Path $PSScriptRoot 'Installer.Uninstall.Standalone.ps1')
}

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
            elseif ([string]::Equals([string]$registration.RegistrationMode, 'manual-cli-artifact', [System.StringComparison]::OrdinalIgnoreCase)) {
                $trustedBackupTarget = Get-TrustedRecordedRegistrationTarget -ClientBaseId $clientBaseId -RegistrationRecord $registration
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
            Move-InstallerPathWithRetry -SourcePath $installBase -DestinationPath $rollbackPath
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

        $removedRuntimeScreenshotCache = Remove-InstallerRuntimeScreenshotCache
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
            removedRuntimeScreenshotCache = $removedRuntimeScreenshotCache
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
                    Move-InstallerPathWithRetry -SourcePath $rollbackPath -DestinationPath $installBase
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
