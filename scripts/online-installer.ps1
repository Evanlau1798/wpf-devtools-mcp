param(
    [ValidateSet('install', 'uninstall', 'full-uninstall')]
    [string]$Action = 'install',

    [string]$Version = 'latest',

    [ValidateSet('x64', 'x86', 'arm64')]
    [string]$Architecture,

    [ValidateSet('claude-code', 'codex', 'vscode', 'visual-studio', 'claude-desktop', 'other')]
    [string]$Client,

    [string]$InstallRoot = (Join-Path $env:APPDATA 'WpfDevToolsMcp'),
    [string]$WorkingRoot = (Join-Path ([System.IO.Path]::GetTempPath()) 'wpf-devtools-online-installer'),
    [string]$PackageArchivePath,
    [string]$VsCodeConfigPath,
    [string]$VisualStudioConfigPath,
    [string]$ClaudeDesktopConfigPath,

    [switch]$NonInteractive,
    [switch]$Force,
    [switch]$OutputJson
)

$ErrorActionPreference = 'Stop'
$script:InstallRootWasSpecified = $PSBoundParameters.ContainsKey('InstallRoot')
$script:InstallerTestResponses = New-Object System.Collections.Generic.Queue[string]

if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_TEST_RESPONSES)) {
    foreach ($entry in ($env:WPFDEVTOOLS_INSTALLER_TEST_RESPONSES -split '\|\|')) {
        $script:InstallerTestResponses.Enqueue($entry)
    }
}

function Read-InstallerInput {
    param(
        [Parameter(Mandatory)] [string]$Prompt,
        [string]$DefaultValue
    )

    if ($script:InstallerTestResponses.Count -gt 0) {
        return $script:InstallerTestResponses.Dequeue()
    }

    if ([string]::IsNullOrWhiteSpace($DefaultValue)) {
        return Read-Host $Prompt
    }

    return Read-Host "$Prompt [$DefaultValue]"
}

function Write-InstallerMessage {
    param([Parameter(Mandatory)] [AllowEmptyString()] [string]$Message)

    if (-not $OutputJson) {
        Write-Host $Message
    }
}

function Resolve-AbsoluteDirectory {
    param([Parameter(Mandatory)] [string]$Path)

    New-Item -ItemType Directory -Force -Path $Path | Out-Null
    return (Resolve-Path $Path).Path
}

function Remove-PathIfExists {
    param([string]$Path)

    if (-not [string]::IsNullOrWhiteSpace($Path) -and (Test-Path $Path)) {
        Remove-Item -Path $Path -Recurse -Force
    }
}

function Get-SystemDefaultArchitecture {
    if ($env:PROCESSOR_ARCHITEW6432 -eq 'ARM64' -or $env:PROCESSOR_ARCHITECTURE -eq 'ARM64') {
        return 'arm64'
    }

    if ($env:PROCESSOR_ARCHITEW6432 -eq 'x86' -or $env:PROCESSOR_ARCHITECTURE -eq 'x86') {
        return 'x86'
    }

    return 'x64'
}

$script:InstallerHelperManifestFileName = 'installer-helpers.manifest.json'
$script:InstallerHelperSourcePaths = @(
    'scripts/installer/Tui.ScreenModel.ps1'
    'scripts/installer/Tui.Renderer.ps1'
    'scripts/installer/Tui.Input.ps1'
    'scripts/installer/Tui.Flow.ps1'
    'scripts/installer/Tui.Confirm.ps1'
    'scripts/installer/Installer.Discovery.ps1'
    'scripts/installer/Installer.Uninstall.ps1'
)
$script:TuiHelperDownloadBaseUri = 'https://raw.githubusercontent.com/Evanlau1798/wpf-devtools-mcp/master/scripts/installer'
$script:TuiScreenNames = @('HomeScreen', 'InstallScreen', 'UninstallScreen', 'ConfirmScreen', 'ProgressScreen')
$script:TuiUiMarkers = @('Installed v', 'Update available', 'Architecture', 'Install location', 'Update All')
$script:TuiConfirmationModes = @('unregister', 'full-uninstall')
$script:TuiUninstallActions = @('UnregisterTarget', 'FullUninstall', 'Full Uninstall')
$script:InstallerDiscoveryContractFields = @('RegistrationMode', 'InstalledExecutable', 'InstallerOwned', 'ConfirmationStep')
$script:TuiNavigationKeys = @(
    [ConsoleKey]::UpArrow
    [ConsoleKey]::DownArrow
    [ConsoleKey]::Enter
    [ConsoleKey]::Escape
    [ConsoleKey]::Backspace
)
$script:TuiNavigationTokens = @('ConsoleKey.UpArrow', 'ConsoleKey.DownArrow', 'ConsoleKey.Enter')

function Resolve-InstallerScriptRoot {
    if (-not [string]::IsNullOrWhiteSpace($PSScriptRoot)) {
        return $PSScriptRoot
    }

    if (-not [string]::IsNullOrWhiteSpace($PSCommandPath)) {
        return (Split-Path -Parent $PSCommandPath)
    }

    return $null
}

function Get-TuiHelperRuntimeRoot {
    $runtimeRoot = Join-Path (Resolve-AbsoluteDirectory -Path $WorkingRoot) 'tui-helpers'
    New-Item -ItemType Directory -Force -Path $runtimeRoot | Out-Null
    return $runtimeRoot
}

function Get-TuiHelperCacheKeyPath {
    param([Parameter(Mandatory)] [string]$RuntimeRoot)

    return (Join-Path $RuntimeRoot 'helper-cache-key.txt')
}

function Get-TuiHelperManifestPath {
    param([Parameter(Mandatory)] [string]$RootPath)

    return (Join-Path $RootPath $script:InstallerHelperManifestFileName)
}

function Get-TuiHelperOverrideDirectory {
    if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_HELPER_DIRECTORY)) {
        return $env:WPFDEVTOOLS_INSTALLER_HELPER_DIRECTORY
    }

    return $null
}

function Get-HelperLeafNames {
    return @($script:InstallerHelperSourcePaths | ForEach-Object { Split-Path $_ -Leaf })
}

function Get-InstallerTimeoutSeconds {
    param(
        [Parameter(Mandatory)] [string]$EnvironmentVariable,
        [Parameter(Mandatory)] [int]$DefaultValue,
        [int]$MinimumValue = 1,
        [int]$MaximumValue = 120
    )

    $rawValue = [Environment]::GetEnvironmentVariable($EnvironmentVariable)
    if ([string]::IsNullOrWhiteSpace($rawValue)) {
        return $DefaultValue
    }

    $parsedValue = 0
    if (-not [int]::TryParse($rawValue, [ref]$parsedValue)) {
        return $DefaultValue
    }

    return [Math]::Min($MaximumValue, [Math]::Max($MinimumValue, $parsedValue))
}

function Get-TuiHelperRequestTimeoutSeconds {
    return (Get-InstallerTimeoutSeconds -EnvironmentVariable 'WPFDEVTOOLS_INSTALLER_HELPER_TIMEOUT_SEC' -DefaultValue 5 -MinimumValue 1 -MaximumValue 30)
}

function Get-TuiHelperBootstrapTimeoutSeconds {
    return (Get-InstallerTimeoutSeconds -EnvironmentVariable 'WPFDEVTOOLS_INSTALLER_HELPER_BOOTSTRAP_TIMEOUT_SEC' -DefaultValue 20 -MinimumValue 3 -MaximumValue 120)
}

function Get-InstallerVerificationTimeoutSeconds {
    return (Get-InstallerTimeoutSeconds -EnvironmentVariable 'WPFDEVTOOLS_INSTALLER_VERIFICATION_TIMEOUT_SEC' -DefaultValue 2 -MinimumValue 1 -MaximumValue 30)
}

function Write-TuiBootstrapMessage {
    param([Parameter(Mandatory)] [string]$Message)

    if ($script:LastTuiBootstrapMessage -eq $Message) {
        return
    }

    Write-InstallerMessage $Message
    $script:LastTuiBootstrapMessage = $Message
}

function Get-ComputedInstallerHelperCacheKey {
    param(
        [Parameter(Mandatory)] [string]$HelperDirectory,
        [Parameter(Mandatory)] [string[]]$HelperFiles
    )

    $records = New-Object System.Collections.Generic.List[string]
    foreach ($helperFile in ($HelperFiles | Sort-Object)) {
        $helperPath = Join-Path $HelperDirectory $helperFile
        if (-not (Test-Path $helperPath)) {
            throw "Helper file was not found while computing the installer cache key: $helperPath"
        }

        $fileHash = (Get-FileHash -Algorithm SHA256 -Path $helperPath).Hash.ToLowerInvariant()
        $records.Add("${helperFile}:$fileHash")
    }

    $utf8 = [System.Text.Encoding]::UTF8.GetBytes(($records -join '|'))
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hashBytes = $sha256.ComputeHash($utf8)
    }
    finally {
        $sha256.Dispose()
    }

    return 'sha256:' + (($hashBytes | ForEach-Object { $_.ToString('x2') }) -join '')
}

function Get-InstallerHelperRuntimeCacheKey {
    param([Parameter(Mandatory)] $Manifest)

    $seedParts = @(
        [string]$Manifest.CacheKey
        ((Get-Item 'function:Test-TuiSupport').ScriptBlock.ToString())
        ((Get-Item 'function:Resolve-Selection').ScriptBlock.ToString())
        ((Get-Item 'function:Invoke-InstallerAction').ScriptBlock.ToString())
        ($script:InstallerHelperSourcePaths -join '|')
    )

    $utf8 = [System.Text.Encoding]::UTF8.GetBytes(($seedParts -join '|'))
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hashBytes = $sha256.ComputeHash($utf8)
    }
    finally {
        $sha256.Dispose()
    }

    return 'runtime-sha256:' + (($hashBytes | ForEach-Object { $_.ToString('x2') }) -join '')
}

function Read-TuiHelperManifest {
    param(
        [Parameter(Mandatory)] [string]$ManifestPath,
        [Parameter(Mandatory)] [string]$HelperDirectory
    )

    if (-not (Test-Path $ManifestPath)) {
        return $null
    }

    $parsed = Get-Content -Path $ManifestPath -Raw | ConvertFrom-Json
    $helperFiles = @()
    if ($null -ne $parsed.helperFiles) {
        foreach ($entry in $parsed.helperFiles) {
            if (-not [string]::IsNullOrWhiteSpace([string]$entry)) {
                $helperFiles += [string]$entry
            }
        }
    }

    if ($helperFiles.Count -eq 0) {
        $helperFiles = @(Get-HelperLeafNames)
    }

    $cacheKey = [string]$parsed.cacheKey
    if ([string]::IsNullOrWhiteSpace($cacheKey)) {
        $cacheKey = Get-ComputedInstallerHelperCacheKey -HelperDirectory $HelperDirectory -HelperFiles $helperFiles
    }

    return [ordered]@{
        CacheKey = $cacheKey
        HelperFiles = @($helperFiles)
    }
}

function Get-TuiHelperManifest {
    if ($null -ne $script:TuiHelperManifest) {
        return $script:TuiHelperManifest
    }

    $localScriptRoot = Resolve-InstallerScriptRoot
    $candidateRoots = New-Object System.Collections.Generic.List[string]
    if (-not [string]::IsNullOrWhiteSpace($localScriptRoot)) {
        $candidateRoots.Add((Join-Path $localScriptRoot 'installer'))
    }

    $overrideDirectory = Get-TuiHelperOverrideDirectory
    if (-not [string]::IsNullOrWhiteSpace($overrideDirectory)) {
        $candidateRoots.Add($overrideDirectory)
    }

    foreach ($candidateRoot in $candidateRoots) {
        if ([string]::IsNullOrWhiteSpace($candidateRoot) -or -not (Test-Path $candidateRoot)) {
            continue
        }

        $helperFiles = @(Get-HelperLeafNames)
        $allPresent = $true
        foreach ($helperFile in $helperFiles) {
            if (-not (Test-Path (Join-Path $candidateRoot $helperFile))) {
                $allPresent = $false
                break
            }
        }

        if (-not $allPresent) {
            continue
        }

        $manifestPath = Get-TuiHelperManifestPath -RootPath $candidateRoot
        $manifest = Read-TuiHelperManifest -ManifestPath $manifestPath -HelperDirectory $candidateRoot
        if ($null -eq $manifest) {
            $manifest = [ordered]@{
                CacheKey = (Get-ComputedInstallerHelperCacheKey -HelperDirectory $candidateRoot -HelperFiles $helperFiles)
                HelperFiles = $helperFiles
            }
        }

        $script:TuiHelperManifest = $manifest
        return $manifest
    }

    if (Resolve-InstallerMode -ne 'online') {
        return $null
    }

    $runtimeRoot = Get-TuiHelperRuntimeRoot
    $manifestPath = Get-TuiHelperManifestPath -RootPath $runtimeRoot
    $downloadBaseUri = if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_HELPER_BASE_URI)) {
        $env:WPFDEVTOOLS_INSTALLER_HELPER_BASE_URI.TrimEnd('/')
    }
    else {
        $script:TuiHelperDownloadBaseUri
    }

    $manifestUri = "$downloadBaseUri/$($script:InstallerHelperManifestFileName)"
    $temporaryManifestPath = "$manifestPath.download"
    try {
        Invoke-WebRequest -Uri $manifestUri -OutFile $temporaryManifestPath -Headers @{ 'User-Agent' = 'wpf-devtools-online-installer' } -TimeoutSec (Get-TuiHelperRequestTimeoutSeconds)
        Move-Item -Path $temporaryManifestPath -Destination $manifestPath -Force
    }
    catch {
        Remove-PathIfExists -Path $temporaryManifestPath
        throw "Failed to download installer helper manifest from $manifestUri. $($_.Exception.Message)"
    }

    $script:TuiHelperManifest = Read-TuiHelperManifest -ManifestPath $manifestPath -HelperDirectory $runtimeRoot
    return $script:TuiHelperManifest
}

function Ensure-TuiHelpersAvailable {
    if (-not [string]::IsNullOrWhiteSpace($script:TuiHelperResolvedRoot)) {
        return $script:TuiHelperResolvedRoot
    }

    $manifest = Get-TuiHelperManifest
    $localScriptRoot = Resolve-InstallerScriptRoot
    $candidateRoots = New-Object System.Collections.Generic.List[string]
    if (-not [string]::IsNullOrWhiteSpace($localScriptRoot)) {
        $candidateRoots.Add((Join-Path $localScriptRoot 'installer'))
    }

    $overrideDirectory = Get-TuiHelperOverrideDirectory
    if (-not [string]::IsNullOrWhiteSpace($overrideDirectory)) {
        $candidateRoots.Add($overrideDirectory)
    }

    $helperFiles = if ($null -ne $manifest -and $manifest.HelperFiles.Count -gt 0) { @($manifest.HelperFiles) } else { @(Get-HelperLeafNames) }
    foreach ($candidateRoot in $candidateRoots) {
        if ([string]::IsNullOrWhiteSpace($candidateRoot)) {
            continue
        }

        $allPresent = $true
        foreach ($helperFile in $helperFiles) {
            if (-not (Test-Path (Join-Path $candidateRoot $helperFile))) {
                $allPresent = $false
                break
            }
        }

        if ($allPresent) {
            $script:TuiHelperResolvedRoot = $candidateRoot
            return $candidateRoot
        }
    }

    if (Resolve-InstallerMode -ne 'online') {
        return $null
    }

    $runtimeRoot = Get-TuiHelperRuntimeRoot
    $cacheKeyPath = Get-TuiHelperCacheKeyPath -RuntimeRoot $runtimeRoot
    $cachedKey = if (Test-Path $cacheKeyPath) { (Get-Content -Path $cacheKeyPath -Raw).Trim() } else { $null }
    $targetCacheKey = if ($null -ne $manifest) { Get-InstallerHelperRuntimeCacheKey -Manifest $manifest } else { $null }
    if ($cachedKey -ne $targetCacheKey) {
        Remove-PathIfExists -Path $runtimeRoot
        New-Item -ItemType Directory -Force -Path $runtimeRoot | Out-Null
        if ($null -ne $manifest) {
            $manifest | ConvertTo-Json -Depth 5 | Set-Content -Path (Get-TuiHelperManifestPath -RootPath $runtimeRoot) -Encoding UTF8
        }
    }

    $downloadBaseUri = if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_HELPER_BASE_URI)) {
        $env:WPFDEVTOOLS_INSTALLER_HELPER_BASE_URI.TrimEnd('/')
    }
    else {
        $script:TuiHelperDownloadBaseUri
    }

    $requestTimeoutSeconds = Get-TuiHelperRequestTimeoutSeconds
    $bootstrapDeadline = [DateTimeOffset]::UtcNow.AddSeconds((Get-TuiHelperBootstrapTimeoutSeconds))
    $totalHelperCount = $helperFiles.Count
    $downloadIndex = 0

    foreach ($helperFile in $helperFiles) {
        if ([DateTimeOffset]::UtcNow -gt $bootstrapDeadline) {
            throw 'Installer UI bootstrap timed out before the runtime assets finished downloading.'
        }

        $destinationPath = Join-Path $runtimeRoot $helperFile
        if (Test-Path $destinationPath) {
            $downloadIndex += 1
            continue
        }

        $downloadIndex += 1
        Write-TuiBootstrapMessage "Preparing installer UI... ($downloadIndex/$totalHelperCount)"
        $downloadUri = "$downloadBaseUri/$helperFile"
        try {
            Invoke-WebRequest -Uri $downloadUri -OutFile $destinationPath -Headers @{ 'User-Agent' = 'wpf-devtools-online-installer' } -TimeoutSec $requestTimeoutSeconds
        }
        catch {
            throw "Failed to download installer UI runtime from $downloadUri. $($_.Exception.Message)"
        }
    }

    Set-Content -Path $cacheKeyPath -Value $targetCacheKey -Encoding UTF8
    $script:TuiHelperResolvedRoot = $runtimeRoot
    return $runtimeRoot
}

function Import-TuiHelpers {
    $helperRoot = Ensure-TuiHelpersAvailable
    if ([string]::IsNullOrWhiteSpace($helperRoot)) {
        throw 'TUI helper scripts are unavailable in the current execution context.'
    }

    $helperPaths = New-Object System.Collections.Generic.List[string]
    foreach ($repoRelativePath in $script:InstallerHelperSourcePaths) {
        $leafName = Split-Path $repoRelativePath -Leaf
        $runtimePath = Join-Path $helperRoot $leafName
        if (-not (Test-Path $runtimePath)) {
            throw "TUI helper script was not found: $runtimePath"
        }

        $helperPaths.Add($runtimePath)
    }

    return @($helperPaths)
}

function Invoke-WithTuiHelpers {
    param([Parameter(Mandatory)] [scriptblock]$ScriptBlock)

    foreach ($helperPath in @(Import-TuiHelpers)) {
        . $helperPath
    }

    return (. $ScriptBlock)
}

function Get-NextArchitecture {
    param(
        [Parameter(Mandatory)] [string]$Current,
        [Parameter(Mandatory)] [int]$Direction
    )

    $architectures = @('x64', 'x86', 'arm64')
    $index = [Array]::IndexOf($architectures, $Current)
    if ($index -lt 0) {
        $index = [Array]::IndexOf($architectures, (Get-SystemDefaultArchitecture))
    }

    $index = ($index + $Direction) % $architectures.Count
    if ($index -lt 0) {
        $index += $architectures.Count
    }

    return $architectures[$index]
}

function Get-SupportedClients {
    return @(
        [pscustomobject]@{ Id = 'claude-code'; Label = 'Claude Code'; ConfigType = 'cli' }
        [pscustomobject]@{ Id = 'codex'; Label = 'Codex'; ConfigType = 'cli' }
        [pscustomobject]@{ Id = 'vscode'; Label = 'VS Code'; ConfigType = 'json-file' }
        [pscustomobject]@{ Id = 'visual-studio'; Label = 'Visual Studio'; ConfigType = 'json-file' }
        [pscustomobject]@{ Id = 'claude-desktop'; Label = 'Claude Desktop'; ConfigType = 'json-file' }
        [pscustomobject]@{ Id = 'other'; Label = 'Other'; ConfigType = 'artifact-only' }
    )
}

function Resolve-ClientLabel {
    param([Parameter(Mandatory)] [string]$ClientId)

    $client = Get-SupportedClients | Where-Object { $_.Id -eq $ClientId } | Select-Object -First 1
    if ($null -ne $client) {
        return [string]$client.Label
    }

    return $ClientId
}

function Get-DefaultClient {
    if ($null -ne (Get-Command 'claude' -ErrorAction SilentlyContinue)) { return 'claude-code' }
    if ($null -ne (Get-Command 'codex' -ErrorAction SilentlyContinue)) { return 'codex' }
    if (Test-Path (Join-Path $env:APPDATA 'Code\User')) { return 'vscode' }
    if (Test-Path (Join-Path $env:USERPROFILE '.mcp.json')) { return 'visual-studio' }
    return 'other'
}

function Resolve-InstallerStatePath {
    $stateRoot = Resolve-AbsoluteDirectory -Path (Join-Path $env:APPDATA 'WpfDevToolsMcp')
    return (Join-Path $stateRoot 'installer-state.json')
}

function Get-EmptyInstallerState {
    return [ordered]@{
        lastInstallRoot = $null
        architectures = [ordered]@{}
        registrations = [ordered]@{}
    }
}

function Get-InstallerState {
    $statePath = Resolve-InstallerStatePath
    $state = Get-EmptyInstallerState

    if (-not (Test-Path $statePath)) {
        return $state
    }

    $raw = Get-Content -Path $statePath -Raw
    if ([string]::IsNullOrWhiteSpace($raw)) {
        return $state
    }

    $parsed = $raw | ConvertFrom-Json
    $state.lastInstallRoot = [string]$parsed.lastInstallRoot

    if ($null -ne $parsed.architectures) {
        foreach ($property in $parsed.architectures.PSObject.Properties) {
            $state.architectures[$property.Name] = [ordered]@{
                version = [string]$property.Value.version
                executable = [string]$property.Value.executable
                installRoot = [string]$property.Value.installRoot
            }
        }
    }

    if ($null -ne $parsed.registrations) {
        foreach ($property in $parsed.registrations.PSObject.Properties) {
            $state.registrations[$property.Name] = [ordered]@{
                architecture = [string]$property.Value.architecture
                installRoot = [string]$property.Value.installRoot
                mode = [string]$property.Value.mode
                target = [string]$property.Value.target
                resolvedVersion = [string]$property.Value.resolvedVersion
                installedExecutable = [string]$property.Value.installedExecutable
                lastVerifiedUtc = [string]$property.Value.lastVerifiedUtc
            }
        }
    }

    return $state
}

function Save-InstallerState {
    param([Parameter(Mandatory)] $State)

    $statePath = Resolve-InstallerStatePath
    $State | ConvertTo-Json -Depth 10 | Set-Content -Path $statePath -Encoding UTF8
    return $statePath
}

function Resolve-PreferredInstallRoot {
    if ($script:InstallRootWasSpecified) {
        return $InstallRoot
    }

    $state = Get-InstallerState
    if (-not [string]::IsNullOrWhiteSpace($state.lastInstallRoot)) {
        return [string]$state.lastInstallRoot
    }

    # Default install root: %APPDATA%\WpfDevToolsMcp
    return (Join-Path $env:APPDATA 'WpfDevToolsMcp')
}

function Backup-ConfigFile {
    param([Parameter(Mandatory)] [string]$Path)

    if (-not (Test-Path $Path)) {
        return $null
    }

    $backupPath = "$Path.bak-$(Get-Date -Format 'yyyyMMddHHmmssfff')"
    Copy-Item -Path $Path -Destination $backupPath -Force
    return $backupPath
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

    $parsed = $raw | ConvertFrom-Json
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

    switch ($SelectedClient) {
        'claude-code' {
            return @(Invoke-RegistrationCommand -Command 'claude' -Arguments @('mcp', 'add', '--transport', 'stdio', 'wpf-devtools', '--', $InstalledExecutable) -ClientName $SelectedClient)
        }
        'codex' {
            return @(Invoke-RegistrationCommand -Command 'codex' -Arguments @('mcp', 'add', 'wpf-devtools', '--', $InstalledExecutable) -ClientName $SelectedClient)
        }
        'vscode' {
            return @(Set-JsonConfigRegistration -ClientName $SelectedClient -CollectionName 'servers' -ConfigPath (Resolve-VsCodeConfigPath) -InstalledExecutable $InstalledExecutable)
        }
        'visual-studio' {
            return @(Set-JsonConfigRegistration -ClientName $SelectedClient -CollectionName 'servers' -ConfigPath (Resolve-VisualStudioConfigPath) -InstalledExecutable $InstalledExecutable)
        }
        'claude-desktop' {
            return @(Set-JsonConfigRegistration -ClientName $SelectedClient -CollectionName 'mcpServers' -ConfigPath (Resolve-ClaudeDesktopConfigPath) -InstalledExecutable $InstalledExecutable)
        }
        'other' {
            return @([ordered]@{
                    client = $SelectedClient
                    mode = 'artifact-only'
                    target = (Join-Path $InstallBase 'client-registration\other.mcpServers.json')
                    backupPath = $null
                    applied = $true
                })
        }
    }
}

function Invoke-ClientUnregistration {
    param([Parameter(Mandatory)] [string]$SelectedClient)

    switch ($SelectedClient) {
        'claude-code' {
            return @(Invoke-OptionalRemovalCommand -Command 'claude' -Arguments @('mcp', 'remove', 'wpf-devtools') -ClientName $SelectedClient)
        }
        'codex' {
            return @(Invoke-OptionalRemovalCommand -Command 'codex' -Arguments @('mcp', 'remove', 'wpf-devtools') -ClientName $SelectedClient)
        }
        'vscode' {
            return @(Remove-JsonConfigRegistration -ClientName $SelectedClient -CollectionName 'servers' -ConfigPath (Resolve-VsCodeConfigPath))
        }
        'visual-studio' {
            return @(Remove-JsonConfigRegistration -ClientName $SelectedClient -CollectionName 'servers' -ConfigPath (Resolve-VisualStudioConfigPath))
        }
        'claude-desktop' {
            return @(Remove-JsonConfigRegistration -ClientName $SelectedClient -CollectionName 'mcpServers' -ConfigPath (Resolve-ClaudeDesktopConfigPath))
        }
        'other' {
            return @([ordered]@{
                    client = $SelectedClient
                    mode = 'artifact-only'
                    target = $null
                    backupPath = $null
                    applied = $true
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

function Get-ReleaseAssetName {
    param(
        [Parameter(Mandatory)] [string]$ResolvedVersion,
        [Parameter(Mandatory)] [string]$ResolvedArchitecture
    )

    return [string]::Format('release_{0}_win-{1}.zip', $ResolvedVersion, $ResolvedArchitecture)
}

function Get-ReleaseDownloadUri {
    param(
        [Parameter(Mandatory)] [string]$ResolvedVersion,
        [Parameter(Mandatory)] [string]$ResolvedArchitecture
    )

    $assetName = Get-ReleaseAssetName -ResolvedVersion $ResolvedVersion -ResolvedArchitecture $ResolvedArchitecture
    if ($ResolvedVersion -eq 'latest') {
        return "https://github.com/Evanlau1798/wpf-devtools-mcp/releases/latest/download/$assetName"
    }

    return "https://github.com/Evanlau1798/wpf-devtools-mcp/releases/download/$ResolvedVersion/$assetName"
}

function Get-GitHubReleaseApiUri {
    param([Parameter(Mandatory)] [string]$ResolvedVersion)

    $apiBase = 'https://api.github.com/repos/Evanlau1798/wpf-devtools-mcp/releases'
    if ($ResolvedVersion -eq 'latest') {
        return "$apiBase/latest"
    }

    $tag = if ($ResolvedVersion.StartsWith('v')) { $ResolvedVersion } else { "v$ResolvedVersion" }
    return "$apiBase/tags/$tag"
}

function Get-ReleaseAssetDownloadDetails {
    param(
        [Parameter(Mandatory)] [string]$ResolvedVersion,
        [Parameter(Mandatory)] [string]$ResolvedArchitecture
    )

    $assetName = Get-ReleaseAssetName -ResolvedVersion $ResolvedVersion -ResolvedArchitecture $ResolvedArchitecture
    $fallbackUri = Get-ReleaseDownloadUri -ResolvedVersion $ResolvedVersion -ResolvedArchitecture $ResolvedArchitecture

    try {
        $release = Invoke-RestMethod -Uri (Get-GitHubReleaseApiUri -ResolvedVersion $ResolvedVersion) -Headers @{ 'User-Agent' = 'wpf-devtools-online-installer' } -TimeoutSec 15
        $asset = @($release.assets) | Where-Object { $_.name -eq $assetName } | Select-Object -First 1
        if ($null -ne $asset) {
            return [ordered]@{
                AssetName = $assetName
                DownloadUri = [string]$asset.browser_download_url
                ResolvedVersion = ([string]$release.tag_name).TrimStart('v')
            }
        }
    }
    catch {
    }

    return [ordered]@{
        AssetName = $assetName
        DownloadUri = $fallbackUri
        ResolvedVersion = $ResolvedVersion
    }
}

function Resolve-LocalPackageRoot {
    $scriptRoot = Resolve-InstallerScriptRoot
    if ([string]::IsNullOrWhiteSpace($scriptRoot)) {
        return $null
    }

    $binManifestPath = Join-Path $scriptRoot 'manifest.json'
    if (Test-Path $binManifestPath) {
        return (Split-Path -Parent $scriptRoot)
    }

    $packageManifestPath = Join-Path $scriptRoot 'bin\manifest.json'
    if (Test-Path $packageManifestPath) {
        return $scriptRoot
    }

    return $null
}

function Resolve-LatestVersionCachePath {
    $stateRoot = Resolve-AbsoluteDirectory -Path (Join-Path $env:APPDATA 'WpfDevToolsMcp')
    return (Join-Path $stateRoot 'latest-release-cache.json')
}

function Get-CachedLatestInstallerVersion {
    $cachePath = Resolve-LatestVersionCachePath
    if (-not (Test-Path $cachePath)) {
        return $null
    }

    try {
        $parsed = Get-Content -Path $cachePath -Raw | ConvertFrom-Json
        return [string]$parsed.version
    }
    catch {
        return $null
    }
}

function Save-LatestInstallerVersionCache {
    param([Parameter(Mandatory)] [string]$VersionValue)

    if ([string]::IsNullOrWhiteSpace($VersionValue)) {
        return
    }

    $cachePath = Resolve-LatestVersionCachePath
    [ordered]@{
        version = $VersionValue
        refreshedUtc = [DateTime]::UtcNow.ToString('o')
    } | ConvertTo-Json -Depth 3 | Set-Content -Path $cachePath -Encoding UTF8
}

function Resolve-InstallerMode {
    if (-not [string]::IsNullOrWhiteSpace($PackageArchivePath)) { return 'offline' }
    if (-not [string]::IsNullOrWhiteSpace((Resolve-LocalPackageRoot))) { return 'offline' }
    return 'online'
}

function Resolve-PackageManifestPath {
    param([Parameter(Mandatory)] [string]$PackageDirectory)

    foreach ($candidate in @((Join-Path $PackageDirectory 'manifest.json'), (Join-Path $PackageDirectory 'bin\manifest.json'))) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    throw "manifest.json was not found under package path: $PackageDirectory"
}

function Resolve-PackageExecutable {
    param(
        [Parameter(Mandatory)] [string]$PackageDirectory,
        [Parameter(Mandatory)] [string]$ResolvedArchitecture
    )

    foreach ($candidate in @(
            (Join-Path $PackageDirectory "bin\wpf-devtools-$ResolvedArchitecture.exe"),
            (Join-Path $PackageDirectory "wpf-devtools-$ResolvedArchitecture.exe"),
            (Join-Path $PackageDirectory 'bin\WpfDevTools.Mcp.Server.exe'))) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    throw "Package does not contain an executable for architecture '$ResolvedArchitecture'."
}

function Resolve-PackageSession {
    param(
        [Parameter(Mandatory)] [string]$Mode,
        [Parameter(Mandatory)] [string]$ResolvedVersion,
        [Parameter(Mandatory)] [string]$ResolvedArchitecture
    )

    $workingRootPath = Resolve-AbsoluteDirectory -Path $WorkingRoot
    $sessionRoot = Join-Path $workingRootPath ([Guid]::NewGuid().ToString('N'))
    $extractRoot = Join-Path $sessionRoot 'package'

    if ($Mode -eq 'offline' -and -not [string]::IsNullOrWhiteSpace($PackageArchivePath)) {
        New-Item -ItemType Directory -Force -Path $extractRoot | Out-Null
        Expand-Archive -Path (Resolve-Path $PackageArchivePath).Path -DestinationPath $extractRoot -Force
        return [ordered]@{
            PackageDirectory = $extractRoot
            SessionRoot = $sessionRoot
            CleanupSession = $true
            DownloadSource = 'local-package'
            DownloadUri = Get-ReleaseDownloadUri -ResolvedVersion $ResolvedVersion -ResolvedArchitecture $ResolvedArchitecture
            PackageAssetName = (Split-Path -Leaf $PackageArchivePath)
            ResolvedVersion = $ResolvedVersion
        }
    }

    if ($Mode -eq 'offline') {
        $localRoot = Resolve-LocalPackageRoot
        $manifest = Get-Content -Path (Resolve-PackageManifestPath -PackageDirectory $localRoot) -Raw | ConvertFrom-Json
        return [ordered]@{
            PackageDirectory = $localRoot
            SessionRoot = $null
            CleanupSession = $false
            DownloadSource = 'local-package'
            DownloadUri = $null
            PackageAssetName = $null
            ResolvedVersion = [string]$manifest.version
        }
    }

    $downloadDetails = Get-ReleaseAssetDownloadDetails -ResolvedVersion $ResolvedVersion -ResolvedArchitecture $ResolvedArchitecture
    $archivePath = Join-Path $workingRootPath ([string]$downloadDetails.AssetName)
    Invoke-WebRequest -Uri ([string]$downloadDetails.DownloadUri) -OutFile $archivePath
    New-Item -ItemType Directory -Force -Path $extractRoot | Out-Null
    Expand-Archive -Path $archivePath -DestinationPath $extractRoot -Force
    return [ordered]@{
        PackageDirectory = $extractRoot
        SessionRoot = $sessionRoot
        CleanupSession = $true
        DownloadSource = 'github-release'
        DownloadUri = [string]$downloadDetails.DownloadUri
        PackageAssetName = [string]$downloadDetails.AssetName
        ResolvedVersion = [string]$downloadDetails.ResolvedVersion
    }
}

function Get-OfflineVersionHint {
    param([Parameter(Mandatory)] [string]$Mode)

    if ($Mode -ne 'offline') {
        return $null
    }

    try {
        $localRoot = Resolve-LocalPackageRoot
        if ([string]::IsNullOrWhiteSpace($localRoot)) {
            return $null
        }

        $manifest = Get-Content -Path (Resolve-PackageManifestPath -PackageDirectory $localRoot) -Raw | ConvertFrom-Json
        $localVersion = [string]$manifest.version
        if ([string]::IsNullOrWhiteSpace($localVersion)) {
            return $null
        }

        $latestVersion = Get-LatestInstallerVersion -UseCacheOnly
        if ([string]::IsNullOrWhiteSpace($latestVersion) -or $latestVersion -eq $localVersion) {
            return "Offline package version v$localVersion."
        }

        return "Offline package version v$localVersion. Latest release is v$latestVersion."
    }
    catch {
        return $null
    }
}

function Install-PackagePayload {
    param(
        [Parameter(Mandatory)] [string]$PackageDirectory,
        [Parameter(Mandatory)] [psobject]$PackageManifest,
        [Parameter(Mandatory)] [string]$ResolvedArchitecture,
        [Parameter(Mandatory)] [string]$ResolvedInstallRoot,
        [Parameter(Mandatory)] [string]$ResolvedVersion
    )

    $packageExecutable = Resolve-PackageExecutable -PackageDirectory $PackageDirectory -ResolvedArchitecture $ResolvedArchitecture
    $installRootFullPath = Resolve-AbsoluteDirectory -Path $ResolvedInstallRoot
    $installBase = Join-Path $installRootFullPath $ResolvedArchitecture
    $currentDir = Join-Path $installBase 'current'
    $installManifestPath = Join-Path $installBase 'install-manifest.json'
    $reusedExistingBinary = $false

    if ((Test-Path $installManifestPath) -and -not $Force) {
        $existingManifest = Get-Content -Path $installManifestPath -Raw | ConvertFrom-Json
        if (($existingManifest.version -eq $ResolvedVersion) -and (Test-Path ([string]$existingManifest.executable))) {
            $reusedExistingBinary = $true
            New-ClientRegistrationArtifacts -InstallBase $installBase -InstalledExecutable ([string]$existingManifest.executable)
            return [ordered]@{
                installRoot = $installRootFullPath
                installBase = $installBase
                installedExecutable = [string]$existingManifest.executable
                reusedExistingBinary = $reusedExistingBinary
            }
        }
    }

    Remove-PathIfExists -Path $currentDir
    New-Item -ItemType Directory -Force -Path $installBase | Out-Null
    New-Item -ItemType Directory -Force -Path $currentDir | Out-Null
    Copy-Item -Path (Join-Path $PackageDirectory '*') -Destination $currentDir -Recurse -Force
    Remove-PathIfExists -Path (Join-Path $currentDir 'run.bat')
    Remove-PathIfExists -Path (Join-Path $currentDir 'bin\install.ps1')

    $relativeExecutable = $packageExecutable.Substring($PackageDirectory.Length).TrimStart('\', '/')
    $installedExecutable = Join-Path $currentDir $relativeExecutable

    ([ordered]@{
            name = 'wpf-devtools'
            architecture = $ResolvedArchitecture
            version = $ResolvedVersion
            installRoot = $installRootFullPath
            installDir = $currentDir
            executable = $installedExecutable
            channel = [string]$PackageManifest.channel
            buildConfiguration = [string]$PackageManifest.buildConfiguration
            signaturePolicy = [string]$PackageManifest.signaturePolicy
            installedUtc = [DateTime]::UtcNow.ToString('o')
        } | ConvertTo-Json -Depth 5) | Set-Content -Path $installManifestPath -Encoding UTF8

    New-ClientRegistrationArtifacts -InstallBase $installBase -InstalledExecutable $installedExecutable
    return [ordered]@{
        installRoot = $installRootFullPath
        installBase = $installBase
        installedExecutable = $installedExecutable
        reusedExistingBinary = $reusedExistingBinary
    }
}

function Update-InstallerStateAfterInstall {
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)] [string]$ResolvedInstallRoot,
        [Parameter(Mandatory)] [string]$ResolvedArchitecture,
        [Parameter(Mandatory)] [string]$ResolvedVersion,
        [Parameter(Mandatory)] [string]$InstalledExecutable,
        [Parameter(Mandatory)] [string]$SelectedClient,
        [Parameter(Mandatory)] $Registration,
        [Parameter(Mandatory)] [string]$LastVerifiedUtc
    )

    $State.lastInstallRoot = $ResolvedInstallRoot
    $State.architectures[$ResolvedArchitecture] = [ordered]@{
        version = $ResolvedVersion
        executable = $InstalledExecutable
        installRoot = $ResolvedInstallRoot
    }
    $State.registrations[$SelectedClient] = [ordered]@{
        architecture = $ResolvedArchitecture
        installRoot = $ResolvedInstallRoot
        mode = [string]$Registration.mode
        target = [string]$Registration.target
        resolvedVersion = $ResolvedVersion
        installedExecutable = $InstalledExecutable
        lastVerifiedUtc = $LastVerifiedUtc
    }
}

function Get-AvailableInstallerUpdates {
    param(
        [Parameter(Mandatory)] $State,
        [string]$LatestVersion
    )

    $updates = @()
    if ([string]::IsNullOrWhiteSpace($LatestVersion)) {
        return @($updates)
    }

    foreach ($property in $State.registrations.GetEnumerator()) {
        $resolvedVersion = [string]$property.Value.resolvedVersion
        if ([string]::IsNullOrWhiteSpace($resolvedVersion)) {
            continue
        }

        if ($resolvedVersion -ne $LatestVersion) {
            $updates += [ordered]@{
                Client = [string]$property.Key
                CurrentVersion = $resolvedVersion
                LatestVersion = $LatestVersion
                InstallRoot = [string]$property.Value.installRoot
                Architecture = [string]$property.Value.architecture
            }
        }
    }

    return @($updates)
}

function Get-InstalledClientStatusMap {
    param([Parameter(Mandatory)] $State)

    $map = [ordered]@{}
    foreach ($client in Get-SupportedClients) {
        $isInstalled = $false
        if ($State.registrations.Contains($client.Id)) {
            $isInstalled = $true
        }
        else {
            switch ($client.Id) {
                'vscode' { $isInstalled = Test-JsonConfigRegistration -CollectionName 'servers' -ConfigPath (Resolve-VsCodeConfigPath) }
                'visual-studio' { $isInstalled = Test-JsonConfigRegistration -CollectionName 'servers' -ConfigPath (Resolve-VisualStudioConfigPath) }
                'claude-desktop' { $isInstalled = Test-JsonConfigRegistration -CollectionName 'mcpServers' -ConfigPath (Resolve-ClaudeDesktopConfigPath) }
            }
        }

        $map[$client.Id] = $isInstalled
    }

    return $map
}

function Test-JsonConfigRegistrationMatchesExecutable {
    param(
        [Parameter(Mandatory)] [string]$CollectionName,
        [Parameter(Mandatory)] [string]$ConfigPath,
        [Parameter(Mandatory)] [string]$InstalledExecutable
    )

    if (-not (Test-Path $ConfigPath)) {
        return $false
    }

    $root = Get-ExistingConfigMap -Path $ConfigPath
    $servers = Get-ConfigCollectionMap -Root $root -CollectionName $CollectionName
    if (-not $servers.Contains('wpf-devtools')) {
        return $false
    }

    return ([string]$servers['wpf-devtools'].command -eq $InstalledExecutable)
}

function Test-ArtifactRegistrationMatchesExecutable {
    param(
        [Parameter(Mandatory)] [string]$ArtifactPath,
        [Parameter(Mandatory)] [string]$InstalledExecutable
    )

    if (-not (Test-Path $ArtifactPath)) {
        return $false
    }

    $artifact = Get-Content -Path $ArtifactPath -Raw | ConvertFrom-Json
    $serverCollection = if ($null -ne $artifact.mcpServers) { $artifact.mcpServers } else { $artifact.servers }
    if ($null -eq $serverCollection -or $null -eq $serverCollection.'wpf-devtools') {
        return $false
    }

    return ([string]$serverCollection.'wpf-devtools'.command -eq $InstalledExecutable)
}

function Invoke-VerificationCommand {
    param(
        [Parameter(Mandatory)] [string]$Command,
        [Parameter(Mandatory)] [string[]]$Arguments,
        [Parameter(Mandatory)] [string]$ExpectedToken,
        [Parameter(Mandatory)] [bool]$ExpectPresent
    )

    $resolvedCommands = @(Get-Command $Command -All -CommandType Application,ExternalScript -ErrorAction SilentlyContinue)
    if ($resolvedCommands.Count -eq 0) {
        return [ordered]@{
            Succeeded = $false
            Output = "$Command is not installed."
            ExitCode = -1
        }
    }

    $selectedCommandPath = $null
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
            [string]$resolvedCommand.Name
        }

        if (-not [string]::IsNullOrWhiteSpace($candidatePath)) {
            $selectedCommandPath = $candidatePath
            break
        }
    }

    if ([string]::IsNullOrWhiteSpace($selectedCommandPath)) {
        return [ordered]@{
            Succeeded = $false
            Output = "$Command is not installed."
            ExitCode = -1
        }
    }

    $timeoutSeconds = Get-InstallerVerificationTimeoutSeconds
    $quotedArguments = @($Arguments | ForEach-Object {
            if ([string]::IsNullOrWhiteSpace([string]$_)) {
                '""'
            }
            elseif ([string]$_ -match '[\s"]') {
                '"' + ([string]$_).Replace('"', '\"') + '"'
            }
            else {
                [string]$_
            }
        })

    $startInfo = New-Object System.Diagnostics.ProcessStartInfo
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true

    $filePath = $selectedCommandPath
    $argumentText = $quotedArguments -join ' '
    $selectedExtension = [System.IO.Path]::GetExtension($selectedCommandPath).ToLowerInvariant()
    if (@('.cmd', '.bat') -contains $selectedExtension) {
        $filePath = if (-not [string]::IsNullOrWhiteSpace($env:ComSpec)) { $env:ComSpec } else { 'cmd.exe' }
        $argumentText = '/c "' + $selectedCommandPath + '"'
        if (-not [string]::IsNullOrWhiteSpace($argumentText) -and $quotedArguments.Count -gt 0) {
            $argumentText += ' ' + ($quotedArguments -join ' ')
        }
    }
    elseif ($selectedExtension -eq '.ps1') {
        $filePath = (Get-Process -Id $PID).Path
        $argumentText = '-NoProfile -ExecutionPolicy Bypass -File "' + $selectedCommandPath + '"'
        if ($quotedArguments.Count -gt 0) {
            $argumentText += ' ' + ($quotedArguments -join ' ')
        }
    }

    $process = $null
    $exitCode = -3
    try {
        $startInfo.FileName = $filePath
        $startInfo.Arguments = $argumentText
        $process = New-Object System.Diagnostics.Process
        $process.StartInfo = $startInfo
        $null = $process.Start()
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($timeoutSeconds * 1000)) {
            try {
                $process.Kill($true)
            }
            catch {
                try {
                    & taskkill.exe /PID $process.Id /T /F *> $null
                }
                catch {
                    try {
                        $process.Kill()
                    }
                    catch {
                    }
                }
            }

            $timeoutDrainMs = 250
            try {
                $null = $process.WaitForExit($timeoutDrainMs)
            }
            catch {
            }

            $timeoutOutput = @()
            if ($stdoutTask.IsCompleted) {
                $timeoutOutput += $stdoutTask.GetAwaiter().GetResult()
            }
            if ($stderrTask.IsCompleted) {
                $timeoutOutput += $stderrTask.GetAwaiter().GetResult()
            }
            $timeoutOutput = ($timeoutOutput -join [Environment]::NewLine).Trim()

            return [ordered]@{
                Succeeded = $false
                Output = ("$Command timed out after $timeoutSeconds second(s). " + $timeoutOutput).Trim()
                ExitCode = -2
            }
        }

        $exitCode = $process.ExitCode
    }
    catch {
        return [ordered]@{
            Succeeded = $false
            Output = $_.Exception.Message
            ExitCode = -3
        }
    }
    finally {
        if ($null -ne $process) {
            $process.Dispose()
        }
    }

    $output = @(
        $stdoutTask.GetAwaiter().GetResult()
        $stderrTask.GetAwaiter().GetResult()
    ) -join [Environment]::NewLine
    $output = $output.Trim()
    if ($exitCode -ne 0) {
        return [ordered]@{
            Succeeded = $false
            Output = $output
            ExitCode = $exitCode
        }
    }

    $containsToken = if ([string]::IsNullOrWhiteSpace($output)) { $ExpectPresent } else { $output.Contains($ExpectedToken) }
    return [ordered]@{
        Succeeded = ($containsToken -eq $ExpectPresent)
        Output = $output
        ExitCode = $exitCode
    }
}

function Read-ValidatedChoice {
    param(
        [Parameter(Mandatory)] [string]$Prompt,
        [Parameter(Mandatory)] [string]$DefaultValue,
        [Parameter(Mandatory)] [string[]]$AllowedValues
    )

    while ($true) {
        $response = Read-InstallerInput -Prompt $Prompt -DefaultValue $DefaultValue
        if ([string]::IsNullOrWhiteSpace($response)) {
            return $DefaultValue
        }

        $normalized = $response.Trim().ToLowerInvariant()
        if ($AllowedValues -contains $normalized) {
            return $normalized
        }

        Write-InstallerMessage ("Allowed values: " + ($AllowedValues -join ', '))
    }
}

function Get-CliSelection {
    $defaultAction = $Action
    $defaultArchitecture = if ([string]::IsNullOrWhiteSpace($Architecture)) { Get-SystemDefaultArchitecture } else { $Architecture }
    $defaultClient = if ([string]::IsNullOrWhiteSpace($Client)) { Get-DefaultClient } else { $Client }
    $defaultInstallRoot = Resolve-PreferredInstallRoot

    if ($NonInteractive -or $OutputJson) {
        return [ordered]@{
            Action = $defaultAction
            Architecture = $defaultArchitecture
            Client = $defaultClient
            InstallRoot = $defaultInstallRoot
        }
    }

    $resolvedAction = Read-ValidatedChoice -Prompt 'Action (install/uninstall)' -DefaultValue $defaultAction -AllowedValues @('install', 'uninstall', 'full-uninstall')
    $resolvedArchitecture = Read-ValidatedChoice -Prompt 'Architecture (x64/x86/arm64)' -DefaultValue $defaultArchitecture -AllowedValues @('x64', 'x86', 'arm64')
    $resolvedClient = Read-ValidatedChoice -Prompt 'Client (claude-code/codex/vscode/visual-studio/claude-desktop/other)' -DefaultValue $defaultClient -AllowedValues @('claude-code', 'codex', 'vscode', 'visual-studio', 'claude-desktop', 'other')
    $installRootPrompt = Read-InstallerInput -Prompt 'Install root' -DefaultValue $defaultInstallRoot
    if ([string]::IsNullOrWhiteSpace($installRootPrompt)) {
        $installRootPrompt = $defaultInstallRoot
    }

    return [ordered]@{
        Action = $resolvedAction
        Architecture = $resolvedArchitecture
        Client = $resolvedClient
        InstallRoot = $installRootPrompt.Trim()
    }
}

function Get-InstalledClientLabel {
    param(
        [Parameter(Mandatory)] [string]$ClientId,
        [Parameter(Mandatory)] $State
    )

    $clientLabel = Resolve-ClientLabel -ClientId $ClientId
    if (-not $State.registrations.Contains($ClientId)) {
        return $clientLabel
    }

    $resolvedVersion = [string]$State.registrations[$ClientId].resolvedVersion
    if ([string]::IsNullOrWhiteSpace($resolvedVersion)) {
        return "$clientLabel (Installed)"
    }

    return "$clientLabel (Installed v$resolvedVersion)"
}

function Invoke-InstallVerification {
    param(
        [Parameter(Mandatory)] [string]$SelectedClient,
        [Parameter(Mandatory)] [string]$ResolvedVersion,
        [Parameter(Mandatory)] [string]$InstalledExecutable,
        [Parameter(Mandatory)] $Registration
    )

    $verificationSucceeded = $false
    $verificationMessage = $null
    switch ($SelectedClient) {
        'claude-code' {
            $verification = Invoke-VerificationCommand -Command 'claude' -Arguments @('mcp', 'list') -ExpectedToken 'wpf-devtools' -ExpectPresent $true
            $verificationSucceeded = ($verification.Succeeded -and (Test-Path $InstalledExecutable))
            $verificationMessage = if ($verificationSucceeded) { 'Verified with claude mcp list.' } else { "Claude verification failed: $($verification.Output)" }
        }
        'codex' {
            $verification = Invoke-VerificationCommand -Command 'codex' -Arguments @('mcp', 'list') -ExpectedToken 'wpf-devtools' -ExpectPresent $true
            $verificationSucceeded = ($verification.Succeeded -and (Test-Path $InstalledExecutable))
            $verificationMessage = if ($verificationSucceeded) { 'Verified with codex mcp list.' } else { "Codex verification failed: $($verification.Output)" }
        }
        'vscode' {
            $verificationSucceeded = Test-JsonConfigRegistrationMatchesExecutable -CollectionName 'servers' -ConfigPath (Resolve-VsCodeConfigPath) -InstalledExecutable $InstalledExecutable
            $verificationMessage = if ($verificationSucceeded) { 'Verified VS Code configuration.' } else { 'VS Code verification failed.' }
        }
        'visual-studio' {
            $verificationSucceeded = Test-JsonConfigRegistrationMatchesExecutable -CollectionName 'servers' -ConfigPath (Resolve-VisualStudioConfigPath) -InstalledExecutable $InstalledExecutable
            $verificationMessage = if ($verificationSucceeded) { 'Verified Visual Studio configuration.' } else { 'Visual Studio verification failed.' }
        }
        'claude-desktop' {
            $verificationSucceeded = Test-JsonConfigRegistrationMatchesExecutable -CollectionName 'mcpServers' -ConfigPath (Resolve-ClaudeDesktopConfigPath) -InstalledExecutable $InstalledExecutable
            $verificationMessage = if ($verificationSucceeded) { 'Verified Claude Desktop configuration.' } else { 'Claude Desktop verification failed.' }
        }
        'other' {
            $verificationSucceeded = Test-ArtifactRegistrationMatchesExecutable -ArtifactPath ([string]$Registration.target) -InstalledExecutable $InstalledExecutable
            $verificationMessage = if ($verificationSucceeded) { 'Verified exported registration artifact.' } else { 'Artifact verification failed.' }
        }
    }

    return [ordered]@{
        Succeeded = $verificationSucceeded
        InstalledVersion = $ResolvedVersion
        VerificationMessage = $verificationMessage
        LastVerifiedUtc = [DateTime]::UtcNow.ToString('o')
    }
}

function Invoke-UninstallVerification {
    param([Parameter(Mandatory)] [string]$SelectedClient)

    $verificationSucceeded = switch ($SelectedClient) {
        'claude-code' {
            (Invoke-VerificationCommand -Command 'claude' -Arguments @('mcp', 'list') -ExpectedToken 'wpf-devtools' -ExpectPresent $false).Succeeded
            break
        }
        'codex' {
            (Invoke-VerificationCommand -Command 'codex' -Arguments @('mcp', 'list') -ExpectedToken 'wpf-devtools' -ExpectPresent $false).Succeeded
            break
        }
        'vscode' { -not (Test-JsonConfigRegistration -CollectionName 'servers' -ConfigPath (Resolve-VsCodeConfigPath)); break }
        'visual-studio' { -not (Test-JsonConfigRegistration -CollectionName 'servers' -ConfigPath (Resolve-VisualStudioConfigPath)); break }
        'claude-desktop' { -not (Test-JsonConfigRegistration -CollectionName 'mcpServers' -ConfigPath (Resolve-ClaudeDesktopConfigPath)); break }
        default { $true }
    }

    return [ordered]@{
        Succeeded = [bool]$verificationSucceeded
        VerificationMessage = "Verified uninstall state for $SelectedClient."
    }
}

function Get-LatestInstallerVersion {
    param([switch]$UseCacheOnly)

    if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_TEST_LATEST_VERSION)) {
        return $env:WPFDEVTOOLS_INSTALLER_TEST_LATEST_VERSION
    }

    $cachedVersion = Get-CachedLatestInstallerVersion
    if ($UseCacheOnly) {
        return $cachedVersion
    }

    try {
        $latestVersion = [string](Invoke-RestMethod -Uri (Get-GitHubReleaseApiUri -ResolvedVersion 'latest') -Headers @{ 'User-Agent' = 'wpf-devtools-online-installer' } -TimeoutSec 10).tag_name.TrimStart('v')
        if (-not [string]::IsNullOrWhiteSpace($latestVersion)) {
            Save-LatestInstallerVersionCache -VersionValue $latestVersion
            return $latestVersion
        }
    }
    catch {
    }

    return $cachedVersion
}

function Test-TuiSupport {
    if ($NonInteractive -or $OutputJson) {
        return $false
    }

    $script:LastTuiBootstrapMessage = $null
    $script:LastTuiBootstrapFailureReason = $null
    try {
        $null = Ensure-TuiHelpersAvailable
    }
    catch {
        $script:LastTuiBootstrapFailureReason = $_.Exception.Message
        Write-InstallerMessage 'Installer UI bootstrap failed. Falling back to plain CLI.'
        return $false
    }

    if ([string]::IsNullOrWhiteSpace($script:TuiHelperResolvedRoot)) {
        return $false
    }

    return (Invoke-WithTuiHelpers -ScriptBlock { Test-TuiSupportCore })
}

function Render-TuiScreen {
    param([Parameter(Mandatory)] $State)

    Invoke-WithTuiHelpers -ScriptBlock { Render-TuiScreenCore -State $State } | Out-Null
}

function Read-TuiKey {
    return (Invoke-WithTuiHelpers -ScriptBlock { Read-TuiKeyCore })
}

function Update-TuiSelection {
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)] $KeyInfo
    )

    return (Invoke-WithTuiHelpers -ScriptBlock { Update-TuiSelectionCore -State $State -KeyInfo $KeyInfo })
}

function Invoke-TuiInstallOperation {
    param([Parameter(Mandatory)] $State)

    return (Invoke-WithTuiHelpers -ScriptBlock { Invoke-TuiInstallOperationCore -State $State })
}

function Invoke-TuiUninstallOperation {
    param([Parameter(Mandatory)] $State)

    return (Invoke-WithTuiHelpers -ScriptBlock { Invoke-TuiUninstallOperationCore -State $State })
}

function Invoke-TuiUpdateAllOperation {
    param([Parameter(Mandatory)] $State)

    return (Invoke-WithTuiHelpers -ScriptBlock { Invoke-TuiUpdateAllOperationCore -State $State })
}

function Initialize-TuiStartupState {
    param([Parameter(Mandatory)] $State)

    return (Invoke-WithTuiHelpers -ScriptBlock { Initialize-TuiStartupStateCore -State $State })
}

function Assert-InstallerHelperRuntimeAvailable {
    param([Parameter(Mandatory)] [string]$ResolvedAction)

    if ($ResolvedAction -eq 'install') {
        return
    }

    if (-not [string]::IsNullOrWhiteSpace($script:LastTuiBootstrapFailureReason)) {
        throw "The installer runtime required for $ResolvedAction is unavailable. Re-run the installer with network access or use a full offline package. $script:LastTuiBootstrapFailureReason"
    }

    try {
        $helperRoot = Ensure-TuiHelpersAvailable
    }
    catch {
        throw "The installer runtime required for $ResolvedAction is unavailable. Re-run the installer with network access or use a full offline package. $($_.Exception.Message)"
    }

    if ([string]::IsNullOrWhiteSpace($helperRoot)) {
        throw "The installer runtime required for $ResolvedAction is unavailable. Re-run the installer with network access or use a full offline package."
    }
}

function Invoke-InstallerAction {
    param(
        [Parameter(Mandatory)] [ValidateSet('install', 'uninstall', 'full-uninstall')] [string]$ResolvedAction,
        [Parameter(Mandatory)] [string]$ResolvedArchitecture,
        [Parameter(Mandatory)] [string]$ResolvedClient,
        [Parameter(Mandatory)] [string]$ResolvedInstallRoot,
        [Parameter(Mandatory)] [string]$RequestedVersion,
        [switch]$UseLatestRelease
    )

    Assert-InstallerHelperRuntimeAvailable -ResolvedAction $ResolvedAction

    if ($ResolvedAction -eq 'full-uninstall') {
        $state = Get-InstallerState
        $result = Invoke-WithTuiHelpers -ScriptBlock { Invoke-InstallerFullUninstallCore -State $state }
        return [ordered]@{
            action = 'full-uninstall'
            mode = 'offline'
            downloadSource = 'none'
            version = $RequestedVersion
            resolvedVersion = $null
            architecture = 'all'
            client = 'all'
            packageAssetName = $null
            downloadUri = $null
            installRoot = $null
            installedExecutable = $null
            selectedClients = @()
            statePath = [string]$result.statePath
            removedInstallation = [bool]$result.removedInstallation
            removedInstallations = @($result.removedInstallations)
            registrations = @($result.registrations)
            verificationMessage = [string]$result.verificationMessage
        }
    }

    if ($ResolvedAction -eq 'uninstall') {
        $state = Get-InstallerState
        $detectedRegistrations = Invoke-WithTuiHelpers -ScriptBlock { Get-DetectedInstallerRegistrationMap -State $state }
        $detectedRegistration = if ($detectedRegistrations.Contains($ResolvedClient)) { $detectedRegistrations[$ResolvedClient] } else { $null }
        $registrationRecord = if ($state.registrations.Contains($ResolvedClient)) { $state.registrations[$ResolvedClient] } else { $detectedRegistration }
        if ($null -ne $registrationRecord) {
            if ([string]::IsNullOrWhiteSpace($ResolvedArchitecture)) {
                $ResolvedArchitecture = if ($registrationRecord.Contains('architecture')) { [string]$registrationRecord.architecture } else { [string]$registrationRecord.Architecture }
            }
            if (-not $script:InstallRootWasSpecified -and [string]::IsNullOrWhiteSpace($ResolvedInstallRoot)) {
                $ResolvedInstallRoot = if ($registrationRecord.Contains('installRoot')) { [string]$registrationRecord.installRoot } else { [string]$registrationRecord.InstallRoot }
            }
        }

        if ([string]::IsNullOrWhiteSpace($ResolvedArchitecture)) {
            $ResolvedArchitecture = Get-SystemDefaultArchitecture
        }
        if ([string]::IsNullOrWhiteSpace($ResolvedInstallRoot)) {
            $ResolvedInstallRoot = Resolve-PreferredInstallRoot
        }

        $ResolvedInstallRoot = Resolve-AbsoluteDirectory -Path $ResolvedInstallRoot
        $installedExecutable = if ($null -ne $detectedRegistration) { [string]$detectedRegistration.InstalledExecutable } else { $null }
        $registrations = @(Invoke-ClientUnregistration -SelectedClient $ResolvedClient)
        $verification = Invoke-UninstallVerification -SelectedClient $ResolvedClient
        if (-not $verification.Succeeded) {
            throw $verification.VerificationMessage
        }

        if ($state.registrations.Contains($ResolvedClient)) {
            [void]$state.registrations.Remove($ResolvedClient)
        }

        $statePath = Save-InstallerState -State $state
        return [ordered]@{
            action = 'uninstall'
            mode = 'offline'
            downloadSource = 'none'
            version = $RequestedVersion
            resolvedVersion = $null
            architecture = $ResolvedArchitecture
            client = $ResolvedClient
            packageAssetName = $null
            downloadUri = $null
            installRoot = $ResolvedInstallRoot
            installedExecutable = $installedExecutable
            selectedClients = @($ResolvedClient)
            statePath = $statePath
            removedInstallation = $false
            registrations = @($registrations)
            verificationMessage = [string]$verification.VerificationMessage
        }
    }

    $mode = if ($UseLatestRelease) { 'online' } else { Resolve-InstallerMode }
    $session = Resolve-PackageSession -Mode $mode -ResolvedVersion $RequestedVersion -ResolvedArchitecture $ResolvedArchitecture
    try {
        $packageManifest = Get-Content -Path (Resolve-PackageManifestPath -PackageDirectory $session.PackageDirectory) -Raw | ConvertFrom-Json
        $resolvedVersion = if ([string]::IsNullOrWhiteSpace([string]$packageManifest.version)) { [string]$session.ResolvedVersion } else { [string]$packageManifest.version }
        $installResult = Install-PackagePayload -PackageDirectory $session.PackageDirectory -PackageManifest $packageManifest -ResolvedArchitecture $ResolvedArchitecture -ResolvedInstallRoot $ResolvedInstallRoot -ResolvedVersion $resolvedVersion
        $registrations = @(Invoke-ClientRegistration -SelectedClient $ResolvedClient -InstalledExecutable $installResult.installedExecutable -InstallBase $installResult.installBase)
        $verification = Invoke-InstallVerification -SelectedClient $ResolvedClient -ResolvedVersion $resolvedVersion -InstalledExecutable $installResult.installedExecutable -Registration $registrations[0]
        if (-not $verification.Succeeded) {
            throw $verification.VerificationMessage
        }

        $state = Get-InstallerState
        Update-InstallerStateAfterInstall -State $state -ResolvedInstallRoot $installResult.installRoot -ResolvedArchitecture $ResolvedArchitecture -ResolvedVersion ([string]$verification.InstalledVersion) -InstalledExecutable $installResult.installedExecutable -SelectedClient $ResolvedClient -Registration $registrations[0] -LastVerifiedUtc ([string]$verification.LastVerifiedUtc)
        $statePath = Save-InstallerState -State $state
        return [ordered]@{
            action = 'install'
            mode = $mode
            downloadSource = [string]$session.DownloadSource
            version = $RequestedVersion
            resolvedVersion = [string]$verification.InstalledVersion
            architecture = $ResolvedArchitecture
            client = $ResolvedClient
            packageAssetName = [string]$session.PackageAssetName
            downloadUri = [string]$session.DownloadUri
            installRoot = $installResult.installRoot
            installedExecutable = $installResult.installedExecutable
            selectedClients = @($ResolvedClient)
            statePath = $statePath
            reusedExistingBinary = [bool]$installResult.reusedExistingBinary
            registrations = @($registrations)
            verificationMessage = [string]$verification.VerificationMessage
            lastVerifiedUtc = [string]$verification.LastVerifiedUtc
        }
    }
    finally {
        if ($session.CleanupSession) {
            Remove-PathIfExists -Path $session.SessionRoot
        }
    }
}

function Start-TuiInstaller {
    param(
        [Parameter(Mandatory)] [string]$DefaultAction,
        [Parameter(Mandatory)] [string]$DefaultArchitecture,
        [Parameter(Mandatory)] [string]$DefaultClient,
        [Parameter(Mandatory)] [string]$DefaultInstallRoot,
        [Parameter(Mandatory)] $InstallerState,
        [string]$VersionHint,
        [string]$LatestVersion
    )

    return (Invoke-WithTuiHelpers -ScriptBlock { Start-TuiInstallerCore `
            -DefaultAction $DefaultAction `
            -DefaultArchitecture $DefaultArchitecture `
            -DefaultClient $DefaultClient `
            -DefaultInstallRoot $DefaultInstallRoot `
            -InstallerState $InstallerState `
            -VersionHint $VersionHint `
            -LatestVersion $LatestVersion })
}

function Resolve-Selection {
    $installerState = Get-InstallerState
    $defaultArchitecture = if ([string]::IsNullOrWhiteSpace($Architecture)) { Get-SystemDefaultArchitecture } else { $Architecture }
    $defaultClient = if ([string]::IsNullOrWhiteSpace($Client)) { Get-DefaultClient } else { $Client }
    $defaultInstallRoot = Resolve-PreferredInstallRoot
    $mode = Resolve-InstallerMode
    $versionHint = Get-OfflineVersionHint -Mode $mode
    $latestVersion = Get-LatestInstallerVersion -UseCacheOnly

    if (Test-TuiSupport) {
        $tuiResult = Start-TuiInstaller `
            -DefaultAction $Action `
            -DefaultArchitecture $defaultArchitecture `
            -DefaultClient $defaultClient `
            -DefaultInstallRoot $defaultInstallRoot `
            -InstallerState $installerState `
            -VersionHint $versionHint `
            -LatestVersion $latestVersion

        return [ordered]@{
            Cancelled = [bool]$tuiResult.Cancelled
            Selection = $tuiResult.Selection
            VersionHint = $versionHint
            HandledInWindow = [bool]$tuiResult.HandledInWindow
        }
    }

    return [ordered]@{
        Cancelled = $false
        Selection = (Get-CliSelection)
        VersionHint = $versionHint
        HandledInWindow = $false
    }
}

function Resolve-InstallBasePath {
    param(
        [Parameter(Mandatory)] [string]$ResolvedInstallRoot,
        [Parameter(Mandatory)] [string]$ResolvedArchitecture
    )

    return (Join-Path (Resolve-AbsoluteDirectory -Path $ResolvedInstallRoot) $ResolvedArchitecture)
}

function Get-StandardInstallJson {
    param(
        [Parameter(Mandatory)] [string]$ResolvedInstallRoot,
        [Parameter(Mandatory)] [string]$ResolvedArchitecture
    )

    $installBase = Resolve-InstallBasePath -ResolvedInstallRoot $ResolvedInstallRoot -ResolvedArchitecture $ResolvedArchitecture
    $installedExecutable = Join-Path $installBase "current\\bin\\wpf-devtools-$ResolvedArchitecture.exe"
    $serverNode = [ordered]@{
        type = 'stdio'
        command = $installedExecutable
        args = @()
    }

    return ([ordered]@{
            mcpServers = [ordered]@{
                'wpf-devtools' = $serverNode
            }
        } | ConvertTo-Json -Depth 5)
}

function Get-RegistrationsForArchitecture {
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)] [string]$ResolvedArchitecture,
        [Parameter(Mandatory)] [string]$ResolvedInstallRoot,
        [string]$ExcludeClient
    )

    $remaining = @()
    foreach ($property in $State.registrations.GetEnumerator()) {
        if ($property.Key -eq $ExcludeClient) {
            continue
        }

        if (($property.Value.architecture -eq $ResolvedArchitecture) -and ($property.Value.installRoot -eq $ResolvedInstallRoot)) {
            $remaining += $property.Key
        }
    }

    return @($remaining)
}

$selectionContext = Resolve-Selection
if ($selectionContext.Cancelled) {
    return
}
if ($selectionContext.HandledInWindow) {
    return
}

$interactiveSelection = $selectionContext.Selection
$resolvedAction = [string]$interactiveSelection.Action
$resolvedArchitecture = [string]$interactiveSelection.Architecture
$resolvedClient = [string]$interactiveSelection.Client
$resolvedInstallRoot = [string]$interactiveSelection.InstallRoot
$versionHint = [string]$selectionContext.VersionHint

$result = Invoke-InstallerAction -ResolvedAction $resolvedAction -ResolvedArchitecture $resolvedArchitecture -ResolvedClient $resolvedClient -ResolvedInstallRoot $resolvedInstallRoot -RequestedVersion $Version

if ($OutputJson) {
    $result | ConvertTo-Json -Depth 10
}
else {
    Write-InstallerMessage "$($result.action) finished for $($result.client)."
    if (-not [string]::IsNullOrWhiteSpace($result.installedExecutable)) {
        Write-InstallerMessage "Executable: $($result.installedExecutable)"
    }
    if (-not [string]::IsNullOrWhiteSpace($result.verificationMessage)) {
        Write-InstallerMessage $result.verificationMessage
    }
    if (-not [string]::IsNullOrWhiteSpace($versionHint)) {
        Write-InstallerMessage $versionHint
    }
}
