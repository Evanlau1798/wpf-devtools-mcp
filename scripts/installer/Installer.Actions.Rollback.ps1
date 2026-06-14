function Update-ClientRegistrationArtifactsTransactional {
    param(
        [Parameter(Mandatory)] [string]$InstallBase,
        [Parameter(Mandatory)] [string]$InstalledExecutable,
        [switch]$RestoreOnError
    )

    $registrationArtifactsDir = Join-Path $InstallBase 'client-registration'
    $rollbackBackupRegistrationDir = $null
    if (Test-Path $registrationArtifactsDir) {
        $rollbackBackupRegistrationDir = "$registrationArtifactsDir.rollback-$([guid]::NewGuid().ToString('N'))"
        Move-InstallerPathWithRetry -SourcePath $registrationArtifactsDir -DestinationPath $rollbackBackupRegistrationDir
    }

    try {
        New-ClientRegistrationArtifacts -InstallBase $InstallBase -InstalledExecutable $InstalledExecutable
        return $rollbackBackupRegistrationDir
    }
    catch {
        if ($RestoreOnError) {
            Restore-InstallerBackupDirectory -BackupPath $rollbackBackupRegistrationDir -TargetPath $registrationArtifactsDir -RemoveTargetWhenNoBackup
        }

        throw
    }
}

function Restore-InstallerBackupFile {
    param(
        [string]$BackupPath,
        [string]$TargetPath
    )

    if ([string]::IsNullOrWhiteSpace($BackupPath) -or [string]::IsNullOrWhiteSpace($TargetPath)) {
        return
    }

    try {
        $trustedBackupPath = Assert-InstallerLocalPathTrusted -Path $BackupPath
        $trustedTargetPath = Assert-InstallerLocalPathTrusted -Path $TargetPath
    }
    catch {
        return
    }

    if (-not (Test-Path -LiteralPath $trustedBackupPath)) {
        return
    }

    Remove-PathIfExists -Path $trustedTargetPath
    $targetDirectory = Split-Path -Parent $trustedTargetPath
    if (-not [string]::IsNullOrWhiteSpace($targetDirectory)) {
        New-Item -ItemType Directory -Force -Path $targetDirectory | Out-Null
        Assert-InstallerLocalPathTrusted -Path $targetDirectory | Out-Null
    }

    Move-InstallerPathWithRetry -SourcePath $trustedBackupPath -DestinationPath $trustedTargetPath
}

function Restore-InstallerBackupDirectory {
    param(
        [string]$BackupPath,
        [string]$TargetPath,
        [switch]$RemoveTargetWhenNoBackup
    )

    if ([string]::IsNullOrWhiteSpace($TargetPath)) {
        return
    }

    $trustedTargetPath = $null
    try {
        $trustedTargetPath = Assert-InstallerLocalPathTrusted -Path $TargetPath
    }
    catch {
        return
    }

    if (-not [string]::IsNullOrWhiteSpace($BackupPath)) {
        try {
            $trustedBackupPath = Assert-InstallerLocalPathTrusted -Path $BackupPath
        }
        catch {
            return
        }

        if (-not (Test-Path -LiteralPath $trustedBackupPath)) {
            if ($RemoveTargetWhenNoBackup) {
                Remove-PathIfExists -Path $trustedTargetPath
            }

            return
        }

        Remove-PathIfExists -Path $trustedTargetPath
        $targetParent = Split-Path -Parent $trustedTargetPath
        if (-not [string]::IsNullOrWhiteSpace($targetParent)) {
            New-Item -ItemType Directory -Force -Path $targetParent | Out-Null
            Assert-InstallerLocalPathTrusted -Path $targetParent | Out-Null
        }

        Move-InstallerPathWithRetry -SourcePath $trustedBackupPath -DestinationPath $trustedTargetPath
        return
    }

    if ($RemoveTargetWhenNoBackup) {
        Remove-PathIfExists -Path $trustedTargetPath
    }
}

function Undo-InstalledPayload {
    param($InstallResult)

    if ($null -eq $InstallResult) {
        return
    }

    $registrationArtifactsPath = if (-not [string]::IsNullOrWhiteSpace([string]$InstallResult.installBase)) {
        Join-Path ([string]$InstallResult.installBase) 'client-registration'
    }
    else {
        $null
    }

    if ([bool]$InstallResult.reusedExistingBinary) {
        Restore-InstallerBackupDirectory -BackupPath ([string]$InstallResult.rollbackBackupRegistrationDir) -TargetPath $registrationArtifactsPath -RemoveTargetWhenNoBackup
        Restore-InstallerBackupFile -BackupPath ([string]$InstallResult.rollbackBackupManifestPath) -TargetPath ([string]$InstallResult.installManifestPath)
        return
    }

    Remove-PathIfExists -Path ([string]$InstallResult.currentDir)
    Remove-PathIfExists -Path ([string]$InstallResult.installManifestPath)
    Restore-InstallerBackupDirectory -BackupPath ([string]$InstallResult.rollbackBackupRegistrationDir) -TargetPath $registrationArtifactsPath -RemoveTargetWhenNoBackup
    Restore-InstallerBackupDirectory -BackupPath ([string]$InstallResult.rollbackBackupCurrentDir) -TargetPath ([string]$InstallResult.currentDir)
    Restore-InstallerBackupFile -BackupPath ([string]$InstallResult.rollbackBackupManifestPath) -TargetPath ([string]$InstallResult.installManifestPath)
}

function Complete-InstalledPayloadCommit {
    param($InstallResult)

    if ($null -eq $InstallResult) {
        return
    }

    Remove-PathIfExists -Path ([string]$InstallResult.rollbackBackupRegistrationDir)
    Remove-PathIfExists -Path ([string]$InstallResult.rollbackBackupManifestPath)

    if ([bool]$InstallResult.reusedExistingBinary) {
        return
    }

    Remove-PathIfExists -Path ([string]$InstallResult.rollbackBackupCurrentDir)
}

function Update-InstalledManifestManagedRegistrationTarget {
    param(
        [Parameter(Mandatory)] [string]$InstallBase,
        [Parameter(Mandatory)] [string]$SelectedClient,
        [Parameter(Mandatory)] $Registration
    )

    $registrationMode = [string]$Registration.mode
    $registrationTarget = [string]$Registration.target
    if ([string]::IsNullOrWhiteSpace($registrationTarget) -or
        (-not [string]::Equals($registrationMode, 'json-file', [System.StringComparison]::OrdinalIgnoreCase) -and
         -not $registrationMode.StartsWith('cursor-', [System.StringComparison]::OrdinalIgnoreCase))) {
        return
    }

    try {
        $manifestPath = Assert-InstallerLocalPathTrusted -Path (Join-Path $InstallBase 'install-manifest.json')
    }
    catch {
        return
    }

    if (-not (Test-Path -LiteralPath $manifestPath)) {
        return
    }

    $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    $managedRegistrationTargets = [ordered]@{}
    $existingTargets = $manifest.PSObject.Properties['managedRegistrationTargets']
    if ($null -ne $existingTargets -and $null -ne $existingTargets.Value) {
        foreach ($property in $existingTargets.Value.PSObject.Properties) {
            if (-not [string]::IsNullOrWhiteSpace([string]$property.Value)) {
                $managedRegistrationTargets[$property.Name] = [string]$property.Value
            }
        }
    }

    $stateKey = Resolve-ClientStateKey -ClientId $SelectedClient -RegistrationMode $registrationMode
    $managedRegistrationTargets[$stateKey] = $registrationTarget

    $updatedManifest = [ordered]@{}
    foreach ($property in $manifest.PSObject.Properties) {
        if ($property.Name -eq 'managedRegistrationTargets') {
            continue
        }

        $updatedManifest[$property.Name] = $property.Value
    }

    $updatedManifest.managedRegistrationTargets = $managedRegistrationTargets

    $tempManifestPath = "$manifestPath.tmp-$([guid]::NewGuid().ToString('N'))"
    try {
        Write-InstallerUtf8NoBomFile -Path $tempManifestPath -Content ($updatedManifest | ConvertTo-Json -Depth 10)
        Move-InstallerPathWithRetry -SourcePath $tempManifestPath -DestinationPath $manifestPath
    }
    finally {
        if (Test-Path $tempManifestPath) {
            Remove-Item -Path $tempManifestPath -Force
        }
    }
}

function Restore-RegistrationArtifact {
    param(
        $Registration,
        [switch]$RemoveTargetWhenNoBackup
    )

    if ($null -eq $Registration) {
        return
    }

    $targetPath = [string]$Registration.target
    $backupPath = [string]$Registration.backupPath
    if (-not [string]::Equals([string]$Registration.mode, 'json-file', [System.StringComparison]::OrdinalIgnoreCase) -and
        -not [string]::Equals([string]$Registration.mode, 'artifact-only', [System.StringComparison]::OrdinalIgnoreCase) -and
        -not [string]::Equals([string]$Registration.mode, 'manual-cli-artifact', [System.StringComparison]::OrdinalIgnoreCase)) {
        return
    }

    if (-not [string]::IsNullOrWhiteSpace($backupPath)) {
        try {
            $trustedBackupPath = Assert-InstallerLocalPathTrusted -Path $backupPath
            $trustedTargetPath = Assert-InstallerLocalPathTrusted -Path $targetPath
        }
        catch {
            return
        }

        if (Test-Path -LiteralPath $trustedBackupPath) {
            Copy-Item -LiteralPath $trustedBackupPath -Destination $trustedTargetPath -Force
            Remove-PathIfExists -Path $trustedBackupPath
            return
        }
    }

    if ($RemoveTargetWhenNoBackup -and -not [string]::IsNullOrWhiteSpace($targetPath)) {
        try {
            $trustedTargetPath = Assert-InstallerLocalPathTrusted -Path $targetPath
        }
        catch {
            return
        }

        Remove-PathIfExists -Path $trustedTargetPath
        return
    }
}

function Undo-ClientRegistrationChanges {
    param(
        [Parameter(Mandatory)] [string]$SelectedClient,
        [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]]$Registrations,
        [Parameter(Mandatory)] [ValidateSet('install', 'uninstall')] [string]$RollbackMode,
        [string]$InstalledExecutable,
        [string]$InstallBase,
        $RegistrationRecord
    )

    $registrationList = @($Registrations)
    [array]::Reverse($registrationList)
    foreach ($registration in $registrationList) {
        if ($null -eq $registration -or -not [bool]$registration.applied) {
            continue
        }

        $mode = [string]$registration.mode
        if ([string]::Equals($mode, 'json-file', [System.StringComparison]::OrdinalIgnoreCase) -or
            [string]::Equals($mode, 'artifact-only', [System.StringComparison]::OrdinalIgnoreCase) -or
            [string]::Equals($mode, 'manual-cli-artifact', [System.StringComparison]::OrdinalIgnoreCase)) {
            Restore-RegistrationArtifact -Registration $registration -RemoveTargetWhenNoBackup:($RollbackMode -eq 'install')
            continue
        }

        if (-not [string]::Equals($mode, 'cli', [System.StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        if ($RollbackMode -eq 'install') {
            [void](Invoke-ClientUnregistration -SelectedClient $SelectedClient -RegistrationRecord $RegistrationRecord)
            continue
        }

        if (-not [string]::IsNullOrWhiteSpace($InstalledExecutable) -and -not [string]::IsNullOrWhiteSpace($InstallBase)) {
            [void](Invoke-ClientRegistration -SelectedClient $SelectedClient -InstalledExecutable $InstalledExecutable -InstallBase $InstallBase)
        }
    }
}
