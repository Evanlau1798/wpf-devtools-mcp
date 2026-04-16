function Backup-ConfigFile {
    param([Parameter(Mandatory)] [string]$Path)

    if (-not (Test-Path $Path)) {
        return $null
    }

    $backupPath = "$Path.bak-$(Get-Date -Format 'yyyyMMddHHmmssfff')"
    Copy-Item -Path $Path -Destination $backupPath -Force
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

    $map = [ordered]@{}
    if (-not (Test-Path $Path)) {
        return $map
    }

    $raw = Get-Content -Path $Path -Raw
    if ([string]::IsNullOrWhiteSpace($raw)) {
        return $map
    }

    try {
        $parsed = $raw | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        throw (Get-ConfigJsonParseFailureMessage -Path $Path -ErrorMessage $_.Exception.Message)
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

    $directory = Split-Path -Parent $ConfigPath
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }

    $root = Get-ExistingConfigMap -Path $ConfigPath
    $servers = Get-ConfigCollectionMap -Root $root -CollectionName $CollectionName
    $servers['wpf-devtools'] = [ordered]@{
        type = 'stdio'
        command = $InstalledExecutable
        args = @()
    }

    $root[$CollectionName] = $servers
    $backupPath = Backup-ConfigFile -Path $ConfigPath
    $root | ConvertTo-Json -Depth 10 | Set-Content -Path $ConfigPath -Encoding UTF8

    return [ordered]@{
        client = $ClientName
        mode = 'json-file'
        target = $ConfigPath
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

    if (-not (Test-Path $ConfigPath)) {
        return [ordered]@{
            client = $ClientName
            mode = 'json-file'
            target = $ConfigPath
            backupPath = $null
            applied = $false
        }
    }

    $root = Get-ExistingConfigMap -Path $ConfigPath
    $servers = Get-ConfigCollectionMap -Root $root -CollectionName $CollectionName
    if (-not $servers.Contains('wpf-devtools')) {
        return [ordered]@{
            client = $ClientName
            mode = 'json-file'
            target = $ConfigPath
            backupPath = $null
            applied = $false
        }
    }

    [void]$servers.Remove('wpf-devtools')
    $backupPath = Backup-ConfigFile -Path $ConfigPath

    if ($servers.Count -gt 0) {
        $root[$CollectionName] = $servers
    }
    else {
        [void]$root.Remove($CollectionName)
    }

    if ($root.Count -eq 0) {
        '{}' | Set-Content -Path $ConfigPath -Encoding UTF8
    }
    else {
        $root | ConvertTo-Json -Depth 10 | Set-Content -Path $ConfigPath -Encoding UTF8
    }

    return [ordered]@{
        client = $ClientName
        mode = 'json-file'
        target = $ConfigPath
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

    if (-not (Test-Path $ConfigPath)) {
        return $false
    }

    $root = Get-ExistingConfigMap -Path $ConfigPath
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
    if (-not [string]::IsNullOrWhiteSpace($installedExecutable)) {
        $expectedExecutable = Join-Path $expectedInstallBase "current\bin\wpf-devtools-$resolvedArchitecture.exe"
        if ((Test-Path $installedExecutable) -and (Test-InstallerPathEqualsCore -Left $installedExecutable -Right $expectedExecutable)) {
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

    $manifestTarget = Get-TrustedManagedRegistrationTargetFromManifest -StateKeys @('other') -RegistrationRecord $RegistrationRecord
    if (-not [string]::IsNullOrWhiteSpace($manifestTarget)) {
        return $manifestTarget
    }

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
    if (-not [string]::IsNullOrWhiteSpace($recordedTarget) -and
        -not [string]::IsNullOrWhiteSpace($recordInstallRoot) -and
        -not [string]::IsNullOrWhiteSpace($recordArchitecture) -and
        -not [string]::IsNullOrWhiteSpace($installedExecutable)) {
        $normalizedArchitecture = $recordArchitecture.ToLowerInvariant()
        $expectedInstallBase = Join-Path $recordInstallRoot $normalizedArchitecture
        $expectedArtifactTarget = Join-Path $expectedInstallBase 'client-registration\other.mcpServers.json'
        $expectedExecutable = Join-Path $expectedInstallBase "current\bin\wpf-devtools-$normalizedArchitecture.exe"
        if ((Test-Path $installedExecutable) -and
            (Test-InstallerPathEqualsCore -Left $recordedTarget -Right $expectedArtifactTarget) -and
            (Test-InstallerPathEqualsCore -Left $installedExecutable -Right $expectedExecutable)) {
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

    if ([string]::IsNullOrWhiteSpace($manifestPath) -or -not (Test-Path $manifestPath)) {
        return $null
    }

    try {
        $manifest = Get-Content -Path $manifestPath -Raw | ConvertFrom-Json
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

    if ($ClientBaseId -ne 'other' -and $installerOwned -and -not [string]::Equals($evidenceSource, 'state', [System.StringComparison]::OrdinalIgnoreCase)) {
        return $recordedTarget
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

    $resolvedCommand = Get-Command $Command -ErrorAction SilentlyContinue
    if ($null -eq $resolvedCommand) {
        throw "$Command is not installed. Cannot register $ClientName automatically."
    }

    & $Command @Arguments | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "$Command registration failed for $ClientName with exit code $LASTEXITCODE."
    }

    return [ordered]@{
        client = $ClientName
        mode = 'cli'
        target = $Command
        backupPath = $null
        applied = $true
    }
}

function Invoke-OptionalRemovalCommand {
    param(
        [Parameter(Mandatory)] [string]$Command,
        [Parameter(Mandatory)] [string[]]$Arguments,
        [Parameter(Mandatory)] [string]$ClientName
    )

    $resolvedCommand = Get-Command $Command -ErrorAction SilentlyContinue
    if ($null -eq $resolvedCommand) {
        return [ordered]@{
            client = $ClientName
            mode = 'cli'
            target = $Command
            backupPath = $null
            applied = $false
        }
    }

    & $Command @Arguments | Out-Null
    $succeeded = ($LASTEXITCODE -eq 0)
    return [ordered]@{
        client = $ClientName
        mode = 'cli'
        target = $Command
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
                if ([string]::IsNullOrWhiteSpace([string]$candidateTarget) -or -not (Test-Path $candidateTarget)) {
                    continue
                }

                if ([string]::IsNullOrWhiteSpace($backupPath)) {
                    $targetPath = [string]$candidateTarget
                    $backupPath = "$targetPath.bak-$([guid]::NewGuid().ToString('N'))"
                    Copy-Item -Path $targetPath -Destination $backupPath -Force
                }

                Remove-PathIfExists -Path ([string]$candidateTarget)
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
    New-Item -ItemType Directory -Force -Path $registrationDir | Out-Null

    $serverNode = [ordered]@{
        type = 'stdio'
        command = $InstalledExecutable
        args = @()
    }

    ([ordered]@{ servers = [ordered]@{ 'wpf-devtools' = $serverNode } } |
        ConvertTo-Json -Depth 5) | Set-Content -Path (Join-Path $registrationDir 'vscode.json') -Encoding UTF8
    ([ordered]@{ servers = [ordered]@{ 'wpf-devtools' = $serverNode } } |
        ConvertTo-Json -Depth 5) | Set-Content -Path (Join-Path $registrationDir 'visual-studio.json') -Encoding UTF8
    ([ordered]@{ mcpServers = [ordered]@{ 'wpf-devtools' = $serverNode } } |
        ConvertTo-Json -Depth 5) | Set-Content -Path (Join-Path $registrationDir 'cursor.global.json') -Encoding UTF8
    ([ordered]@{ mcpServers = [ordered]@{ 'wpf-devtools' = $serverNode } } |
        ConvertTo-Json -Depth 5) | Set-Content -Path (Join-Path $registrationDir 'cursor.project.json') -Encoding UTF8
    ([ordered]@{ mcpServers = [ordered]@{ 'wpf-devtools' = $serverNode } } |
        ConvertTo-Json -Depth 5) | Set-Content -Path (Join-Path $registrationDir 'claude-desktop.json') -Encoding UTF8
    ([ordered]@{ mcpServers = [ordered]@{ 'wpf-devtools' = $serverNode } } |
        ConvertTo-Json -Depth 5) | Set-Content -Path (Join-Path $registrationDir 'other.mcpServers.json') -Encoding UTF8

    @"
claude mcp add --transport stdio wpf-devtools -- "$InstalledExecutable"

claude mcp remove wpf-devtools
"@ | Set-Content -Path (Join-Path $registrationDir 'claude-code.txt') -Encoding UTF8

    @"
codex mcp add wpf-devtools -- "$InstalledExecutable"

codex mcp remove wpf-devtools
"@ | Set-Content -Path (Join-Path $registrationDir 'codex.txt') -Encoding UTF8
}
