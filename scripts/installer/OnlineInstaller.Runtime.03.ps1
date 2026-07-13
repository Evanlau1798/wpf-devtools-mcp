function Remove-StandaloneJsonConfigRegistration {
    param(
        [Parameter(Mandatory)] [string]$CollectionName,
        [Parameter(Mandatory)] [string]$ConfigPath
    )

    $resolvedConfigPath = Assert-InstallerLocalPathTrusted -Path $ConfigPath
    if (-not (Test-Path -LiteralPath $resolvedConfigPath)) {
        return [ordered]@{
            backupPath = $null
            applied = $false
        }
    }

    $root = Get-StandaloneExistingConfigMap -Path $resolvedConfigPath
    $servers = Get-StandaloneConfigCollectionMap -Root $root -CollectionName $CollectionName
    if (-not $servers.Contains('wpf-devtools')) {
        return [ordered]@{
            backupPath = $null
            applied = $false
        }
    }

    $backupPath = Assert-InstallerLocalPathTrusted -Path "$resolvedConfigPath.bak-$(Get-Date -Format 'yyyyMMddHHmmssfff')"
    Assert-InstallerLocalPathTrusted -Path $resolvedConfigPath | Out-Null
    Copy-Item -LiteralPath $resolvedConfigPath -Destination $backupPath -Force

    [void]$servers.Remove('wpf-devtools')
    if ($servers.Count -gt 0) {
        $root[$CollectionName] = $servers
    }
    else {
        [void]$root.Remove($CollectionName)
    }

    $resolvedConfigPath = Assert-InstallerLocalPathTrusted -Path $resolvedConfigPath -RejectHardLinks
    if ($root.Count -eq 0) {
        '{}' | Set-Content -LiteralPath $resolvedConfigPath -Encoding UTF8
    }
    else {
        $root | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $resolvedConfigPath -Encoding UTF8
    }

    return [ordered]@{
        backupPath = $backupPath
        applied = $true
    }
}
function Resolve-StandaloneVsCodeConfigPath {
    if (-not [string]::IsNullOrWhiteSpace($VsCodeConfigPath)) { return $VsCodeConfigPath }
    return (Join-Path $env:APPDATA 'Code\User\mcp.json')
}
function Resolve-StandaloneVisualStudioConfigPath {
    if (-not [string]::IsNullOrWhiteSpace($VisualStudioConfigPath)) { return $VisualStudioConfigPath }
    return (Join-Path $env:USERPROFILE '.mcp.json')
}
function Resolve-StandaloneClaudeDesktopConfigPath {
    if (-not [string]::IsNullOrWhiteSpace($ClaudeDesktopConfigPath)) { return $ClaudeDesktopConfigPath }
    return (Join-Path $env:APPDATA 'Claude\claude_desktop_config.json')
}
function Resolve-StandaloneCursorProjectRoot {
    if (-not [string]::IsNullOrWhiteSpace($CursorProjectRoot)) {
        return (Resolve-AbsoluteDirectory -Path $CursorProjectRoot)
    }

    return (Resolve-AbsoluteDirectory -Path (Get-Location).Path)
}
function Resolve-StandaloneCursorGlobalConfigPath {
    if ($CursorMode -eq 'project') {
        return $null
    }

    if (-not [string]::IsNullOrWhiteSpace($CursorConfigPath)) {
        return $CursorConfigPath
    }

    return (Join-Path $env:USERPROFILE '.cursor\mcp.json')
}
function Resolve-StandaloneCursorProjectConfigPath {
    if ($CursorMode -eq 'global') {
        return $null
    }

    if (-not [string]::IsNullOrWhiteSpace($CursorConfigPath)) {
        return $CursorConfigPath
    }

    return (Join-Path (Resolve-StandaloneCursorProjectRoot) '.cursor\mcp.json')
}
function Normalize-StandaloneInstallerPath {
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
function Test-StandaloneInstallerPathEquals {
    param(
        [string]$Left,
        [string]$Right
    )

    $normalizedLeft = Normalize-StandaloneInstallerPath -PathValue $Left
    $normalizedRight = Normalize-StandaloneInstallerPath -PathValue $Right
    if ([string]::IsNullOrWhiteSpace($normalizedLeft) -or [string]::IsNullOrWhiteSpace($normalizedRight)) {
        return $false
    }

    return [string]::Equals($normalizedLeft, $normalizedRight, [System.StringComparison]::OrdinalIgnoreCase)
}
function Resolve-StandaloneInstallerOwnershipFromExecutable {
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

    if (-not (Test-Path -LiteralPath $trustedInstalledExecutable)) {
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
        if (-not [string]::IsNullOrWhiteSpace($manifestExecutable) -and (Test-StandaloneInstallerPathEquals -Left $manifestExecutable -Right $trustedInstalledExecutable)) {
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
function Get-StandaloneRecordStringValue {
    param(
        $Record,
        [Parameter(Mandatory)] [string[]]$PropertyNames
    )

    if ($null -eq $Record) {
        return $null
    }

    if ($Record -is [System.Collections.IDictionary]) {
        foreach ($propertyName in $PropertyNames) {
            if ($Record.Contains($propertyName) -and -not [string]::IsNullOrWhiteSpace([string]$Record[$propertyName])) {
                return [string]$Record[$propertyName]
            }
        }
    }

    foreach ($propertyName in $PropertyNames) {
        $property = $Record.PSObject.Properties[$propertyName]
        if ($null -ne $property -and -not [string]::IsNullOrWhiteSpace([string]$property.Value)) {
            return [string]$property.Value
        }
    }

    return $null
}
function Add-StandaloneTrustedTargetCandidate {
    param(
        [System.Collections.Generic.List[string]]$Targets,
        [string]$Candidate
    )

    if ([string]::IsNullOrWhiteSpace($Candidate)) {
        return
    }

    foreach ($existing in $Targets) {
        if (Test-StandaloneInstallerPathEquals -Left $existing -Right $Candidate) {
            return
        }
    }

    $Targets.Add($Candidate)
}

function Resolve-StandaloneTrustedInstallBaseFromRegistrationRecord {
    param($RegistrationRecord)

    $installedExecutable = Get-StandaloneRecordStringValue -Record $RegistrationRecord -PropertyNames @('installedExecutable', 'InstalledExecutable')
    if (-not [string]::IsNullOrWhiteSpace($installedExecutable)) {
        $ownership = Resolve-StandaloneInstallerOwnershipFromExecutable -InstalledExecutable $installedExecutable
        if ($null -ne $ownership -and [bool]$ownership.InstallerOwned -and -not [string]::IsNullOrWhiteSpace([string]$ownership.InstallBase)) {
            return [string]$ownership.InstallBase
        }
    }

    $resolvedInstallRoot = Get-StandaloneRecordStringValue -Record $RegistrationRecord -PropertyNames @('installRoot', 'InstallRoot')
    if ([string]::IsNullOrWhiteSpace($resolvedInstallRoot)) {
        $resolvedInstallRoot = $InstallRoot
    }

    $resolvedArchitecture = Get-StandaloneRecordStringValue -Record $RegistrationRecord -PropertyNames @('architecture', 'Architecture')
    if ([string]::IsNullOrWhiteSpace($resolvedArchitecture)) {
        $resolvedArchitecture = $Architecture
    }

    if ([string]::IsNullOrWhiteSpace($resolvedInstallRoot) -or [string]::IsNullOrWhiteSpace($resolvedArchitecture)) {
        return $null
    }

    $resolvedArchitecture = $resolvedArchitecture.ToLowerInvariant()
    $expectedInstallBase = Resolve-StandaloneInstallBasePath -ResolvedInstallRoot $resolvedInstallRoot -ResolvedArchitecture $resolvedArchitecture
    $trustedInstalledExecutable = $null
    if (-not [string]::IsNullOrWhiteSpace($installedExecutable)) {
        try {
            $trustedInstalledExecutable = Assert-InstallerLocalPathTrusted -Path $installedExecutable
        }
        catch {
            $trustedInstalledExecutable = $null
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($trustedInstalledExecutable)) {
        $expectedExecutable = Join-Path $expectedInstallBase "current\bin\wpf-devtools-$resolvedArchitecture.exe"
        if ((Test-Path -LiteralPath $trustedInstalledExecutable) -and (Test-StandaloneInstallerPathEquals -Left $trustedInstalledExecutable -Right $expectedExecutable)) {
            return $expectedInstallBase
        }
    }

    $liveEvidence = Get-StandaloneLiveInstallerManifestEvidence -InstallRoot $resolvedInstallRoot -Architecture $resolvedArchitecture
    if ($null -ne $liveEvidence) {
        return $expectedInstallBase
    }

    return $null
}

function Resolve-StandaloneTrustedOtherRegistrationArtifactPath {
    param($RegistrationRecord)

    $installBase = Resolve-StandaloneTrustedInstallBaseFromRegistrationRecord -RegistrationRecord $RegistrationRecord
    if (-not [string]::IsNullOrWhiteSpace($installBase)) {
        return (Join-Path $installBase 'client-registration\other.mcpServers.json')
    }

    return $null
}

function Get-StandaloneTrustedOtherRegistrationArtifactTargets {
    param($RegistrationRecord)

    $targets = New-Object System.Collections.Generic.List[string]
    Add-StandaloneTrustedTargetCandidate -Targets $targets -Candidate (Resolve-StandaloneTrustedOtherRegistrationArtifactPath -RegistrationRecord $RegistrationRecord)

    $recordedTarget = Get-StandaloneRecordStringValue -Record $RegistrationRecord -PropertyNames @('target', 'Target', 'RegistrationTarget')
    $recordInstallRoot = Get-StandaloneRecordStringValue -Record $RegistrationRecord -PropertyNames @('installRoot', 'InstallRoot')
    $recordArchitecture = Get-StandaloneRecordStringValue -Record $RegistrationRecord -PropertyNames @('architecture', 'Architecture')
    $installedExecutable = Get-StandaloneRecordStringValue -Record $RegistrationRecord -PropertyNames @('installedExecutable', 'InstalledExecutable')
    $trustedInstalledExecutable = $null
    if (-not [string]::IsNullOrWhiteSpace($installedExecutable)) {
        try {
            $trustedInstalledExecutable = Assert-InstallerLocalPathTrusted -Path $installedExecutable
        }
        catch {
            $trustedInstalledExecutable = $null
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($recordedTarget) -and
        -not [string]::IsNullOrWhiteSpace($recordInstallRoot) -and
        -not [string]::IsNullOrWhiteSpace($recordArchitecture) -and
        -not [string]::IsNullOrWhiteSpace($trustedInstalledExecutable)) {
        $normalizedArchitecture = $recordArchitecture.ToLowerInvariant()
        $expectedInstallBase = Resolve-StandaloneInstallBasePath -ResolvedInstallRoot $recordInstallRoot -ResolvedArchitecture $normalizedArchitecture
        $expectedArtifactTarget = Join-Path $expectedInstallBase 'client-registration\other.mcpServers.json'
        $expectedExecutable = Join-Path $expectedInstallBase "current\bin\wpf-devtools-$normalizedArchitecture.exe"
        if ((Test-Path -LiteralPath $trustedInstalledExecutable) -and
            (Test-StandaloneInstallerPathEquals -Left $recordedTarget -Right $expectedArtifactTarget) -and
            (Test-StandaloneInstallerPathEquals -Left $trustedInstalledExecutable -Right $expectedExecutable)) {
            Add-StandaloneTrustedTargetCandidate -Targets $targets -Candidate $expectedArtifactTarget
        }
    }

    return @($targets.ToArray())
}
function Get-StandaloneTrustedManagedRegistrationTargetFromManifest {
    param(
        [Parameter(Mandatory)] [string[]]$StateKeys,
        $RegistrationRecord
    )

    $manifestPath = $null
    $installedExecutable = Get-StandaloneRecordStringValue -Record $RegistrationRecord -PropertyNames @('installedExecutable', 'InstalledExecutable')
    if (-not [string]::IsNullOrWhiteSpace($installedExecutable)) {
        $ownership = Resolve-StandaloneInstallerOwnershipFromExecutable -InstalledExecutable $installedExecutable
        if ($null -ne $ownership -and [bool]$ownership.InstallerOwned -and -not [string]::IsNullOrWhiteSpace([string]$ownership.InstallBase)) {
            $manifestPath = Join-Path ([string]$ownership.InstallBase) 'install-manifest.json'
        }
    }

    if ([string]::IsNullOrWhiteSpace($manifestPath)) {
        $installRoot = Get-StandaloneRecordStringValue -Record $RegistrationRecord -PropertyNames @('installRoot', 'InstallRoot')
        $architecture = Get-StandaloneRecordStringValue -Record $RegistrationRecord -PropertyNames @('architecture', 'Architecture')
        if (-not [string]::IsNullOrWhiteSpace($installRoot) -and -not [string]::IsNullOrWhiteSpace($architecture)) {
            $liveEvidence = Get-StandaloneLiveInstallerManifestEvidence -InstallRoot $installRoot -Architecture $architecture
            if ($null -ne $liveEvidence) {
                $manifestPath = Join-Path (Resolve-StandaloneInstallBasePath -ResolvedInstallRoot $installRoot -ResolvedArchitecture $architecture) 'install-manifest.json'
            }
        }
    }

    if ([string]::IsNullOrWhiteSpace($manifestPath)) {
        return $null
    }

    try {
        $manifestPath = Assert-InstallerLocalPathTrusted -Path $manifestPath
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

    $managedTargets = $manifest.PSObject.Properties['managedRegistrationTargets']
    if ($null -eq $managedTargets -or $null -eq $managedTargets.Value) {
        return $null
    }

    foreach ($stateKey in $StateKeys) {
        if ([string]::IsNullOrWhiteSpace($stateKey)) {
            continue
        }

        $property = $managedTargets.Value.PSObject.Properties[$stateKey]
        if ($null -ne $property -and -not [string]::IsNullOrWhiteSpace([string]$property.Value)) {
            return [string]$property.Value
        }
    }

    return $null
}
function Get-StandaloneTrustedManagedJsonRegistrationTarget {
    param(
        [Parameter(Mandatory)] [string]$SelectedClient,
        $RegistrationRecord
    )

    return (Get-StandaloneTrustedManagedRegistrationTargetFromManifest -StateKeys @($SelectedClient) -RegistrationRecord $RegistrationRecord)
}
function Get-StandaloneTrustedCursorManifestTarget {
    param(
        [string]$SelectedClient,
        $RegistrationRecord
    )

    if ($SelectedClient -eq 'cursor-global') {
        return (Get-StandaloneTrustedManagedRegistrationTargetFromManifest -StateKeys @('cursor-global') -RegistrationRecord $RegistrationRecord)
    }

    if ($SelectedClient -eq 'cursor-project') {
        return (Get-StandaloneTrustedManagedRegistrationTargetFromManifest -StateKeys @('cursor-project') -RegistrationRecord $RegistrationRecord)
    }

    $registrationMode = Get-StandaloneRecordStringValue -Record $RegistrationRecord -PropertyNames @('mode', 'Mode', 'RegistrationMode')
    if ($registrationMode -eq 'cursor-project') {
        return (Get-StandaloneTrustedManagedRegistrationTargetFromManifest -StateKeys @('cursor-project') -RegistrationRecord $RegistrationRecord)
    }

    if ($registrationMode -eq 'cursor-global') {
        return (Get-StandaloneTrustedManagedRegistrationTargetFromManifest -StateKeys @('cursor-global') -RegistrationRecord $RegistrationRecord)
    }

    return (Get-StandaloneTrustedManagedRegistrationTargetFromManifest -StateKeys @('cursor-global', 'cursor-project') -RegistrationRecord $RegistrationRecord)
}
