if (-not (Get-Command Resolve-InstallerDiscoveryAbsolutePath -ErrorAction SilentlyContinue)) {
    function Resolve-InstallerDiscoveryAbsolutePath {
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

        $resolvedPath = Resolve-InstallerDiscoveryAbsolutePath -Path $Path
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
        }

        if ($RejectHardLinks -and (Test-Path -LiteralPath $resolvedPath -PathType Leaf) -and (Get-Command Get-InstallerHardLinkCount -ErrorAction SilentlyContinue)) {
            $linkCount = Get-InstallerHardLinkCount -Path $resolvedPath
            if ($linkCount -gt 1) {
                throw "Installer path '$resolvedPath' is blocked because hardlinked installer write targets are not trusted."
            }
        }

        return $resolvedPath
    }
}

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

    foreach ($rawLine in ($Text -split "`r?`n")) {
        $line = [string]$rawLine
        if ($line -notmatch '^\s*(?:[-*]\s*)?["'']?wpf-devtools["'']?(?:\s|:|=|$)') {
            continue
        }

        $match = [regex]::Match($line, '(?<path>[A-Za-z]:\\[^`"\r\n]*wpf-devtools-(x64|x86|arm64)\.exe)', 'IgnoreCase')
        if ($match.Success) {
            return [string]$match.Groups['path'].Value
        }
    }

    return $null
}

function Normalize-InstallerPathCore {
    param([string]$PathValue)

    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        return $null
    }

    $trimmed = [string]$PathValue.Trim().Trim('"')
    if ([string]::IsNullOrWhiteSpace($trimmed)) {
        return $null
    }

    $normalizedSeparators = $trimmed.Replace('/', '\')
    try {
        return [System.IO.Path]::GetFullPath($normalizedSeparators)
    }
    catch {
        return $normalizedSeparators
    }
}

function Test-InstallerPathEqualsCore {
    param(
        [string]$Left,
        [string]$Right
    )

    $normalizedLeft = Normalize-InstallerPathCore -PathValue $Left
    $normalizedRight = Normalize-InstallerPathCore -PathValue $Right
    if ([string]::IsNullOrWhiteSpace($normalizedLeft) -or [string]::IsNullOrWhiteSpace($normalizedRight)) {
        return $false
    }

    return [string]::Equals($normalizedLeft, $normalizedRight, [System.StringComparison]::OrdinalIgnoreCase)
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

    try {
        $trustedInstalledExecutable = Assert-InstallerLocalPathTrusted -Path $InstalledExecutable
    }
    catch {
        return $result
    }

    $architectureMatch = [regex]::Match($trustedInstalledExecutable, 'wpf-devtools-(x64|x86|arm64)\.exe', 'IgnoreCase')
    if ($architectureMatch.Success) {
        $result.Architecture = [string]$architectureMatch.Groups[1].Value.ToLowerInvariant()
    }

    $binDirectory = Split-Path -Parent $trustedInstalledExecutable
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

    try {
        $manifestPath = Assert-InstallerLocalPathTrusted -Path (Join-Path $installBase 'install-manifest.json')
    }
    catch {
        return $result
    }

    if (-not (Test-Path -LiteralPath $manifestPath)) {
        return $result
    }

    try {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        $manifestExecutable = [string]$manifest.executable
        if (-not [string]::IsNullOrWhiteSpace($manifestExecutable) -and (Test-InstallerPathEqualsCore -Left $manifestExecutable -Right $trustedInstalledExecutable)) {
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
    $preferSecondaryFields = @(
        'RegistrationMode',
        'RegistrationTarget',
        'InstalledExecutable',
        'InstallRoot',
        'Architecture',
        'EvidenceSource',
        'ResolvedVersion'
    )
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
        $preferSecondary = $preferSecondaryFields -contains $field
        $merged[$field] = if ($preferSecondary) {
            if (-not [string]::IsNullOrWhiteSpace([string]$secondaryValue)) { $secondaryValue } else { $primaryValue }
        }
        else {
            if (-not [string]::IsNullOrWhiteSpace([string]$primaryValue)) { $primaryValue } else { $secondaryValue }
        }
    }

    $merged['InstallerOwned'] = ([bool]$Primary.InstallerOwned -or [bool]$Secondary.InstallerOwned)

    $primaryEvidenceSource = [string]$Primary.EvidenceSource
    $secondaryEvidenceSource = [string]$Secondary.EvidenceSource
    $primaryTarget = [string]$Primary.RegistrationTarget
    $secondaryTarget = [string]$Secondary.RegistrationTarget
    $primaryClientBaseId = Resolve-ClientBaseId -ClientId ([string]$Primary.ClientId)
    $collectionName = switch ($primaryClientBaseId) {
        'vscode' { 'servers' }
        'visual-studio' { 'servers' }
        'claude-desktop' { 'mcpServers' }
        'cursor' { 'mcpServers' }
        default { $null }
    }

    if ([string]::Equals($primaryEvidenceSource, 'state', [System.StringComparison]::OrdinalIgnoreCase) -and
        [string]::Equals($secondaryEvidenceSource, 'json-file', [System.StringComparison]::OrdinalIgnoreCase) -and
        -not [string]::IsNullOrWhiteSpace($collectionName) -and
        -not [string]::IsNullOrWhiteSpace($primaryTarget) -and
        -not [string]::IsNullOrWhiteSpace($secondaryTarget)) {
        $primaryHasRegistration = Test-JsonConfigRegistration -CollectionName $collectionName -ConfigPath $primaryTarget
        $secondaryHasRegistration = Test-JsonConfigRegistration -CollectionName $collectionName -ConfigPath $secondaryTarget
        if ($primaryHasRegistration -and $secondaryHasRegistration -and -not (Test-InstallerPathEqualsCore -Left $primaryTarget -Right $secondaryTarget)) {
            $merged['RegistrationTarget'] = $primaryTarget
            $merged['RegistrationMode'] = if (-not [string]::IsNullOrWhiteSpace([string]$Primary.RegistrationMode)) { [string]$Primary.RegistrationMode } else { [string]$merged['RegistrationMode'] }
            $merged['InstalledExecutable'] = if (-not [string]::IsNullOrWhiteSpace([string]$Primary.InstalledExecutable)) { [string]$Primary.InstalledExecutable } else { [string]$merged['InstalledExecutable'] }
            $merged['InstallRoot'] = if (-not [string]::IsNullOrWhiteSpace([string]$Primary.InstallRoot)) { [string]$Primary.InstallRoot } else { [string]$merged['InstallRoot'] }
            $merged['Architecture'] = if (-not [string]::IsNullOrWhiteSpace([string]$Primary.Architecture)) { [string]$Primary.Architecture } else { [string]$merged['Architecture'] }
            $merged['ResolvedVersion'] = if (-not [string]::IsNullOrWhiteSpace([string]$Primary.ResolvedVersion)) { [string]$Primary.ResolvedVersion } else { [string]$merged['ResolvedVersion'] }
        }
    }

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
            -ClientId (Resolve-ClientStateKey -ClientId $ClientId -RegistrationMode ([string]$registration.mode)) `
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

function Get-StateRegistrationEvidencesForClient {
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)] [string]$ClientId
    )

    if ($ClientId -ne 'cursor') {
        $stateEvidence = Get-StateRegistrationEvidence -State $State -ClientId $ClientId
        return @($(if ($null -ne $stateEvidence) { $stateEvidence }))
    }

    return @($State.registrations.Keys |
            Where-Object { $_ -eq 'cursor' -or $_ -like 'cursor-*' } |
            Sort-Object |
            ForEach-Object {
                Get-StateRegistrationEvidence -State $State -ClientId ([string]$_)
            } |
            Where-Object { $null -ne $_ })
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
