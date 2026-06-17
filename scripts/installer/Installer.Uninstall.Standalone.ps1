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

function Remove-InstallerRuntimeScreenshotCache {
    if ([string]::IsNullOrWhiteSpace($env:LOCALAPPDATA)) {
        return $false
    }

    $cachePath = Join-Path $env:LOCALAPPDATA 'WpfDevTools\tmp\screenshots'
    $trustedCachePath = Assert-InstallerLocalPathTrusted -Path $cachePath
    if (-not (Test-Path -LiteralPath $trustedCachePath)) {
        return $false
    }

    Remove-PathIfExists -Path $trustedCachePath
    return $true
}

function Get-JsonConfigRegisteredExecutable {
    param(
        [Parameter(Mandatory)] [string]$CollectionName,
        [AllowEmptyString()] [string]$ConfigPath
    )

    if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
        return $null
    }

    try {
        $resolvedConfigPath = Assert-InstallerLocalPathTrusted -Path $ConfigPath
    }
    catch {
        return $null
    }

    if (-not (Test-Path -LiteralPath $resolvedConfigPath)) {
        return $null
    }

    $root = Get-ExistingConfigMap -Path $resolvedConfigPath
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

        try {
            $trustedInstallRoot = Assert-InstallerLocalPathTrusted -Path ([string]$registration.InstallRoot)
            $trustedInstallBase = Assert-InstallerLocalPathTrusted -Path (Resolve-InstallBasePath -ResolvedInstallRoot $trustedInstallRoot -ResolvedArchitecture ([string]$registration.Architecture))
        }
        catch {
            continue
        }

        $key = "{0}|{1}" -f $trustedInstallRoot.ToLowerInvariant(), ([string]$registration.Architecture).ToLowerInvariant()
        $installations[$key] = [ordered]@{
            InstallRoot = $trustedInstallRoot
            Architecture = [string]$registration.Architecture
            InstallBase = $trustedInstallBase
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
        $trustedInstallBase = $null
        try {
            $trustedInstallBase = Assert-InstallerLocalPathTrusted -Path ([string]$installation.InstallBase)
        }
        catch {
            $trustedInstallBase = $null
        }

        if (-not [string]::IsNullOrWhiteSpace($trustedInstallBase) -and (Test-Path -LiteralPath $trustedInstallBase)) {
            $verificationFailures += "Installation root still exists: $([string]$installation.InstallBase)"
        }

        $trustedInstalledExecutable = $null
        if (-not [string]::IsNullOrWhiteSpace([string]$installation.InstalledExecutable)) {
            try {
                $trustedInstalledExecutable = Assert-InstallerLocalPathTrusted -Path ([string]$installation.InstalledExecutable)
            }
            catch {
                $trustedInstalledExecutable = $null
            }
        }

        if (-not [string]::IsNullOrWhiteSpace($trustedInstalledExecutable) -and (Test-Path -LiteralPath $trustedInstalledExecutable)) {
            $verificationFailures += "Executable still exists: $([string]$installation.InstalledExecutable)"
        }
    }

    if ($verificationFailures.Count -gt 0) {
        throw ($verificationFailures -join ' ')
    }

    $removedRuntimeScreenshotCache = Remove-InstallerRuntimeScreenshotCache
    $newState = Get-EmptyInstallerState
    $statePath = Save-InstallerState -State $newState
    return [ordered]@{
        statePath = $statePath
        removedInstallation = ($removedInstallations.Count -gt 0)
        removedInstallations = @($removedInstallations)
        removedRuntimeScreenshotCache = $removedRuntimeScreenshotCache
        registrations = @($unregistrationResults)
        verificationMessage = "Verified removal of $($detectedRegistrations.Count) registration(s) and $($removedInstallations.Count) installer-owned server location(s)."
    }
}
