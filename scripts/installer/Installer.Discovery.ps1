function New-DetectedInstallerRegistration {
    param(
        [Parameter(Mandatory)] [string]$ClientId,
        [string]$RegistrationMode,
        [string]$RegistrationTarget,
        [string]$InstalledExecutable,
        [string]$InstallRoot,
        [string]$Architecture,
        [bool]$InstallerOwned,
        [string]$EvidenceSource,
        [string]$ResolvedVersion,
        [string]$LastVerifiedUtc
    )

    return [ordered]@{
        ClientId = $ClientId
        RegistrationMode = $RegistrationMode
        RegistrationTarget = $RegistrationTarget
        InstalledExecutable = $InstalledExecutable
        InstallRoot = $InstallRoot
        Architecture = $Architecture
        InstallerOwned = $InstallerOwned
        EvidenceSource = $EvidenceSource
        ResolvedVersion = $ResolvedVersion
        LastVerifiedUtc = $LastVerifiedUtc
    }
}

function Get-WpfDevToolsExecutableFromText {
    param([string]$Text)

    if ([string]::IsNullOrWhiteSpace($Text)) {
        return $null
    }

    $match = [regex]::Match($Text, '(?<path>[A-Za-z]:\\[^`"\r\n]*wpf-devtools-(x64|x86|arm64)\.exe)', 'IgnoreCase')
    if (-not $match.Success) {
        return $null
    }

    return [string]$match.Groups['path'].Value
}

function Resolve-InstallerOwnershipFromExecutable {
    param([string]$InstalledExecutable)

    $result = [ordered]@{
        InstallerOwned = $false
        InstalledExecutable = $InstalledExecutable
        InstallBase = $null
        InstallRoot = $null
        Architecture = $null
        ResolvedVersion = $null
    }

    if ([string]::IsNullOrWhiteSpace($InstalledExecutable)) {
        return $result
    }

    $architectureMatch = [regex]::Match($InstalledExecutable, 'wpf-devtools-(x64|x86|arm64)\.exe', 'IgnoreCase')
    if ($architectureMatch.Success) {
        $result.Architecture = [string]$architectureMatch.Groups[1].Value.ToLowerInvariant()
    }

    $binDirectory = Split-Path -Parent $InstalledExecutable
    if ([string]::IsNullOrWhiteSpace($binDirectory)) {
        return $result
    }

    $currentDirectory = Split-Path -Parent $binDirectory
    if ([string]::IsNullOrWhiteSpace($currentDirectory)) {
        return $result
    }

    $installBase = Split-Path -Parent $currentDirectory
    if ([string]::IsNullOrWhiteSpace($installBase)) {
        return $result
    }

    $manifestPath = Join-Path $installBase 'install-manifest.json'
    if (-not (Test-Path $manifestPath)) {
        return $result
    }

    try {
        $manifest = Get-Content -Path $manifestPath -Raw | ConvertFrom-Json
        $manifestExecutable = [string]$manifest.executable
        if (-not [string]::IsNullOrWhiteSpace($manifestExecutable) -and $manifestExecutable -eq $InstalledExecutable) {
            $result.InstallerOwned = $true
            $result.InstallBase = $installBase
            $result.InstallRoot = [string]$manifest.installRoot
            if ([string]::IsNullOrWhiteSpace($result.InstallRoot)) {
                $result.InstallRoot = Split-Path -Parent $installBase
            }

            if ([string]::IsNullOrWhiteSpace([string]$result.Architecture)) {
                $result.Architecture = [string]$manifest.architecture
            }

            $result.ResolvedVersion = [string]$manifest.version
        }
    }
    catch {
    }

    return $result
}

function Merge-DetectedInstallerRegistration {
    param(
        [Parameter(Mandatory)] $Primary,
        [Parameter(Mandatory)] $Secondary
    )

    $merged = [ordered]@{}
    foreach ($field in @(
            'ClientId',
            'RegistrationMode',
            'RegistrationTarget',
            'InstalledExecutable',
            'InstallRoot',
            'Architecture',
            'EvidenceSource',
            'ResolvedVersion',
            'LastVerifiedUtc')) {
        $primaryValue = $Primary[$field]
        $secondaryValue = $Secondary[$field]
        $merged[$field] = if (-not [string]::IsNullOrWhiteSpace([string]$primaryValue)) { $primaryValue } else { $secondaryValue }
    }

    $merged['InstallerOwned'] = ([bool]$Primary.InstallerOwned -or [bool]$Secondary.InstallerOwned)
    return $merged
}

function Get-StateRegistrationEvidence {
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)] [string]$ClientId
    )

    if (-not $State.registrations.Contains($ClientId)) {
        return $null
    }

    $registration = $State.registrations[$ClientId]
    $ownership = Resolve-InstallerOwnershipFromExecutable -InstalledExecutable ([string]$registration.installedExecutable)
    return (New-DetectedInstallerRegistration `
            -ClientId $ClientId `
            -RegistrationMode ([string]$registration.mode) `
            -RegistrationTarget ([string]$registration.target) `
            -InstalledExecutable ([string]$registration.installedExecutable) `
            -InstallRoot ($(if (-not [string]::IsNullOrWhiteSpace([string]$registration.installRoot)) { [string]$registration.installRoot } else { [string]$ownership.InstallRoot })) `
            -Architecture ($(if (-not [string]::IsNullOrWhiteSpace([string]$registration.architecture)) { [string]$registration.architecture } else { [string]$ownership.Architecture })) `
            -InstallerOwned ([bool]$ownership.InstallerOwned) `
            -EvidenceSource 'state' `
            -ResolvedVersion ($(if (-not [string]::IsNullOrWhiteSpace([string]$registration.resolvedVersion)) { [string]$registration.resolvedVersion } else { [string]$ownership.ResolvedVersion })) `
            -LastVerifiedUtc ([string]$registration.lastVerifiedUtc))
}

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

function Get-DetectedCliRegistrations {
    return @(
        (Get-CliRegistrationEvidence -ClientId 'claude-code' -CommandName 'claude')
        (Get-CliRegistrationEvidence -ClientId 'codex' -CommandName 'codex')
    ) | Where-Object { $null -ne $_ }
}

function Get-DetectedInstallerRegistrations {
    param([Parameter(Mandatory)] $State)

    $detected = [ordered]@{}
    foreach ($client in Get-SupportedClients) {
        $clientId = [string]$client.Id
        $stateEvidence = Get-StateRegistrationEvidence -State $State -ClientId $clientId
        $externalEvidence = switch ($clientId) {
            'claude-code' { Get-CliRegistrationEvidence -ClientId $clientId -CommandName 'claude'; break }
            'codex' { Get-CliRegistrationEvidence -ClientId $clientId -CommandName 'codex'; break }
            'vscode' { Get-JsonRegistrationEvidence -ClientId $clientId -CollectionName 'servers' -ConfigPath (Resolve-VsCodeConfigPath); break }
            'visual-studio' { Get-JsonRegistrationEvidence -ClientId $clientId -CollectionName 'servers' -ConfigPath (Resolve-VisualStudioConfigPath); break }
            'claude-desktop' { Get-JsonRegistrationEvidence -ClientId $clientId -CollectionName 'mcpServers' -ConfigPath (Resolve-ClaudeDesktopConfigPath); break }
            default { $null; break }
        }

        if ($null -ne $stateEvidence -and $null -ne $externalEvidence) {
            $detected[$clientId] = Merge-DetectedInstallerRegistration -Primary $stateEvidence -Secondary $externalEvidence
            continue
        }

        if ($null -ne $stateEvidence) {
            $detected[$clientId] = $stateEvidence
            continue
        }

        if ($null -ne $externalEvidence) {
            $detected[$clientId] = $externalEvidence
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

    return @($installations.Values)
}
