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

    foreach ($registration in $detectedRegistrations) {
        $unregistrationResults += @(Invoke-ClientUnregistration -SelectedClient ([string]$registration.ClientId) -RegistrationRecord $registration)
    }

    $verificationFailures = @()
    foreach ($registration in $detectedRegistrations) {
        $verification = Invoke-UninstallVerification -SelectedClient ([string]$registration.ClientId) -RegistrationRecord $registration
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
        action = 'full-uninstall'
        client = 'all'
        statePath = $statePath
        removedInstallation = ($removedInstallations.Count -gt 0)
        removedInstallations = @($removedInstallations)
        registrations = @($unregistrationResults)
        verificationMessage = "Verified removal of $($detectedRegistrations.Count) registration(s) and $($removedInstallations.Count) installer-owned server location(s)."
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
            if ([string]::IsNullOrWhiteSpace([string]$existing.RegistrationTarget)) {
                $existing.RegistrationTarget = [string]$registration.RegistrationTarget
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

    foreach ($registration in $detectedRegistrations) {
        $unregistrationResults += @(Invoke-ClientUnregistration -SelectedClient ([string]$registration.ClientId) -RegistrationRecord $registration)
    }

    $verificationFailures = @()
    foreach ($registration in $detectedRegistrations) {
        $verification = Invoke-UninstallVerification -SelectedClient ([string]$registration.ClientId) -RegistrationRecord $registration
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
