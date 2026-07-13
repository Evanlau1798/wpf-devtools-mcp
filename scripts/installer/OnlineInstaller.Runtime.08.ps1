function Invoke-StandaloneSelectedUninstallActionCore {
    param(
        [Parameter(Mandatory)] [ValidateSet('uninstall', 'full-uninstall')] [string]$ResolvedAction,
        [Parameter(Mandatory)] [string]$ResolvedArchitecture,
        [Parameter(Mandatory)] [string]$ResolvedClient,
        [Parameter(Mandatory)] [AllowEmptyString()] [AllowNull()] [string]$ResolvedInstallRoot,
        [Parameter(Mandatory)] [string]$RequestedVersion,
        [switch]$UseLatestRelease
    )

    $state = Get-StandaloneInstallerState
    $effectiveInstallRoot = Resolve-StandaloneRemovalInstallRoot -ResolvedInstallRoot $ResolvedInstallRoot -State $state

    $detectedRegistrationMap = Get-StandaloneDetectedInstallerRegistrationMap -State $state
    $registrationKey = if ($detectedRegistrationMap.Contains($ResolvedClient)) {
        $ResolvedClient
    }
    elseif ($ResolvedClient -eq 'cursor') {
        ($detectedRegistrationMap.Keys | Where-Object { $_ -like 'cursor-*' } | Select-Object -First 1)
    }
    else {
        $null
    }

    $registrationRecord = if (-not [string]::IsNullOrWhiteSpace([string]$registrationKey) -and $detectedRegistrationMap.Contains($registrationKey)) {
        $detectedRegistrationMap[$registrationKey]
    }
    else {
        Get-StandaloneFallbackRegistrationRecord -SelectedClient $ResolvedClient -ResolvedInstallRoot $effectiveInstallRoot -ResolvedArchitecture $ResolvedArchitecture
    }

    if ($script:InstallRootWasSpecified -and $null -ne $registrationRecord -and -not (Test-StandaloneRegistrationMatchesInstallRoot -RegistrationRecord $registrationRecord -ExpectedInstallRoot $effectiveInstallRoot)) {
        $registrationKey = $null
        $registrationRecord = Get-StandaloneFallbackRegistrationRecord -SelectedClient $ResolvedClient -ResolvedInstallRoot $effectiveInstallRoot -ResolvedArchitecture $ResolvedArchitecture
    }

    $registrations = @()
    $skipSelectedUninstall = Test-StandaloneExplicitRootCliUninstallNoOp -RegistrationRecord $registrationRecord -InstallRootWasSpecified:([bool]$script:InstallRootWasSpecified)
    try {
        if ($null -ne $registrationRecord -and -not $skipSelectedUninstall) {
            $rawMode = Get-StandaloneRecordStringValue -Record $registrationRecord -PropertyNames @('mode', 'Mode', 'RegistrationMode')
            $mode = Get-StandaloneNormalizedRegistrationMode -RegistrationMode $rawMode
            $targetPath = Get-StandaloneTrustedRecordedTarget -SelectedClient $ResolvedClient -RegistrationRecord $registrationRecord
            $clientBaseId = Resolve-ClientBaseId -ClientId $ResolvedClient

            if ([string]::IsNullOrWhiteSpace($targetPath) -and [string]::Equals($mode, 'json-file', [System.StringComparison]::OrdinalIgnoreCase)) {
                if ($clientBaseId -eq 'cursor') {
                    $targetPath = Get-StandaloneTrustedCursorManifestTarget -SelectedClient $ResolvedClient -RegistrationRecord $registrationRecord
                }
                else {
                    $targetPath = Get-StandaloneTrustedManagedJsonRegistrationTarget -SelectedClient $ResolvedClient -RegistrationRecord $registrationRecord
                }
            }

            if ([string]::Equals($mode, 'artifact-only', [System.StringComparison]::OrdinalIgnoreCase) -and [string]::IsNullOrWhiteSpace($targetPath)) {
                $artifactTargets = @(Get-StandaloneTrustedOtherRegistrationArtifactTargets -RegistrationRecord $registrationRecord)
                if ($artifactTargets.Count -gt 0) {
                    $targetPath = [string]$artifactTargets[0]
                }
            }

            if ([string]::Equals($mode, 'json-file', [System.StringComparison]::OrdinalIgnoreCase)) {
                $collectionName = switch ($clientBaseId) {
                    'vscode' { 'servers' }
                    'visual-studio' { 'servers' }
                    'claude-desktop' { 'mcpServers' }
                    'cursor' { 'mcpServers' }
                    default { $null }
                }

                if (-not [string]::IsNullOrWhiteSpace($collectionName) -and -not [string]::IsNullOrWhiteSpace($targetPath)) {
                    $removal = Remove-StandaloneJsonConfigRegistration -CollectionName $collectionName -ConfigPath $targetPath
                    $registrations += [ordered]@{
                        client = $clientBaseId
                        mode = 'json-file'
                        target = $targetPath
                        backupPath = [string]$removal.backupPath
                        installedExecutable = (Get-StandaloneRecordStringValue -Record $registrationRecord -PropertyNames @('InstalledExecutable', 'installedExecutable'))
                        applied = [bool]$removal.applied
                    }
                }
            }
            elseif ([string]::Equals($mode, 'artifact-only', [System.StringComparison]::OrdinalIgnoreCase)) {
                $backupPath = $null
                $artifactTargets = @(Get-StandaloneTrustedOtherRegistrationArtifactTargets -RegistrationRecord $registrationRecord)
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

                    if ([string]::IsNullOrWhiteSpace($backupPath)) {
                        $targetPath = $trustedCandidateTarget
                        $backupPath = Assert-InstallerLocalPathTrusted -Path "$targetPath.bak-$([guid]::NewGuid().ToString('N'))"
                        Copy-Item -LiteralPath $targetPath -Destination $backupPath -Force
                    }

                    Remove-PathIfExists -Path $trustedCandidateTarget
                }
                $registrations += [ordered]@{
                    client = $clientBaseId
                    mode = 'artifact-only'
                    target = $targetPath
                    backupPath = $backupPath
                    installedExecutable = (Get-StandaloneRecordStringValue -Record $registrationRecord -PropertyNames @('InstalledExecutable', 'installedExecutable'))
                    applied = (-not [string]::IsNullOrWhiteSpace($backupPath))
                }
            }
            elseif ([string]::Equals($mode, 'cli', [System.StringComparison]::OrdinalIgnoreCase)) {
                $command = Resolve-StandaloneCliCommandName -ClientBaseId $clientBaseId
                $resolvedCommandPath = Resolve-StandaloneExecutableCommandPath -Command $command
                if (-not [string]::IsNullOrWhiteSpace($resolvedCommandPath)) {
                    & $resolvedCommandPath @(Get-StandaloneCliRemoveArguments -ClientBaseId $clientBaseId) | Out-Null
                }

                $registrations += [ordered]@{
                    client = $clientBaseId
                    mode = 'cli'
                    target = $command
                    backupPath = $null
                    installedExecutable = (Get-StandaloneRecordStringValue -Record $registrationRecord -PropertyNames @('InstalledExecutable', 'installedExecutable'))
                    applied = ($LASTEXITCODE -eq 0)
                }
            }
        }

        if ($skipSelectedUninstall) {
            $verification = [ordered]@{
                Succeeded = $true
                VerificationMessage = "Verified no matching $ResolvedClient registration under $effectiveInstallRoot."
            }
        }
        else {
            $verification = Invoke-StandaloneUninstallVerification -SelectedClient $ResolvedClient -RegistrationRecord $registrationRecord -RegistrationChanges @($registrations)
            if (-not $verification.Succeeded) {
                throw $verification.VerificationMessage
            }
        }

        if (-not [string]::IsNullOrWhiteSpace([string]$registrationKey) -and $state.registrations.Contains($registrationKey)) {
            [void]$state.registrations.Remove($registrationKey)
        }

        $statePath = Save-StandaloneInstallerState -State $state
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
            installRoot = $effectiveInstallRoot
            installedExecutable = [string]$registrationRecord.installedExecutable
            selectedClients = @($ResolvedClient)
            statePath = $statePath
            removedInstallation = $false
            registrations = @($registrations)
            cleanupScope = 'selected-registration-only'
            cleanupGuidance = Get-StandaloneUninstallCleanupGuidance -ResolvedClient $ResolvedClient
            verificationMessage = [string]$verification.VerificationMessage
        }
    }
    catch {
        $registrationsInReverse = @($registrations)
        [array]::Reverse($registrationsInReverse)
        foreach ($registration in $registrationsInReverse) {
            if (-not [bool]$registration.applied) {
                continue
            }

            if ([string]::Equals([string]$registration.mode, 'json-file', [System.StringComparison]::OrdinalIgnoreCase) -or
                [string]::Equals([string]$registration.mode, 'artifact-only', [System.StringComparison]::OrdinalIgnoreCase)) {
                if (-not [string]::IsNullOrWhiteSpace([string]$registration.backupPath) -and
                    -not [string]::IsNullOrWhiteSpace([string]$registration.target)) {
                    try {
                        $trustedBackupPath = Assert-InstallerLocalPathTrusted -Path ([string]$registration.backupPath)
                        $trustedTargetPath = Assert-InstallerLocalPathTrusted -Path ([string]$registration.target) -RejectHardLinks
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

            if ([string]::Equals([string]$registration.mode, 'cli', [System.StringComparison]::OrdinalIgnoreCase) -and
                -not [string]::IsNullOrWhiteSpace([string]$registration.installedExecutable)) {
                $command = Resolve-StandaloneCliCommandName -ClientBaseId ([string]$registration.client)
                $resolvedCommandPath = Resolve-StandaloneExecutableCommandPath -Command $command
                if (-not [string]::IsNullOrWhiteSpace($resolvedCommandPath)) {
                    & $resolvedCommandPath @(Get-StandaloneCliAddArguments -ClientBaseId ([string]$registration.client) -InstalledExecutable ([string]$registration.installedExecutable)) | Out-Null
                }
            }
        }

        throw
    }
}
function Invoke-StandaloneInstallerActionCore {
    param(
        [Parameter(Mandatory)] [ValidateSet('uninstall', 'full-uninstall')] [string]$ResolvedAction,
        [Parameter(Mandatory)] [string]$ResolvedArchitecture,
        [Parameter(Mandatory)] [string]$ResolvedClient,
        [Parameter(Mandatory)] [AllowEmptyString()] [AllowNull()] [string]$ResolvedInstallRoot,
        [Parameter(Mandatory)] [string]$RequestedVersion,
        [switch]$UseLatestRelease
    )

    $parameters = @{
        ResolvedAction = $ResolvedAction
        ResolvedArchitecture = $ResolvedArchitecture
        ResolvedClient = $ResolvedClient
        ResolvedInstallRoot = $ResolvedInstallRoot
        RequestedVersion = $RequestedVersion
        UseLatestRelease = $UseLatestRelease
    }
    if ($ResolvedAction -eq 'full-uninstall') {
        return (Invoke-StandaloneFullUninstallActionCore @parameters)
    }

    return (Invoke-StandaloneSelectedUninstallActionCore @parameters)
}
function Add-InstalledInstallerHelperRoot {
    param(
        [System.Collections.Generic.List[string]]$Roots,
        [string]$InstallRoot,
        [string]$Architecture,
        [string]$InstalledExecutable
    )

    if (-not [string]::IsNullOrWhiteSpace($InstalledExecutable)) {
        $binRoot = Split-Path -Parent $InstalledExecutable
        if (-not [string]::IsNullOrWhiteSpace($binRoot)) {
            Add-InstallerHelperRootCandidate -Roots $Roots -CandidateRoot (Join-Path $binRoot 'installer')
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($InstallRoot) -and -not [string]::IsNullOrWhiteSpace($Architecture)) {
        Add-InstallerHelperRootCandidate -Roots $Roots -CandidateRoot (Join-Path (Join-Path $InstallRoot "$Architecture\current\bin") 'installer')
    }
}
function Get-InstalledInstallerHelperRoots {
    $helperRoots = New-Object System.Collections.Generic.List[string]
    $resolvedArchitecture = if ([string]::IsNullOrWhiteSpace($Architecture)) { Get-SystemDefaultArchitecture } else { [string]$Architecture }
    Add-InstalledInstallerHelperRoot -Roots $helperRoots -InstallRoot $InstallRoot -Architecture $resolvedArchitecture -InstalledExecutable $null

    $state = Get-StandaloneInstallerStateSnapshot
    if ($null -eq $state) {
        return @($helperRoots)
    }

    if ($null -ne $state.architectures) {
        foreach ($property in $state.architectures.PSObject.Properties) {
            Add-InstalledInstallerHelperRoot `
                -Roots $helperRoots `
                -InstallRoot ([string]$property.Value.installRoot) `
                -Architecture ([string]$property.Name) `
                -InstalledExecutable ([string]$property.Value.executable)
        }
    }

    if ($null -ne $state.registrations) {
        foreach ($property in $state.registrations.PSObject.Properties) {
            Add-InstalledInstallerHelperRoot `
                -Roots $helperRoots `
                -InstallRoot ([string]$property.Value.installRoot) `
                -Architecture ([string]$property.Value.architecture) `
                -InstalledExecutable ([string]$property.Value.installedExecutable)
        }
    }

    return @($helperRoots.ToArray())
}
function Test-InstalledInstallerHelperRootCandidate {
    param([string]$CandidateRoot)

    if ([string]::IsNullOrWhiteSpace($CandidateRoot)) {
        return $false
    }

    foreach ($helperRoot in @(Get-InstalledInstallerHelperRoots)) {
        if (Test-StandaloneInstallerPathEquals -Left $CandidateRoot -Right $helperRoot) {
            return $true
        }
    }

    return $false
}
function Test-InstallerTestModeEnabled {
    return [bool]$script:WpfDevToolsInstallerTestModeEnabled
}
function Get-TuiHelperOverrideDirectory {
    if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_HELPER_DIRECTORY)) {
        if (-not (Test-InstallerTestModeEnabled)) {
            throw 'WPFDEVTOOLS_INSTALLER_HELPER_DIRECTORY is supported only when WPFDEVTOOLS_INSTALLER_TEST_MODE=1.'
        }

        return $env:WPFDEVTOOLS_INSTALLER_HELPER_DIRECTORY
    }

    return $null
}
function Get-TuiHelperOverrideDownloadBaseUri {
    if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_HELPER_BASE_URI)) {
        if (-not (Test-InstallerTestModeEnabled)) {
            throw 'WPFDEVTOOLS_INSTALLER_HELPER_BASE_URI is supported only when WPFDEVTOOLS_INSTALLER_TEST_MODE=1.'
        }

        return $env:WPFDEVTOOLS_INSTALLER_HELPER_BASE_URI.TrimEnd('/')
    }

    return $null
}
function Get-HelperLeafNames {
    return @(@($script:InstallerHelperSourcePaths) + @($script:OnlineInstallerRuntimeSourcePaths) |
        ForEach-Object { Split-Path $_ -Leaf })
}
function Resolve-InstallerBootstrapUiPath {
    foreach ($candidateRoot in @(Get-LocalInstallerHelperRoots)) {
        if ([string]::IsNullOrWhiteSpace($candidateRoot)) {
            continue
        }

        try {
            $trustedCandidateRoot = Assert-InstallerLocalPathTrusted -Path $candidateRoot
        }
        catch {
            continue
        }

        if (-not (Test-Path -LiteralPath $trustedCandidateRoot)) {
            continue
        }

        $bootstrapHelperPath = Assert-InstallerLocalPathTrusted -Path (Join-Path $trustedCandidateRoot 'Installer.BootstrapUi.ps1')
        if (Test-Path -LiteralPath $bootstrapHelperPath) {
            return $bootstrapHelperPath
        }
    }

    return $null
}

$script:InstallerBootstrapUiPath = Resolve-InstallerBootstrapUiPath
if ($null -eq (Get-Command Enter-TuiBootstrapTerminalSession -ErrorAction SilentlyContinue)) {
    function Enter-TuiBootstrapTerminalSession { return $null }
}
if ($null -eq (Get-Command Exit-TuiBootstrapTerminalSession -ErrorAction SilentlyContinue)) {
    function Exit-TuiBootstrapTerminalSession { param($Session) }
}
if ($null -eq (Get-Command Close-TuiBootstrapScreen -ErrorAction SilentlyContinue)) {
    function Close-TuiBootstrapScreen { }
}
if ($null -eq (Get-Command Write-TuiBootstrapScreen -ErrorAction SilentlyContinue)) {
    function Write-TuiBootstrapScreen { param([Parameter(Mandatory)] [AllowEmptyString()] [string]$Message); if ([string]::IsNullOrWhiteSpace($Message)) { return '' }; return $Message }
}
function Get-InstallerTimeoutSeconds {
    param(
        [Parameter(Mandatory)] [string]$EnvironmentVariable,
        [Parameter(Mandatory)] [int]$DefaultValue,
        [int]$MinimumValue = 1,
        [int]$MaximumValue = 120
    )

    $rawValue = [Environment]::GetEnvironmentVariable($EnvironmentVariable)
    if ([string]::IsNullOrWhiteSpace($rawValue)) {
        return $DefaultValue
    }

    $parsedValue = 0
    if (-not [int]::TryParse($rawValue, [ref]$parsedValue)) {
        return $DefaultValue
    }

    return [Math]::Min($MaximumValue, [Math]::Max($MinimumValue, $parsedValue))
}
function Get-TuiHelperRequestTimeoutSeconds {
    return (Get-InstallerTimeoutSeconds -EnvironmentVariable 'WPFDEVTOOLS_INSTALLER_HELPER_TIMEOUT_SEC' -DefaultValue 5 -MinimumValue 1 -MaximumValue 30)
}
function Get-ReleaseArchiveDownloadTimeoutSeconds {
    return (Get-InstallerTimeoutSeconds -EnvironmentVariable 'WPFDEVTOOLS_INSTALLER_DOWNLOAD_TIMEOUT_SEC' -DefaultValue 30 -MinimumValue 5 -MaximumValue 300)
}
function Expand-InstallerArchive {
    param(
        [Parameter(Mandatory)] [string]$ArchivePath,
        [Parameter(Mandatory)] [string]$DestinationPath
    )

    $previousProgressPreference = $ProgressPreference
    try {
        $ProgressPreference = 'SilentlyContinue'
        Expand-Archive -Path $ArchivePath -DestinationPath $DestinationPath -Force
    }
    finally {
        $ProgressPreference = $previousProgressPreference
    }
}
function Get-TuiHelperBootstrapTimeoutSeconds {
    return (Get-InstallerTimeoutSeconds -EnvironmentVariable 'WPFDEVTOOLS_INSTALLER_HELPER_BOOTSTRAP_TIMEOUT_SEC' -DefaultValue 20 -MinimumValue 3 -MaximumValue 120)
}
function Get-InstallerVerificationTimeoutSeconds {
    return (Get-InstallerTimeoutSeconds -EnvironmentVariable 'WPFDEVTOOLS_INSTALLER_VERIFICATION_TIMEOUT_SEC' -DefaultValue 2 -MinimumValue 1 -MaximumValue 30)
}
function Get-Sha256FileHashHex {
    param([Parameter(Mandatory)] [string]$Path)

    if (Get-Command Get-FileHash -ErrorAction SilentlyContinue) {
        return (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant()
    }

    $stream = [System.IO.File]::OpenRead($Path)
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hashBytes = $sha256.ComputeHash($stream)
    }
    finally {
        $sha256.Dispose()
        $stream.Dispose()
    }

    return (($hashBytes | ForEach-Object { $_.ToString('x2') }) -join '')
}
function Get-ComputedInstallerHelperCacheKey {
    param(
        [Parameter(Mandatory)] [string]$HelperDirectory,
        [Parameter(Mandatory)] [string[]]$HelperFiles
    )

    $records = New-Object System.Collections.Generic.List[string]
    foreach ($helperFile in ($HelperFiles | Sort-Object)) {
        $helperPath = Join-Path $HelperDirectory $helperFile
        if (-not (Test-Path $helperPath)) {
            throw "Helper file was not found while computing the installer cache key: $helperPath"
        }

        $fileHash = Get-Sha256FileHashHex -Path $helperPath
        $records.Add("${helperFile}:$fileHash")
    }

    $utf8 = [System.Text.Encoding]::UTF8.GetBytes(($records -join '|'))
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hashBytes = $sha256.ComputeHash($utf8)
    }
    finally {
        $sha256.Dispose()
    }

    return 'sha256:' + (($hashBytes | ForEach-Object { $_.ToString('x2') }) -join '')
}
