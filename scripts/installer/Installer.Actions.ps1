<#
.SYNOPSIS
    Installs a prepared package directory into the requested install root.

.PARAMETER TrustedArchiveManifestPolicy
    Indicates the package directory came from an archive session that already
    passed release checksum and archive safety validation, allowing
    DebugTrustedRootSkip manifests to skip per-payload Authenticode checks.
#>
if (-not (Get-Command Write-InstallerUtf8NoBomFile -ErrorAction SilentlyContinue)) {
    $encodingHelperPath = Join-Path $PSScriptRoot 'Installer.Encoding.ps1'
    if (Test-Path -LiteralPath $encodingHelperPath) {
        . $encodingHelperPath
    }
}

if (-not (Get-Command Resolve-AbsolutePath -ErrorAction SilentlyContinue)) {
    function Resolve-AbsolutePath {
        param([Parameter(Mandatory)] [string]$Path)

        if ([System.IO.Path]::IsPathRooted($Path)) {
            return [System.IO.Path]::GetFullPath($Path)
        }

        return [System.IO.Path]::GetFullPath((Join-Path (Get-Location).Path $Path))
    }
}

if (-not (Get-Command Test-InstallerUncOrDevicePath -ErrorAction SilentlyContinue)) {
    function Test-InstallerUncOrDevicePath {
        param([Parameter(Mandatory)] [string]$Path)

        return $Path.StartsWith('\\', [System.StringComparison]::Ordinal) -or
            $Path.StartsWith('\\?\', [System.StringComparison]::OrdinalIgnoreCase) -or
            $Path.StartsWith('\\.\', [System.StringComparison]::OrdinalIgnoreCase)
    }
}

if (-not (Get-Command Assert-InstallerLocalPathTrusted -ErrorAction SilentlyContinue)) {
    function Assert-InstallerLocalPathTrusted {
        param(
            [Parameter(Mandatory)] [string]$Path,
            [switch]$RejectHardLinks
        )

        $resolvedPath = Resolve-AbsolutePath -Path $Path
        if (Test-InstallerUncOrDevicePath -Path $resolvedPath) {
            throw "Installer path '$resolvedPath' is blocked because elevated installer file operations require a local path."
        }

        $root = [System.IO.Path]::GetPathRoot($resolvedPath)
        if ([string]::IsNullOrWhiteSpace($root)) {
            throw "Installer path '$resolvedPath' is blocked because elevated installer file operations require an absolute local path."
        }

        try {
            $drive = [System.IO.DriveInfo]::new($root)
            if ($drive.DriveType -eq [System.IO.DriveType]::Network) {
                throw "Installer path '$resolvedPath' is blocked because elevated installer file operations require a local path."
            }
        }
        catch [System.ArgumentException] {
            throw "Installer path '$resolvedPath' is blocked because elevated installer file operations require a local path."
        }

        $relativePath = $resolvedPath.Substring($root.Length).Trim('\', '/')
        if ([string]::IsNullOrWhiteSpace($relativePath)) {
            return $resolvedPath
        }

        $currentPath = $root
        foreach ($segment in $relativePath -split '[\\/]') {
            if ([string]::IsNullOrWhiteSpace($segment)) {
                continue
            }

            $currentPath = Join-Path $currentPath $segment
            if (-not (Test-Path -LiteralPath $currentPath)) {
                break
            }

            $item = Get-Item -LiteralPath $currentPath -Force
            if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Installer path '$resolvedPath' is blocked because '$currentPath' is a reparse point."
            }

            if ($RejectHardLinks -and -not $item.PSIsContainer -and (Get-Command Get-InstallerHardLinkCount -ErrorAction SilentlyContinue)) {
                $hardLinkCount = Get-InstallerHardLinkCount -Path $currentPath
                if ($hardLinkCount -gt 1) {
                    throw "Installer path '$resolvedPath' is blocked because '$currentPath' has multiple hard links."
                }
            }
        }

        return $resolvedPath
    }
}

function Install-PackagePayload {
    param(
        [Parameter(Mandatory)] [string]$PackageDirectory,
        [Parameter(Mandatory)] [psobject]$PackageManifest,
        [Parameter(Mandatory)] [string]$ResolvedArchitecture,
        [Parameter(Mandatory)] [string]$ResolvedInstallRoot,
        [Parameter(Mandatory)] [string]$ResolvedVersion,
        [string]$TrustedSignerThumbprint,
        [string]$TrustedSignerSubject,
        [switch]$TrustedArchiveManifestPolicy
    )

    $packageExecutable = Resolve-PackageExecutable -PackageDirectory $PackageDirectory -ResolvedArchitecture $ResolvedArchitecture
    Assert-PackagePayloadIntegrity `
        -PackageDirectory $PackageDirectory `
        -PackageManifest $PackageManifest `
        -TrustedSignerThumbprint $TrustedSignerThumbprint `
        -TrustedSignerSubject $TrustedSignerSubject `
        -TrustedArchiveManifestPolicy:$TrustedArchiveManifestPolicy

    $installRootFullPath = Resolve-AbsoluteDirectory -Path $ResolvedInstallRoot
    $installBase = Assert-InstallerLocalPathTrusted -Path (Join-Path $installRootFullPath $ResolvedArchitecture)
    $currentDir = Assert-InstallerLocalPathTrusted -Path (Join-Path $installBase 'current')
    $installManifestPath = Assert-InstallerLocalPathTrusted -Path (Join-Path $installBase 'install-manifest.json')
    $registrationArtifactsDir = Assert-InstallerLocalPathTrusted -Path (Join-Path $installBase 'client-registration')
    $reusedExistingBinary = $false
    $rollbackBackupCurrentDir = $null
    $rollbackBackupManifestPath = $null
    $rollbackBackupRegistrationDir = $null

    if ((Test-Path -LiteralPath $installManifestPath) -and -not $Force) {
        $existingManifest = Get-Content -LiteralPath $installManifestPath -Raw | ConvertFrom-Json
        $existingExecutable = [string]$existingManifest.executable
        $existingBinaryLooksOwned = $false
        $trustedExistingExecutable = $null
        if (-not [string]::IsNullOrWhiteSpace($existingExecutable)) {
            try {
                $trustedExistingExecutable = Assert-InstallerLocalPathTrusted -Path $existingExecutable
            }
            catch {
                $trustedExistingExecutable = $null
            }
        }

        if (-not [string]::IsNullOrWhiteSpace($trustedExistingExecutable) -and (Test-Path -LiteralPath $trustedExistingExecutable)) {
            $ownershipResolver = Get-Command 'Resolve-InstallerOwnershipFromExecutable' -ErrorAction SilentlyContinue
            $pathComparer = Get-Command 'Test-InstallerPathEqualsCore' -ErrorAction SilentlyContinue
            if ($null -ne $ownershipResolver -and $null -ne $pathComparer) {
                $existingOwnership = Resolve-InstallerOwnershipFromExecutable -InstalledExecutable $trustedExistingExecutable
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

    Assert-InstallerLocalPathTrusted -Path $installBase | Out-Null
    New-Item -ItemType Directory -Force -Path $installBase | Out-Null
    Assert-InstallerLocalPathTrusted -Path $installBase | Out-Null
    if (Test-Path $currentDir) {
        $rollbackBackupCurrentDir = "$currentDir.rollback-$([guid]::NewGuid().ToString('N'))"
        Move-InstallerPathWithRetry -SourcePath $currentDir -DestinationPath $rollbackBackupCurrentDir
    }
    if (Test-Path $installManifestPath) {
        $rollbackBackupManifestPath = "$installManifestPath.rollback-$([guid]::NewGuid().ToString('N'))"
        Move-InstallerPathWithRetry -SourcePath $installManifestPath -DestinationPath $rollbackBackupManifestPath
    }

    try {
        Assert-InstallerLocalPathTrusted -Path $currentDir | Out-Null
        New-Item -ItemType Directory -Force -Path $currentDir | Out-Null
        Assert-InstallerLocalPathTrusted -Path $currentDir | Out-Null
        Copy-Item -Path (Join-Path $PackageDirectory '*') -Destination $currentDir -Recurse -Force
        Remove-PathIfExists -Path (Join-Path $currentDir 'run.bat')

        $relativeExecutable = $packageExecutable.Substring($PackageDirectory.Length).TrimStart('\', '/')
        $installedExecutable = Join-Path $currentDir $relativeExecutable

        $installManifestJson = ([ordered]@{
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
            } | ConvertTo-Json -Depth 5)
        Assert-InstallerLocalPathTrusted -Path $installManifestPath | Out-Null
        Write-InstallerUtf8NoBomFile -Path $installManifestPath -Content $installManifestJson
        Assert-InstallerLocalPathTrusted -Path $installManifestPath | Out-Null

        if (Test-Path $registrationArtifactsDir) {
            Assert-InstallerLocalPathTrusted -Path $registrationArtifactsDir | Out-Null
            $rollbackBackupRegistrationDir = "$registrationArtifactsDir.rollback-$([guid]::NewGuid().ToString('N'))"
            Move-InstallerPathWithRetry -SourcePath $registrationArtifactsDir -DestinationPath $rollbackBackupRegistrationDir
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
        Restore-InstallerBackupDirectory -BackupPath $rollbackBackupCurrentDir -TargetPath $currentDir
        throw
    }
}

function Test-InstallerTransientFileSystemError {
    param([System.Exception]$Exception)

    $candidate = $Exception
    while ($null -ne $candidate) {
        if ($candidate -is [System.IO.IOException] -or $candidate -is [System.UnauthorizedAccessException]) {
            return $true
        }

        $candidate = $candidate.InnerException
    }

    return $false
}

function Move-InstallerPathWithRetry {
    param(
        [Parameter(Mandatory)] [string]$SourcePath,
        [Parameter(Mandatory)] [string]$DestinationPath
    )

    for ($attempt = 0; $attempt -lt 20; $attempt++) {
        $pathsTrusted = $false
        $resolvedSourcePath = $null
        $resolvedDestinationPath = $null
        try {
            $resolvedSourcePath = Assert-InstallerLocalPathTrusted -Path $SourcePath
            $resolvedDestinationPath = Assert-InstallerLocalPathTrusted -Path $DestinationPath
            $pathsTrusted = $true
            $sourceExists = Test-Path -LiteralPath $resolvedSourcePath
            $destinationExists = Test-Path -LiteralPath $resolvedDestinationPath

            if (-not $sourceExists -and $destinationExists) {
                return
            }

            if ($sourceExists -and $destinationExists) {
                Assert-InstallerLocalPathTrusted -Path $resolvedDestinationPath | Out-Null
                Remove-Item -LiteralPath $resolvedDestinationPath -Recurse -Force
            }

            Assert-InstallerLocalPathTrusted -Path $resolvedSourcePath | Out-Null
            Assert-InstallerLocalPathTrusted -Path $resolvedDestinationPath | Out-Null
            Move-Item -LiteralPath $resolvedSourcePath -Destination $resolvedDestinationPath -Force
            return
        }
        catch {
            if ($pathsTrusted -and -not (Test-Path -LiteralPath $resolvedSourcePath) -and (Test-Path -LiteralPath $resolvedDestinationPath)) {
                return
            }

            if (-not (Test-InstallerTransientFileSystemError -Exception $_.Exception) -or $attempt -ge 19) {
                throw
            }

            Start-Sleep -Milliseconds ([Math]::Min(150 * ($attempt + 1), 2000))
        }
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
        -not [string]::Equals([string]$Registration.mode, 'artifact-only', [System.StringComparison]::OrdinalIgnoreCase)) {
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

function Merge-RegistrationRecordWithStateFallback {
    param(
        $RegistrationRecord,
        $StateRecord,
        [string]$SelectedClient
    )

    if ($null -eq $RegistrationRecord -and $null -eq $StateRecord) {
        return $null
    }

    $clientId = if ($null -ne $RegistrationRecord -and -not [string]::IsNullOrWhiteSpace((Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('ClientId', 'clientId', 'client')))) {
        Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('ClientId', 'clientId', 'client')
    }
    else {
        $SelectedClient
    }

    $installerOwned = $false
    if ($null -ne $RegistrationRecord) {
        if ($RegistrationRecord.Contains('InstallerOwned')) {
            $installerOwned = [bool]$RegistrationRecord.InstallerOwned
        }
        elseif ($RegistrationRecord.Contains('installerOwned')) {
            $installerOwned = [bool]$RegistrationRecord.installerOwned
        }
    }

    $evidenceSource = if ($null -ne $RegistrationRecord) {
        Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('EvidenceSource', 'evidenceSource')
    }
    else {
        $null
    }

    return [ordered]@{
        ClientId = $clientId
        RegistrationMode = if ($null -ne $RegistrationRecord -and -not [string]::IsNullOrWhiteSpace((Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('RegistrationMode', 'registrationMode', 'mode')))) { Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('RegistrationMode', 'registrationMode', 'mode') } else { Get-InstallerRecordStringValueCore -Record $StateRecord -PropertyNames @('mode', 'RegistrationMode', 'registrationMode') }
        RegistrationTarget = if ($null -ne $RegistrationRecord -and -not [string]::IsNullOrWhiteSpace((Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('RegistrationTarget', 'target')))) { Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('RegistrationTarget', 'target') } else { Get-InstallerRecordStringValueCore -Record $StateRecord -PropertyNames @('target', 'RegistrationTarget') }
        InstalledExecutable = if ($null -ne $RegistrationRecord -and -not [string]::IsNullOrWhiteSpace((Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('InstalledExecutable', 'installedExecutable')))) { Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('InstalledExecutable', 'installedExecutable') } else { Get-InstallerRecordStringValueCore -Record $StateRecord -PropertyNames @('installedExecutable', 'InstalledExecutable') }
        InstallRoot = if ($null -ne $RegistrationRecord -and -not [string]::IsNullOrWhiteSpace((Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('InstallRoot', 'installRoot')))) { Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('InstallRoot', 'installRoot') } else { Get-InstallerRecordStringValueCore -Record $StateRecord -PropertyNames @('installRoot', 'InstallRoot') }
        Architecture = if ($null -ne $RegistrationRecord -and -not [string]::IsNullOrWhiteSpace((Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('Architecture', 'architecture')))) { Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('Architecture', 'architecture') } else { Get-InstallerRecordStringValueCore -Record $StateRecord -PropertyNames @('architecture', 'Architecture') }
        InstallerOwned = $installerOwned
        EvidenceSource = $evidenceSource
        ResolvedVersion = if ($null -ne $RegistrationRecord -and -not [string]::IsNullOrWhiteSpace((Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('ResolvedVersion', 'resolvedVersion')))) { Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('ResolvedVersion', 'resolvedVersion') } else { Get-InstallerRecordStringValueCore -Record $StateRecord -PropertyNames @('resolvedVersion', 'ResolvedVersion') }
        LastVerifiedUtc = if ($null -ne $RegistrationRecord -and -not [string]::IsNullOrWhiteSpace((Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('LastVerifiedUtc', 'lastVerifiedUtc')))) { Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('LastVerifiedUtc', 'lastVerifiedUtc') } else { Get-InstallerRecordStringValueCore -Record $StateRecord -PropertyNames @('lastVerifiedUtc', 'LastVerifiedUtc') }
    }
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

        $stateRegistrationRecord = if (-not [string]::IsNullOrWhiteSpace($stateRegistrationKey) -and $state.registrations.Contains($stateRegistrationKey)) {
            $state.registrations[$stateRegistrationKey]
        }
        else {
            $null
        }
        $registrationRecord = if ($null -ne $detectedRegistration) {
            $detectedRegistration
        }
        elseif ($null -ne $stateRegistrationRecord) {
            $stateRegistrationRecord
        }
        else {
            Get-StandaloneFallbackRegistrationRecord -SelectedClient $ResolvedClient -ResolvedInstallRoot $ResolvedInstallRoot -ResolvedArchitecture $ResolvedArchitecture
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

    if ([string]::IsNullOrWhiteSpace($ResolvedInstallRoot)) {
        $ResolvedInstallRoot = Resolve-PreferredInstallRoot
    }

    $mode = if ($UseLatestRelease) { 'online' } else { Resolve-InstallerMode }
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

        $installResult = Install-PackagePayload `
            -PackageDirectory $session.PackageDirectory `
            -PackageManifest $packageManifest `
            -ResolvedArchitecture $resolvedArchitecture `
            -ResolvedInstallRoot $ResolvedInstallRoot `
            -ResolvedVersion $resolvedVersion `
            -TrustedSignerThumbprint ([string]$session.TrustedSignerThumbprint) `
            -TrustedSignerSubject ([string]$session.TrustedSignerSubject) `
            -TrustedArchiveManifestPolicy:([bool]$session.TrustedArchiveManifestPolicy)
        $registrations = @(Invoke-ClientRegistration -SelectedClient $ResolvedClient -InstalledExecutable $installResult.installedExecutable -InstallBase $installResult.installBase)
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
