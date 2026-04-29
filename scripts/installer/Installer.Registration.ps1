if (-not (Get-Command Write-InstallerUtf8NoBomFile -ErrorAction SilentlyContinue)) {
    $encodingHelperPath = Join-Path $PSScriptRoot 'Installer.Encoding.ps1'
    if (Test-Path -LiteralPath $encodingHelperPath) {
        . $encodingHelperPath
    }
}

if (-not (Get-Command Resolve-InstallerRegistrationAbsolutePath -ErrorAction SilentlyContinue)) {
    function Resolve-InstallerRegistrationAbsolutePath {
        param([Parameter(Mandatory)] [string]$Path)

        $resolver = Get-Command Resolve-AbsolutePath -ErrorAction SilentlyContinue
        if ($null -ne $resolver) {
            return (Resolve-AbsolutePath -Path $Path)
        }

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

        $resolvedPath = Resolve-InstallerRegistrationAbsolutePath -Path $Path
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

            if ($RejectHardLinks -and -not $item.PSIsContainer -and (Get-Command Get-InstallerHardLinkCount -ErrorAction SilentlyContinue)) {
                $hardLinkCount = Get-InstallerHardLinkCount -Path $currentPath
                if ($hardLinkCount -gt 1) {
                    throw "Installer path '$resolvedPath' is blocked because '$currentPath' has multiple hard links."
                }
            }
        }

        return $resolvedPath
    }
}

function Backup-ConfigFile {
    param([Parameter(Mandatory)] [string]$Path)

    $resolvedPath = Assert-InstallerLocalPathTrusted -Path $Path
    if (-not (Test-Path -LiteralPath $resolvedPath)) {
        return $null
    }

    $backupPath = Assert-InstallerLocalPathTrusted -Path "$resolvedPath.bak-$(Get-Date -Format 'yyyyMMddHHmmssfff')"
    Assert-InstallerLocalPathTrusted -Path $resolvedPath | Out-Null
    Copy-Item -LiteralPath $resolvedPath -Destination $backupPath -Force
    return $backupPath
}

function Get-ConfigJsonParseFailureMessage {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$ErrorMessage
    )

    return "Failed to parse JSON config file '$Path'. Fix the malformed JSON and retry. The installer did not modify the file or update registration state. Parser error: $ErrorMessage"
}

function Get-ExistingConfigMap {
    param([Parameter(Mandatory)] [string]$Path)

    $resolvedPath = Assert-InstallerLocalPathTrusted -Path $Path
    $map = [ordered]@{}
    if (-not (Test-Path -LiteralPath $resolvedPath)) {
        return $map
    }

    $raw = Get-Content -LiteralPath $resolvedPath -Raw
    if ([string]::IsNullOrWhiteSpace($raw)) {
        return $map
    }

    try {
        $parsed = $raw | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        throw (Get-ConfigJsonParseFailureMessage -Path $resolvedPath -ErrorMessage $_.Exception.Message)
    }

    foreach ($property in $parsed.PSObject.Properties) {
        $map[$property.Name] = $property.Value
    }

    return $map
}

function Get-ConfigCollectionMap {
    param(
        [Parameter(Mandatory)] $Root,
        [Parameter(Mandatory)] [string]$CollectionName
    )

    $servers = [ordered]@{}
    if ($Root.Contains($CollectionName) -and $null -ne $Root[$CollectionName]) {
        foreach ($property in $Root[$CollectionName].PSObject.Properties) {
            $servers[$property.Name] = $property.Value
        }
    }

    return $servers
}

function Set-JsonConfigRegistration {
    param(
        [Parameter(Mandatory)] [string]$ClientName,
        [Parameter(Mandatory)] [string]$CollectionName,
        [Parameter(Mandatory)] [string]$ConfigPath,
        [Parameter(Mandatory)] [string]$InstalledExecutable
    )

    $resolvedConfigPath = Assert-InstallerLocalPathTrusted -Path $ConfigPath
    $directory = Split-Path -Parent $resolvedConfigPath
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
        Assert-InstallerLocalPathTrusted -Path $directory | Out-Null
    }

    $root = Get-ExistingConfigMap -Path $resolvedConfigPath
    $servers = Get-ConfigCollectionMap -Root $root -CollectionName $CollectionName
    $servers['wpf-devtools'] = [ordered]@{
        type = 'stdio'
        command = $InstalledExecutable
        args = @()
    }

    $root[$CollectionName] = $servers
    $backupPath = Backup-ConfigFile -Path $resolvedConfigPath
    Assert-InstallerLocalPathTrusted -Path $resolvedConfigPath | Out-Null
    Write-InstallerUtf8NoBomFile -Path $resolvedConfigPath -Content ($root | ConvertTo-Json -Depth 10)

    return [ordered]@{
        client = $ClientName
        mode = 'json-file'
        target = $resolvedConfigPath
        backupPath = $backupPath
        applied = $true
    }
}

function Remove-JsonConfigRegistration {
    param(
        [Parameter(Mandatory)] [string]$ClientName,
        [Parameter(Mandatory)] [string]$CollectionName,
        [Parameter(Mandatory)] [string]$ConfigPath
    )

    $resolvedConfigPath = Assert-InstallerLocalPathTrusted -Path $ConfigPath
    if (-not (Test-Path -LiteralPath $resolvedConfigPath)) {
        return [ordered]@{
            client = $ClientName
            mode = 'json-file'
            target = $resolvedConfigPath
            backupPath = $null
            applied = $false
        }
    }

    $root = Get-ExistingConfigMap -Path $resolvedConfigPath
    $servers = Get-ConfigCollectionMap -Root $root -CollectionName $CollectionName
    if (-not $servers.Contains('wpf-devtools')) {
        return [ordered]@{
            client = $ClientName
            mode = 'json-file'
            target = $resolvedConfigPath
            backupPath = $null
            applied = $false
        }
    }

    [void]$servers.Remove('wpf-devtools')
    $backupPath = Backup-ConfigFile -Path $resolvedConfigPath

    if ($servers.Count -gt 0) {
        $root[$CollectionName] = $servers
    }
    else {
        [void]$root.Remove($CollectionName)
    }

    if ($root.Count -eq 0) {
        Assert-InstallerLocalPathTrusted -Path $resolvedConfigPath | Out-Null
        Write-InstallerUtf8NoBomFile -Path $resolvedConfigPath -Content '{}'
    }
    else {
        Assert-InstallerLocalPathTrusted -Path $resolvedConfigPath | Out-Null
        Write-InstallerUtf8NoBomFile -Path $resolvedConfigPath -Content ($root | ConvertTo-Json -Depth 10)
    }

    return [ordered]@{
        client = $ClientName
        mode = 'json-file'
        target = $resolvedConfigPath
        backupPath = $backupPath
        applied = $true
    }
}

function Test-JsonConfigRegistration {
    param(
        [Parameter(Mandatory)] [string]$CollectionName,
        [Parameter(Mandatory)] [string]$ConfigPath
    )

    if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
        return $false
    }

    $resolvedConfigPath = Assert-InstallerLocalPathTrusted -Path $ConfigPath
    if (-not (Test-Path -LiteralPath $resolvedConfigPath)) {
        return $false
    }

    $root = Get-ExistingConfigMap -Path $resolvedConfigPath
    $servers = Get-ConfigCollectionMap -Root $root -CollectionName $CollectionName
    return $servers.Contains('wpf-devtools')
}

function Resolve-VsCodeConfigPath {
    if (-not [string]::IsNullOrWhiteSpace($VsCodeConfigPath)) { return $VsCodeConfigPath }
    return (Join-Path $env:APPDATA 'Code\User\mcp.json')
}

function Resolve-VisualStudioConfigPath {
    if (-not [string]::IsNullOrWhiteSpace($VisualStudioConfigPath)) { return $VisualStudioConfigPath }
    return (Join-Path $env:USERPROFILE '.mcp.json')
}

function Resolve-ClaudeDesktopConfigPath {
    if (-not [string]::IsNullOrWhiteSpace($ClaudeDesktopConfigPath)) { return $ClaudeDesktopConfigPath }
    return (Join-Path $env:APPDATA 'Claude\claude_desktop_config.json')
}

function Test-InstallerRunningElevated {
    if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_ASSUME_ELEVATED)) {
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

function Test-ElevatedCliCommandPathOverrideAllowed {
    $rawValue = [Environment]::GetEnvironmentVariable('WPFDEVTOOLS_ALLOW_ELEVATED_CLI_COMMAND_PATH')
    return [string]::Equals($rawValue, '1', [System.StringComparison]::Ordinal) -or
        [string]::Equals($rawValue, 'true', [System.StringComparison]::OrdinalIgnoreCase)
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

    if ((Test-InstallerRunningElevated) -and -not (Test-ElevatedCliCommandPathOverrideAllowed)) {
        throw "$envVarName cannot be used while the installer is elevated unless WPFDEVTOOLS_ALLOW_ELEVATED_CLI_COMMAND_PATH=1 is set in the elevated environment."
    }

    if (-not [System.IO.Path]::IsPathRooted($configuredPath)) {
        throw "$envVarName must be an absolute path when provided."
    }

    $resolvedPath = [System.IO.Path]::GetFullPath($configuredPath)
    if (-not (Test-Path -LiteralPath $resolvedPath)) {
        throw "$envVarName points to a command path that does not exist: $resolvedPath"
    }

    return $resolvedPath
}

function Get-ElevatedCliCommandBlockMessage {
    param(
        [Parameter(Mandatory)] [string]$Command,
        [Parameter(Mandatory)] [string]$ClientName,
        [Parameter(Mandatory)] [string]$OperationName
    )

    $envVarName = Get-TrustedCliCommandPathEnvVarName -Command $Command
    $overrideHint = if (-not [string]::IsNullOrWhiteSpace($envVarName)) {
        " or provide a trusted absolute path via $envVarName with WPFDEVTOOLS_ALLOW_ELEVATED_CLI_COMMAND_PATH=1 in the elevated environment."
    }
    else {
        "."
    }

    return "Automatic $ClientName $OperationName is blocked while the installer is elevated because resolving '$Command' from PATH is unsafe. Rerun the packaged launcher with WPFDEVTOOLS_SKIP_ELEVATION=1, complete the CLI step from an unelevated shell, or register manually after install$overrideHint"
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

function Resolve-CursorProjectRoot {
    if (-not [string]::IsNullOrWhiteSpace($CursorProjectRoot)) {
        return (Resolve-AbsoluteDirectory -Path $CursorProjectRoot)
    }

    return (Resolve-AbsoluteDirectory -Path (Get-Location).Path)
}

function Resolve-CursorGlobalConfigPath {
    if ($CursorMode -eq 'project') {
        return $null
    }

    if (-not [string]::IsNullOrWhiteSpace($CursorConfigPath)) {
        return $CursorConfigPath
    }

    return (Join-Path $env:USERPROFILE '.cursor\mcp.json')
}

function Resolve-CursorProjectConfigPath {
    if ($CursorMode -eq 'global') {
        return $null
    }

    if (-not [string]::IsNullOrWhiteSpace($CursorConfigPath)) {
        return $CursorConfigPath
    }

    return (Join-Path (Resolve-CursorProjectRoot) '.cursor\mcp.json')
}

function Get-TrustedCursorRecordedTarget {
    param(
        [string]$SelectedClient,
        $RegistrationRecord
    )

    $recordedTarget = Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('target', 'RegistrationTarget')
    if ([string]::IsNullOrWhiteSpace($recordedTarget)) {
        return $null
    }

    $allowedTargets = New-Object System.Collections.Generic.List[string]
    if (-not [string]::IsNullOrWhiteSpace($CursorConfigPath)) {
        Add-TrustedRegistrationTargetCandidate -Targets $allowedTargets -Candidate $CursorConfigPath
    }

    Add-TrustedRegistrationTargetCandidate -Targets $allowedTargets -Candidate (Get-TrustedCursorManifestTarget -SelectedClient $SelectedClient -RegistrationRecord $RegistrationRecord)

    switch ($SelectedClient) {
        'cursor-global' {
            Add-TrustedRegistrationTargetCandidate -Targets $allowedTargets -Candidate (Resolve-CursorGlobalConfigPath)
        }
        'cursor-project' {
            Add-TrustedRegistrationTargetCandidate -Targets $allowedTargets -Candidate (Resolve-CursorProjectConfigPath)
        }
        default {
            if ($CursorMode -eq 'project') {
                Add-TrustedRegistrationTargetCandidate -Targets $allowedTargets -Candidate (Resolve-CursorProjectConfigPath)
            }
            else {
                Add-TrustedRegistrationTargetCandidate -Targets $allowedTargets -Candidate (Resolve-CursorGlobalConfigPath)
                if (-not [string]::IsNullOrWhiteSpace($CursorProjectRoot)) {
                    Add-TrustedRegistrationTargetCandidate -Targets $allowedTargets -Candidate (Resolve-CursorProjectConfigPath)
                }
            }
        }
    }

    foreach ($allowedTarget in $allowedTargets) {
        if (Test-InstallerPathEqualsCore -Left $recordedTarget -Right $allowedTarget) {
            return $allowedTarget
        }
    }

    return $null
}

function Resolve-CursorRegistrationProfile {
    param(
        [string]$SelectedClient,
        [switch]$PromptIfNeeded,
        $RegistrationRecord
    )

    $selectedMode = switch ($SelectedClient) {
        'cursor-project' { 'project' }
        'cursor-global' { 'global' }
        default { $null }
    }

    $recordedMode = Get-InstallerRecordStringValueCore -Record $RegistrationRecord -PropertyNames @('mode', 'RegistrationMode')
    if ($recordedMode -like 'cursor-*') {
        $recordedMode = $recordedMode.Substring(7)
    }

    $recordedTarget = Get-TrustedCursorRecordedTarget -SelectedClient $SelectedClient -RegistrationRecord $RegistrationRecord
    if ([string]::IsNullOrWhiteSpace($recordedTarget)) {
        $recordedTarget = Get-TrustedCursorManifestTarget -SelectedClient $SelectedClient -RegistrationRecord $RegistrationRecord
    }

    $resolvedMode = if (-not [string]::IsNullOrWhiteSpace($CursorMode)) {
        [string]$CursorMode
    }
    elseif (-not [string]::IsNullOrWhiteSpace($selectedMode)) {
        [string]$selectedMode
    }
    elseif (-not [string]::IsNullOrWhiteSpace($recordedMode)) {
        [string]$recordedMode
    }
    elseif ($PromptIfNeeded -and -not $NonInteractive -and -not $OutputJson) {
        Read-ValidatedChoice -Prompt 'Cursor mode (global/project)' -DefaultValue 'global' -AllowedValues @('global', 'project')
    }
    else {
        'global'
    }

    if ($resolvedMode -eq 'project') {
        $projectRoot = Resolve-CursorProjectRoot
        return [ordered]@{
            Mode = 'project'
            ConfigPath = if (-not [string]::IsNullOrWhiteSpace($CursorConfigPath)) { $CursorConfigPath } elseif (-not [string]::IsNullOrWhiteSpace($recordedTarget)) { $recordedTarget } else { Join-Path $projectRoot '.cursor\mcp.json' }
            Target = $projectRoot
        }
    }

    $globalConfigPath = if (-not [string]::IsNullOrWhiteSpace($CursorConfigPath)) { $CursorConfigPath } elseif (-not [string]::IsNullOrWhiteSpace($recordedTarget)) { $recordedTarget } else { Join-Path $env:USERPROFILE '.cursor\mcp.json' }
    return [ordered]@{
        Mode = 'global'
        ConfigPath = $globalConfigPath
        Target = $globalConfigPath
    }
}

function Get-CursorVerificationConfigPaths {
    param(
        [string]$SelectedClient,
        $RegistrationRecord
    )

    $paths = New-Object System.Collections.Generic.List[string]
    $recordTarget = Get-TrustedCursorRecordedTarget -SelectedClient $SelectedClient -RegistrationRecord $RegistrationRecord
    if (-not [string]::IsNullOrWhiteSpace($recordTarget)) {
        $paths.Add($recordTarget)
    }

    $manifestTarget = Get-TrustedCursorManifestTarget -SelectedClient $SelectedClient -RegistrationRecord $RegistrationRecord
    if (-not [string]::IsNullOrWhiteSpace($manifestTarget)) {
        $alreadyAdded = $false
        foreach ($existingPath in $paths) {
            if (Test-InstallerPathEqualsCore -Left $existingPath -Right $manifestTarget) {
                $alreadyAdded = $true
                break
            }
        }

        if (-not $alreadyAdded) {
            $paths.Add($manifestTarget)
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($CursorMode) -or -not [string]::IsNullOrWhiteSpace($CursorConfigPath) -or $SelectedClient -like 'cursor-*') {
        $profile = Resolve-CursorRegistrationProfile -SelectedClient $SelectedClient -RegistrationRecord $RegistrationRecord
        if (-not [string]::IsNullOrWhiteSpace([string]$profile.ConfigPath)) {
            $alreadyAdded = $false
            foreach ($existingPath in $paths) {
                if (Test-InstallerPathEqualsCore -Left $existingPath -Right ([string]$profile.ConfigPath)) {
                    $alreadyAdded = $true
                    break
                }
            }

            if (-not $alreadyAdded) {
                $paths.Add([string]$profile.ConfigPath)
            }
        }
    }

    $defaultCandidatePaths = switch ($SelectedClient) {
        'cursor-global' { @((Resolve-CursorGlobalConfigPath)) }
        'cursor-project' { @((Resolve-CursorProjectConfigPath)) }
        default {
            switch ($CursorMode) {
                'global' { @((Resolve-CursorGlobalConfigPath)) }
                'project' { @((Resolve-CursorProjectConfigPath)) }
                default {
                    @(
                        (Resolve-CursorProjectConfigPath)
                        (Resolve-CursorGlobalConfigPath)
                    )
                }
            }
        }
    }

    foreach ($candidatePath in $defaultCandidatePaths) {
        if ([string]::IsNullOrWhiteSpace($candidatePath)) {
            continue
        }

        $alreadyAdded = $false
        foreach ($existingPath in $paths) {
            if (Test-InstallerPathEqualsCore -Left $existingPath -Right $candidatePath) {
                $alreadyAdded = $true
                break
            }
        }

        if (-not $alreadyAdded) {
            $paths.Add($candidatePath)
        }
    }

    return @($paths)
}

function Invoke-RegistrationCommand {
    param(
        [Parameter(Mandatory)] [string]$Command,
        [Parameter(Mandatory)] [string[]]$Arguments,
        [Parameter(Mandatory)] [string]$ClientName
    )

    $isElevated = Test-InstallerRunningElevated
    $resolvedCommandPath = Resolve-ExecutableCommandPath -Command $Command -AllowPathResolution:(-not $isElevated)
    if ([string]::IsNullOrWhiteSpace($resolvedCommandPath)) {
        if ($isElevated) {
            throw (Get-ElevatedCliCommandBlockMessage -Command $Command -ClientName $ClientName -OperationName 'registration')
        }

        throw "$Command is not installed. Cannot register $ClientName automatically."
    }

    & $resolvedCommandPath @Arguments | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "$Command registration failed for $ClientName with exit code $LASTEXITCODE."
    }

    return [ordered]@{
        client = $ClientName
        mode = 'cli'
        target = $resolvedCommandPath
        backupPath = $null
        applied = $true
    }
}

function Resolve-ExecutableCommandPath {
    param(
        [Parameter(Mandatory)] [string]$Command,
        [bool]$AllowPathResolution = $true
    )

    $trustedCommandPath = Resolve-TrustedCliCommandPath -Command $Command
    if (-not [string]::IsNullOrWhiteSpace($trustedCommandPath)) {
        return $trustedCommandPath
    }

    if (-not $AllowPathResolution) {
        return $null
    }

    $resolvedCommands = @(Get-Command $Command -All -CommandType Application,ExternalScript -ErrorAction SilentlyContinue)
    foreach ($resolvedCommand in $resolvedCommands) {
        $candidatePath = if (-not [string]::IsNullOrWhiteSpace([string]$resolvedCommand.Path)) {
            [string]$resolvedCommand.Path
        }
        elseif (-not [string]::IsNullOrWhiteSpace([string]$resolvedCommand.Source)) {
            [string]$resolvedCommand.Source
        }
        elseif (-not [string]::IsNullOrWhiteSpace([string]$resolvedCommand.Definition)) {
            [string]$resolvedCommand.Definition
        }
        else {
            $null
        }

        if (-not [string]::IsNullOrWhiteSpace($candidatePath)) {
            return $candidatePath
        }
    }

    return $null
}

function Invoke-OptionalRemovalCommand {
    param(
        [Parameter(Mandatory)] [string]$Command,
        [Parameter(Mandatory)] [string[]]$Arguments,
        [Parameter(Mandatory)] [string]$ClientName
    )

    $isElevated = Test-InstallerRunningElevated
    $resolvedCommandPath = Resolve-ExecutableCommandPath -Command $Command -AllowPathResolution:(-not $isElevated)
    if ([string]::IsNullOrWhiteSpace($resolvedCommandPath)) {
        if ($isElevated) {
            throw (Get-ElevatedCliCommandBlockMessage -Command $Command -ClientName $ClientName -OperationName 'removal')
        }

        return [ordered]@{
            client = $ClientName
            mode = 'cli'
            target = $null
            backupPath = $null
            applied = $false
        }
    }

    & $resolvedCommandPath @Arguments | Out-Null
    $succeeded = ($LASTEXITCODE -eq 0)
    return [ordered]@{
        client = $ClientName
        mode = 'cli'
        target = $resolvedCommandPath
        backupPath = $null
        applied = $succeeded
    }
}

function Invoke-DocsHomepage {
    $uri = 'https://wpf-mcptools.evanlau1798.com'
    if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_OPEN_BROWSER_COMMAND)) {
        & $env:WPFDEVTOOLS_INSTALLER_OPEN_BROWSER_COMMAND $uri | Out-Null
        return
    }

    Start-Process $uri | Out-Null
}

function Invoke-ClientRegistration {
    param(
        [Parameter(Mandatory)] [string]$SelectedClient,
        [Parameter(Mandatory)] [string]$InstalledExecutable,
        [Parameter(Mandatory)] [string]$InstallBase
    )

    $clientBaseId = Resolve-ClientBaseId -ClientId $SelectedClient
    switch ($clientBaseId) {
        'claude-code' {
            return @(Invoke-RegistrationCommand -Command 'claude' -Arguments @('mcp', 'add', '--transport', 'stdio', 'wpf-devtools', '--', $InstalledExecutable) -ClientName $clientBaseId)
        }
        'codex' {
            return @(Invoke-RegistrationCommand -Command 'codex' -Arguments @('mcp', 'add', 'wpf-devtools', '--', $InstalledExecutable) -ClientName $clientBaseId)
        }
        'cursor' {
            $cursorProfile = Resolve-CursorRegistrationProfile -SelectedClient $SelectedClient -PromptIfNeeded
            $registration = Set-JsonConfigRegistration -ClientName $clientBaseId -CollectionName 'mcpServers' -ConfigPath ([string]$cursorProfile.ConfigPath) -InstalledExecutable $InstalledExecutable
            $registration['mode'] = "cursor-$([string]$cursorProfile.Mode)"
            $registration['target'] = [string]$cursorProfile.ConfigPath
            return @($registration)
        }
        'vscode' {
            return @(Set-JsonConfigRegistration -ClientName $clientBaseId -CollectionName 'servers' -ConfigPath (Resolve-VsCodeConfigPath) -InstalledExecutable $InstalledExecutable)
        }
        'visual-studio' {
            return @(Set-JsonConfigRegistration -ClientName $clientBaseId -CollectionName 'servers' -ConfigPath (Resolve-VisualStudioConfigPath) -InstalledExecutable $InstalledExecutable)
        }
        'claude-desktop' {
            return @(Set-JsonConfigRegistration -ClientName $clientBaseId -CollectionName 'mcpServers' -ConfigPath (Resolve-ClaudeDesktopConfigPath) -InstalledExecutable $InstalledExecutable)
        }
        'other' {
            return @([ordered]@{
                    client = $clientBaseId
                    mode = 'artifact-only'
                    target = (Join-Path $InstallBase 'client-registration\other.mcpServers.json')
                    backupPath = $null
                    applied = $true
                })
        }
    }
}

function Invoke-ClientUnregistration {
    param(
        [Parameter(Mandatory)] [string]$SelectedClient,
        $RegistrationRecord
    )

    $clientBaseId = Resolve-ClientBaseId -ClientId $SelectedClient
    $recordedTarget = Get-TrustedRecordedRegistrationTarget -ClientBaseId $clientBaseId -RegistrationRecord $RegistrationRecord
    switch ($clientBaseId) {
        'claude-code' {
            return @(Invoke-OptionalRemovalCommand -Command 'claude' -Arguments @('mcp', 'remove', 'wpf-devtools') -ClientName $clientBaseId)
        }
        'codex' {
            return @(Invoke-OptionalRemovalCommand -Command 'codex' -Arguments @('mcp', 'remove', 'wpf-devtools') -ClientName $clientBaseId)
        }
        'cursor' {
            $cursorProfile = Resolve-CursorRegistrationProfile -SelectedClient $SelectedClient -PromptIfNeeded -RegistrationRecord $RegistrationRecord
            $registration = Remove-JsonConfigRegistration -ClientName $clientBaseId -CollectionName 'mcpServers' -ConfigPath ([string]$cursorProfile.ConfigPath)
            $registration['mode'] = "cursor-$([string]$cursorProfile.Mode)"
            $registration['target'] = [string]$cursorProfile.ConfigPath
            return @($registration)
        }
        'vscode' {
            $manifestTarget = Get-TrustedManagedJsonRegistrationTarget -ClientBaseId $clientBaseId -RegistrationRecord $RegistrationRecord
            $configPath = if (-not [string]::IsNullOrWhiteSpace($manifestTarget)) { $manifestTarget } elseif (-not [string]::IsNullOrWhiteSpace($recordedTarget)) { $recordedTarget } else { Resolve-VsCodeConfigPath }
            return @(Remove-JsonConfigRegistration -ClientName $clientBaseId -CollectionName 'servers' -ConfigPath $configPath)
        }
        'visual-studio' {
            $manifestTarget = Get-TrustedManagedJsonRegistrationTarget -ClientBaseId $clientBaseId -RegistrationRecord $RegistrationRecord
            $configPath = if (-not [string]::IsNullOrWhiteSpace($manifestTarget)) { $manifestTarget } elseif (-not [string]::IsNullOrWhiteSpace($recordedTarget)) { $recordedTarget } else { Resolve-VisualStudioConfigPath }
            return @(Remove-JsonConfigRegistration -ClientName $clientBaseId -CollectionName 'servers' -ConfigPath $configPath)
        }
        'claude-desktop' {
            $manifestTarget = Get-TrustedManagedJsonRegistrationTarget -ClientBaseId $clientBaseId -RegistrationRecord $RegistrationRecord
            $configPath = if (-not [string]::IsNullOrWhiteSpace($manifestTarget)) { $manifestTarget } elseif (-not [string]::IsNullOrWhiteSpace($recordedTarget)) { $recordedTarget } else { Resolve-ClaudeDesktopConfigPath }
            return @(Remove-JsonConfigRegistration -ClientName $clientBaseId -CollectionName 'mcpServers' -ConfigPath $configPath)
        }
        'other' {
            $artifactTargets = @(Get-TrustedOtherRegistrationArtifactTargets -RegistrationRecord $RegistrationRecord)
            $targetPath = if ($artifactTargets.Count -gt 0) {
                [string]$artifactTargets[0]
            }
            elseif (-not [string]::IsNullOrWhiteSpace($recordedTarget)) {
                $recordedTarget
            }
            else {
                $null
            }
            $backupPath = $null
            $applied = $false
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
                $applied = $true
            }

            return @([ordered]@{
                    client = $clientBaseId
                    mode = 'artifact-only'
                    target = $targetPath
                    backupPath = $backupPath
                    applied = $applied
                })
        }
    }
}

function New-ClientRegistrationArtifacts {
    param(
        [Parameter(Mandatory)] [string]$InstallBase,
        [Parameter(Mandatory)] [string]$InstalledExecutable
    )

    $registrationDir = Join-Path $InstallBase 'client-registration'
    $registrationDir = Assert-InstallerLocalPathTrusted -Path $registrationDir
    New-Item -ItemType Directory -Force -Path $registrationDir | Out-Null
    Assert-InstallerLocalPathTrusted -Path $registrationDir | Out-Null

    $serverNode = [ordered]@{
        type = 'stdio'
        command = $InstalledExecutable
        args = @()
    }

    $stdioRegistrationJson = ([ordered]@{ servers = [ordered]@{ 'wpf-devtools' = $serverNode } } |
        ConvertTo-Json -Depth 5)
    $mcpServersRegistrationJson = ([ordered]@{ mcpServers = [ordered]@{ 'wpf-devtools' = $serverNode } } |
        ConvertTo-Json -Depth 5)

    Write-InstallerUtf8NoBomFile -Path (Join-Path $registrationDir 'vscode.json') -Content $stdioRegistrationJson
    Write-InstallerUtf8NoBomFile -Path (Join-Path $registrationDir 'visual-studio.json') -Content $stdioRegistrationJson
    Write-InstallerUtf8NoBomFile -Path (Join-Path $registrationDir 'cursor.global.json') -Content $mcpServersRegistrationJson
    Write-InstallerUtf8NoBomFile -Path (Join-Path $registrationDir 'cursor.project.json') -Content $mcpServersRegistrationJson
    Write-InstallerUtf8NoBomFile -Path (Join-Path $registrationDir 'claude-desktop.json') -Content $mcpServersRegistrationJson
    Write-InstallerUtf8NoBomFile -Path (Join-Path $registrationDir 'other.mcpServers.json') -Content $mcpServersRegistrationJson

    Write-InstallerUtf8NoBomFile -Path (Join-Path $registrationDir 'claude-code.txt') -Content @"
claude mcp add --transport stdio wpf-devtools -- "$InstalledExecutable"

claude mcp remove wpf-devtools
"@

    Write-InstallerUtf8NoBomFile -Path (Join-Path $registrationDir 'codex.txt') -Content @"
codex mcp add wpf-devtools -- "$InstalledExecutable"

codex mcp remove wpf-devtools
"@
}
