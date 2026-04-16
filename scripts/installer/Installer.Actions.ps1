function Install-PackagePayload {
    param(
        [Parameter(Mandatory)] [string]$PackageDirectory,
        [Parameter(Mandatory)] [psobject]$PackageManifest,
        [Parameter(Mandatory)] [string]$ResolvedArchitecture,
        [Parameter(Mandatory)] [string]$ResolvedInstallRoot,
        [Parameter(Mandatory)] [string]$ResolvedVersion
    )

    $packageExecutable = Resolve-PackageExecutable -PackageDirectory $PackageDirectory -ResolvedArchitecture $ResolvedArchitecture
    Assert-PackagePayloadIntegrity -PackageDirectory $PackageDirectory -PackageManifest $PackageManifest

    $installRootFullPath = Resolve-AbsoluteDirectory -Path $ResolvedInstallRoot
    $installBase = Join-Path $installRootFullPath $ResolvedArchitecture
    $currentDir = Join-Path $installBase 'current'
    $installManifestPath = Join-Path $installBase 'install-manifest.json'
    $registrationArtifactsDir = Join-Path $installBase 'client-registration'
    $reusedExistingBinary = $false
    $rollbackBackupCurrentDir = $null
    $rollbackBackupManifestPath = $null
    $rollbackBackupRegistrationDir = $null

    if ((Test-Path $installManifestPath) -and -not $Force) {
        $existingManifest = Get-Content -Path $installManifestPath -Raw | ConvertFrom-Json
        $existingExecutable = [string]$existingManifest.executable
        $existingBinaryLooksOwned = $false
        if (-not [string]::IsNullOrWhiteSpace($existingExecutable) -and (Test-Path $existingExecutable)) {
            $ownershipResolver = Get-Command 'Resolve-InstallerOwnershipFromExecutable' -ErrorAction SilentlyContinue
            $pathComparer = Get-Command 'Test-InstallerPathEqualsCore' -ErrorAction SilentlyContinue
            if ($null -ne $ownershipResolver -and $null -ne $pathComparer) {
                $existingOwnership = Resolve-InstallerOwnershipFromExecutable -InstalledExecutable $existingExecutable
                $existingBinaryLooksOwned = [bool]$existingOwnership.InstallerOwned -and (Test-InstallerPathEqualsCore -Left ([string]$existingOwnership.InstallBase) -Right $installBase)
            }
            else {
                $existingBinaryLooksOwned = $true
            }
        }

        if (($existingManifest.version -eq $ResolvedVersion) -and $existingBinaryLooksOwned) {
            $reusedExistingBinary = $true
            $rollbackBackupManifestPath = "$installManifestPath.rollback-$([guid]::NewGuid().ToString('N'))"
            Copy-Item -LiteralPath $installManifestPath -Destination $rollbackBackupManifestPath -Force
            $rollbackBackupRegistrationDir = Update-ClientRegistrationArtifactsTransactional -InstallBase $installBase -InstalledExecutable $existingExecutable -RestoreOnError
            return [ordered]@{
                installRoot = $installRootFullPath
                installBase = $installBase
                installManifestPath = $installManifestPath
                installedExecutable = $existingExecutable
                reusedExistingBinary = $reusedExistingBinary
                rollbackBackupManifestPath = $rollbackBackupManifestPath
                rollbackBackupRegistrationDir = $rollbackBackupRegistrationDir
            }
        }
    }

    New-Item -ItemType Directory -Force -Path $installBase | Out-Null
    if (Test-Path $currentDir) {
        $rollbackBackupCurrentDir = "$currentDir.rollback-$([guid]::NewGuid().ToString('N'))"
        Move-Item -Path $currentDir -Destination $rollbackBackupCurrentDir -Force
    }
    if (Test-Path $installManifestPath) {
        $rollbackBackupManifestPath = "$installManifestPath.rollback-$([guid]::NewGuid().ToString('N'))"
        Move-Item -Path $installManifestPath -Destination $rollbackBackupManifestPath -Force
    }

    try {
        New-Item -ItemType Directory -Force -Path $currentDir | Out-Null
        Copy-Item -Path (Join-Path $PackageDirectory '*') -Destination $currentDir -Recurse -Force
        Remove-PathIfExists -Path (Join-Path $currentDir 'run.bat')
        Remove-PathIfExists -Path (Join-Path $currentDir 'bin\install.ps1')

        $relativeExecutable = $packageExecutable.Substring($PackageDirectory.Length).TrimStart('\', '/')
        $installedExecutable = Join-Path $currentDir $relativeExecutable

        ([ordered]@{
                name = 'wpf-devtools'
                architecture = $ResolvedArchitecture
                version = $ResolvedVersion
                installRoot = $installRootFullPath
                installDir = $currentDir
                executable = $installedExecutable
                channel = [string]$PackageManifest.channel
                buildConfiguration = [string]$PackageManifest.buildConfiguration
                signaturePolicy = [string]$PackageManifest.signaturePolicy
                installedUtc = [DateTime]::UtcNow.ToString('o')
            } | ConvertTo-Json -Depth 5) | Set-Content -Path $installManifestPath -Encoding UTF8

        if (Test-Path $registrationArtifactsDir) {
            $rollbackBackupRegistrationDir = "$registrationArtifactsDir.rollback-$([guid]::NewGuid().ToString('N'))"
            Move-Item -Path $registrationArtifactsDir -Destination $rollbackBackupRegistrationDir -Force
        }

        New-ClientRegistrationArtifacts -InstallBase $installBase -InstalledExecutable $installedExecutable

        return [ordered]@{
            installRoot = $installRootFullPath
            installBase = $installBase
            currentDir = $currentDir
            installManifestPath = $installManifestPath
            installedExecutable = $installedExecutable
            reusedExistingBinary = $reusedExistingBinary
            rollbackBackupCurrentDir = $rollbackBackupCurrentDir
            rollbackBackupManifestPath = $rollbackBackupManifestPath
            rollbackBackupRegistrationDir = $rollbackBackupRegistrationDir
        }
    }
    catch {
        Restore-InstallerBackupDirectory -BackupPath $rollbackBackupRegistrationDir -TargetPath $registrationArtifactsDir -RemoveTargetWhenNoBackup
        Restore-InstallerBackupFile -BackupPath $rollbackBackupManifestPath -TargetPath $installManifestPath
        Restore-InstallerBackupFile -BackupPath $rollbackBackupCurrentDir -TargetPath $currentDir
        throw
    }
}

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
        Move-Item -Path $registrationArtifactsDir -Destination $rollbackBackupRegistrationDir -Force
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

    if (-not (Test-Path $BackupPath)) {
        return
    }

    Remove-PathIfExists -Path $TargetPath
    $targetDirectory = Split-Path -Parent $TargetPath
    if (-not [string]::IsNullOrWhiteSpace($targetDirectory)) {
        New-Item -ItemType Directory -Force -Path $targetDirectory | Out-Null
    }

    Move-Item -Path $BackupPath -Destination $TargetPath -Force
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

    if (-not [string]::IsNullOrWhiteSpace($BackupPath) -and (Test-Path $BackupPath)) {
        Remove-PathIfExists -Path $TargetPath
        $targetParent = Split-Path -Parent $TargetPath
        if (-not [string]::IsNullOrWhiteSpace($targetParent)) {
            New-Item -ItemType Directory -Force -Path $targetParent | Out-Null
        }

        Move-Item -Path $BackupPath -Destination $TargetPath -Force
        return
    }

    if ($RemoveTargetWhenNoBackup) {
        Remove-PathIfExists -Path $TargetPath
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
    Restore-InstallerBackupFile -BackupPath ([string]$InstallResult.rollbackBackupCurrentDir) -TargetPath ([string]$InstallResult.currentDir)
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

    $manifestPath = Join-Path $InstallBase 'install-manifest.json'
    if (-not (Test-Path $manifestPath)) {
        return
    }

    $manifest = Get-Content -Path $manifestPath -Raw | ConvertFrom-Json
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
        $updatedManifest | ConvertTo-Json -Depth 10 | Set-Content -Path $tempManifestPath -Encoding UTF8
        Move-Item -Path $tempManifestPath -Destination $manifestPath -Force
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
        -not [string]::Equals([string]$Registration.mode, 'artifact-only', [System.StringComparison]::OrdinalIgnoreCase)) {
        return
    }

    if (-not [string]::IsNullOrWhiteSpace($backupPath) -and (Test-Path $backupPath)) {
        Copy-Item -Path $backupPath -Destination $targetPath -Force
        Remove-PathIfExists -Path $backupPath
        return
    }

    if ($RemoveTargetWhenNoBackup -and -not [string]::IsNullOrWhiteSpace($targetPath)) {
        Remove-PathIfExists -Path $targetPath
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
            [string]::Equals($mode, 'artifact-only', [System.StringComparison]::OrdinalIgnoreCase)) {
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

function Update-InstallerStateAfterInstall {
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)] [string]$ResolvedInstallRoot,
        [Parameter(Mandatory)] [string]$ResolvedArchitecture,
        [Parameter(Mandatory)] [string]$ResolvedVersion,
        [Parameter(Mandatory)] [string]$InstalledExecutable,
        [Parameter(Mandatory)] [string]$SelectedClient,
        [Parameter(Mandatory)] $Registration,
        [Parameter(Mandatory)] [string]$LastVerifiedUtc
    )

    $State.lastInstallRoot = $ResolvedInstallRoot
    $State.architectures[$ResolvedArchitecture] = [ordered]@{
        version = $ResolvedVersion
        executable = $InstalledExecutable
        installRoot = $ResolvedInstallRoot
    }
    $stateKey = Resolve-ClientStateKey -ClientId $SelectedClient -RegistrationMode ([string]$Registration.mode)
    $State.registrations[$stateKey] = [ordered]@{
        architecture = $ResolvedArchitecture
        installRoot = $ResolvedInstallRoot
        mode = [string]$Registration.mode
        target = [string]$Registration.target
        resolvedVersion = $ResolvedVersion
        installedExecutable = $InstalledExecutable
        lastVerifiedUtc = $LastVerifiedUtc
    }
}

function Invoke-InstallerActionCore {
    param(
        [Parameter(Mandatory)] [ValidateSet('install', 'uninstall', 'full-uninstall')] [string]$ResolvedAction,
        [Parameter(Mandatory)] [string]$ResolvedArchitecture,
        [Parameter(Mandatory)] [string]$ResolvedClient,
        [Parameter(Mandatory)] [string]$ResolvedInstallRoot,
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
            verificationMessage = [string]$result.verificationMessage
        }
    }

    if ($ResolvedAction -eq 'uninstall') {
        $state = Get-InstallerState
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

        $registrationRecord = if ($null -ne $detectedRegistration) {
            $detectedRegistration
        }
        elseif (-not [string]::IsNullOrWhiteSpace($stateRegistrationKey) -and $state.registrations.Contains($stateRegistrationKey)) {
            $state.registrations[$stateRegistrationKey]
        }
        else {
            Get-StandaloneFallbackRegistrationRecord -SelectedClient $ResolvedClient -ResolvedInstallRoot $ResolvedInstallRoot -ResolvedArchitecture $ResolvedArchitecture
        }
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
            $ResolvedInstallRoot = Resolve-PreferredInstallRoot
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
        try {
            $registrations = @(Invoke-ClientUnregistration -SelectedClient $ResolvedClient -RegistrationRecord $registrationRecord)
            $verification = Invoke-UninstallVerification -SelectedClient $ResolvedClient -RegistrationRecord $registrationRecord -RegistrationChanges $registrations
            if (-not $verification.Succeeded) {
                throw $verification.VerificationMessage
            }

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
            registrations = @($registrations)
            verificationMessage = [string]$verification.VerificationMessage
        }
    }

    $mode = if ($UseLatestRelease) { 'online' } else { Resolve-InstallerMode }
    $session = Resolve-PackageSession -Mode $mode -ResolvedVersion $RequestedVersion -ResolvedArchitecture $ResolvedArchitecture
    $installResult = $null
    $registrations = @()
    $allowPayloadRollback = $true
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

        $installResult = Install-PackagePayload -PackageDirectory $session.PackageDirectory -PackageManifest $packageManifest -ResolvedArchitecture $resolvedArchitecture -ResolvedInstallRoot $ResolvedInstallRoot -ResolvedVersion $resolvedVersion
        $registrations = @(Invoke-ClientRegistration -SelectedClient $ResolvedClient -InstalledExecutable $installResult.installedExecutable -InstallBase $installResult.installBase)
        $verification = Invoke-InstallVerification -SelectedClient $ResolvedClient -ResolvedVersion $resolvedVersion -InstalledExecutable $installResult.installedExecutable -Registration $registrations[0]
        if (-not $verification.Succeeded) {
            throw $verification.VerificationMessage
        }

        Update-InstalledManifestManagedRegistrationTarget -InstallBase $installResult.installBase -SelectedClient $ResolvedClient -Registration $registrations[0]

        $state = Get-InstallerState
        Update-InstallerStateAfterInstall -State $state -ResolvedInstallRoot $installResult.installRoot -ResolvedArchitecture $resolvedArchitecture -ResolvedVersion ([string]$verification.InstalledVersion) -InstalledExecutable $installResult.installedExecutable -SelectedClient $ResolvedClient -Registration $registrations[0] -LastVerifiedUtc ([string]$verification.LastVerifiedUtc)
        $statePath = Save-InstallerState -State $state
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
        $rollbackInstalledExecutable = if ($null -ne $installResult) { [string]$installResult.installedExecutable } else { $null }
        $rollbackInstallBase = if ($null -ne $installResult) { [string]$installResult.installBase } else { $null }
        if ($allowPayloadRollback) {
            Undo-ClientRegistrationChanges -SelectedClient $ResolvedClient -Registrations $registrations -RollbackMode 'install' -InstalledExecutable $rollbackInstalledExecutable -InstallBase $rollbackInstallBase
            Undo-InstalledPayload -InstallResult $installResult
        }
        throw
    }
    finally {
        if ($session.CleanupSession) {
            Remove-PathIfExists -Path $session.SessionRoot -BestEffort
        }
    }
}
