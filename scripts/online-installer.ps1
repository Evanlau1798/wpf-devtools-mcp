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

function Get-SystemDefaultArchitecture {
    switch ([string]$env:PROCESSOR_ARCHITECTURE) {
        'ARM64' { return 'arm64' }
        'X86' { return 'x86' }
        default { return 'x64' }
    }
}

function Get-DefaultClient {
    if ($null -ne (Get-Command 'claude' -ErrorAction SilentlyContinue)) { return 'claude-code' }
    if ($null -ne (Get-Command 'codex' -ErrorAction SilentlyContinue)) { return 'codex' }
    if (Test-Path (Join-Path $env:APPDATA 'Code\User')) { return 'vscode' }
    return 'other'
}

function Resolve-InstallerStatePath {
    $stateRoot = Resolve-AbsoluteDirectory -Path (Join-Path $env:APPDATA 'WpfDevToolsMcp')
    return (Join-Path $stateRoot 'installer-state.json')
}

function Get-InstallerState {
    $statePath = Resolve-InstallerStatePath
    $state = [ordered]@{
        lastInstallRoot = $null
        architectures = [ordered]@{}
        registrations = [ordered]@{}
    }

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

function Remove-PathIfExists {
    param([string]$Path)

    if (-not [string]::IsNullOrWhiteSpace($Path) -and (Test-Path $Path)) {
        Remove-Item -Path $Path -Recurse -Force
    }
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
        $release = Invoke-RestMethod -Uri (Get-GitHubReleaseApiUri -ResolvedVersion $ResolvedVersion) -Headers @{ 'User-Agent' = 'wpf-devtools-online-installer' }
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
    if ((Test-Path (Join-Path $PSScriptRoot 'manifest.json')) -and
        (Get-ChildItem -Path $PSScriptRoot -Filter 'wpf-devtools-*.exe' -File -ErrorAction SilentlyContinue | Select-Object -First 1)) {
        return (Split-Path -Parent $PSScriptRoot)
    }

    if (Test-Path (Join-Path $PSScriptRoot 'bin\manifest.json')) {
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
        if (Test-Path $candidate) { return $candidate }
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
        if (Test-Path $candidate) { return $candidate }
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

function Backup-ConfigFile {
    param([Parameter(Mandatory)] [string]$Path)

    if (-not (Test-Path $Path)) { return $null }
    $backupPath = "$Path.bak-$(Get-Date -Format 'yyyyMMddHHmmssfff')"
    Copy-Item -Path $Path -Destination $backupPath -Force
    return $backupPath
}

function Get-ExistingConfigMap {
    param([Parameter(Mandatory)] [string]$Path)

    $map = [ordered]@{}
    if (-not (Test-Path $Path)) { return $map }

    $raw = Get-Content -Path $Path -Raw
    if ([string]::IsNullOrWhiteSpace($raw)) { return $map }

    $parsed = $raw | ConvertFrom-Json
    foreach ($property in $parsed.PSObject.Properties) {
        $map[$property.Name] = $property.Value
    }

    return $map
}

function Set-JsonConfigRegistration {
    param(
        [Parameter(Mandatory)] [string]$ClientName,
        [Parameter(Mandatory)] [string]$CollectionName,
        [Parameter(Mandatory)] [string]$ConfigPath,
        [Parameter(Mandatory)] [string]$InstalledExecutable
    )

    $directory = Split-Path -Parent $ConfigPath
    if (-not [string]::IsNullOrWhiteSpace($directory)) { New-Item -ItemType Directory -Force -Path $directory | Out-Null }

    $root = Get-ExistingConfigMap -Path $ConfigPath
    $servers = [ordered]@{}
    if ($root.Contains($CollectionName) -and $null -ne $root[$CollectionName]) {
        foreach ($property in $root[$CollectionName].PSObject.Properties) {
            $servers[$property.Name] = $property.Value
        }
    }

    $servers['wpf-devtools'] = [ordered]@{ command = $InstalledExecutable; args = @() }
    $root[$CollectionName] = $servers
    $backupPath = Backup-ConfigFile -Path $ConfigPath
    $root | ConvertTo-Json -Depth 10 | Set-Content -Path $ConfigPath -Encoding UTF8

    return [ordered]@{ client = $ClientName; mode = 'json-file'; target = $ConfigPath; backupPath = $backupPath; applied = $true }
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

function Invoke-ClientRegistration {
    param(
        [Parameter(Mandatory)] [string]$SelectedClient,
        [Parameter(Mandatory)] [string]$InstalledExecutable,
        [Parameter(Mandatory)] [string]$InstallBase
    )

    switch ($SelectedClient) {
        'vscode' { return @(Set-JsonConfigRegistration -ClientName $SelectedClient -CollectionName 'servers' -ConfigPath (Resolve-VsCodeConfigPath) -InstalledExecutable $InstalledExecutable) }
        'visual-studio' { return @(Set-JsonConfigRegistration -ClientName $SelectedClient -CollectionName 'servers' -ConfigPath (Resolve-VisualStudioConfigPath) -InstalledExecutable $InstalledExecutable) }
        'claude-desktop' { return @(Set-JsonConfigRegistration -ClientName $SelectedClient -CollectionName 'mcpServers' -ConfigPath (Resolve-ClaudeDesktopConfigPath) -InstalledExecutable $InstalledExecutable) }
        'other' { return @([ordered]@{ client = $SelectedClient; mode = 'artifact-only'; target = (Join-Path $InstallBase 'client-registration\other.mcpServers.json'); backupPath = $null; applied = $true }) }
        'claude-code' { return @([ordered]@{ client = $SelectedClient; mode = 'cli'; target = 'claude'; backupPath = $null; applied = $false }) }
        'codex' { return @([ordered]@{ client = $SelectedClient; mode = 'cli'; target = 'codex'; backupPath = $null; applied = $false }) }
    }
}

function New-ClientRegistrationArtifacts {
    param(
        [Parameter(Mandatory)] [string]$InstallBase,
        [Parameter(Mandatory)] [string]$InstalledExecutable
    )

    $registrationDir = Join-Path $InstallBase 'client-registration'
    New-Item -ItemType Directory -Force -Path $registrationDir | Out-Null

    ([ordered]@{ servers = [ordered]@{ 'wpf-devtools' = [ordered]@{ command = $InstalledExecutable; args = @() } } } |
        ConvertTo-Json -Depth 5) | Set-Content -Path (Join-Path $registrationDir 'vscode.json') -Encoding UTF8
    ([ordered]@{ mcpServers = [ordered]@{ 'wpf-devtools' = [ordered]@{ command = $InstalledExecutable; args = @() } } } |
        ConvertTo-Json -Depth 5) | Set-Content -Path (Join-Path $registrationDir 'other.mcpServers.json') -Encoding UTF8
}

function Install-PackagePayload {
    param(
        [Parameter(Mandatory)] [string]$PackageDirectory,
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

    $relativeExecutable = $packageExecutable.Substring($PackageDirectory.Length).TrimStart('\', '/')
    $installedExecutable = Join-Path $currentDir $relativeExecutable
    ([ordered]@{
            name = 'wpf-devtools'
            architecture = $ResolvedArchitecture
            version = $ResolvedVersion
            installDir = $currentDir
            installedUtc = [DateTime]::UtcNow.ToString('o')
            executable = $installedExecutable
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
        [Parameter(Mandatory)] [string]$SelectedClient
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
    }
}

function Show-InstallerWindow {
    if ($NonInteractive -or $OutputJson) { return $null }

    try {
        Add-Type -AssemblyName PresentationFramework
        Add-Type -AssemblyName PresentationCore
        Add-Type -AssemblyName WindowsBase

        $xaml = @'
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="WPF DevTools Installer" Width="560" Height="320" WindowStartupLocation="CenterScreen">
    <Grid Margin="16">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>
        <Grid.ColumnDefinitions>
            <ColumnDefinition Width="*"/>
            <ColumnDefinition Width="160"/>
        </Grid.ColumnDefinitions>
        <TextBlock Grid.Row="0" Grid.Column="0" FontSize="24" FontWeight="SemiBold" Text="WPF DevTools MCP Installer"/>
        <StackPanel Grid.Row="0" Grid.Column="1">
            <TextBlock Text="Architecture"/>
            <ComboBox x:Name="ArchitectureSelector"/>
        </StackPanel>
        <TextBlock Grid.Row="1" Grid.ColumnSpan="2" Margin="0,16,0,4" Text="Install root"/>
        <TextBox x:Name="InstallRootTextBox" Grid.Row="2" Grid.ColumnSpan="2" Height="30"/>
        <StackPanel Grid.Row="3" Grid.ColumnSpan="2" Orientation="Horizontal" HorizontalAlignment="Right" Margin="0,16,0,0">
            <Button x:Name="InstallButton" Width="100">Install</Button>
        </StackPanel>
    </Grid>
</Window>
'@
        $reader = New-Object System.Xml.XmlNodeReader ([xml]$xaml)
        [void][Windows.Markup.XamlReader]::Load($reader)
    }
    catch {
    }

    return $null
}

function Get-CliSelection {
    return [ordered]@{
        Action = $Action
        Architecture = if ([string]::IsNullOrWhiteSpace($Architecture)) { Get-SystemDefaultArchitecture } else { $Architecture }
        Client = if ([string]::IsNullOrWhiteSpace($Client)) { Get-DefaultClient } else { $Client }
        InstallRoot = Resolve-PreferredInstallRoot
    }
}

$interactiveSelection = Show-InstallerWindow
if ($null -eq $interactiveSelection -and -not $NonInteractive -and -not $OutputJson) {
    $interactiveSelection = Get-CliSelection
}

$resolvedAction = if ($null -ne $interactiveSelection) { $interactiveSelection.Action } else { $Action }
$resolvedArchitecture = if ($null -ne $interactiveSelection) { $interactiveSelection.Architecture } else { if ([string]::IsNullOrWhiteSpace($Architecture)) { Get-SystemDefaultArchitecture } else { $Architecture } }
$resolvedClient = if ($null -ne $interactiveSelection) { $interactiveSelection.Client } else { if ([string]::IsNullOrWhiteSpace($Client)) { Get-DefaultClient } else { $Client } }
$resolvedInstallRoot = if ($null -ne $interactiveSelection) { $interactiveSelection.InstallRoot } else { Resolve-PreferredInstallRoot }

if ($resolvedAction -eq 'uninstall') {
    $state = Get-InstallerState
    if ($state.registrations.Contains($resolvedClient)) { $state.registrations.Remove($resolvedClient) }
    Save-InstallerState -State $state | Out-Null
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
        installedExecutable = $null
        selectedClients = @($resolvedClient)
        statePath = Resolve-InstallerStatePath
        removedInstallation = $false
    }
}
else {
    $mode = Resolve-InstallerMode
    $session = Resolve-PackageSession -Mode $mode -ResolvedVersion $Version -ResolvedArchitecture $resolvedArchitecture

    try {
        $manifest = Get-Content -Path (Resolve-PackageManifestPath -PackageDirectory $session.PackageDirectory) -Raw | ConvertFrom-Json
        $resolvedVersion = if ([string]::IsNullOrWhiteSpace([string]$manifest.version)) { [string]$session.ResolvedVersion } else { [string]$manifest.version }
        $installResult = Install-PackagePayload -PackageDirectory $session.PackageDirectory -ResolvedArchitecture $resolvedArchitecture -ResolvedInstallRoot $resolvedInstallRoot -ResolvedVersion $resolvedVersion
        $registrations = @(Invoke-ClientRegistration -SelectedClient $resolvedClient -InstalledExecutable $installResult.installedExecutable -InstallBase $installResult.installBase)
        $state = Get-InstallerState
        Update-InstallerStateAfterInstall -State $state -ResolvedInstallRoot $installResult.installRoot -ResolvedArchitecture $resolvedArchitecture -ResolvedVersion $resolvedVersion -InstalledExecutable $installResult.installedExecutable -SelectedClient $resolvedClient
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
        if ($session.CleanupSession) { Remove-PathIfExists -Path $session.SessionRoot }
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
}
