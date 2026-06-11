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
