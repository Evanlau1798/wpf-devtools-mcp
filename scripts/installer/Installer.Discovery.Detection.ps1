function Get-JsonRegistrationEvidence {
    param(
        [Parameter(Mandatory)] [string]$ClientId,
        [Parameter(Mandatory)] [string]$CollectionName,
        [Parameter(Mandatory)] [string]$ConfigPath
    )

    if (-not (Test-JsonConfigRegistration -CollectionName $CollectionName -ConfigPath $ConfigPath)) {
        return $null
    }

    $root = Get-ExistingConfigMap -Path $ConfigPath
    $servers = Get-ConfigCollectionMap -Root $root -CollectionName $CollectionName
    $command = [string]$servers['wpf-devtools'].command
    $ownership = Resolve-InstallerOwnershipFromExecutable -InstalledExecutable $command
    return (New-DetectedInstallerRegistration `
            -ClientId $ClientId `
            -RegistrationMode 'json-file' `
            -RegistrationTarget $ConfigPath `
            -InstalledExecutable $command `
            -InstallRoot ([string]$ownership.InstallRoot) `
            -Architecture ([string]$ownership.Architecture) `
            -InstallerOwned ([bool]$ownership.InstallerOwned) `
            -EvidenceSource 'json-file' `
            -ResolvedVersion ([string]$ownership.ResolvedVersion) `
            -LastVerifiedUtc $null)
}

function Get-CursorRegistrationEvidences {
    param($StateEvidenceRecords)

    $candidatePaths = New-Object System.Collections.Generic.List[string]
    foreach ($stateEvidence in @($StateEvidenceRecords)) {
        $stateTarget = Get-InstallerRecordStringValueCore -Record $stateEvidence -PropertyNames @('RegistrationTarget', 'target')
        if (-not [string]::IsNullOrWhiteSpace($stateTarget)) {
            $candidatePaths.Add($stateTarget)
        }
    }

    foreach ($configPath in @(
            (Resolve-CursorProjectConfigPath)
            (Resolve-CursorGlobalConfigPath)
        )) {
        if ([string]::IsNullOrWhiteSpace($configPath)) {
            continue
        }

        $alreadyAdded = $false
        foreach ($existingPath in $candidatePaths) {
            if (Test-InstallerPathEqualsCore -Left $existingPath -Right $configPath) {
                $alreadyAdded = $true
                break
            }
        }

        if (-not $alreadyAdded) {
            $candidatePaths.Add($configPath)
        }
    }

    $globalConfigPath = Resolve-CursorGlobalConfigPath
    $registrations = @()
    foreach ($configPath in $candidatePaths) {
        if (-not (Test-JsonConfigRegistration -CollectionName 'mcpServers' -ConfigPath $configPath)) {
            continue
        }

        $root = Get-ExistingConfigMap -Path $configPath
        $servers = Get-ConfigCollectionMap -Root $root -CollectionName 'mcpServers'
        $command = [string]$servers['wpf-devtools'].command
        $ownership = Resolve-InstallerOwnershipFromExecutable -InstalledExecutable $command
        $registrationMode = $null
        foreach ($stateEvidence in @($StateEvidenceRecords)) {
            $stateTarget = Get-InstallerRecordStringValueCore -Record $stateEvidence -PropertyNames @('RegistrationTarget', 'target')
            if (Test-InstallerPathEqualsCore -Left $stateTarget -Right $configPath) {
                $registrationMode = Get-InstallerRecordStringValueCore -Record $stateEvidence -PropertyNames @('RegistrationMode', 'mode', 'ClientId')
                break
            }
        }

        if ([string]::IsNullOrWhiteSpace($registrationMode)) {
            $registrationMode = if (-not [string]::IsNullOrWhiteSpace($globalConfigPath) -and (Test-InstallerPathEqualsCore -Left $configPath -Right $globalConfigPath)) {
                'cursor-global'
            }
            else {
                'cursor-project'
            }
        }

        $registrations += (New-DetectedInstallerRegistration `
                -ClientId $registrationMode `
                -RegistrationMode $registrationMode `
                -RegistrationTarget $configPath `
                -InstalledExecutable $command `
                -InstallRoot ([string]$ownership.InstallRoot) `
                -Architecture ([string]$ownership.Architecture) `
                -InstallerOwned ([bool]$ownership.InstallerOwned) `
                -EvidenceSource 'json-file' `
                -ResolvedVersion ([string]$ownership.ResolvedVersion) `
                -LastVerifiedUtc $null)
    }

    return @($registrations)
}

function Get-CliRegistrationEvidence {
    param(
        [Parameter(Mandatory)] [string]$ClientId,
        [Parameter(Mandatory)] [string]$CommandName
    )

    $verification = Invoke-VerificationCommand -Command $CommandName -Arguments @('mcp', 'list') -ExpectedToken 'wpf-devtools' -ExpectPresent $true
    if (-not $verification.Succeeded) {
        return $null
    }

    $installedExecutable = Get-WpfDevToolsExecutableFromText -Text ([string]$verification.Output)
    $ownership = Resolve-InstallerOwnershipFromExecutable -InstalledExecutable $installedExecutable
    return (New-DetectedInstallerRegistration `
            -ClientId $ClientId `
            -RegistrationMode 'cli' `
            -RegistrationTarget $CommandName `
            -InstalledExecutable $installedExecutable `
            -InstallRoot ([string]$ownership.InstallRoot) `
            -Architecture ([string]$ownership.Architecture) `
            -InstallerOwned ([bool]$ownership.InstallerOwned) `
            -EvidenceSource 'cli' `
            -ResolvedVersion ([string]$ownership.ResolvedVersion) `
            -LastVerifiedUtc $null)
}

function Get-JsonCollectionNameForClientId {
    param([Parameter(Mandatory)] [string]$ClientId)

    switch (Resolve-ClientBaseId -ClientId $ClientId) {
        'vscode' { return 'servers' }
        'visual-studio' { return 'servers' }
        'claude-desktop' { return 'mcpServers' }
        'cursor' { return 'mcpServers' }
        default { return $null }
    }
}

function Get-ManagedRegistrationEvidenceFromInstall {
    param(
        [Parameter(Mandatory)] [string]$ClientId,
        [Parameter(Mandatory)] [string]$RegistrationTarget,
        [Parameter(Mandatory)] [string]$ResolvedInstallRoot,
        [Parameter(Mandatory)] [string]$ResolvedArchitecture,
        [string]$InstalledExecutable,
        [string]$ResolvedVersion
    )

    $collectionName = Get-JsonCollectionNameForClientId -ClientId $ClientId
    if ([string]::IsNullOrWhiteSpace($collectionName) -or [string]::IsNullOrWhiteSpace($RegistrationTarget)) {
        return $null
    }

    if (-not (Test-JsonConfigRegistration -CollectionName $collectionName -ConfigPath $RegistrationTarget)) {
        return $null
    }

    $root = Get-ExistingConfigMap -Path $RegistrationTarget
    $servers = Get-ConfigCollectionMap -Root $root -CollectionName $collectionName
    $command = [string]$servers['wpf-devtools'].command
    $ownership = Resolve-InstallerOwnershipFromExecutable -InstalledExecutable $command
    if (-not [bool]$ownership.InstallerOwned) {
        return $null
    }

    if (-not [string]::IsNullOrWhiteSpace([string]$ownership.InstallRoot) -and -not (Test-InstallerPathEqualsCore -Left ([string]$ownership.InstallRoot) -Right $ResolvedInstallRoot)) {
        return $null
    }

    if (-not [string]::IsNullOrWhiteSpace([string]$ownership.Architecture) -and -not [string]::Equals([string]$ownership.Architecture, $ResolvedArchitecture, [System.StringComparison]::OrdinalIgnoreCase)) {
        return $null
    }

    $registrationMode = if ($ClientId -like 'cursor-*') { $ClientId } else { 'json-file' }
    return (New-DetectedInstallerRegistration `
            -ClientId $ClientId `
            -RegistrationMode $registrationMode `
            -RegistrationTarget $RegistrationTarget `
            -InstalledExecutable $(if (-not [string]::IsNullOrWhiteSpace($command)) { $command } else { $InstalledExecutable }) `
            -InstallRoot $(if (-not [string]::IsNullOrWhiteSpace([string]$ownership.InstallRoot)) { [string]$ownership.InstallRoot } else { $ResolvedInstallRoot }) `
            -Architecture $(if (-not [string]::IsNullOrWhiteSpace([string]$ownership.Architecture)) { [string]$ownership.Architecture } else { $ResolvedArchitecture }) `
            -InstallerOwned $true `
            -EvidenceSource 'install-manifest' `
            -ResolvedVersion $(if (-not [string]::IsNullOrWhiteSpace([string]$ownership.ResolvedVersion)) { [string]$ownership.ResolvedVersion } else { $ResolvedVersion }) `
            -LastVerifiedUtc $null)
}

function Get-ManagedRegistrationsFromInstall {
    param(
        [Parameter(Mandatory)] [string]$ResolvedInstallRoot,
        [Parameter(Mandatory)] [string]$ResolvedArchitecture
    )

    $liveEvidence = Get-LiveInstallerManifestEvidence -InstallRoot $ResolvedInstallRoot -Architecture $ResolvedArchitecture
    if ($null -eq $liveEvidence) {
        return @()
    }

    try {
        $manifestPath = Assert-InstallerLocalPathTrusted -Path (Join-Path (Resolve-InstallBasePath -ResolvedInstallRoot $ResolvedInstallRoot -ResolvedArchitecture $ResolvedArchitecture) 'install-manifest.json')
    }
    catch {
        return @()
    }

    if (-not (Test-Path -LiteralPath $manifestPath)) {
        return @()
    }

    try {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    }
    catch {
        return @()
    }

    $managedTargets = $manifest.PSObject.Properties['managedRegistrationTargets']
    if ($null -eq $managedTargets -or $null -eq $managedTargets.Value) {
        return @()
    }

    $registrations = New-Object System.Collections.Generic.List[object]
    foreach ($property in $managedTargets.Value.PSObject.Properties) {
        $targetPath = [string]$property.Value
        if ([string]::IsNullOrWhiteSpace($targetPath)) {
            continue
        }

        $registration = Get-ManagedRegistrationEvidenceFromInstall `
            -ClientId ([string]$property.Name) `
            -RegistrationTarget $targetPath `
            -ResolvedInstallRoot $ResolvedInstallRoot `
            -ResolvedArchitecture $ResolvedArchitecture `
            -InstalledExecutable ([string]$liveEvidence.InstalledExecutable) `
            -ResolvedVersion ([string]$liveEvidence.ResolvedVersion)
        if ($null -ne $registration) {
            $registrations.Add($registration)
        }
    }

    return @($registrations.ToArray())
}

function Add-InstallerDiscoveryCandidateRoot {
    param(
        [Parameter(Mandatory)] $Roots,
        [string]$Candidate
    )

    if ([string]::IsNullOrWhiteSpace($Candidate)) {
        return
    }

    foreach ($existing in $Roots) {
        if (Test-InstallerPathEqualsCore -Left $existing -Right $Candidate) {
            return
        }
    }

    $Roots.Add($Candidate)
}

function Get-InstallerDiscoveryCandidateRoots {
    param([Parameter(Mandatory)] $State)

    $roots = New-Object System.Collections.Generic.List[string]
    Add-InstallerDiscoveryCandidateRoot -Roots $roots -Candidate ([string]$State.lastInstallRoot)

    $preferredInstallRootResolver = Get-Command 'Resolve-PreferredInstallRoot' -ErrorAction SilentlyContinue
    if ($null -ne $preferredInstallRootResolver) {
        Add-InstallerDiscoveryCandidateRoot -Roots $roots -Candidate (Resolve-PreferredInstallRoot)
    }

    if ($null -ne $State.registrations) {
        foreach ($registrationEntry in $State.registrations.GetEnumerator()) {
            $record = $registrationEntry.Value
            $installRoot = Get-InstallerRecordStringValueCore -Record $record -PropertyNames @('installRoot', 'InstallRoot')
            if ([string]::IsNullOrWhiteSpace($installRoot)) {
                $installedExecutable = Get-InstallerRecordStringValueCore -Record $record -PropertyNames @('installedExecutable', 'InstalledExecutable')
                $ownership = Resolve-InstallerOwnershipFromExecutable -InstalledExecutable $installedExecutable
                $installRoot = [string]$ownership.InstallRoot
            }

            Add-InstallerDiscoveryCandidateRoot -Roots $roots -Candidate $installRoot
        }
    }

    if ($null -ne $State.architectures) {
        foreach ($architectureEntry in $State.architectures.GetEnumerator()) {
            $record = $architectureEntry.Value
            $installRoot = Get-InstallerRecordStringValueCore -Record $record -PropertyNames @('installRoot', 'InstallRoot')
            if ([string]::IsNullOrWhiteSpace($installRoot)) {
                $installedExecutable = Get-InstallerRecordStringValueCore -Record $record -PropertyNames @('executable', 'Executable')
                $ownership = Resolve-InstallerOwnershipFromExecutable -InstalledExecutable $installedExecutable
                $installRoot = [string]$ownership.InstallRoot
            }

            Add-InstallerDiscoveryCandidateRoot -Roots $roots -Candidate $installRoot
        }
    }

    return @($roots.ToArray())
}

function Get-DetectedInstallerRegistrations {
    param([Parameter(Mandatory)] $State)

    $detected = [ordered]@{}
    foreach ($client in Get-SupportedClients) {
        $clientId = [string]$client.Id
        $stateEvidenceMap = [ordered]@{}
        foreach ($stateEvidence in @(Get-StateRegistrationEvidencesForClient -State $State -ClientId $clientId)) {
            $stateEvidenceMap[[string]$stateEvidence.ClientId] = $stateEvidence
        }

        $externalEvidenceMap = [ordered]@{}
        foreach ($externalEvidence in @(switch ($clientId) {
                    'claude-code' { Get-CliRegistrationEvidence -ClientId $clientId -CommandName 'claude'; break }
                    'codex' { Get-CliRegistrationEvidence -ClientId $clientId -CommandName 'codex'; break }
                    'cursor' { Get-CursorRegistrationEvidences -StateEvidenceRecords @($stateEvidenceMap.Values); break }
                    'vscode' { Get-JsonRegistrationEvidence -ClientId $clientId -CollectionName 'servers' -ConfigPath (Resolve-VsCodeConfigPath); break }
                    'visual-studio' { Get-JsonRegistrationEvidence -ClientId $clientId -CollectionName 'servers' -ConfigPath (Resolve-VisualStudioConfigPath); break }
                    'claude-desktop' { Get-JsonRegistrationEvidence -ClientId $clientId -CollectionName 'mcpServers' -ConfigPath (Resolve-ClaudeDesktopConfigPath); break }
                    default { $null; break }
                })) {
            if ($null -ne $externalEvidence) {
                $externalEvidenceMap[[string]$externalEvidence.ClientId] = $externalEvidence
            }
        }

        $keys = New-Object System.Collections.Generic.List[string]
        foreach ($key in $stateEvidenceMap.Keys + $externalEvidenceMap.Keys) {
            if (-not $keys.Contains([string]$key)) {
                $keys.Add([string]$key)
            }
        }

        foreach ($key in $keys) {
            if ($stateEvidenceMap.Contains($key) -and $externalEvidenceMap.Contains($key)) {
                $detected[$key] = Merge-DetectedInstallerRegistration -Primary $stateEvidenceMap[$key] -Secondary $externalEvidenceMap[$key]
                continue
            }

            if ($stateEvidenceMap.Contains($key)) {
                $detected[$key] = $stateEvidenceMap[$key]
                continue
            }

            if ($externalEvidenceMap.Contains($key)) {
                $detected[$key] = $externalEvidenceMap[$key]
            }
        }
    }

    foreach ($installRoot in @(Get-InstallerDiscoveryCandidateRoots -State $State)) {
        foreach ($architecture in @(Get-InstallerKnownArchitecturesCore)) {
            foreach ($manifestRegistration in @(Get-ManagedRegistrationsFromInstall -ResolvedInstallRoot $installRoot -ResolvedArchitecture $architecture)) {
                $stateKey = Resolve-ClientStateKey -ClientId ([string]$manifestRegistration.ClientId) -RegistrationMode ([string]$manifestRegistration.RegistrationMode)
                if ($detected.Contains($stateKey)) {
                    $detected[$stateKey] = Merge-DetectedInstallerRegistration -Primary $detected[$stateKey] -Secondary $manifestRegistration
                    continue
                }

                $detected[$stateKey] = $manifestRegistration
            }
        }
    }

    return @($detected.Values)
}

function Get-DetectedInstallerRegistrationMap {
    param([Parameter(Mandatory)] $State)

    $map = [ordered]@{}
    foreach ($registration in @(Get-DetectedInstallerRegistrations -State $State)) {
        $map[[string]$registration.ClientId] = $registration
    }

    return $map
}

function Get-DetectedInstallerInstallations {
    param([Parameter(Mandatory)] $State)

    $installations = [ordered]@{}
    foreach ($registration in @(Get-DetectedInstallerRegistrations -State $State)) {
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
        $ownership = Resolve-InstallerOwnershipFromExecutable -InstalledExecutable ([string]$record.executable)
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
            InstalledExecutable = [string]$record.executable
            ResolvedVersion = [string]$record.version
            InstallerOwned = $true
        }
    }

    foreach ($candidateRoot in @(Get-InstallerDiscoveryCandidateRoots -State $State)) {
        foreach ($architecture in @(Get-InstallerKnownArchitecturesCore)) {
            $evidence = Get-LiveInstallerManifestEvidence -InstallRoot $candidateRoot -Architecture $architecture
            if ($null -eq $evidence) {
                continue
            }

            $key = "{0}|{1}" -f $candidateRoot.ToLowerInvariant(), $architecture.ToLowerInvariant()
            $installations[$key] = [ordered]@{
                InstallRoot = $candidateRoot
                Architecture = $architecture
                InstallBase = Resolve-InstallBasePath -ResolvedInstallRoot $candidateRoot -ResolvedArchitecture $architecture
                InstalledExecutable = [string]$evidence.InstalledExecutable
                ResolvedVersion = [string]$evidence.ResolvedVersion
                InstallerOwned = $true
            }
        }
    }

    return @($installations.Values)
}
