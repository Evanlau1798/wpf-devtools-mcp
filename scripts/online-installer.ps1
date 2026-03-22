param(
    [ValidateSet('install', 'uninstall')]
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
    $binManifestPath = Join-Path $PSScriptRoot 'manifest.json'
    if (Test-Path $binManifestPath) {
        return (Split-Path -Parent $PSScriptRoot)
    }

    $packageManifestPath = Join-Path $PSScriptRoot 'bin\manifest.json'
    if (Test-Path $packageManifestPath) {
        return $PSScriptRoot
    }

    return $null
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

        $latest = Invoke-RestMethod -Uri (Get-GitHubReleaseApiUri -ResolvedVersion 'latest') -Headers @{ 'User-Agent' = 'wpf-devtools-online-installer' } -TimeoutSec 10
        $latestVersion = ([string]$latest.tag_name).TrimStart('v')
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

    $resolvedCommand = Get-Command $Command -ErrorAction SilentlyContinue
    if ($null -eq $resolvedCommand) {
        return [ordered]@{
            Succeeded = $false
            Output = "$Command is not installed."
            ExitCode = -1
        }
    }

    $output = (& $Command @Arguments 2>&1 | Out-String).Trim()
    $exitCode = $LASTEXITCODE
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

    $resolvedAction = Read-ValidatedChoice -Prompt 'Action (install/uninstall)' -DefaultValue $defaultAction -AllowedValues @('install', 'uninstall')
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

function Ensure-DwmMicaHelper {
    try {
        [DwmMicaHelper] | Out-Null
    }
    catch {
        try {
            Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class DwmMicaHelper {
    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, uint attr, ref int value, int size);

    public static void Apply(IntPtr hwnd) {
        int darkMode = 1;
        DwmSetWindowAttribute(hwnd, 20, ref darkMode, 4);
        int backdropType = 2;
        DwmSetWindowAttribute(hwnd, 38, ref backdropType, 4);
        int rounded = 2;
        DwmSetWindowAttribute(hwnd, 33, ref rounded, 4);
    }
}
"@ -ErrorAction SilentlyContinue
        }
        catch {
        }
    }
}

function Switch-Page {
    param(
        [System.Windows.UIElement]$From,
        [System.Windows.UIElement]$To,
        [double]$Direction
    )

    if ($null -eq $To) {
        return
    }

    try {
        $ms = 280
        $duration = [System.Windows.Duration]::new([TimeSpan]::FromMilliseconds($ms))
        $ease = [System.Windows.Media.Animation.CubicEase]::new()
        $ease.EasingMode = 'EaseOut'

        if ($null -ne $From -and $From.RenderTransform -isnot [System.Windows.Media.TranslateTransform]) {
            $From.RenderTransform = [System.Windows.Media.TranslateTransform]::new()
        }
        if ($To.RenderTransform -isnot [System.Windows.Media.TranslateTransform]) {
            $To.RenderTransform = [System.Windows.Media.TranslateTransform]::new()
        }

        $To.RenderTransform.X = 400 * $Direction
        $To.Opacity = 0
        $To.Visibility = 'Visible'

        if ($null -ne $From) {
            $slideOut = [System.Windows.Media.Animation.DoubleAnimation]::new()
            $slideOut.To = -200 * $Direction
            $slideOut.Duration = $duration
            $slideOut.EasingFunction = $ease

            $fadeOut = [System.Windows.Media.Animation.DoubleAnimation]::new()
            $fadeOut.To = 0.0
            $fadeOut.Duration = $duration
            $fadeOut.EasingFunction = $ease

            $From.RenderTransform.BeginAnimation([System.Windows.Media.TranslateTransform]::XProperty, $slideOut)
            $From.BeginAnimation([System.Windows.UIElement]::OpacityProperty, $fadeOut)
        }

        $slideIn = [System.Windows.Media.Animation.DoubleAnimation]::new()
        $slideIn.To = 0.0
        $slideIn.Duration = $duration
        $slideIn.EasingFunction = $ease

        $fadeIn = [System.Windows.Media.Animation.DoubleAnimation]::new()
        $fadeIn.To = 1.0
        $fadeIn.Duration = $duration
        $fadeIn.EasingFunction = $ease

        $To.RenderTransform.BeginAnimation([System.Windows.Media.TranslateTransform]::XProperty, $slideIn)
        $To.BeginAnimation([System.Windows.UIElement]::OpacityProperty, $fadeIn)

        if ($null -ne $From) {
            $capturedFrom = $From
            $timer = [System.Windows.Threading.DispatcherTimer]::new()
            $timer.Interval = [TimeSpan]::FromMilliseconds($ms + 50)
            $capturedTimer = $timer
            $timer.Add_Tick({
                    try {
                        $capturedTimer.Stop()
                        $capturedFrom.Visibility = 'Collapsed'
                        $capturedFrom.BeginAnimation([System.Windows.UIElement]::OpacityProperty, $null)
                        $capturedFrom.RenderTransform.BeginAnimation([System.Windows.Media.TranslateTransform]::XProperty, $null)
                        $capturedFrom.Opacity = 1
                        $capturedFrom.RenderTransform.X = 0
                    }
                    catch {
                    }
                }.GetNewClosure())
            $timer.Start()
        }
    }
    catch {
        if ($null -ne $From) {
            $From.Visibility = 'Collapsed'
            $From.Opacity = 1
        }
        $To.Visibility = 'Visible'
        $To.Opacity = 1
    }
}

function Request-WindowClose {
    param([Parameter(Mandatory)] $Window)

    try {
        $targetWindow = $Window
        $closeAction = [Action]{
            try {
                if ($null -ne $targetWindow) {
                    try {
                        $targetWindow.DialogResult = $false
                    }
                    catch {
                    }
                    $targetWindow.Close()
                }
            }
            catch {
            }
        }.GetNewClosure()
        $null = $Window.Dispatcher.BeginInvoke($closeAction, [System.Windows.Threading.DispatcherPriority]::Normal)
    }
    catch {
        try {
            $Window.Close()
        }
        catch {
        }
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

function Refresh-InstalledLabels {
    param(
        [Parameter(Mandatory)] $Window,
        [Parameter(Mandatory)] $State
    )

    $buttonMaps = @(
        @{ Client = 'claude-code'; Install = 'InstallClaudeCodeButton'; Uninstall = 'UninstallClaudeCodeButton' }
        @{ Client = 'codex'; Install = 'InstallCodexButton'; Uninstall = 'UninstallCodexButton' }
        @{ Client = 'vscode'; Install = 'InstallVsCodeButton'; Uninstall = 'UninstallVsCodeButton' }
        @{ Client = 'visual-studio'; Install = 'InstallVisualStudioButton'; Uninstall = 'UninstallVisualStudioButton' }
        @{ Client = 'claude-desktop'; Install = 'InstallClaudeDesktopButton'; Uninstall = 'UninstallClaudeDesktopButton' }
        @{ Client = 'other'; Install = 'InstallOtherButton'; Uninstall = 'UninstallOtherButton' }
    )

    foreach ($map in $buttonMaps) {
        $label = Get-InstalledClientLabel -ClientId $map.Client -State $State
        foreach ($buttonName in @($map.Install, $map.Uninstall)) {
            $button = $Window.FindName($buttonName)
            if ($button -and $button.Content -is [System.Windows.Controls.Grid]) {
                $button.Content.Children[0].Text = $label
            }
        }
    }
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

function Refresh-UpdateBanner {
    param(
        [Parameter(Mandatory)] $Window,
        [string]$LatestVersion
    )

    $updateBanner = $Window.FindName('UpdateBanner')
    $updateBannerText = $Window.FindName('UpdateBannerText')
    $updateAllButton = $Window.FindName('UpdateAllButton')
    if ([string]::IsNullOrWhiteSpace($LatestVersion)) {
        $updateBanner.Visibility = 'Collapsed'
        $updateBannerText.Text = ''
        $updateAllButton.IsEnabled = $false
        return
    }
    $availableUpdates = @(Get-AvailableInstallerUpdates -State (Get-InstallerState) -LatestVersion $LatestVersion)

    if ($availableUpdates.Count -gt 0) {
        $updateBanner.Visibility = 'Visible'
        $updateBannerText.Text = "$($availableUpdates.Count) installed target(s) can be updated to v$LatestVersion."
        $updateAllButton.IsEnabled = $true
        return
    }

    $updateBanner.Visibility = 'Collapsed'
    $updateBannerText.Text = ''
    $updateAllButton.IsEnabled = $false
}

function Set-UiBusyState {
    param(
        [Parameter(Mandatory)] $Window,
        [Parameter(Mandatory)] [bool]$IsBusy
    )

    foreach ($name in @(
            'ArchitectureSelector',
            'BrowseInstallRootButton',
            'GenerateStandardInstallJsonButton',
            'GoInstallButton',
            'GoUninstallButton',
            'BackFromInstallButton',
            'BackFromUninstallButton',
            'UpdateAllButton',
            'InstallClaudeCodeButton',
            'InstallCodexButton',
            'InstallVsCodeButton',
            'InstallVisualStudioButton',
            'InstallClaudeDesktopButton',
            'InstallOtherButton',
            'UninstallClaudeCodeButton',
            'UninstallCodexButton',
            'UninstallVsCodeButton',
            'UninstallVisualStudioButton',
            'UninstallClaudeDesktopButton',
            'UninstallOtherButton')) {
        $element = $Window.FindName($name)
        if ($null -ne $element) {
            $element.IsEnabled = -not $IsBusy
        }
    }

    $installRootTextBox = $Window.FindName('InstallRootTextBox')
    if ($null -ne $installRootTextBox) {
        $installRootTextBox.IsReadOnly = $IsBusy
    }

    try {
        $Window.Cursor = if ($IsBusy) { [System.Windows.Input.Cursors]::Wait } else { [System.Windows.Input.Cursors]::Arrow }
        $null = $Window.Dispatcher.Invoke([Action] {}, [System.Windows.Threading.DispatcherPriority]::Background)
    }
    catch {
    }
}

function Invoke-GuiInstallOperation {
    param(
        [Parameter(Mandatory)] $Window,
        [Parameter(Mandatory)] [string]$SelectedClient,
        [string]$LatestVersion
    )

    $txtInstMsg = $Window.FindName('TxtInstMsg')
    $selectedArchitecture = [string]$Window.FindName('ArchitectureSelector').SelectedItem
    if ([string]::IsNullOrWhiteSpace($selectedArchitecture)) {
        $selectedArchitecture = Get-SystemDefaultArchitecture
    }
    $selectedRoot = [string]$Window.FindName('InstallRootTextBox').Text
    if ([string]::IsNullOrWhiteSpace($selectedRoot)) {
        $selectedRoot = Resolve-PreferredInstallRoot
    }

    Set-UiBusyState -Window $Window -IsBusy $true
    $txtInstMsg.Text = "Installing $(Resolve-ClientLabel -ClientId $SelectedClient)..."

    try {
        $result = Invoke-InstallerAction -ResolvedAction 'install' -ResolvedArchitecture $selectedArchitecture -ResolvedClient $SelectedClient -ResolvedInstallRoot $selectedRoot.Trim() -RequestedVersion $Version
        $txtInstMsg.Text = "Installed $(Resolve-ClientLabel -ClientId $SelectedClient) v$($result.resolvedVersion). $($result.verificationMessage)"
        Update-AllStatus -Window $Window -InstalledStatus (Get-InstalledClientStatusMap -State (Get-InstallerState)) -LatestVersion $LatestVersion
        return [ordered]@{ Succeeded = $true; Result = $result }
    }
    catch {
        $txtInstMsg.Text = "Installation failed: $($_.Exception.Message)"
        return [ordered]@{ Succeeded = $false; Error = $_.Exception.Message }
    }
    finally {
        Set-UiBusyState -Window $Window -IsBusy $false
        Update-AllStatus -Window $Window -InstalledStatus (Get-InstalledClientStatusMap -State (Get-InstallerState)) -LatestVersion $LatestVersion
    }
}

function Invoke-GuiUninstallOperation {
    param(
        [Parameter(Mandatory)] $Window,
        [Parameter(Mandatory)] [string]$SelectedClient,
        [string]$LatestVersion
    )

    $txtUninstMsg = $Window.FindName('TxtUninstMsg')
    $state = Get-InstallerState
    $registration = if ($state.registrations.Contains($SelectedClient)) { $state.registrations[$SelectedClient] } else { $null }
    $selectedArchitecture = if ($null -ne $registration) { [string]$registration.architecture } else { [string]$Window.FindName('ArchitectureSelector').SelectedItem }
    $selectedRoot = if ($null -ne $registration) { [string]$registration.installRoot } else { [string]$Window.FindName('InstallRootTextBox').Text }

    Set-UiBusyState -Window $Window -IsBusy $true
    $txtUninstMsg.Text = "Uninstalling $(Resolve-ClientLabel -ClientId $SelectedClient)..."

    try {
        $result = Invoke-InstallerAction -ResolvedAction 'uninstall' -ResolvedArchitecture $selectedArchitecture -ResolvedClient $SelectedClient -ResolvedInstallRoot $selectedRoot -RequestedVersion $Version
        $txtUninstMsg.Text = "Removed $(Resolve-ClientLabel -ClientId $SelectedClient). $($result.verificationMessage)"
        Update-AllStatus -Window $Window -InstalledStatus (Get-InstalledClientStatusMap -State (Get-InstallerState)) -LatestVersion $LatestVersion
        return [ordered]@{ Succeeded = $true; Result = $result }
    }
    catch {
        $txtUninstMsg.Text = "Uninstall failed: $($_.Exception.Message)"
        return [ordered]@{ Succeeded = $false; Error = $_.Exception.Message }
    }
    finally {
        Set-UiBusyState -Window $Window -IsBusy $false
        Update-AllStatus -Window $Window -InstalledStatus (Get-InstalledClientStatusMap -State (Get-InstallerState)) -LatestVersion $LatestVersion
    }
}

function Invoke-UpdateAllOperation {
    param(
        [Parameter(Mandatory)] $Window,
        [Parameter(Mandatory)] [string]$LatestVersion
    )

    $txtInstMsg = $Window.FindName('TxtInstMsg')
    $updates = @(Get-AvailableInstallerUpdates -State (Get-InstallerState) -LatestVersion $LatestVersion)
    if ($updates.Count -eq 0) {
        $txtInstMsg.Text = 'All installed targets are already on the latest release.'
        return [ordered]@{ Succeeded = $true; UpdatedCount = 0 }
    }

    Set-UiBusyState -Window $Window -IsBusy $true
    $updated = 0
    try {
        foreach ($update in $updates) {
            $txtInstMsg.Text = "Updating $(Resolve-ClientLabel -ClientId ([string]$update.Client)) to v$LatestVersion..."
            $null = Invoke-InstallerAction -ResolvedAction 'install' -ResolvedArchitecture ([string]$update.Architecture) -ResolvedClient ([string]$update.Client) -ResolvedInstallRoot ([string]$update.InstallRoot) -RequestedVersion 'latest' -UseLatestRelease
            $updated++
        }
        $txtInstMsg.Text = "Updated $updated target(s) to v$LatestVersion."
        Update-AllStatus -Window $Window -InstalledStatus (Get-InstalledClientStatusMap -State (Get-InstallerState)) -LatestVersion $LatestVersion
        return [ordered]@{ Succeeded = $true; UpdatedCount = $updated }
    }
    catch {
        $txtInstMsg.Text = "Update All failed: $($_.Exception.Message)"
        return [ordered]@{ Succeeded = $false; Error = $_.Exception.Message; UpdatedCount = $updated }
    }
    finally {
        Set-UiBusyState -Window $Window -IsBusy $false
        Update-AllStatus -Window $Window -InstalledStatus (Get-InstalledClientStatusMap -State (Get-InstallerState)) -LatestVersion $LatestVersion
    }
}

function Update-AllStatus {
    param(
        [Parameter(Mandatory)] $Window,
        [Parameter(Mandatory)] $InstalledStatus,
        [string]$LatestVersion
    )

    $state = Get-InstallerState
    Refresh-InstalledLabels -Window $Window -State $state

    $buttonMaps = @(
        @{ Client = 'claude-code'; Install = 'InstallClaudeCodeButton'; Uninstall = 'UninstallClaudeCodeButton' }
        @{ Client = 'codex'; Install = 'InstallCodexButton'; Uninstall = 'UninstallCodexButton' }
        @{ Client = 'vscode'; Install = 'InstallVsCodeButton'; Uninstall = 'UninstallVsCodeButton' }
        @{ Client = 'visual-studio'; Install = 'InstallVisualStudioButton'; Uninstall = 'UninstallVisualStudioButton' }
        @{ Client = 'claude-desktop'; Install = 'InstallClaudeDesktopButton'; Uninstall = 'UninstallClaudeDesktopButton' }
        @{ Client = 'other'; Install = 'InstallOtherButton'; Uninstall = 'UninstallOtherButton' }
    )

    foreach ($map in $buttonMaps) {
        $installed = [bool]$InstalledStatus[$map.Client]
        $visibility = if ($installed) { 'Visible' } else { 'Collapsed' }
        $installButton = $Window.FindName($map.Install)
        $uninstallButton = $Window.FindName($map.Uninstall)

        if ($installButton -and $installButton.Content -is [System.Windows.Controls.Grid]) {
            $installButton.Content.Children[1].Visibility = $visibility
        }

        if ($uninstallButton -and $uninstallButton.Content -is [System.Windows.Controls.Grid]) {
            $uninstallButton.Content.Children[1].Visibility = $visibility
            $uninstallButton.IsEnabled = $installed
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($LatestVersion)) {
        Refresh-UpdateBanner -Window $Window -LatestVersion $LatestVersion
    }
}

function Invoke-InstallerAction {
    param(
        [Parameter(Mandatory)] [ValidateSet('install', 'uninstall')] [string]$ResolvedAction,
        [Parameter(Mandatory)] [string]$ResolvedArchitecture,
        [Parameter(Mandatory)] [string]$ResolvedClient,
        [Parameter(Mandatory)] [string]$ResolvedInstallRoot,
        [Parameter(Mandatory)] [string]$RequestedVersion,
        [switch]$UseLatestRelease
    )

    if ($ResolvedAction -eq 'uninstall') {
        $state = Get-InstallerState
        $registrationRecord = if ($state.registrations.Contains($ResolvedClient)) { $state.registrations[$ResolvedClient] } else { $null }
        if ($null -ne $registrationRecord) {
            if ([string]::IsNullOrWhiteSpace($ResolvedArchitecture)) {
                $ResolvedArchitecture = [string]$registrationRecord.architecture
            }
            if (-not $script:InstallRootWasSpecified -and [string]::IsNullOrWhiteSpace($ResolvedInstallRoot)) {
                $ResolvedInstallRoot = [string]$registrationRecord.installRoot
            }
        }

        if ([string]::IsNullOrWhiteSpace($ResolvedArchitecture)) {
            $ResolvedArchitecture = Get-SystemDefaultArchitecture
        }
        if ([string]::IsNullOrWhiteSpace($ResolvedInstallRoot)) {
            $ResolvedInstallRoot = Resolve-PreferredInstallRoot
        }

        $ResolvedInstallRoot = Resolve-AbsoluteDirectory -Path $ResolvedInstallRoot
        $installBase = Resolve-InstallBasePath -ResolvedInstallRoot $ResolvedInstallRoot -ResolvedArchitecture $ResolvedArchitecture
        $installManifestPath = Join-Path $installBase 'install-manifest.json'
        $installedExecutable = if (Test-Path $installManifestPath) { ([string](Get-Content -Path $installManifestPath -Raw | ConvertFrom-Json).executable) } else { $null }
        $registrations = @(Invoke-ClientUnregistration -SelectedClient $ResolvedClient)
        $remainingRegistrations = Get-RegistrationsForArchitecture -State $state -ResolvedArchitecture $ResolvedArchitecture -ResolvedInstallRoot $ResolvedInstallRoot -ExcludeClient $ResolvedClient
        $removedInstallation = $false
        if ($remainingRegistrations.Count -eq 0) {
            Remove-PathIfExists -Path $installBase
            $removedInstallation = $true
        }

        $verification = Invoke-UninstallVerification -SelectedClient $ResolvedClient
        if (-not $verification.Succeeded) {
            throw $verification.VerificationMessage
        }

        if ($state.registrations.Contains($ResolvedClient)) {
            [void]$state.registrations.Remove($ResolvedClient)
        }
        if ($removedInstallation -and $state.architectures.Contains($ResolvedArchitecture)) {
            [void]$state.architectures.Remove($ResolvedArchitecture)
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
            removedInstallation = $removedInstallation
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

function Show-InstallerWindow {
    param(
        [Parameter(Mandatory)] [string]$DefaultAction,
        [Parameter(Mandatory)] [string]$DefaultArchitecture,
        [Parameter(Mandatory)] [string]$DefaultClient,
        [Parameter(Mandatory)] [string]$DefaultInstallRoot,
        [Parameter(Mandatory)] $InstalledStatus,
        [string]$VersionHint
    )

    if ($NonInteractive -or $OutputJson) {
        return [ordered]@{ Launched = $false; Cancelled = $false; Selection = $null; HandledInWindow = $false }
    }

    try {
        Add-Type -AssemblyName PresentationFramework
        Add-Type -AssemblyName PresentationCore
        Add-Type -AssemblyName WindowsBase
        Add-Type -AssemblyName System.Windows.Forms
        Ensure-DwmMicaHelper

        $xaml = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:shell="clr-namespace:System.Windows.Shell;assembly=PresentationFramework"
        Title="WPF DevTools MCP"
        Width="640"
        Height="620"
        WindowStartupLocation="CenterScreen"
        WindowStyle="None"
        ResizeMode="CanMinimize"
        Background="#FF1E1E2E"
        FontFamily="Segoe UI Variable Display, Segoe UI, sans-serif"
        Foreground="#FFE4E4E7">
    <shell:WindowChrome.WindowChrome>
        <shell:WindowChrome CaptionHeight="40" GlassFrameThickness="0,0,0,1"
                            ResizeBorderThickness="0"/>
    </shell:WindowChrome.WindowChrome>

    <Window.Resources>
        <Style x:Key="CaptionBtn" TargetType="Button">
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="Foreground" Value="#99FFFFFF"/>
            <Setter Property="BorderThickness" Value="0"/>
            <Setter Property="Width" Value="46"/>
            <Setter Property="Height" Value="32"/>
            <Setter Property="FontFamily" Value="Segoe MDL2 Assets"/>
            <Setter Property="FontSize" Value="10"/>
            <Setter Property="shell:WindowChrome.IsHitTestVisibleInChrome" Value="True"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Button">
                        <Border x:Name="bd" Background="{TemplateBinding Background}">
                            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="bd" Property="Background" Value="#20FFFFFF"/>
                            </Trigger>
                            <Trigger Property="IsPressed" Value="True">
                                <Setter TargetName="bd" Property="Background" Value="#10FFFFFF"/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <Style x:Key="CloseBtn" TargetType="Button" BasedOn="{StaticResource CaptionBtn}">
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Button">
                        <Border x:Name="bd" Background="Transparent">
                            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="bd" Property="Background" Value="#E81123"/>
                                <Setter Property="Foreground" Value="White"/>
                            </Trigger>
                            <Trigger Property="IsPressed" Value="True">
                                <Setter TargetName="bd" Property="Background" Value="#BF0F1D"/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <Style x:Key="MainBtn" TargetType="Button">
            <Setter Property="Background" Value="#FF2D2D44"/>
            <Setter Property="Foreground" Value="#FFE4E4E7"/>
            <Setter Property="FontSize" Value="15"/>
            <Setter Property="Height" Value="72"/>
            <Setter Property="Margin" Value="0,5"/>
            <Setter Property="Cursor" Value="Hand"/>
            <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Button">
                        <Border x:Name="bd" Background="{TemplateBinding Background}"
                                CornerRadius="8" Padding="20,14"
                                BorderBrush="#18FFFFFF" BorderThickness="1">
                            <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
                                              VerticalAlignment="Center"/>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="bd" Property="Background" Value="#FF3A3A58"/>
                                <Setter TargetName="bd" Property="BorderBrush" Value="#28FFFFFF"/>
                            </Trigger>
                            <Trigger Property="IsPressed" Value="True">
                                <Setter TargetName="bd" Property="Background" Value="#FF252540"/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <Style x:Key="ItemBtn" TargetType="Button">
            <Setter Property="Background" Value="#FF282840"/>
            <Setter Property="Foreground" Value="#FFE4E4E7"/>
            <Setter Property="FontSize" Value="14"/>
            <Setter Property="Height" Value="48"/>
            <Setter Property="Margin" Value="0,3"/>
            <Setter Property="Cursor" Value="Hand"/>
            <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Button">
                        <Border x:Name="bd" Background="{TemplateBinding Background}"
                                CornerRadius="6" Padding="16,10"
                                BorderBrush="#12FFFFFF" BorderThickness="1">
                            <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
                                              VerticalAlignment="Center"/>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="bd" Property="Background" Value="#FF353555"/>
                                <Setter TargetName="bd" Property="BorderBrush" Value="#22FFFFFF"/>
                            </Trigger>
                            <Trigger Property="IsPressed" Value="True">
                                <Setter TargetName="bd" Property="Background" Value="#FF222238"/>
                            </Trigger>
                            <Trigger Property="IsEnabled" Value="False">
                                <Setter Property="Opacity" Value="0.35"/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <Style x:Key="InstallRootTextBoxStyle" TargetType="TextBox">
            <Setter Property="Height" Value="34"/>
            <Setter Property="Padding" Value="10,6"/>
            <Setter Property="Background" Value="#CC202033"/>
            <Setter Property="Foreground" Value="#FFEDEDF2"/>
            <Setter Property="BorderBrush" Value="#32FFFFFF"/>
            <Setter Property="BorderThickness" Value="1"/>
        </Style>

        <Style x:Key="InstallRootActionButtonStyle" TargetType="Button">
            <Setter Property="Background" Value="#CC2B2B3F"/>
            <Setter Property="Foreground" Value="#FFF3F4F6"/>
            <Setter Property="BorderBrush" Value="#22FFFFFF"/>
            <Setter Property="BorderThickness" Value="1"/>
            <Setter Property="Padding" Value="14,0"/>
            <Setter Property="Height" Value="34"/>
            <Setter Property="FontSize" Value="12"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Button">
                        <Border x:Name="bd" Background="{TemplateBinding Background}"
                                BorderBrush="{TemplateBinding BorderBrush}"
                                BorderThickness="{TemplateBinding BorderThickness}"
                                CornerRadius="8">
                            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="bd" Property="Background" Value="#E633334A"/>
                                <Setter TargetName="bd" Property="BorderBrush" Value="#38FFFFFF"/>
                            </Trigger>
                            <Trigger Property="IsPressed" Value="True">
                                <Setter TargetName="bd" Property="Background" Value="#FF232338"/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <Style x:Key="NavBtn" TargetType="Button">
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="Foreground" Value="#99FFFFFF"/>
            <Setter Property="FontSize" Value="13"/>
            <Setter Property="Height" Value="32"/>
            <Setter Property="Cursor" Value="Hand"/>
            <Setter Property="HorizontalAlignment" Value="Left"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Button">
                        <Border x:Name="bd" Background="{TemplateBinding Background}"
                                CornerRadius="6" Padding="12,4">
                            <ContentPresenter HorizontalAlignment="Left" VerticalAlignment="Center"/>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="bd" Property="Background" Value="#15FFFFFF"/>
                                <Setter Property="Foreground" Value="#CCFFFFFF"/>
                            </Trigger>
                            <Trigger Property="IsPressed" Value="True">
                                <Setter TargetName="bd" Property="Background" Value="#0AFFFFFF"/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <Style x:Key="ArchitectureComboBoxToggleStyle" TargetType="ToggleButton">
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="BorderBrush" Value="Transparent"/>
            <Setter Property="BorderThickness" Value="0"/>
            <Setter Property="Focusable" Value="False"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="ToggleButton">
                        <Grid Background="Transparent">
                            <Path x:Name="ArrowGlyph" Width="8" Height="5" Margin="0,1,0,0"
                                  HorizontalAlignment="Center" VerticalAlignment="Center"
                                  Data="M 0 0 L 4 4 L 8 0 Z"
                                  Fill="#B8FFFFFF"/>
                        </Grid>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="ArrowGlyph" Property="Fill" Value="#FFFFFFFF"/>
                            </Trigger>
                            <Trigger Property="IsChecked" Value="True">
                                <Setter TargetName="ArrowGlyph" Property="RenderTransform">
                                    <Setter.Value>
                                        <RotateTransform Angle="180" CenterX="4" CenterY="2.5"/>
                                    </Setter.Value>
                                </Setter>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <Style x:Key="ArchitectureComboBoxItemStyle" TargetType="ComboBoxItem">
            <Setter Property="Foreground" Value="#FFE4E4E7"/>
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="Padding" Value="10,7"/>
            <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="ComboBoxItem">
                        <Border x:Name="ItemBorder" Background="{TemplateBinding Background}" CornerRadius="6">
                            <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
                                              VerticalAlignment="Center"/>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsHighlighted" Value="True">
                                <Setter TargetName="ItemBorder" Property="Background" Value="#20FFFFFF"/>
                            </Trigger>
                            <Trigger Property="IsSelected" Value="True">
                                <Setter TargetName="ItemBorder" Property="Background" Value="#FF353555"/>
                            </Trigger>
                            <Trigger Property="IsEnabled" Value="False">
                                <Setter Property="Opacity" Value="0.35"/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <Style x:Key="ArchitectureComboBoxStyle" TargetType="ComboBox">
            <Setter Property="Foreground" Value="#FFE4E4E7"/>
            <Setter Property="Background" Value="#FF282840"/>
            <Setter Property="BorderBrush" Value="#24FFFFFF"/>
            <Setter Property="BorderThickness" Value="1"/>
            <Setter Property="Padding" Value="10,4,30,4"/>
            <Setter Property="FontSize" Value="12"/>
            <Setter Property="MinHeight" Value="28"/>
            <Setter Property="MaxDropDownHeight" Value="220"/>
            <Setter Property="ScrollViewer.CanContentScroll" Value="True"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="ComboBox">
                        <Grid>
                            <Border x:Name="ComboBorder"
                                    Background="{TemplateBinding Background}"
                                    BorderBrush="{TemplateBinding BorderBrush}"
                                    BorderThickness="{TemplateBinding BorderThickness}"
                                    CornerRadius="6">
                                <Grid>
                                    <ContentPresenter Margin="{TemplateBinding Padding}"
                                                      VerticalAlignment="Center"
                                                      HorizontalAlignment="Left"
                                                      Content="{TemplateBinding SelectionBoxItem}"
                                                      ContentTemplate="{TemplateBinding SelectionBoxItemTemplate}"
                                                      ContentStringFormat="{TemplateBinding SelectionBoxItemStringFormat}"/>
                                    <ToggleButton Width="24"
                                                  HorizontalAlignment="Right"
                                                  Margin="0,0,4,0"
                                                  IsChecked="{Binding IsDropDownOpen, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}"
                                                  Style="{StaticResource ArchitectureComboBoxToggleStyle}"/>
                                </Grid>
                            </Border>
                            <Popup x:Name="PART_Popup"
                                   AllowsTransparency="True"
                                   Focusable="False"
                                   IsOpen="{TemplateBinding IsDropDownOpen}"
                                   Placement="Bottom"
                                   PopupAnimation="Fade">
                                <Border Margin="0,6,0,0"
                                        MinWidth="{Binding ActualWidth, RelativeSource={RelativeSource TemplatedParent}}"
                                        MaxHeight="{TemplateBinding MaxDropDownHeight}"
                                        Background="#F21E1E2E"
                                        BorderBrush="#24FFFFFF"
                                        BorderThickness="1"
                                        CornerRadius="8">
                                    <ScrollViewer Margin="6"
                                                  SnapsToDevicePixels="True">
                                        <StackPanel IsItemsHost="True"
                                                    KeyboardNavigation.DirectionalNavigation="Contained"/>
                                    </ScrollViewer>
                                </Border>
                            </Popup>
                        </Grid>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter TargetName="ComboBorder" Property="BorderBrush" Value="#38FFFFFF"/>
                            </Trigger>
                            <Trigger Property="IsDropDownOpen" Value="True">
                                <Setter TargetName="ComboBorder" Property="BorderBrush" Value="#44FFFFFF"/>
                                <Setter TargetName="ComboBorder" Property="Background" Value="#FF30304A"/>
                            </Trigger>
                            <Trigger Property="HasItems" Value="False">
                                <Setter Property="MinHeight" Value="28"/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
    </Window.Resources>

    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="40"/>
            <RowDefinition Height="*"/>
        </Grid.RowDefinitions>

        <Grid Grid.Row="0" Background="#08FFFFFF">
            <TextBlock Text="  WPF DevTools MCP" Foreground="#60FFFFFF"
                       VerticalAlignment="Center" Margin="12,0,0,0" FontSize="12"/>
            <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
                <Border Background="#12FFFFFF" CornerRadius="6" Height="28" Margin="0,6,10,6"
                        Padding="8,0" VerticalAlignment="Center"
                        shell:WindowChrome.IsHitTestVisibleInChrome="True">
                    <DockPanel LastChildFill="True">
                        <TextBlock Text="Arch" Foreground="#80FFFFFF" Margin="0,0,8,0" VerticalAlignment="Center"/>
                        <ComboBox x:Name="ArchitectureSelector" Width="92"
                                  Style="{StaticResource ArchitectureComboBoxStyle}"
                                  ItemContainerStyle="{StaticResource ArchitectureComboBoxItemStyle}"
                                  MaxDropDownHeight="220"/>
                    </DockPanel>
                </Border>
                <Button x:Name="BtnMin" Content="&#xE949;" Style="{StaticResource CaptionBtn}"/>
                <Button x:Name="BtnClose" Content="&#xE8BB;" Style="{StaticResource CloseBtn}"/>
            </StackPanel>
        </Grid>

        <Grid Grid.Row="1" ClipToBounds="True">
            <Grid x:Name="PageMain">
                <Grid.RenderTransform><TranslateTransform/></Grid.RenderTransform>
                <StackPanel VerticalAlignment="Center" Margin="48,28">
                    <Border x:Name="UpdateBanner" Visibility="Collapsed" Margin="0,0,0,18"
                            Background="#CC243B2B" BorderBrush="#4468D391" BorderThickness="1" CornerRadius="10"
                            Padding="14,12">
                        <DockPanel LastChildFill="True">
                            <StackPanel>
                                <TextBlock Text="Updates available" Foreground="#FFE8FFF1" FontWeight="SemiBold"/>
                                <TextBlock x:Name="UpdateBannerText" Margin="0,4,0,0"
                                           Foreground="#CFEFE0" TextWrapping="Wrap"/>
                            </StackPanel>
                            <Button x:Name="UpdateAllButton" DockPanel.Dock="Right" Width="96" Margin="14,0,0,0"
                                    Style="{StaticResource InstallRootActionButtonStyle}" Content="Update All"/>
                        </DockPanel>
                    </Border>
                    <TextBlock Text="WPF DevTools MCP" FontSize="30" FontWeight="Bold"
                               Foreground="White" Margin="0,0,0,2"/>
                    <TextBlock Text="Model Context Protocol Server" FontSize="13"
                               Foreground="#60FFFFFF" Margin="0,0,0,6"/>
                    <TextBlock Text="Installation Manager" FontSize="13"
                               Foreground="#45FFFFFF" Margin="0,0,0,24"/>

                    <Button x:Name="GoInstallButton" Style="{StaticResource MainBtn}">
                        <StackPanel>
                            <TextBlock FontSize="15" FontWeight="SemiBold">
                                <Run Text="&#xE710;" FontFamily="Segoe MDL2 Assets" FontSize="14"/>
                                <Run Text="  Install"/>
                            </TextBlock>
                            <TextBlock Text="Choose the target client and install the current release executable."
                                       FontSize="11" Foreground="#60FFFFFF" Margin="0,4,0,0" TextWrapping="Wrap"/>
                        </StackPanel>
                    </Button>

                    <Button x:Name="GoUninstallButton" Style="{StaticResource MainBtn}">
                        <StackPanel>
                            <TextBlock FontSize="15" FontWeight="SemiBold">
                                <Run Text="&#xE74D;" FontFamily="Segoe MDL2 Assets" FontSize="14"/>
                                <Run Text="  Uninstall"/>
                            </TextBlock>
                            <TextBlock Text="Remove the MCP registration and delete the shared server when no client still uses it."
                                       FontSize="11" Foreground="#60FFFFFF" Margin="0,4,0,0" TextWrapping="Wrap"/>
                        </StackPanel>
                    </Button>

                    <TextBlock x:Name="TxtVersion" Text="WPF DevTools Installer" FontSize="11"
                               Foreground="#30FFFFFF" Margin="0,24,0,0"/>
                </StackPanel>
            </Grid>

            <Grid x:Name="PageInstall" Visibility="Collapsed" Opacity="0">
                <Grid.RenderTransform><TranslateTransform/></Grid.RenderTransform>
                <StackPanel Margin="48,20">
                    <Button x:Name="BackFromInstallButton" Style="{StaticResource NavBtn}" Margin="0,0,0,12" Content="&lt;-"/>
                    <ScrollViewer x:Name="InstallPageScrollViewer"
                                  VerticalScrollBarVisibility="Auto"
                                  HorizontalScrollBarVisibility="Disabled">
                        <StackPanel>
                            <TextBlock Margin="0,0,0,4" FontSize="22" FontWeight="Bold" Foreground="White" Text="Install to"/>
                            <TextBlock Margin="0,0,0,16" FontSize="12" Foreground="#50FFFFFF" Text="Select the client that should use the release executable."/>
                            <Border x:Name="InstallRootPanel" Margin="0,0,0,12" Padding="14,14,14,12"
                                    Background="#CC1F1F31" BorderBrush="#22FFFFFF" BorderThickness="1" CornerRadius="10">
                                <Grid>
                                    <Grid.RowDefinitions>
                                        <RowDefinition Height="Auto"/>
                                        <RowDefinition Height="Auto"/>
                                        <RowDefinition Height="Auto"/>
                                    </Grid.RowDefinitions>
                                    <TextBlock Foreground="#F5F6FB" Text="Install location" FontWeight="SemiBold"/>
                                    <TextBlock Grid.Row="1" Margin="0,4,0,10" Foreground="#80FFFFFF"
                                               Text="Choose where the shared MCP server executable should be stored." TextWrapping="Wrap"/>
                                    <Grid Grid.Row="2">
                                        <Grid.ColumnDefinitions>
                                            <ColumnDefinition Width="*"/>
                                            <ColumnDefinition Width="Auto"/>
                                        </Grid.ColumnDefinitions>
                                        <TextBox x:Name="InstallRootTextBox" Style="{StaticResource InstallRootTextBoxStyle}" Margin="0,0,10,0"/>
                                        <Button x:Name="BrowseInstallRootButton" Grid.Column="1" Width="92"
                                                Style="{StaticResource InstallRootActionButtonStyle}" Content="Browse"/>
                                    </Grid>
                                </Grid>
                            </Border>
                            <TextBlock x:Name="VersionHintText" Margin="0,0,0,10" Foreground="#A78BFA" TextWrapping="Wrap"/>

                            <Button x:Name="InstallClaudeCodeButton" Style="{StaticResource ItemBtn}">
                                <Grid>
                                    <TextBlock Text="Claude Code" VerticalAlignment="Center"/>
                                    <TextBlock Text="(Installed)" HorizontalAlignment="Right"
                                               VerticalAlignment="Center" Foreground="#A78BFA" FontSize="12"
                                               Visibility="Collapsed"/>
                                </Grid>
                            </Button>
                            <Button x:Name="InstallCodexButton" Style="{StaticResource ItemBtn}">
                                <Grid>
                                    <TextBlock Text="Codex" VerticalAlignment="Center"/>
                                    <TextBlock Text="(Installed)" HorizontalAlignment="Right"
                                               VerticalAlignment="Center" Foreground="#A78BFA" FontSize="12"
                                               Visibility="Collapsed"/>
                                </Grid>
                            </Button>
                            <Button x:Name="InstallVsCodeButton" Style="{StaticResource ItemBtn}">
                                <Grid>
                                    <TextBlock Text="VS Code" VerticalAlignment="Center"/>
                                    <TextBlock Text="(Installed)" HorizontalAlignment="Right"
                                               VerticalAlignment="Center" Foreground="#A78BFA" FontSize="12"
                                               Visibility="Collapsed"/>
                                </Grid>
                            </Button>
                            <Button x:Name="InstallVisualStudioButton" Style="{StaticResource ItemBtn}">
                                <Grid>
                                    <TextBlock Text="Visual Studio" VerticalAlignment="Center"/>
                                    <TextBlock Text="(Installed)" HorizontalAlignment="Right"
                                               VerticalAlignment="Center" Foreground="#A78BFA" FontSize="12"
                                               Visibility="Collapsed"/>
                                </Grid>
                            </Button>
                            <Button x:Name="InstallClaudeDesktopButton" Style="{StaticResource ItemBtn}">
                                <Grid>
                                    <TextBlock Text="Claude Desktop" VerticalAlignment="Center"/>
                                    <TextBlock Text="(Installed)" HorizontalAlignment="Right"
                                               VerticalAlignment="Center" Foreground="#A78BFA" FontSize="12"
                                               Visibility="Collapsed"/>
                                </Grid>
                            </Button>
                            <Button x:Name="InstallOtherButton" Style="{StaticResource ItemBtn}">
                                <Grid>
                                    <TextBlock Text="Other" VerticalAlignment="Center"/>
                                    <TextBlock Text="(Installed)" HorizontalAlignment="Right"
                                               VerticalAlignment="Center" Foreground="#A78BFA" FontSize="12"
                                               Visibility="Collapsed"/>
                                </Grid>
                            </Button>

                            <Border Height="1" Background="#15FFFFFF" Margin="0,12"/>
                            <Button x:Name="GenerateStandardInstallJsonButton" Style="{StaticResource ItemBtn}" Margin="0,8,0,0">
                                <Grid>
                                    <TextBlock Text="Generate Standard Install JSON" VerticalAlignment="Center" FontWeight="SemiBold"/>
                                </Grid>
                            </Button>

                            <TextBlock x:Name="TxtInstMsg" Margin="0,14,0,0" FontSize="12"
                                       Foreground="#6C63FF" TextWrapping="Wrap"/>
                        </StackPanel>
                    </ScrollViewer>
                </StackPanel>
            </Grid>

            <Grid x:Name="PageUninstall" Visibility="Collapsed" Opacity="0">
                <Grid.RenderTransform><TranslateTransform/></Grid.RenderTransform>
                <StackPanel Margin="48,20">
                    <Button x:Name="BackFromUninstallButton" Style="{StaticResource NavBtn}" Margin="0,0,0,12" Content="&lt;-"/>
                    <ScrollViewer x:Name="UninstallPageScrollViewer"
                                  VerticalScrollBarVisibility="Auto"
                                  HorizontalScrollBarVisibility="Disabled">
                        <StackPanel>
                            <TextBlock Margin="0,0,0,4" FontSize="22" FontWeight="Bold" Foreground="White" Text="Uninstall from"/>
                            <TextBlock Margin="0,0,0,16" FontSize="12" Foreground="#50FFFFFF" Text="Only registered targets can be removed."/>
                            <Button x:Name="UninstallClaudeCodeButton" Style="{StaticResource ItemBtn}">
                                <Grid>
                                    <TextBlock Text="Claude Code" VerticalAlignment="Center"/>
                                    <TextBlock Text="(Installed)" HorizontalAlignment="Right"
                                               VerticalAlignment="Center" Foreground="#A78BFA" FontSize="12"
                                               Visibility="Collapsed"/>
                                </Grid>
                            </Button>
                            <Button x:Name="UninstallCodexButton" Style="{StaticResource ItemBtn}">
                                <Grid>
                                    <TextBlock Text="Codex" VerticalAlignment="Center"/>
                                    <TextBlock Text="(Installed)" HorizontalAlignment="Right"
                                               VerticalAlignment="Center" Foreground="#A78BFA" FontSize="12"
                                               Visibility="Collapsed"/>
                                </Grid>
                            </Button>
                            <Button x:Name="UninstallVsCodeButton" Style="{StaticResource ItemBtn}">
                                <Grid>
                                    <TextBlock Text="VS Code" VerticalAlignment="Center"/>
                                    <TextBlock Text="(Installed)" HorizontalAlignment="Right"
                                               VerticalAlignment="Center" Foreground="#A78BFA" FontSize="12"
                                               Visibility="Collapsed"/>
                                </Grid>
                            </Button>
                            <Button x:Name="UninstallVisualStudioButton" Style="{StaticResource ItemBtn}">
                                <Grid>
                                    <TextBlock Text="Visual Studio" VerticalAlignment="Center"/>
                                    <TextBlock Text="(Installed)" HorizontalAlignment="Right"
                                               VerticalAlignment="Center" Foreground="#A78BFA" FontSize="12"
                                               Visibility="Collapsed"/>
                                </Grid>
                            </Button>
                            <Button x:Name="UninstallClaudeDesktopButton" Style="{StaticResource ItemBtn}">
                                <Grid>
                                    <TextBlock Text="Claude Desktop" VerticalAlignment="Center"/>
                                    <TextBlock Text="(Installed)" HorizontalAlignment="Right"
                                               VerticalAlignment="Center" Foreground="#A78BFA" FontSize="12"
                                               Visibility="Collapsed"/>
                                </Grid>
                            </Button>
                            <Button x:Name="UninstallOtherButton" Style="{StaticResource ItemBtn}">
                                <Grid>
                                    <TextBlock Text="Other" VerticalAlignment="Center"/>
                                    <TextBlock Text="(Installed)" HorizontalAlignment="Right"
                                               VerticalAlignment="Center" Foreground="#A78BFA" FontSize="12"
                                               Visibility="Collapsed"/>
                                </Grid>
                            </Button>

                            <TextBlock x:Name="TxtUninstMsg" Margin="0,14,0,0" FontSize="12"
                                       Foreground="#E85D75" TextWrapping="Wrap"/>
                        </StackPanel>
                    </ScrollViewer>
                </StackPanel>
            </Grid>
        </Grid>
    </Grid>
</Window>
'@
        $processedXaml = $xaml -replace ' x:Name="(?!bd")', ' Name="'
        $sr = [System.IO.StringReader]::new($processedXaml)
        $xr = [System.Xml.XmlReader]::Create($sr)
        $window = [Windows.Markup.XamlReader]::Load($xr)

        $architectureSelector = $window.FindName('ArchitectureSelector')
        $updateBanner = $window.FindName('UpdateBanner')
        $updateBannerText = $window.FindName('UpdateBannerText')
        $updateAllButton = $window.FindName('UpdateAllButton')
        $installRootPanel = $window.FindName('InstallRootPanel')
        $installRootTextBox = $window.FindName('InstallRootTextBox')
        $browseInstallRootButton = $window.FindName('BrowseInstallRootButton')
        $versionHintText = $window.FindName('VersionHintText')
        $pageMain = $window.FindName('PageMain')
        $pageInstall = $window.FindName('PageInstall')
        $pageUninstall = $window.FindName('PageUninstall')
        $generateStandardInstallJsonButton = $window.FindName('GenerateStandardInstallJsonButton')
        $TxtInstMsg = $window.FindName('TxtInstMsg')
        $TxtUninstMsg = $window.FindName('TxtUninstMsg')

        $latestVersion = $null
        try {
            $latestVersion = [string](Invoke-RestMethod -Uri (Get-GitHubReleaseApiUri -ResolvedVersion 'latest') -Headers @{ 'User-Agent' = 'wpf-devtools-online-installer' } -TimeoutSec 10).tag_name
            $latestVersion = $latestVersion.TrimStart('v')
        }
        catch {
        }

        $architectureSelector.ItemsSource = @('x64', 'x86', 'arm64')
        $architectureSelector.SelectedItem = $DefaultArchitecture
        $installRootTextBox.Text = $DefaultInstallRoot
        $versionHintText.Text = if ([string]::IsNullOrWhiteSpace($VersionHint)) { '' } else { $VersionHint }
        Refresh-UpdateBanner -Window $window -LatestVersion $latestVersion

        $wireActionButton = {
            param([string]$Name, [string]$ResolvedAction, [string]$ResolvedClient)
            $button = $window.FindName($Name)
            $button.Add_Click({
                    if ($ResolvedAction -eq 'install') {
                        Invoke-GuiInstallOperation -Window $window -SelectedClient $ResolvedClient -LatestVersion $latestVersion | Out-Null
                    }
                    else {
                        Invoke-GuiUninstallOperation -Window $window -SelectedClient $ResolvedClient -LatestVersion $latestVersion | Out-Null
                    }
                })
        }

        $window.FindName('BtnMin').Add_Click({
                try { $window.WindowState = 'Minimized' } catch {}
            })
        $window.FindName('BtnClose').Add_Click({
                Request-WindowClose -Window $window
            })
        $window.FindName('GoInstallButton').Add_Click({
                Update-AllStatus -Window $window -InstalledStatus (Get-InstalledClientStatusMap -State (Get-InstallerState)) -LatestVersion $latestVersion
                $TxtInstMsg.Text = ''
                Switch-Page -From $pageMain -To $pageInstall -Direction 1
            })
        $window.FindName('GoUninstallButton').Add_Click({
                Update-AllStatus -Window $window -InstalledStatus (Get-InstalledClientStatusMap -State (Get-InstallerState)) -LatestVersion $latestVersion
                $TxtUninstMsg.Text = ''
                Switch-Page -From $pageMain -To $pageUninstall -Direction 1
            })
        $window.FindName('BackFromInstallButton').Add_Click({
                Switch-Page -From $pageInstall -To $pageMain -Direction -1
            })
        $window.FindName('BackFromUninstallButton').Add_Click({
                Switch-Page -From $pageUninstall -To $pageMain -Direction -1
            })
        $browseInstallRootButton.Add_Click({
                try {
                    $dialog = New-Object System.Windows.Forms.FolderBrowserDialog
                    $dialog.SelectedPath = $installRootTextBox.Text
                    $dialog.Description = 'Select an install location for the shared WPF DevTools MCP server.'
                    if ($dialog.ShowDialog() -eq [System.Windows.Forms.DialogResult]::OK -and
                        -not [string]::IsNullOrWhiteSpace($dialog.SelectedPath)) {
                        $installRootTextBox.Text = $dialog.SelectedPath
                    }
                }
                catch {
                    $TxtInstMsg.Text = "Unable to browse for install location: $($_.Exception.Message)"
                }
            })
        $updateAllButton.Add_Click({
                Invoke-UpdateAllOperation -Window $window -LatestVersion $latestVersion | Out-Null
            })
        $generateStandardInstallJsonButton.Add_Click({
                try {
                    $selectedArchitecture = [string]$architectureSelector.SelectedItem
                    if ([string]::IsNullOrWhiteSpace($selectedArchitecture)) {
                        $selectedArchitecture = $DefaultArchitecture
                    }

                    $selectedRoot = $installRootTextBox.Text
                    if ([string]::IsNullOrWhiteSpace($selectedRoot)) {
                        $selectedRoot = $DefaultInstallRoot
                    }

                    $standardInstallJson = Get-StandardInstallJson -ResolvedInstallRoot $selectedRoot.Trim() -ResolvedArchitecture $selectedArchitecture
                    [System.Windows.Clipboard]::SetText($standardInstallJson)
                    $TxtInstMsg.Text = 'Standard install JSON copied to clipboard.'
                }
                catch {
                    $TxtInstMsg.Text = "Unable to copy install JSON: $($_.Exception.Message)"
                }
            })

        & $wireActionButton 'InstallClaudeCodeButton' 'install' 'claude-code'
        & $wireActionButton 'InstallCodexButton' 'install' 'codex'
        & $wireActionButton 'InstallVsCodeButton' 'install' 'vscode'
        & $wireActionButton 'InstallVisualStudioButton' 'install' 'visual-studio'
        & $wireActionButton 'InstallClaudeDesktopButton' 'install' 'claude-desktop'
        & $wireActionButton 'InstallOtherButton' 'install' 'other'

        & $wireActionButton 'UninstallClaudeCodeButton' 'uninstall' 'claude-code'
        & $wireActionButton 'UninstallCodexButton' 'uninstall' 'codex'
        & $wireActionButton 'UninstallVsCodeButton' 'uninstall' 'vscode'
        & $wireActionButton 'UninstallVisualStudioButton' 'uninstall' 'visual-studio'
        & $wireActionButton 'UninstallClaudeDesktopButton' 'uninstall' 'claude-desktop'
        & $wireActionButton 'UninstallOtherButton' 'uninstall' 'other'

        $buttonMap = @{
            'claude-code' = 'UninstallClaudeCodeButton'
            'codex' = 'UninstallCodexButton'
            'vscode' = 'UninstallVsCodeButton'
            'visual-studio' = 'UninstallVisualStudioButton'
            'claude-desktop' = 'UninstallClaudeDesktopButton'
            'other' = 'UninstallOtherButton'
        }
        foreach ($client in Get-SupportedClients) {
            $uninstallButton = $window.FindName($buttonMap[$client.Id])
            $uninstallButton.IsEnabled = [bool]$InstalledStatus[$client.Id]
        }

        $window.Add_Loaded({
                try {
                    $hwnd = ([System.Windows.Interop.WindowInteropHelper]::new($window)).Handle
                    [DwmMicaHelper]::Apply($hwnd)
                    Update-AllStatus -Window $window -InstalledStatus $InstalledStatus -LatestVersion $latestVersion
                }
                catch {
                }
            })

        [void]$window.ShowDialog()
        return [ordered]@{
            Launched = $true
            Cancelled = $false
            Selection = $null
            HandledInWindow = $true
        }
    }
    catch {
        return [ordered]@{
            Launched = $false
            Cancelled = $false
            Selection = $null
            HandledInWindow = $false
        }
    }
}

function Show-OperationSummary {
    param(
        [Parameter(Mandatory)] $Result,
        [string]$VersionHint
    )

    if ($NonInteractive -or $OutputJson) {
        return
    }

    try {
        Add-Type -AssemblyName PresentationFramework
        Add-Type -AssemblyName PresentationCore
        Add-Type -AssemblyName WindowsBase
        Ensure-DwmMicaHelper

        $isInstall = ($Result.action -eq 'install')
        $title = if ($isInstall) { 'Installation complete' } else { 'Uninstall complete' }
        $message = if ($isInstall) {
            "Installed for $(Resolve-ClientLabel -ClientId $Result.client).`n`nExecutable:`n$($Result.installedExecutable)"
        }
        else {
            "Uninstalled from $(Resolve-ClientLabel -ClientId $Result.client)."
        }

        $xaml = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:shell="clr-namespace:System.Windows.Shell;assembly=PresentationFramework"
        Title="WPF DevTools Installer"
        Width="560"
        Height="360"
        WindowStartupLocation="CenterScreen"
        ResizeMode="NoResize"
        WindowStyle="None"
        Background="#FF1E1E2E"
        FontFamily="Segoe UI Variable Display, Segoe UI, sans-serif"
        Foreground="#FFE4E4E7">
    <shell:WindowChrome.WindowChrome>
        <shell:WindowChrome CaptionHeight="40" GlassFrameThickness="0,0,0,1"
                            ResizeBorderThickness="0"/>
    </shell:WindowChrome.WindowChrome>

    <Window.Resources>
        <Style x:Key="CaptionBtn" TargetType="Button">
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="Foreground" Value="#CCFFFFFF"/>
            <Setter Property="BorderBrush" Value="Transparent"/>
            <Setter Property="BorderThickness" Value="0"/>
            <Setter Property="Width" Value="42"/>
            <Setter Property="Height" Value="32"/>
            <Setter Property="FontFamily" Value="Segoe MDL2 Assets"/>
            <Setter Property="FontSize" Value="10"/>
            <Setter Property="shell:WindowChrome.IsHitTestVisibleInChrome" Value="True"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Button">
                        <Border Background="{TemplateBinding Background}" CornerRadius="8">
                            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter Property="Background" Value="#18FFFFFF"/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <Style x:Key="CloseBtn" TargetType="Button" BasedOn="{StaticResource CaptionBtn}">
            <Style.Triggers>
                <Trigger Property="IsMouseOver" Value="True">
                    <Setter Property="Background" Value="#FFB42318"/>
                </Trigger>
            </Style.Triggers>
        </Style>

        <Style x:Key="MainBtn" TargetType="Button">
            <Setter Property="Background" Value="#FFF4A261"/>
            <Setter Property="Foreground" Value="#FF1E1E2E"/>
            <Setter Property="BorderBrush" Value="Transparent"/>
            <Setter Property="BorderThickness" Value="0"/>
            <Setter Property="FontSize" Value="16"/>
            <Setter Property="FontWeight" Value="SemiBold"/>
            <Setter Property="Height" Value="44"/>
            <Setter Property="Padding" Value="20,0"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Button">
                        <Border Background="{TemplateBinding Background}" CornerRadius="14">
                            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                        </Border>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>

        <Style x:Key="ItemBtn" TargetType="Button">
            <Setter Property="Background" Value="#12FFFFFF"/>
            <Setter Property="Foreground" Value="#FFF8FAFC"/>
            <Setter Property="BorderBrush" Value="Transparent"/>
            <Setter Property="BorderThickness" Value="0"/>
            <Setter Property="FontSize" Value="14"/>
            <Setter Property="Height" Value="44"/>
            <Setter Property="Padding" Value="16,0"/>
            <Setter Property="Template">
                <Setter.Value>
                    <ControlTemplate TargetType="Button">
                        <Border Background="{TemplateBinding Background}" CornerRadius="14">
                            <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center"/>
                        </Border>
                        <ControlTemplate.Triggers>
                            <Trigger Property="IsMouseOver" Value="True">
                                <Setter Property="Background" Value="#20FFFFFF"/>
                            </Trigger>
                        </ControlTemplate.Triggers>
                    </ControlTemplate>
                </Setter.Value>
            </Setter>
        </Style>
    </Window.Resources>

    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="40"/>
            <RowDefinition Height="*"/>
        </Grid.RowDefinitions>

        <Border Grid.Row="0" Background="#14000000" BorderBrush="#18FFFFFF" BorderThickness="0,0,0,1">
            <Grid>
                <TextBlock Text="WPF DevTools Installer" Margin="18,0,0,0" VerticalAlignment="Center"
                           FontSize="13" Foreground="#CCFFFFFF"/>
                <StackPanel Orientation="Horizontal" HorizontalAlignment="Right">
                    <Button x:Name="SummaryCloseCaptionButton" Style="{StaticResource CloseBtn}" Content="&#xE8BB;"/>
                </StackPanel>
            </Grid>
        </Border>

        <Grid Grid.Row="1" Margin="28">
            <StackPanel VerticalAlignment="Center">
                <TextBlock Text="WPF DevTools MCP" FontSize="14" FontWeight="SemiBold" Foreground="#80FFFFFF"/>
                <TextBlock x:Name="SummaryTitleText" Margin="0,12,0,0" FontSize="28" FontWeight="SemiBold"/>
                <TextBlock x:Name="SummaryMessageText" Margin="0,16,0,0" FontSize="14" TextWrapping="Wrap"
                           Foreground="#FFD4D4D8"/>
                <TextBlock x:Name="SummaryVersionText" Margin="0,12,0,0" FontSize="12" TextWrapping="Wrap"
                           Foreground="#A78BFA"/>

                <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,26,0,0">
                    <Button x:Name="OpenDocsButton" Style="{StaticResource ItemBtn}" Width="220" Margin="0,0,10,0">
                        Open documentation homepage
                    </Button>
                    <Button x:Name="CloseSummaryButton" Style="{StaticResource MainBtn}" Width="120">
                        Close
                    </Button>
                </StackPanel>
            </StackPanel>
        </Grid>
    </Grid>
</Window>
'@
        $processedXaml = $xaml -replace ' x:Name="(?!bd")', ' Name="'
        $sr = [System.IO.StringReader]::new($processedXaml)
        $xr = [System.Xml.XmlReader]::Create($sr)
        $window = [Windows.Markup.XamlReader]::Load($xr)

        $summaryTitleText = $window.FindName('SummaryTitleText')
        $summaryMessageText = $window.FindName('SummaryMessageText')
        $summaryVersionText = $window.FindName('SummaryVersionText')
        $openDocsButton = $window.FindName('OpenDocsButton')
        $closeSummaryButton = $window.FindName('CloseSummaryButton')
        $summaryCloseCaptionButton = $window.FindName('SummaryCloseCaptionButton')

        $summaryTitleText.Text = $title
        $summaryMessageText.Text = $message
        $summaryVersionText.Text = if ([string]::IsNullOrWhiteSpace($VersionHint)) { '' } else { $VersionHint }
        $summaryVersionText.Visibility = if ([string]::IsNullOrWhiteSpace($VersionHint)) { 'Collapsed' } else { 'Visible' }
        $openDocsButton.Visibility = if ($isInstall) { 'Visible' } else { 'Collapsed' }

        $closeAction = {
            Request-WindowClose -Window $window
        }
        $summaryCloseCaptionButton.Add_Click($closeAction)
        $closeSummaryButton.Add_Click($closeAction)
        $openDocsButton.Add_Click({
                try {
                    Invoke-DocsHomepage
                    Request-WindowClose -Window $window
                }
                catch {
                    $summaryVersionText.Text = "Unable to open documentation: $($_.Exception.Message)"
                    $summaryVersionText.Visibility = 'Visible'
                }
            })

        $window.Add_Loaded({
                try {
                    $hwnd = ([System.Windows.Interop.WindowInteropHelper]::new($window)).Handle
                    [DwmMicaHelper]::Apply($hwnd)
                }
                catch {
                }
            })

        [void]$window.ShowDialog()
    }
    catch {
        Write-InstallerMessage $message
        if (-not [string]::IsNullOrWhiteSpace($VersionHint)) {
            Write-InstallerMessage $VersionHint
        }
    }
}

function Resolve-Selection {
    $state = Get-InstallerState
    $defaultArchitecture = if ([string]::IsNullOrWhiteSpace($Architecture)) { Get-SystemDefaultArchitecture } else { $Architecture }
    $defaultClient = if ([string]::IsNullOrWhiteSpace($Client)) { Get-DefaultClient } else { $Client }
    $defaultInstallRoot = Resolve-PreferredInstallRoot
    $mode = Resolve-InstallerMode
    $versionHint = Get-OfflineVersionHint -Mode $mode

    $windowResult = Show-InstallerWindow `
        -DefaultAction $Action `
        -DefaultArchitecture $defaultArchitecture `
        -DefaultClient $defaultClient `
        -DefaultInstallRoot $defaultInstallRoot `
        -InstalledStatus (Get-InstalledClientStatusMap -State $state) `
        -VersionHint $versionHint

    if ($windowResult.Launched) {
        return [ordered]@{
            Cancelled = [bool]$windowResult.Cancelled
            Selection = $windowResult.Selection
            VersionHint = $versionHint
            HandledInWindow = [bool]$windowResult.HandledInWindow
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

    Show-OperationSummary -Result $result -VersionHint $versionHint
}
