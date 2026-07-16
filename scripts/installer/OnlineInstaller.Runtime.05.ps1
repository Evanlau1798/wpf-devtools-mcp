function Get-StandaloneDetectedInstallerRegistrations {
    param([Parameter(Mandatory)] $State)

    $registrationMap = [ordered]@{}
    foreach ($entry in $State.registrations.GetEnumerator()) {
        $record = $entry.Value
        $installedExecutable = Get-StandaloneRecordStringValue -Record $record -PropertyNames @('installedExecutable', 'InstalledExecutable')
        $installRoot = Get-StandaloneRecordStringValue -Record $record -PropertyNames @('installRoot', 'InstallRoot')
        $architecture = Get-StandaloneRecordStringValue -Record $record -PropertyNames @('architecture', 'Architecture')
        $resolvedVersion = Get-StandaloneRecordStringValue -Record $record -PropertyNames @('resolvedVersion', 'ResolvedVersion')
        $registrationMode = Get-StandaloneRecordStringValue -Record $record -PropertyNames @('mode', 'Mode', 'RegistrationMode')
        $registrationTarget = Get-StandaloneRecordStringValue -Record $record -PropertyNames @('target', 'Target', 'RegistrationTarget')
        $installerOwned = $false

        if (-not [string]::IsNullOrWhiteSpace($installedExecutable)) {
            $ownership = Resolve-StandaloneInstallerOwnershipFromExecutable -InstalledExecutable $installedExecutable
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
        $stateKey = Resolve-ClientStateKey -ClientId ([string]$registration.ClientId) -RegistrationMode ([string]$registration.RegistrationMode)
        if ($registrationMap.Contains($stateKey)) {
            $existing = $registrationMap[$stateKey]
            $clientBaseId = Resolve-ClientBaseId -ClientId ([string]$registration.ClientId)
            $collectionName = Get-StandaloneJsonCollectionName -ClientBaseId $clientBaseId
            $existingTarget = [string]$existing.RegistrationTarget
            $liveTarget = [string]$registration.RegistrationTarget
            $existingTargetHasRegistration = $false
            $liveTargetHasRegistration = -not [string]::IsNullOrWhiteSpace($liveTarget)
            if (-not [string]::IsNullOrWhiteSpace($collectionName)) {
                if (-not [string]::IsNullOrWhiteSpace($existingTarget)) {
                    $existingTargetHasRegistration = Test-StandaloneJsonConfigRegistration -CollectionName $collectionName -ConfigPath $existingTarget
                }
            }

            if ($existingTargetHasRegistration) {
                $existingLiveExecutable = Get-StandaloneJsonRegisteredExecutable -CollectionName $collectionName -ConfigPath $existingTarget
                $existingLiveOwnership = Resolve-StandaloneInstallerOwnershipFromExecutable -InstalledExecutable $existingLiveExecutable
                $existing.InstalledExecutable = $existingLiveExecutable
                $existing.InstallRoot = [string]$existingLiveOwnership.InstallRoot
                $existing.Architecture = [string]$existingLiveOwnership.Architecture
                $existing.InstallerOwned = [bool]$existingLiveOwnership.InstallerOwned
                $existing.ResolvedVersion = [string]$existingLiveOwnership.ResolvedVersion
            }

            if ($existingTargetHasRegistration -and $liveTargetHasRegistration -and
                -not (Test-StandaloneInstallerPathEquals -Left $existingTarget -Right $liveTarget)) {
                $registrationMap["$stateKey|target|$($liveTarget.ToLowerInvariant())"] = $registration
                continue
            }

            $preferLiveEvidence = $liveTargetHasRegistration -and (
                [string]::IsNullOrWhiteSpace($existingTarget) -or
                -not $existingTargetHasRegistration -or
                (Test-StandaloneInstallerPathEquals -Left $existingTarget -Right $liveTarget))
            if ($preferLiveEvidence) {
                $existing.RegistrationTarget = $liveTarget
                $existing.RegistrationMode = [string]$registration.RegistrationMode
                $existing.InstalledExecutable = [string]$registration.InstalledExecutable
                $existing.InstallRoot = [string]$registration.InstallRoot
                $existing.Architecture = [string]$registration.Architecture
                $existing.InstallerOwned = [bool]$registration.InstallerOwned
                $existing.ResolvedVersion = [string]$registration.ResolvedVersion
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
function Get-StandaloneDetectedInstallerRegistrationMap {
    param([Parameter(Mandatory)] $State)

    $registrationMap = [ordered]@{}
    foreach ($registration in @(Get-StandaloneDetectedInstallerRegistrations -State $State)) {
        $stateKey = Resolve-ClientStateKey -ClientId ([string]$registration.ClientId) -RegistrationMode ([string]$registration.RegistrationMode)
        $registrationMap[$stateKey] = $registration
        if (-not $registrationMap.Contains([string]$registration.ClientId)) {
            $registrationMap[[string]$registration.ClientId] = $registration
        }
    }

    return $registrationMap
}
function Get-StandaloneDetectedInstallerInstallations {
    param(
        [Parameter(Mandatory)] $State,
        [string]$ExpectedInstallRoot
    )

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
            $trustedInstallBase = Assert-InstallerLocalPathTrusted -Path (Resolve-StandaloneInstallBasePath -ResolvedInstallRoot $trustedInstallRoot -ResolvedArchitecture ([string]$registration.Architecture))
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

        $ownership = Resolve-StandaloneInstallerOwnershipFromExecutable -InstalledExecutable $executable
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

        try {
            $trustedInstallRoot = Assert-InstallerLocalPathTrusted -Path $installRoot
            $trustedInstallBase = Assert-InstallerLocalPathTrusted -Path (Resolve-StandaloneInstallBasePath -ResolvedInstallRoot $trustedInstallRoot -ResolvedArchitecture $arch)
        }
        catch {
            continue
        }

        $key = "{0}|{1}" -f $trustedInstallRoot.ToLowerInvariant(), $arch.ToLowerInvariant()
        $installations[$key] = [ordered]@{
            InstallRoot = $trustedInstallRoot
            Architecture = $arch
            InstallBase = $trustedInstallBase
            InstalledExecutable = $executable
            ResolvedVersion = [string]$record.version
            InstallerOwned = $true
        }
    }

    $candidateRoots = New-Object System.Collections.Generic.List[string]
    foreach ($candidateRoot in @(
            $ExpectedInstallRoot
            [string]$State.lastInstallRoot
        )) {
        if ([string]::IsNullOrWhiteSpace($candidateRoot)) {
            continue
        }

        try {
            $trustedCandidateRoot = Assert-InstallerLocalPathTrusted -Path $candidateRoot
        }
        catch {
            continue
        }

        if (-not $candidateRoots.Contains($trustedCandidateRoot)) {
            $candidateRoots.Add($trustedCandidateRoot)
        }
    }

    foreach ($candidateRoot in $candidateRoots) {
        foreach ($architecture in @(Get-StandaloneKnownArchitectures)) {
            $evidence = Get-StandaloneLiveInstallerManifestEvidence -InstallRoot $candidateRoot -Architecture $architecture
            if ($null -eq $evidence) {
                continue
            }

            $key = "{0}|{1}" -f $candidateRoot.ToLowerInvariant(), $architecture.ToLowerInvariant()
            $installations[$key] = [ordered]@{
                InstallRoot = $candidateRoot
                Architecture = $architecture
                InstallBase = Resolve-StandaloneInstallBasePath -ResolvedInstallRoot $candidateRoot -ResolvedArchitecture $architecture
                InstalledExecutable = [string]$evidence.InstalledExecutable
                ResolvedVersion = [string]$evidence.ResolvedVersion
                InstallerOwned = $true
            }
        }
    }

    return @($installations.Values)
}
function Remove-StandaloneInstallerOwnedEmptyInstallRoots {
    param(
        [object[]]$Installations,
        [switch]$BestEffort
    )

    $installRoots = [ordered]@{}
    $trimChars = @([System.IO.Path]::DirectorySeparatorChar, [System.IO.Path]::AltDirectorySeparatorChar)
    foreach ($installation in @($Installations)) {
        if (-not [bool]$installation.InstallerOwned) {
            continue
        }

        $installRoot = [string]$installation.InstallRoot
        if ([string]::IsNullOrWhiteSpace($installRoot)) {
            continue
        }

        try {
            $trustedInstallRoot = Assert-InstallerLocalPathTrusted -Path $installRoot
        }
        catch {
            if ($BestEffort) {
                continue
            }

            throw
        }

        $volumeRoot = [System.IO.Path]::GetPathRoot($trustedInstallRoot)
        $normalizedRoot = $trustedInstallRoot.TrimEnd($trimChars)
        $normalizedVolumeRoot = $volumeRoot.TrimEnd($trimChars)
        if ([string]::Equals($normalizedRoot, $normalizedVolumeRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            continue
        }

        $key = $normalizedRoot.ToLowerInvariant()
        if (-not $installRoots.Contains($key)) {
            $installRoots[$key] = $trustedInstallRoot
        }
    }

    $removedInstallRoots = @()
    foreach ($installRoot in $installRoots.Values) {
        if (-not (Test-Path -LiteralPath $installRoot)) {
            continue
        }

        $item = Get-Item -LiteralPath $installRoot -Force
        if (-not $item.PSIsContainer) {
            continue
        }

        $hasEntries = $false
        foreach ($entry in @(Get-ChildItem -LiteralPath $installRoot -Force -ErrorAction Stop | Select-Object -First 1)) {
            $hasEntries = $true
        }

        if ($hasEntries) {
            continue
        }

        $removeParameters = @{
            Path = $installRoot
        }
        if ($BestEffort) {
            $removeParameters['BestEffort'] = $true
        }

        Remove-PathIfExists @removeParameters
        $removedInstallRoots += $installRoot
    }

    return @($removedInstallRoots)
}
function Get-StandaloneFallbackRegistrationRecord {
    param(
        [Parameter(Mandatory)] [string]$SelectedClient,
        [Parameter(Mandatory)] [string]$ResolvedInstallRoot,
        [Parameter(Mandatory)] [string]$ResolvedArchitecture
    )

    $clientBaseId = Resolve-ClientBaseId -ClientId $SelectedClient
    $liveEvidence = Get-StandaloneLiveInstallerManifestEvidence -InstallRoot $ResolvedInstallRoot -Architecture $ResolvedArchitecture
    $fallbackExecutable = if ($null -ne $liveEvidence) {
        [string]$liveEvidence.InstalledExecutable
    }
    else {
        try {
            $candidateExecutable = Assert-InstallerLocalPathTrusted -Path (Join-Path (Resolve-StandaloneInstallBasePath -ResolvedInstallRoot $ResolvedInstallRoot -ResolvedArchitecture $ResolvedArchitecture) "current\bin\wpf-devtools-$ResolvedArchitecture.exe")
        }
        catch {
            $candidateExecutable = $null
        }

        if (-not [string]::IsNullOrWhiteSpace($candidateExecutable) -and (Test-Path -LiteralPath $candidateExecutable)) { $candidateExecutable } else { $null }
    }
    $managedRegistrations = @(Get-StandaloneManagedRegistrationsFromInstall -ResolvedInstallRoot $ResolvedInstallRoot -ResolvedArchitecture $ResolvedArchitecture)

    switch ($clientBaseId) {
        'vscode' {
            $registration = $managedRegistrations | Where-Object { [string]$_.ClientId -eq 'vscode' } | Select-Object -First 1
            if ($null -ne $registration) { return $registration }
            break
        }
        'visual-studio' {
            $registration = $managedRegistrations | Where-Object { [string]$_.ClientId -eq 'visual-studio' } | Select-Object -First 1
            if ($null -ne $registration) { return $registration }
            break
        }
        'claude-desktop' {
            $registration = $managedRegistrations | Where-Object { [string]$_.ClientId -eq 'claude-desktop' } | Select-Object -First 1
            if ($null -ne $registration) { return $registration }
            break
        }
        'cursor' {
            $preferredClientId = if ($SelectedClient -eq 'cursor-global') {
                'cursor-global'
            }
            elseif ($SelectedClient -eq 'cursor-project') {
                'cursor-project'
            }
            elseif ($CursorMode -eq 'project') {
                'cursor-project'
            }
            else {
                'cursor-global'
            }

            $registration = $managedRegistrations | Where-Object { [string]$_.ClientId -eq $preferredClientId } | Select-Object -First 1
            if ($null -eq $registration) {
                $registration = $managedRegistrations | Where-Object { [string]$_.ClientId -like 'cursor-*' } | Select-Object -First 1
            }

            if ($null -ne $registration) { return $registration }
            break
        }
    }

    switch ($clientBaseId) {
        'other' {
            return [ordered]@{
                ClientId = 'other'
                RegistrationMode = 'artifact-only'
                RegistrationTarget = (Join-Path (Resolve-StandaloneInstallBasePath -ResolvedInstallRoot $ResolvedInstallRoot -ResolvedArchitecture $ResolvedArchitecture) 'client-registration\other.mcpServers.json')
                InstalledExecutable = $fallbackExecutable
                InstallRoot = $ResolvedInstallRoot
                Architecture = $ResolvedArchitecture
            }
        }
        'claude-code' {
            return [ordered]@{
                ClientId = 'claude-code'
                RegistrationMode = 'cli'
                RegistrationTarget = 'claude'
                InstalledExecutable = $fallbackExecutable
                InstallRoot = $ResolvedInstallRoot
                Architecture = $ResolvedArchitecture
            }
        }
        'codex' {
            return [ordered]@{
                ClientId = 'codex'
                RegistrationMode = 'cli'
                RegistrationTarget = 'codex'
                InstalledExecutable = $fallbackExecutable
                InstallRoot = $ResolvedInstallRoot
                Architecture = $ResolvedArchitecture
            }
        }
        'grok' {
            return [ordered]@{
                ClientId = 'grok'
                RegistrationMode = 'cli'
                RegistrationTarget = 'grok'
                InstalledExecutable = $fallbackExecutable
                InstallRoot = $ResolvedInstallRoot
                Architecture = $ResolvedArchitecture
            }
        }
        default { return $null }
    }
}
function Test-StandaloneRegistrationMatchesInstallRoot {
    param(
        $RegistrationRecord,
        [string]$ExpectedInstallRoot
    )

    if ([string]::IsNullOrWhiteSpace($ExpectedInstallRoot)) {
        return $true
    }

    if ($null -eq $RegistrationRecord) {
        return $false
    }

    $recordInstallRoot = Get-StandaloneRecordStringValue -Record $RegistrationRecord -PropertyNames @('installRoot', 'InstallRoot')
    if (-not [string]::IsNullOrWhiteSpace($recordInstallRoot)) {
        return (Test-StandaloneInstallerPathEquals -Left $recordInstallRoot -Right $ExpectedInstallRoot)
    }

    $installedExecutable = Get-StandaloneRecordStringValue -Record $RegistrationRecord -PropertyNames @('installedExecutable', 'InstalledExecutable', 'executable', 'Executable')
    if ([string]::IsNullOrWhiteSpace($installedExecutable)) {
        return $false
    }

    $ownership = Resolve-StandaloneInstallerOwnershipFromExecutable -InstalledExecutable $installedExecutable
    return ([bool]$ownership.InstallerOwned -and (Test-StandaloneInstallerPathEquals -Left ([string]$ownership.InstallRoot) -Right $ExpectedInstallRoot))
}
function Test-StandaloneExplicitRootCliUninstallNoOp {
    param(
        $RegistrationRecord,
        [bool]$InstallRootWasSpecified
    )

    if (-not $InstallRootWasSpecified -or $null -eq $RegistrationRecord) {
        return $false
    }

    $mode = Get-StandaloneRecordStringValue -Record $RegistrationRecord -PropertyNames @('mode', 'Mode', 'RegistrationMode')
    if (-not [string]::Equals($mode, 'cli', [System.StringComparison]::OrdinalIgnoreCase)) {
        return $false
    }

    $installedExecutable = Get-StandaloneRecordStringValue -Record $RegistrationRecord -PropertyNames @('installedExecutable', 'InstalledExecutable')
    return [string]::IsNullOrWhiteSpace($installedExecutable)
}
function Test-StandaloneInstallerRunningElevated {
    if ($script:WpfDevToolsInstallerTestModeEnabled -and
        -not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_ASSUME_ELEVATED)) {
        $overrideValue = ([string]$env:WPFDEVTOOLS_INSTALLER_ASSUME_ELEVATED).Trim().ToLowerInvariant()
        return @('1', 'true', 'yes', 'on') -contains $overrideValue
    }

    try {
        $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = [System.Security.Principal.WindowsPrincipal]::new($identity)
        return $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
    }
    catch {
        return $false
    }
}
