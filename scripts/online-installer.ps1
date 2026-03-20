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
        [Parameter(Mandatory)] $Registration
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
    }
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

function Update-AllStatus {
    param(
        [Parameter(Mandatory)] $Window,
        [Parameter(Mandatory)] $InstalledStatus
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
        return [ordered]@{ Launched = $false; Cancelled = $false; Selection = $null }
    }

    try {
        Add-Type -AssemblyName PresentationFramework
        Add-Type -AssemblyName PresentationCore
        Add-Type -AssemblyName WindowsBase
        Ensure-DwmMicaHelper

        $xaml = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="WPF DevTools MCP"
        Width="640"
        Height="620"
        WindowStartupLocation="CenterScreen"
        WindowStyle="None"
        ResizeMode="CanMinimize"
        Background="#FF1E1E2E"
        FontFamily="Segoe UI Variable Display, Segoe UI, sans-serif"
        Foreground="#FFE4E4E7">
    <WindowChrome.WindowChrome>
        <WindowChrome CaptionHeight="40" GlassFrameThickness="0,0,0,1"
                      ResizeBorderThickness="0"/>
    </WindowChrome.WindowChrome>

    <Window.Resources>
        <Style x:Key="CaptionBtn" TargetType="Button">
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="Foreground" Value="#99FFFFFF"/>
            <Setter Property="BorderThickness" Value="0"/>
            <Setter Property="Width" Value="46"/>
            <Setter Property="Height" Value="32"/>
            <Setter Property="FontFamily" Value="Segoe MDL2 Assets"/>
            <Setter Property="FontSize" Value="10"/>
            <Setter Property="WindowChrome.IsHitTestVisibleInChrome" Value="True"/>
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
                        WindowChrome.IsHitTestVisibleInChrome="True">
                    <DockPanel LastChildFill="True">
                        <TextBlock Text="Arch" Foreground="#80FFFFFF" Margin="0,0,8,0" VerticalAlignment="Center"/>
                        <ComboBox x:Name="ArchitectureSelector" Width="84" Background="#FF282840" Foreground="#FFE4E4E7"
                                  BorderThickness="0"/>
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
                    <Button x:Name="BackFromInstallButton" Style="{StaticResource NavBtn}" Margin="0,0,0,12" Content="← Back"/>
                    <TextBlock Margin="0,0,0,4" FontSize="22" FontWeight="Bold" Foreground="White" Text="Install to"/>
                    <TextBlock Margin="0,0,0,16" FontSize="12" Foreground="#50FFFFFF" Text="Select the client that should use the release executable."/>
                    <TextBlock Foreground="#80FFFFFF" Text="Install location" Margin="0,0,0,6"/>
                    <TextBox x:Name="InstallRootTextBox" Margin="0,0,0,10" Height="34"
                             Background="#FF282840" Foreground="#FFE4E4E7" BorderBrush="#22FFFFFF" BorderThickness="1"/>
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
            </Grid>

            <Grid x:Name="PageUninstall" Visibility="Collapsed" Opacity="0">
                <Grid.RenderTransform><TranslateTransform/></Grid.RenderTransform>
                <StackPanel Margin="48,20">
                    <Button x:Name="BackFromUninstallButton" Style="{StaticResource NavBtn}" Margin="0,0,0,12" Content="← Back"/>
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
        $installRootTextBox = $window.FindName('InstallRootTextBox')
        $versionHintText = $window.FindName('VersionHintText')
        $pageMain = $window.FindName('PageMain')
        $pageInstall = $window.FindName('PageInstall')
        $pageUninstall = $window.FindName('PageUninstall')
        $TxtInstMsg = $window.FindName('TxtInstMsg')
        $TxtUninstMsg = $window.FindName('TxtUninstMsg')

        $architectureSelector.ItemsSource = @('x64', 'x86', 'arm64')
        $architectureSelector.SelectedItem = $DefaultArchitecture
        $installRootTextBox.Text = $DefaultInstallRoot
        $versionHintText.Text = if ([string]::IsNullOrWhiteSpace($VersionHint)) { '' } else { $VersionHint }

        $selection = $null
        $cancelled = $true

        $setSelection = {
            param([string]$ResolvedAction, [string]$ResolvedClient)
            $selectedRoot = $installRootTextBox.Text
            if ([string]::IsNullOrWhiteSpace($selectedRoot)) {
                $selectedRoot = $DefaultInstallRoot
            }

            $selection = [ordered]@{
                Action = $ResolvedAction
                Architecture = [string]$architectureSelector.SelectedItem
                Client = $ResolvedClient
                InstallRoot = $selectedRoot.Trim()
            }
            $cancelled = $false
            $window.Close()
        }

        $wireActionButton = {
            param([string]$Name, [string]$ResolvedAction, [string]$ResolvedClient)
            $button = $window.FindName($Name)
            $button.Add_Click({
                    & $setSelection $ResolvedAction $ResolvedClient
                })
        }

        $window.FindName('BtnMin').Add_Click({
                try { $window.WindowState = 'Minimized' } catch {}
            })
        $window.FindName('BtnClose').Add_Click({
                try { $window.Close() } catch {}
            })
        $window.FindName('GoInstallButton').Add_Click({
                Update-AllStatus -Window $window -InstalledStatus $InstalledStatus
                $TxtInstMsg.Text = ''
                Switch-Page -From $pageMain -To $pageInstall -Direction 1
            })
        $window.FindName('GoUninstallButton').Add_Click({
                Update-AllStatus -Window $window -InstalledStatus $InstalledStatus
                $TxtUninstMsg.Text = ''
                Switch-Page -From $pageMain -To $pageUninstall -Direction 1
            })
        $window.FindName('BackFromInstallButton').Add_Click({
                Switch-Page -From $pageInstall -To $pageMain -Direction -1
            })
        $window.FindName('BackFromUninstallButton').Add_Click({
                Switch-Page -From $pageUninstall -To $pageMain -Direction -1
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
                    Update-AllStatus -Window $window -InstalledStatus $InstalledStatus
                }
                catch {
                }
            })

        [void]$window.ShowDialog()
        return [ordered]@{
            Launched = $true
            Cancelled = $cancelled
            Selection = $selection
        }
    }
    catch {
        return [ordered]@{
            Launched = $false
            Cancelled = $false
            Selection = $null
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
        $message = if ($Result.action -eq 'install') {
            "Installed for $(Resolve-ClientLabel -ClientId $Result.client).`n`nExecutable:`n$($Result.installedExecutable)"
        }
        else {
            "Uninstalled from $(Resolve-ClientLabel -ClientId $Result.client)."
        }

        if (-not [string]::IsNullOrWhiteSpace($VersionHint)) {
            $message += "`n`n$VersionHint"
        }

        if ($Result.action -eq 'install') {
            $choice = [System.Windows.MessageBox]::Show(
                "$message`n`nOpen documentation homepage?",
                'WPF DevTools Installer',
                [System.Windows.MessageBoxButton]::YesNo,
                [System.Windows.MessageBoxImage]::Information)
            if ($choice -eq [System.Windows.MessageBoxResult]::Yes) {
                Invoke-DocsHomepage
            }
        }
        else {
            [void][System.Windows.MessageBox]::Show(
                $message,
                'WPF DevTools Installer',
                [System.Windows.MessageBoxButton]::OK,
                [System.Windows.MessageBoxImage]::Information)
        }
    }
    catch {
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
        }
    }

    return [ordered]@{
        Cancelled = $false
        Selection = (Get-CliSelection)
        VersionHint = $versionHint
    }
}

function Resolve-InstallBasePath {
    param(
        [Parameter(Mandatory)] [string]$ResolvedInstallRoot,
        [Parameter(Mandatory)] [string]$ResolvedArchitecture
    )

    return (Join-Path (Resolve-AbsoluteDirectory -Path $ResolvedInstallRoot) $ResolvedArchitecture)
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

$interactiveSelection = $selectionContext.Selection
$resolvedAction = [string]$interactiveSelection.Action
$resolvedArchitecture = [string]$interactiveSelection.Architecture
$resolvedClient = [string]$interactiveSelection.Client
$resolvedInstallRoot = [string]$interactiveSelection.InstallRoot
$versionHint = [string]$selectionContext.VersionHint

if ($resolvedAction -eq 'uninstall') {
    $state = Get-InstallerState
    $registrationRecord = if ($state.registrations.Contains($resolvedClient)) { $state.registrations[$resolvedClient] } else { $null }

    if ($null -ne $registrationRecord) {
        if ([string]::IsNullOrWhiteSpace($resolvedArchitecture)) {
            $resolvedArchitecture = [string]$registrationRecord.architecture
        }
        if (-not $script:InstallRootWasSpecified -and [string]::IsNullOrWhiteSpace($resolvedInstallRoot)) {
            $resolvedInstallRoot = [string]$registrationRecord.installRoot
        }
    }

    if ([string]::IsNullOrWhiteSpace($resolvedArchitecture)) {
        $resolvedArchitecture = Get-SystemDefaultArchitecture
    }

    if ([string]::IsNullOrWhiteSpace($resolvedInstallRoot)) {
        $resolvedInstallRoot = Resolve-PreferredInstallRoot
    }

    $resolvedInstallRoot = Resolve-AbsoluteDirectory -Path $resolvedInstallRoot
    $installBase = Resolve-InstallBasePath -ResolvedInstallRoot $resolvedInstallRoot -ResolvedArchitecture $resolvedArchitecture
    $installManifestPath = Join-Path $installBase 'install-manifest.json'
    $installedExecutable = if (Test-Path $installManifestPath) { ([string](Get-Content -Path $installManifestPath -Raw | ConvertFrom-Json).executable) } else { $null }

    $registrations = @(Invoke-ClientUnregistration -SelectedClient $resolvedClient)

    if ($state.registrations.Contains($resolvedClient)) {
        [void]$state.registrations.Remove($resolvedClient)
    }

    $remainingRegistrations = Get-RegistrationsForArchitecture -State $state -ResolvedArchitecture $resolvedArchitecture -ResolvedInstallRoot $resolvedInstallRoot -ExcludeClient $resolvedClient
    $removedInstallation = $false
    if ($remainingRegistrations.Count -eq 0) {
        Remove-PathIfExists -Path $installBase
        if ($state.architectures.Contains($resolvedArchitecture)) {
            [void]$state.architectures.Remove($resolvedArchitecture)
        }
        $removedInstallation = $true
    }

    $statePath = Save-InstallerState -State $state
    $result = [ordered]@{
        action = 'uninstall'
        mode = 'offline'
        downloadSource = 'none'
        version = $Version
        resolvedVersion = $null
        architecture = $resolvedArchitecture
        client = $resolvedClient
        packageAssetName = $null
        downloadUri = $null
        installRoot = $resolvedInstallRoot
        installedExecutable = $installedExecutable
        selectedClients = @($resolvedClient)
        statePath = $statePath
        removedInstallation = $removedInstallation
        registrations = @($registrations)
    }
}
else {
    $mode = Resolve-InstallerMode
    $session = Resolve-PackageSession -Mode $mode -ResolvedVersion $Version -ResolvedArchitecture $resolvedArchitecture

    try {
        $packageManifest = Get-Content -Path (Resolve-PackageManifestPath -PackageDirectory $session.PackageDirectory) -Raw | ConvertFrom-Json
        $resolvedVersion = if ([string]::IsNullOrWhiteSpace([string]$packageManifest.version)) { [string]$session.ResolvedVersion } else { [string]$packageManifest.version }
        $installResult = Install-PackagePayload `
            -PackageDirectory $session.PackageDirectory `
            -PackageManifest $packageManifest `
            -ResolvedArchitecture $resolvedArchitecture `
            -ResolvedInstallRoot $resolvedInstallRoot `
            -ResolvedVersion $resolvedVersion
        $registrations = @(Invoke-ClientRegistration -SelectedClient $resolvedClient -InstalledExecutable $installResult.installedExecutable -InstallBase $installResult.installBase)
        $state = Get-InstallerState
        Update-InstallerStateAfterInstall `
            -State $state `
            -ResolvedInstallRoot $installResult.installRoot `
            -ResolvedArchitecture $resolvedArchitecture `
            -ResolvedVersion $resolvedVersion `
            -InstalledExecutable $installResult.installedExecutable `
            -SelectedClient $resolvedClient `
            -Registration $registrations[0]
        $statePath = Save-InstallerState -State $state

        $result = [ordered]@{
            action = 'install'
            mode = $mode
            downloadSource = [string]$session.DownloadSource
            version = $Version
            resolvedVersion = $resolvedVersion
            architecture = $resolvedArchitecture
            client = $resolvedClient
            packageAssetName = [string]$session.PackageAssetName
            downloadUri = [string]$session.DownloadUri
            installRoot = $installResult.installRoot
            installedExecutable = $installResult.installedExecutable
            selectedClients = @($resolvedClient)
            statePath = $statePath
            reusedExistingBinary = [bool]$installResult.reusedExistingBinary
            registrations = @($registrations)
        }
    }
    finally {
        if ($session.CleanupSession) {
            Remove-PathIfExists -Path $session.SessionRoot
        }
    }
}

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
