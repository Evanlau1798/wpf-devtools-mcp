function Test-InstallerRunningElevated {
    $testModeVariable = Get-Variable -Name WpfDevToolsInstallerTestModeEnabled -Scope Script -ErrorAction SilentlyContinue
    if ($null -ne $testModeVariable -and
        [bool]$testModeVariable.Value -and
        -not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_ASSUME_ELEVATED)) {
        $overrideValue = ([string]$env:WPFDEVTOOLS_INSTALLER_ASSUME_ELEVATED).Trim().ToLowerInvariant()
        return @('1', 'true', 'yes', 'on') -contains $overrideValue
    }

    try {
        $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
        if ($null -eq $identity) {
            return $false
        }

        $principal = New-Object System.Security.Principal.WindowsPrincipal($identity)
        return $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
    }
    catch {
        return $false
    }
}

function Get-TrustedCliCommandPathEnvVarName {
    param([Parameter(Mandatory)] [string]$Command)

    switch ($Command.ToLowerInvariant()) {
        'claude' { return 'WPFDEVTOOLS_CLAUDE_COMMAND_PATH' }
        'codex' { return 'WPFDEVTOOLS_CODEX_COMMAND_PATH' }
        default { return $null }
    }
}

function Test-InstallerFullyQualifiedPath {
    param([Parameter(Mandatory)] [string]$Path)

    if (-not [System.IO.Path]::IsPathRooted($Path)) {
        return $false
    }

    $root = [System.IO.Path]::GetPathRoot($Path)
    if ([string]::IsNullOrWhiteSpace($root)) {
        return $false
    }

    if ([string]::Equals($root, '\', [System.StringComparison]::Ordinal) -or
        [string]::Equals($root, '/', [System.StringComparison]::Ordinal)) {
        return $false
    }

    return -not ($root.Length -eq 2 -and $root[1] -eq ':')
}

function Resolve-TrustedCliCommandPath {
    param([Parameter(Mandatory)] [string]$Command)

    $envVarName = Get-TrustedCliCommandPathEnvVarName -Command $Command
    if ([string]::IsNullOrWhiteSpace($envVarName)) {
        return $null
    }

    $configuredPath = [Environment]::GetEnvironmentVariable($envVarName)
    if ([string]::IsNullOrWhiteSpace($configuredPath)) {
        return $null
    }

    if (Test-InstallerRunningElevated) {
        throw "$envVarName cannot be used while the installer is elevated. Complete the CLI step from an unelevated shell or register manually after install."
    }

    if (-not (Test-InstallerFullyQualifiedPath -Path $configuredPath)) {
        throw "$envVarName must be a fully qualified absolute path when provided."
    }

    $resolvedPath = if (Test-Path Function:\Assert-InstallerLocalPathTrusted) {
        Assert-InstallerLocalPathTrusted -Path $configuredPath -RejectHardLinks
    }
    else {
        [System.IO.Path]::GetFullPath($configuredPath)
    }

    if (-not (Test-Path -LiteralPath $resolvedPath -PathType Leaf)) {
        throw "$envVarName points to a command path that does not exist or is not a file: $resolvedPath"
    }

    return $resolvedPath
}

function Get-ElevatedCliCommandBlockMessage {
    param(
        [Parameter(Mandatory)] [string]$Command,
        [Parameter(Mandatory)] [string]$ClientName,
        [Parameter(Mandatory)] [string]$OperationName
    )

    return "Automatic $ClientName $OperationName is blocked while the installer is elevated because resolving '$Command' from PATH is unsafe. Rerun the packaged launcher with WPFDEVTOOLS_SKIP_ELEVATION=1, complete the CLI step from an unelevated shell, or register manually after install."
}

function Add-TrustedRegistrationTargetCandidate {
    param(
        [System.Collections.Generic.List[string]]$Targets,
        [string]$Candidate
    )

    if ([string]::IsNullOrWhiteSpace($Candidate)) {
        return
    }

    foreach ($existing in $Targets) {
        if (Test-InstallerPathEqualsCore -Left $existing -Right $Candidate) {
            return
        }
    }

    $Targets.Add($Candidate)
}

function Resolve-TrustedInstallBaseFromRegistrationRecord {
    param($RegistrationRecord)

    $installedExecutable = Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('installedExecutable', 'InstalledExecutable')
    if (-not [string]::IsNullOrWhiteSpace($installedExecutable)) {
        $ownership = Resolve-InstallerOwnershipFromExecutable -InstalledExecutable $installedExecutable
        if ($null -ne $ownership -and [bool]$ownership.InstallerOwned -and -not [string]::IsNullOrWhiteSpace([string]$ownership.InstallBase)) {
            return [string]$ownership.InstallBase
        }
    }

    $resolvedInstallRoot = Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('installRoot', 'InstallRoot')
    if ([string]::IsNullOrWhiteSpace($resolvedInstallRoot)) {
        $resolvedInstallRoot = $InstallRoot
    }

    $resolvedArchitecture = Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('architecture', 'Architecture')
    if ([string]::IsNullOrWhiteSpace($resolvedArchitecture)) {
        $resolvedArchitecture = $Architecture
    }

    if ([string]::IsNullOrWhiteSpace($resolvedInstallRoot) -or [string]::IsNullOrWhiteSpace($resolvedArchitecture)) {
        return $null
    }

    $resolvedArchitecture = $resolvedArchitecture.ToLowerInvariant()
    $expectedInstallBase = Join-Path $resolvedInstallRoot $resolvedArchitecture
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
        if ((Test-Path -LiteralPath $trustedInstalledExecutable) -and (Test-InstallerPathEqualsCore -Left $trustedInstalledExecutable -Right $expectedExecutable)) {
            return $expectedInstallBase
        }
    }

    $liveEvidence = Get-LiveInstallerManifestEvidence -InstallRoot $resolvedInstallRoot -Architecture $resolvedArchitecture
    if ($null -ne $liveEvidence) {
        return $expectedInstallBase
    }

    return $null
}

function Resolve-TrustedOtherRegistrationArtifactPath {
    param($RegistrationRecord)

    $installBase = Resolve-TrustedInstallBaseFromRegistrationRecord -RegistrationRecord $RegistrationRecord
    if (-not [string]::IsNullOrWhiteSpace($installBase)) {
        return (Join-Path $installBase 'client-registration\other.mcpServers.json')
    }

    return $null
}

function Get-TrustedOtherRegistrationArtifactTargets {
    param($RegistrationRecord)

    $targets = New-Object System.Collections.Generic.List[string]
    Add-TrustedRegistrationTargetCandidate -Targets $targets -Candidate (Resolve-TrustedOtherRegistrationArtifactPath -RegistrationRecord $RegistrationRecord)

    $recordedTarget = Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('target', 'RegistrationTarget')
    $recordInstallRoot = Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('installRoot', 'InstallRoot')
    $recordArchitecture = Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('architecture', 'Architecture')
    $installedExecutable = Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('installedExecutable', 'InstalledExecutable')
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
        $expectedInstallBase = Join-Path $recordInstallRoot $normalizedArchitecture
        $expectedArtifactTarget = Join-Path $expectedInstallBase 'client-registration\other.mcpServers.json'
        $expectedExecutable = Join-Path $expectedInstallBase "current\bin\wpf-devtools-$normalizedArchitecture.exe"
        if ((Test-Path -LiteralPath $trustedInstalledExecutable) -and
            (Test-InstallerPathEqualsCore -Left $recordedTarget -Right $expectedArtifactTarget) -and
            (Test-InstallerPathEqualsCore -Left $trustedInstalledExecutable -Right $expectedExecutable)) {
            Add-TrustedRegistrationTargetCandidate -Targets $targets -Candidate $expectedArtifactTarget
        }
    }

    return @($targets.ToArray())
}

function Get-TrustedManagedRegistrationTargetFromManifest {
    param(
        [Parameter(Mandatory)] [string[]]$StateKeys,
        $RegistrationRecord
    )

    $manifestPath = $null
    $installedExecutable = Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('installedExecutable', 'InstalledExecutable')
    if (-not [string]::IsNullOrWhiteSpace($installedExecutable)) {
        $ownership = Resolve-InstallerOwnershipFromExecutable -InstalledExecutable $installedExecutable
        if ($null -ne $ownership -and [bool]$ownership.InstallerOwned -and -not [string]::IsNullOrWhiteSpace([string]$ownership.InstallBase)) {
            $manifestPath = Join-Path ([string]$ownership.InstallBase) 'install-manifest.json'
        }
    }

    if ([string]::IsNullOrWhiteSpace($manifestPath)) {
        $installRoot = Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('installRoot', 'InstallRoot')
        $architecture = Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('architecture', 'Architecture')
        if (-not [string]::IsNullOrWhiteSpace($installRoot) -and -not [string]::IsNullOrWhiteSpace($architecture)) {
            $liveEvidence = Get-LiveInstallerManifestEvidence -InstallRoot $installRoot -Architecture $architecture
            if ($null -ne $liveEvidence) {
                $manifestPath = Join-Path (Join-Path $installRoot $architecture) 'install-manifest.json'
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

function Get-TrustedManagedJsonRegistrationTarget {
    param(
        [Parameter(Mandatory)] [string]$ClientBaseId,
        $RegistrationRecord
    )

    return (Get-TrustedManagedRegistrationTargetFromManifest -StateKeys @($ClientBaseId) -RegistrationRecord $RegistrationRecord)
}

function Get-TrustedCursorManifestTarget {
    param(
        [string]$SelectedClient,
        $RegistrationRecord
    )

    if ($SelectedClient -eq 'cursor-global') {
        return (Get-TrustedManagedRegistrationTargetFromManifest -StateKeys @('cursor-global') -RegistrationRecord $RegistrationRecord)
    }

    if ($SelectedClient -eq 'cursor-project') {
        return (Get-TrustedManagedRegistrationTargetFromManifest -StateKeys @('cursor-project') -RegistrationRecord $RegistrationRecord)
    }

    $recordedMode = Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('mode', 'Mode', 'RegistrationMode')
    if ($recordedMode -eq 'cursor-project') {
        return (Get-TrustedManagedRegistrationTargetFromManifest -StateKeys @('cursor-project') -RegistrationRecord $RegistrationRecord)
    }

    if ($recordedMode -eq 'cursor-global') {
        return (Get-TrustedManagedRegistrationTargetFromManifest -StateKeys @('cursor-global') -RegistrationRecord $RegistrationRecord)
    }

    return (Get-TrustedManagedRegistrationTargetFromManifest -StateKeys @('cursor-global', 'cursor-project') -RegistrationRecord $RegistrationRecord)
}

function Get-TrustedRecordedRegistrationTarget {
    param(
        [Parameter(Mandatory)] [string]$ClientBaseId,
        $RegistrationRecord,
        [string[]]$AdditionalAllowedTargets = @()
    )

    $recordedTarget = Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('target', 'RegistrationTarget')
    if ([string]::IsNullOrWhiteSpace($recordedTarget)) {
        return $null
    }

    $evidenceSource = Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('evidenceSource', 'EvidenceSource')
    $installerOwned = $false
    if ($RegistrationRecord.Contains('InstallerOwned')) {
        $installerOwned = [bool]$RegistrationRecord.InstallerOwned
    }
    elseif ($RegistrationRecord.Contains('installerOwned')) {
        $installerOwned = [bool]$RegistrationRecord.installerOwned
    }

    $allowedTargets = New-Object System.Collections.Generic.List[string]
    foreach ($candidate in @($AdditionalAllowedTargets)) {
        Add-TrustedRegistrationTargetCandidate -Targets $allowedTargets -Candidate $candidate
    }

    switch ($ClientBaseId) {
        'vscode' { Add-TrustedRegistrationTargetCandidate -Targets $allowedTargets -Candidate (Resolve-VsCodeConfigPath) }
        'visual-studio' { Add-TrustedRegistrationTargetCandidate -Targets $allowedTargets -Candidate (Resolve-VisualStudioConfigPath) }
        'claude-desktop' { Add-TrustedRegistrationTargetCandidate -Targets $allowedTargets -Candidate (Resolve-ClaudeDesktopConfigPath) }
        'other' { Add-TrustedRegistrationTargetCandidate -Targets $allowedTargets -Candidate (Resolve-TrustedOtherRegistrationArtifactPath -RegistrationRecord $RegistrationRecord) }
    }

    if ($ClientBaseId -ne 'other') {
        Add-TrustedRegistrationTargetCandidate -Targets $allowedTargets -Candidate (Get-TrustedManagedJsonRegistrationTarget -ClientBaseId $ClientBaseId -RegistrationRecord $RegistrationRecord)
    }

    foreach ($allowedTarget in $allowedTargets) {
        if (Test-InstallerPathEqualsCore -Left $recordedTarget -Right $allowedTarget) {
            return $allowedTarget
        }
    }

    return $null
}
