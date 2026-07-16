function Get-StandaloneTrustedRecordedTarget {
    param(
        [Parameter(Mandatory)] [string]$SelectedClient,
        $RegistrationRecord,
        [string[]]$AdditionalAllowedTargets = @()
    )

    $recordedTarget = Get-StandaloneRecordStringValue -Record $RegistrationRecord -PropertyNames @('target', 'Target', 'RegistrationTarget')
    if ([string]::IsNullOrWhiteSpace($recordedTarget)) {
        return $null
    }

    $allowedTargets = New-Object System.Collections.Generic.List[string]
    foreach ($candidate in @($AdditionalAllowedTargets)) {
        Add-StandaloneTrustedTargetCandidate -Targets $allowedTargets -Candidate $candidate
    }

    if ($SelectedClient -like 'cursor*') {
        Add-StandaloneTrustedTargetCandidate -Targets $allowedTargets -Candidate (Get-StandaloneTrustedCursorManifestTarget -SelectedClient $SelectedClient -RegistrationRecord $RegistrationRecord)
    }
    else {
        $manifestClientBaseId = Resolve-ClientBaseId -ClientId $SelectedClient
        if ($manifestClientBaseId -ne 'other' -and $manifestClientBaseId -ne 'claude-code' -and $manifestClientBaseId -ne 'codex' -and $manifestClientBaseId -ne 'grok') {
            Add-StandaloneTrustedTargetCandidate -Targets $allowedTargets -Candidate (Get-StandaloneTrustedManagedJsonRegistrationTarget -SelectedClient $SelectedClient -RegistrationRecord $RegistrationRecord)
        }
    }

    switch ($SelectedClient) {
        'cursor-global' {
            Add-StandaloneTrustedTargetCandidate -Targets $allowedTargets -Candidate (Resolve-StandaloneCursorGlobalConfigPath)
        }
        'cursor-project' {
            Add-StandaloneTrustedTargetCandidate -Targets $allowedTargets -Candidate (Resolve-StandaloneCursorProjectConfigPath)
        }
        default {
            $clientBaseId = Resolve-ClientBaseId -ClientId $SelectedClient
            switch ($clientBaseId) {
                'vscode' { Add-StandaloneTrustedTargetCandidate -Targets $allowedTargets -Candidate (Resolve-StandaloneVsCodeConfigPath) }
                'visual-studio' { Add-StandaloneTrustedTargetCandidate -Targets $allowedTargets -Candidate (Resolve-StandaloneVisualStudioConfigPath) }
                'claude-desktop' { Add-StandaloneTrustedTargetCandidate -Targets $allowedTargets -Candidate (Resolve-StandaloneClaudeDesktopConfigPath) }
                'cursor' {
                    Add-StandaloneTrustedTargetCandidate -Targets $allowedTargets -Candidate (Resolve-StandaloneCursorGlobalConfigPath)
                    Add-StandaloneTrustedTargetCandidate -Targets $allowedTargets -Candidate (Resolve-StandaloneCursorProjectConfigPath)
                }
                'other' { Add-StandaloneTrustedTargetCandidate -Targets $allowedTargets -Candidate (Resolve-StandaloneTrustedOtherRegistrationArtifactPath -RegistrationRecord $RegistrationRecord) }
            }
        }
    }

    foreach ($allowedTarget in $allowedTargets) {
        if (Test-StandaloneInstallerPathEquals -Left $recordedTarget -Right $allowedTarget) {
            return $allowedTarget
        }
    }

    return $null
}
function Get-StandaloneJsonCollectionName {
    param([Parameter(Mandatory)] [string]$ClientBaseId)

    switch ($ClientBaseId) {
        'vscode' { return 'servers' }
        'visual-studio' { return 'servers' }
        'claude-desktop' { return 'mcpServers' }
        'cursor' { return 'mcpServers' }
        default { return $null }
    }
}
function Get-StandaloneNormalizedRegistrationMode {
    param([string]$RegistrationMode)

    if ([string]::IsNullOrWhiteSpace($RegistrationMode)) {
        return $RegistrationMode
    }

    if ($RegistrationMode -like 'cursor-*') {
        return 'json-file'
    }

    return $RegistrationMode
}
function Get-StandaloneManagedRegistrationsFromInstall {
    param(
        [Parameter(Mandatory)] [string]$ResolvedInstallRoot,
        [Parameter(Mandatory)] [string]$ResolvedArchitecture
    )

    $liveEvidence = Get-StandaloneLiveInstallerManifestEvidence -InstallRoot $ResolvedInstallRoot -Architecture $ResolvedArchitecture
    if ($null -eq $liveEvidence) {
        return @()
    }

    try {
        $manifestPath = Assert-InstallerLocalPathTrusted -Path (Join-Path (Resolve-StandaloneInstallBasePath -ResolvedInstallRoot $ResolvedInstallRoot -ResolvedArchitecture $ResolvedArchitecture) 'install-manifest.json')
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

        $clientId = [string]$property.Name
        $registrationMode = if ($clientId -like 'cursor-*') { $clientId } else { 'json-file' }
        $registrations.Add([ordered]@{
                ClientId = $clientId
                RegistrationMode = $registrationMode
                RegistrationTarget = $targetPath
                InstalledExecutable = [string]$liveEvidence.InstalledExecutable
                InstallRoot = $ResolvedInstallRoot
                Architecture = $ResolvedArchitecture
                InstallerOwned = $true
                ResolvedVersion = [string]$liveEvidence.ResolvedVersion
            })
    }

    return @($registrations.ToArray())
}
function Get-StandaloneJsonVerificationTargets {
    param(
        [Parameter(Mandatory)] [string]$SelectedClient,
        $RegistrationRecord,
        $RegistrationChanges
    )

    $targets = New-Object System.Collections.Generic.List[string]
    foreach ($registrationChange in @($RegistrationChanges)) {
        if ($null -eq $registrationChange) {
            continue
        }

        $changeClient = if ($registrationChange.Contains('client')) { [string]$registrationChange.client } else { [string]$registrationChange.Client }
        $clientBaseId = Resolve-ClientBaseId -ClientId $SelectedClient
        if (-not [string]::IsNullOrWhiteSpace($changeClient)) {
            if (-not [string]::Equals($changeClient, $SelectedClient, [System.StringComparison]::OrdinalIgnoreCase) -and -not [string]::Equals($changeClient, $clientBaseId, [System.StringComparison]::OrdinalIgnoreCase)) {
                continue
            }
        }

        $changeTarget = Get-StandaloneRecordStringValue -Record $registrationChange -PropertyNames @('target', 'Target', 'TargetPath')
        if ([string]::IsNullOrWhiteSpace($changeTarget)) {
            continue
        }

        if (-not $targets.Contains($changeTarget)) {
            $targets.Add($changeTarget)
        }
    }

    if ($targets.Count -gt 0) {
        return @($targets.ToArray())
    }

    $recordedTarget = Get-StandaloneTrustedRecordedTarget -SelectedClient $SelectedClient -RegistrationRecord $RegistrationRecord
    if (-not [string]::IsNullOrWhiteSpace($recordedTarget) -and -not $targets.Contains($recordedTarget)) {
        $targets.Add($recordedTarget)
    }

    $manifestTarget = if ((Resolve-ClientBaseId -ClientId $SelectedClient) -eq 'cursor') {
        Get-StandaloneTrustedCursorManifestTarget -SelectedClient $SelectedClient -RegistrationRecord $RegistrationRecord
    }
    else {
        Get-StandaloneTrustedManagedJsonRegistrationTarget -SelectedClient $SelectedClient -RegistrationRecord $RegistrationRecord
    }
    if (-not [string]::IsNullOrWhiteSpace($manifestTarget) -and -not $targets.Contains($manifestTarget)) {
        $targets.Add($manifestTarget)
    }

    $defaultTargets = switch ($SelectedClient) {
        'cursor-global' { @((Resolve-StandaloneCursorGlobalConfigPath) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) }
        'cursor-project' { @((Resolve-StandaloneCursorProjectConfigPath) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) }
        default {
            $clientBaseId = Resolve-ClientBaseId -ClientId $SelectedClient
            switch ($clientBaseId) {
                'vscode' { @((Resolve-StandaloneVsCodeConfigPath)) }
                'visual-studio' { @((Resolve-StandaloneVisualStudioConfigPath)) }
                'claude-desktop' { @((Resolve-StandaloneClaudeDesktopConfigPath)) }
                'cursor' {
                    @(
                        (Resolve-StandaloneCursorProjectConfigPath)
                        (Resolve-StandaloneCursorGlobalConfigPath)
                    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique
                }
                default { @() }
            }
        }
    }

    foreach ($target in $defaultTargets) {
        if (-not [string]::IsNullOrWhiteSpace($target) -and -not $targets.Contains($target)) {
            $targets.Add([string]$target)
        }
    }

    return @($targets.ToArray())
}
function Get-StandaloneJsonRegisteredExecutable {
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

    $root = Get-StandaloneExistingConfigMap -Path $resolvedConfigPath
    $servers = Get-StandaloneConfigCollectionMap -Root $root -CollectionName $CollectionName
    if (-not $servers.Contains('wpf-devtools')) {
        return $null
    }

    return [string]$servers['wpf-devtools'].command
}
function Resolve-StandaloneInstallBasePath {
    param(
        [Parameter(Mandatory)] [string]$ResolvedInstallRoot,
        [Parameter(Mandatory)] [string]$ResolvedArchitecture
    )

    return (Join-Path $ResolvedInstallRoot $ResolvedArchitecture)
}
function Resolve-StandaloneRemovalInstallRoot {
    param(
        [AllowEmptyString()] [AllowNull()] [string]$ResolvedInstallRoot,
        [Parameter(Mandatory)] $State
    )

    if (-not [string]::IsNullOrWhiteSpace($ResolvedInstallRoot)) {
        return $ResolvedInstallRoot
    }

    $lastInstallRoot = [string]$State.lastInstallRoot
    if (-not [string]::IsNullOrWhiteSpace($lastInstallRoot)) {
        return $lastInstallRoot
    }

    if (-not [string]::IsNullOrWhiteSpace($env:APPDATA)) {
        return (Join-Path $env:APPDATA 'WpfDevToolsMcp')
    }

    return (Join-Path ([System.IO.Path]::GetTempPath()) 'WpfDevToolsMcp')
}
function Get-StandaloneLiveInstallerManifestEvidence {
    param(
        [string]$InstallRoot,
        [string]$Architecture
    )

    if ([string]::IsNullOrWhiteSpace($InstallRoot) -or [string]::IsNullOrWhiteSpace($Architecture)) {
        return $null
    }

    try {
        $installBase = Assert-InstallerLocalPathTrusted -Path (Resolve-StandaloneInstallBasePath -ResolvedInstallRoot $InstallRoot -ResolvedArchitecture $Architecture)
        $manifestPath = Assert-InstallerLocalPathTrusted -Path (Join-Path $installBase 'install-manifest.json')
    }
    catch {
        return $null
    }

    if (-not (Test-Path -LiteralPath $manifestPath)) {
        return $null
    }

    try {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    }
    catch {
        return $null
    }

    $manifestInstallRoot = [string]$manifest.installRoot
    if (-not [string]::IsNullOrWhiteSpace($manifestInstallRoot) -and -not (Test-StandaloneInstallerPathEquals -Left $manifestInstallRoot -Right $InstallRoot)) {
        return $null
    }

    $installedExecutable = [string]$manifest.executable
    if ([string]::IsNullOrWhiteSpace($installedExecutable)) {
        $installedExecutable = Join-Path $installBase "current\\bin\\wpf-devtools-$Architecture.exe"
    }

    $ownership = Resolve-StandaloneInstallerOwnershipFromExecutable -InstalledExecutable $installedExecutable
    if (-not [bool]$ownership.InstallerOwned) {
        return $null
    }

    if (-not (Test-StandaloneInstallerPathEquals -Left ([string]$ownership.InstallRoot) -Right $InstallRoot)) {
        return $null
    }

    return [ordered]@{
        Architecture = $Architecture
        InstalledExecutable = [string]$ownership.InstalledExecutable
        ResolvedVersion = [string]$ownership.ResolvedVersion
    }
}
function Get-StandaloneKnownArchitectures {
    return @('x64', 'x86', 'arm64')
}
function Get-StandaloneDetectedConfigRegistrations {
    $registrations = @()
    foreach ($candidate in @(
            [ordered]@{
                ClientId = 'vscode'
                RegistrationMode = 'json-file'
                RegistrationTarget = (Resolve-StandaloneVsCodeConfigPath)
                CollectionName = 'servers'
            }
            [ordered]@{
                ClientId = 'visual-studio'
                RegistrationMode = 'json-file'
                RegistrationTarget = (Resolve-StandaloneVisualStudioConfigPath)
                CollectionName = 'servers'
            }
            [ordered]@{
                ClientId = 'claude-desktop'
                RegistrationMode = 'json-file'
                RegistrationTarget = (Resolve-StandaloneClaudeDesktopConfigPath)
                CollectionName = 'mcpServers'
            }
            [ordered]@{
                ClientId = 'cursor-global'
                RegistrationMode = 'json-file'
                RegistrationTarget = (Resolve-StandaloneCursorGlobalConfigPath)
                CollectionName = 'mcpServers'
            }
            [ordered]@{
                ClientId = 'cursor-project'
                RegistrationMode = 'json-file'
                RegistrationTarget = (Resolve-StandaloneCursorProjectConfigPath)
                CollectionName = 'mcpServers'
            }
        )) {
        $registrationTarget = [string]$candidate.RegistrationTarget
        $installedExecutable = Get-StandaloneJsonRegisteredExecutable -CollectionName ([string]$candidate.CollectionName) -ConfigPath $registrationTarget
        if ([string]::IsNullOrWhiteSpace($installedExecutable)) {
            continue
        }

        $ownership = Resolve-StandaloneInstallerOwnershipFromExecutable -InstalledExecutable $installedExecutable
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
