param(
    [ValidateSet('install', 'uninstall', 'full-uninstall')]
    [string]$Action = 'install',

    [string]$Version = 'latest',

    [ValidateSet('x64', 'x86', 'arm64')]
    [string]$Architecture,

    [ValidateSet('claude-code', 'codex', 'cursor', 'vscode', 'visual-studio', 'claude-desktop', 'other')]
    [string]$Client,

    [string]$InstallRoot = (Join-Path $env:APPDATA 'WpfDevToolsMcp'),
    [string]$WorkingRoot = (Join-Path ([System.IO.Path]::GetTempPath()) 'wpf-devtools-online-installer'),
    [string]$PackageArchivePath,
    [string]$VsCodeConfigPath,
    [string]$VisualStudioConfigPath,
    [string]$ClaudeDesktopConfigPath,
    [string]$CursorConfigPath,
    [string]$CursorProjectRoot,
    [ValidateSet('global', 'project')]
    [string]$CursorMode,

    [switch]$NonInteractive,
    [switch]$Force,
    [switch]$OutputJson
)

$ErrorActionPreference = 'Stop'
$script:InstallRootWasSpecified = $PSBoundParameters.ContainsKey('InstallRoot')
$script:PackageArchivePathWasSpecified = $PSBoundParameters.ContainsKey('PackageArchivePath')
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

    $resolvedPath = Resolve-AbsolutePath -Path $Path
    New-Item -ItemType Directory -Force -Path $resolvedPath | Out-Null
    return $resolvedPath
}
function Resolve-AbsolutePath {
    param([Parameter(Mandatory)] [string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location).Path $Path))
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
    'scripts/installer/Installer.BootstrapUi.ps1'
    'scripts/installer/Tui.Terminal.ps1'
    'scripts/installer/Tui.Layout.ps1'
    'scripts/installer/Tui.ScreenModel.ps1'
    'scripts/installer/Tui.Sections.ps1'
    'scripts/installer/Tui.Window.ps1'
    'scripts/installer/Tui.TitleBar.ps1'
    'scripts/installer/Tui.StatusBar.ps1'
    'scripts/installer/Tui.Presenters.ps1'
    'scripts/installer/Tui.PathEditor.ps1'
    'scripts/installer/Tui.PathEditor.Views.ps1'
    'scripts/installer/Tui.Renderer.ps1'
    'scripts/installer/Tui.Input.ps1'
    'scripts/installer/Tui.Flow.ps1'
    'scripts/installer/Tui.Confirm.ps1'
    'scripts/installer/Installer.Discovery.ps1'
    'scripts/installer/Installer.Uninstall.ps1'
    'scripts/installer/Installer.Release.ps1'
    'scripts/installer/Installer.PackageIntegrity.ps1'
    'scripts/installer/Installer.State.ps1'
    'scripts/installer/Installer.Registration.ps1'
    'scripts/installer/Installer.Verification.ps1'
    'scripts/installer/Installer.Actions.ps1'
)
$script:InstallerHelperRepositoryRelativePath = 'scripts/installer'
# Shared installer modules own Resolve-InstallerStatePath, Save-InstallerState,
# installer-state.json handling, Get-AvailableInstallerUpdates, and the rest of
# the persistent state/update flow.
$script:InstallerSharedHelperLeafNames = @(
    'Installer.Uninstall.ps1'
    'Installer.Release.ps1'
    'Installer.PackageIntegrity.ps1'
    'Installer.State.ps1'
    'Installer.Registration.ps1'
    'Installer.Verification.ps1'
    'Installer.Actions.ps1'
)
$script:CursorClientConfigRelativePath = '.cursor\mcp.json'
$script:TuiScreenNames = @('HomeScreen', 'InstallScreen', 'UninstallScreen', 'ConfirmScreen', 'PathEditorScreen', 'DirectoryPickerScreen', 'FolderNamePromptScreen', 'ProgressScreen')
$script:TuiUiMarkers = @('Installed v', 'Update available', 'Architecture', 'Install location', 'Update All')
$script:TuiConfirmationModes = @('unregister', 'full-uninstall', 'close-app')
$script:TuiUninstallActions = @('UnregisterTarget', 'FullUninstall', 'Full Uninstall')
$script:InstallerDiscoveryContractFields = @('RegistrationMode', 'InstalledExecutable', 'InstallerOwned', 'ConfirmationStep')
$script:TuiNavigationKeys = @(
    [ConsoleKey]::UpArrow
    [ConsoleKey]::DownArrow
    [ConsoleKey]::LeftArrow
    [ConsoleKey]::RightArrow
    [ConsoleKey]::Tab
    [ConsoleKey]::Enter
    [ConsoleKey]::Escape
    [ConsoleKey]::Backspace
)
$script:TuiNavigationTokens = @('ConsoleKey.UpArrow', 'ConsoleKey.DownArrow', 'ConsoleKey.LeftArrow', 'ConsoleKey.RightArrow', 'ConsoleKey.Tab', 'ConsoleKey.Enter')
$script:ResolvedOnlineReleaseVersion = $null
$script:GitHubReleaseApiResponseCache = @{}
$script:GitHubReleaseChecksumRecordCache = @{}
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
function Find-InstallerHelperArchiveEntry {
    param(
        [Parameter(Mandatory)] $Archive,
        [Parameter(Mandatory)] [string]$LeafName
    )

    foreach ($candidatePath in @(
            "bin/installer/$LeafName"
            "installer/$LeafName"
        )) {
        $entry = $Archive.GetEntry($candidatePath)
        if ($null -ne $entry) {
            return $entry
        }
    }

    return $null
}
function Copy-InstallerHelperBundleFromArchive {
    param(
        [Parameter(Mandatory)] [string]$ArchivePath,
        [Parameter(Mandatory)] [string]$DestinationRoot,
        [Parameter(Mandatory)] [string[]]$HelperFiles
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $resolvedArchivePath = (Resolve-Path -LiteralPath $ArchivePath).Path
    $archive = [System.IO.Compression.ZipFile]::OpenRead($resolvedArchivePath)
    try {
        foreach ($leafName in @($script:InstallerHelperManifestFileName) + @($HelperFiles)) {
            $entry = Find-InstallerHelperArchiveEntry -Archive $archive -LeafName $leafName
            if ($null -eq $entry) {
                throw "Installer helper archive entry was not found: $leafName"
            }

            $destinationPath = Join-Path $DestinationRoot $leafName
            $destinationDirectory = Split-Path -Parent $destinationPath
            if (-not [string]::IsNullOrWhiteSpace($destinationDirectory)) {
                New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null
            }

            $entryStream = $entry.Open()
            $fileStream = [System.IO.File]::Open($destinationPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
            try {
                $entryStream.CopyTo($fileStream)
            }
            finally {
                $fileStream.Dispose()
                $entryStream.Dispose()
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}
function Test-PackageArchiveRequested {
    return $script:PackageArchivePathWasSpecified -and -not [string]::IsNullOrWhiteSpace([string]$PackageArchivePath)
}
function Add-InstallerHelperRootCandidate {
    param(
        [System.Collections.Generic.List[string]]$Roots,
        [string]$CandidateRoot
    )

    if ([string]::IsNullOrWhiteSpace($CandidateRoot)) {
        return
    }

    if (-not $Roots.Contains($CandidateRoot)) {
        $Roots.Add($CandidateRoot)
    }
}
function Get-LocalInstallerHelperRoots {
    $candidateRoots = New-Object System.Collections.Generic.List[string]

    $localScriptRoot = Resolve-InstallerScriptRoot
    if (-not [string]::IsNullOrWhiteSpace($localScriptRoot)) {
        Add-InstallerHelperRootCandidate -Roots $candidateRoots -CandidateRoot (Join-Path $localScriptRoot 'installer')
    }

    $overrideDirectory = Get-TuiHelperOverrideDirectory
    Add-InstallerHelperRootCandidate -Roots $candidateRoots -CandidateRoot $overrideDirectory

    if ($Action -ne 'install') {
        foreach ($helperRoot in @(Get-InstalledInstallerHelperRoots)) {
            Add-InstallerHelperRootCandidate -Roots $candidateRoots -CandidateRoot $helperRoot
        }
    }

    $workspaceHelperRoot = Join-Path (Get-Location).Path 'scripts\installer'
    Add-InstallerHelperRootCandidate -Roots $candidateRoots -CandidateRoot $workspaceHelperRoot

    return @($candidateRoots)
}
function Get-StandaloneInstallerStateSnapshot {
    $statePath = Join-Path (Resolve-AbsolutePath -Path (Join-Path $env:APPDATA 'WpfDevToolsMcp')) 'installer-state.json'
    if (-not (Test-Path $statePath)) {
        return $null
    }

    try {
        return (Get-Content -Path $statePath -Raw | ConvertFrom-Json)
    }
    catch {
        return $null
    }
}
function Add-InstalledInstallerHelperRoot {
    param(
        [System.Collections.Generic.List[string]]$Roots,
        [string]$InstallRoot,
        [string]$Architecture,
        [string]$InstalledExecutable
    )

    if (-not [string]::IsNullOrWhiteSpace($InstalledExecutable)) {
        $binRoot = Split-Path -Parent $InstalledExecutable
        if (-not [string]::IsNullOrWhiteSpace($binRoot)) {
            Add-InstallerHelperRootCandidate -Roots $Roots -CandidateRoot (Join-Path $binRoot 'installer')
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($InstallRoot) -and -not [string]::IsNullOrWhiteSpace($Architecture)) {
        Add-InstallerHelperRootCandidate -Roots $Roots -CandidateRoot (Join-Path (Join-Path $InstallRoot "$Architecture\current\bin") 'installer')
    }
}
function Get-InstalledInstallerHelperRoots {
    $helperRoots = New-Object System.Collections.Generic.List[string]
    $resolvedArchitecture = if ([string]::IsNullOrWhiteSpace($Architecture)) { Get-SystemDefaultArchitecture } else { [string]$Architecture }
    Add-InstalledInstallerHelperRoot -Roots $helperRoots -InstallRoot $InstallRoot -Architecture $resolvedArchitecture -InstalledExecutable $null

    $state = Get-StandaloneInstallerStateSnapshot
    if ($null -eq $state) {
        return @($helperRoots)
    }

    if ($null -ne $state.architectures) {
        foreach ($property in $state.architectures.PSObject.Properties) {
            Add-InstalledInstallerHelperRoot `
                -Roots $helperRoots `
                -InstallRoot ([string]$property.Value.installRoot) `
                -Architecture ([string]$property.Name) `
                -InstalledExecutable ([string]$property.Value.executable)
        }
    }

    if ($null -ne $state.registrations) {
        foreach ($property in $state.registrations.PSObject.Properties) {
            Add-InstalledInstallerHelperRoot `
                -Roots $helperRoots `
                -InstallRoot ([string]$property.Value.installRoot) `
                -Architecture ([string]$property.Value.architecture) `
                -InstalledExecutable ([string]$property.Value.installedExecutable)
        }
    }

    return @($helperRoots)
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
function Resolve-InstallerBootstrapUiPath {
    if ($null -ne (Get-Command Write-TuiBootstrapScreen -ErrorAction SilentlyContinue)) {
        return $null
    }

    foreach ($candidateRoot in @(Get-LocalInstallerHelperRoots)) {
        if ([string]::IsNullOrWhiteSpace($candidateRoot) -or -not (Test-Path $candidateRoot)) {
            continue
        }

        $bootstrapHelperPath = Join-Path $candidateRoot 'Installer.BootstrapUi.ps1'
        if (Test-Path $bootstrapHelperPath) {
            return $bootstrapHelperPath
        }
    }

    return $null
}

$script:InstallerBootstrapUiPath = Resolve-InstallerBootstrapUiPath
if (-not [string]::IsNullOrWhiteSpace($script:InstallerBootstrapUiPath)) {
    . $script:InstallerBootstrapUiPath
}
if ($null -eq (Get-Command Enter-TuiBootstrapTerminalSession -ErrorAction SilentlyContinue)) {
    function Enter-TuiBootstrapTerminalSession { return $null }
}
if ($null -eq (Get-Command Exit-TuiBootstrapTerminalSession -ErrorAction SilentlyContinue)) {
    function Exit-TuiBootstrapTerminalSession { param($Session) }
}
if ($null -eq (Get-Command Close-TuiBootstrapScreen -ErrorAction SilentlyContinue)) {
    function Close-TuiBootstrapScreen { }
}
if ($null -eq (Get-Command Write-TuiBootstrapScreen -ErrorAction SilentlyContinue)) {
    function Write-TuiBootstrapScreen {
        param([Parameter(Mandatory)] [AllowEmptyString()] [string]$Message)

        if ([string]::IsNullOrWhiteSpace($Message)) {
            return ''
        }

        return $Message
    }
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
    param([switch]$SuppressBootstrapOutput)

    if ($null -ne $script:TuiHelperManifest) {
        return $script:TuiHelperManifest
    }

    foreach ($candidateRoot in @(Get-LocalInstallerHelperRoots)) {
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
    $downloadBaseUri = Resolve-TuiHelperDownloadBaseUri

    $manifestUri = "$downloadBaseUri/$($script:InstallerHelperManifestFileName)"
    $temporaryManifestPath = "$manifestPath.download"
    try {
        if (-not $SuppressBootstrapOutput) {
            Write-TuiBootstrapScreen 'Preparing installer UI... (manifest)' | Out-Host
        }
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
    param([switch]$SuppressBootstrapOutput)

    if (-not [string]::IsNullOrWhiteSpace($script:TuiHelperResolvedRoot)) {
        return $script:TuiHelperResolvedRoot
    }

    $manifest = Get-TuiHelperManifest -SuppressBootstrapOutput:$SuppressBootstrapOutput
    $helperFiles = if ($null -ne $manifest -and $manifest.HelperFiles.Count -gt 0) { @($manifest.HelperFiles) } else { @(Get-HelperLeafNames) }
    foreach ($candidateRoot in @(Get-LocalInstallerHelperRoots)) {
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

    if (Resolve-InstallerMode -eq 'offline' -and (Test-PackageArchiveRequested)) {
        $runtimeRoot = Get-TuiHelperRuntimeRoot
        $helperFiles = if ($null -ne $manifest -and $manifest.HelperFiles.Count -gt 0) { @($manifest.HelperFiles) } else { @(Get-HelperLeafNames) }
        $archivePath = [string]$PackageArchivePath

        if ([string]::IsNullOrWhiteSpace($archivePath)) {
            return $null
        }

        Remove-PathIfExists -Path $runtimeRoot
        New-Item -ItemType Directory -Force -Path $runtimeRoot | Out-Null
        Copy-InstallerHelperBundleFromArchive -ArchivePath $archivePath -DestinationRoot $runtimeRoot -HelperFiles $helperFiles

        $manifestPath = Get-TuiHelperManifestPath -RootPath $runtimeRoot
        $manifest = Read-TuiHelperManifest -ManifestPath $manifestPath -HelperDirectory $runtimeRoot
        if ($null -eq $manifest) {
            $manifest = [ordered]@{
                CacheKey = (Get-ComputedInstallerHelperCacheKey -HelperDirectory $runtimeRoot -HelperFiles $helperFiles)
                HelperFiles = $helperFiles
            }
        }

        $script:TuiHelperManifest = $manifest
        $script:TuiHelperResolvedRoot = $runtimeRoot
        return $runtimeRoot
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

    $downloadBaseUri = Resolve-TuiHelperDownloadBaseUri

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
        if (-not $SuppressBootstrapOutput) {
            Write-TuiBootstrapScreen "Preparing installer UI... ($downloadIndex/$totalHelperCount)" | Out-Host
        }
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
function Get-InstallerSharedModulePaths {
    $helperRoot = Ensure-TuiHelpersAvailable -SuppressBootstrapOutput
    if ([string]::IsNullOrWhiteSpace($helperRoot)) {
        throw 'Shared installer helper scripts are unavailable in the current execution context.'
    }

    $helperPaths = New-Object System.Collections.Generic.List[string]
    foreach ($helperFile in $script:InstallerSharedHelperLeafNames) {
        $helperPath = Join-Path $helperRoot $helperFile
        if (-not (Test-Path $helperPath)) {
            throw "Shared installer helper script was not found: $helperPath"
        }

        $helperPaths.Add($helperPath)
    }

    return @($helperPaths)
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
        [pscustomobject]@{ Id = 'codex'; Label = 'Codex/Codex CLI'; ConfigType = 'cli' }
        [pscustomobject]@{ Id = 'cursor'; Label = 'Cursor'; ConfigType = 'json-file' }
        [pscustomobject]@{ Id = 'vscode'; Label = 'VS Code'; ConfigType = 'json-file' }
        [pscustomobject]@{ Id = 'visual-studio'; Label = 'Visual Studio'; ConfigType = 'json-file' }
        [pscustomobject]@{ Id = 'claude-desktop'; Label = 'Claude Desktop'; ConfigType = 'json-file' }
        [pscustomobject]@{ Id = 'other'; Label = 'Other'; ConfigType = 'artifact-only' }
    )
}
function Resolve-ClientBaseId {
    param([Parameter(Mandatory)] [string]$ClientId)

    if ($ClientId -like 'cursor-*') {
        return 'cursor'
    }

    return $ClientId
}
function Resolve-ClientStateKey {
    param(
        [Parameter(Mandatory)] [string]$ClientId,
        [string]$RegistrationMode
    )

    if ((Resolve-ClientBaseId -ClientId $ClientId) -ne 'cursor') {
        return $ClientId
    }

    if ($ClientId -in @('cursor-global', 'cursor-project')) {
        return $ClientId
    }

    switch ([string]$RegistrationMode) {
        'cursor-project' { return 'cursor-project' }
        default { return 'cursor-global' }
    }
}
function Resolve-ClientLabel {
    param([Parameter(Mandatory)] [string]$ClientId)

    switch ($ClientId) {
        'cursor-global' { return 'Cursor (Global)' }
        'cursor-project' { return 'Cursor (Project)' }
    }

    $client = Get-SupportedClients | Where-Object { $_.Id -eq (Resolve-ClientBaseId -ClientId $ClientId) } | Select-Object -First 1
    if ($null -ne $client) {
        return [string]$client.Label
    }

    return $ClientId
}
function Get-DefaultClient {
    if ($null -ne (Get-Command 'claude' -ErrorAction SilentlyContinue)) { return 'claude-code' }
    if ($null -ne (Get-Command 'codex' -ErrorAction SilentlyContinue)) { return 'codex' }
    if ($null -ne (Get-Command 'cursor-agent' -ErrorAction SilentlyContinue)) { return 'cursor' }
    if (Test-Path (Join-Path $env:USERPROFILE '.cursor')) { return 'cursor' }
    if (Test-Path (Join-Path $env:APPDATA 'Code\User')) { return 'vscode' }
    if (Test-Path (Join-Path $env:USERPROFILE '.mcp.json')) { return 'visual-studio' }
    return 'other'
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

    $tag = if ($ResolvedVersion.StartsWith('v')) { $ResolvedVersion } else { "v$ResolvedVersion" }
    return "https://github.com/Evanlau1798/wpf-devtools-mcp/releases/download/$tag/$assetName"
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
function Get-GitHubTagRef {
    param([Parameter(Mandatory)] [string]$ResolvedVersion)

    $tagVersion = Resolve-RequestedReleaseVersion -RequestedVersion $ResolvedVersion
    if ([string]::IsNullOrWhiteSpace($tagVersion)) {
        throw 'Failed to resolve the installer helper release version.'
    }

    if ($tagVersion.StartsWith('v')) {
        return $tagVersion
    }

    return "v$tagVersion"
}
function Get-ReleaseRawContentBaseUri {
    param(
        [Parameter(Mandatory)] [string]$ResolvedVersion,
        [Parameter(Mandatory)] [string]$RepositoryRelativePath
    )

    $tagRef = Get-GitHubTagRef -ResolvedVersion $ResolvedVersion
    $normalizedPath = $RepositoryRelativePath.Trim([char[]]@('\', '/')).Replace('\', '/')
    return "https://raw.githubusercontent.com/Evanlau1798/wpf-devtools-mcp/$tagRef/$normalizedPath"
}
function Resolve-TuiHelperDownloadBaseUri {
    if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_HELPER_BASE_URI)) {
        return $env:WPFDEVTOOLS_INSTALLER_HELPER_BASE_URI.TrimEnd('/')
    }

    $resolvedVersion = Resolve-RequestedReleaseVersion -RequestedVersion $Version
    return (Get-ReleaseRawContentBaseUri -ResolvedVersion $resolvedVersion -RepositoryRelativePath $script:InstallerHelperRepositoryRelativePath)
}
function Get-GitHubReleaseApiResponse {
    param([Parameter(Mandatory)] [string]$ResolvedVersion)

    $cacheKey = [string]$ResolvedVersion
    if ($script:GitHubReleaseApiResponseCache.Contains($cacheKey)) {
        return $script:GitHubReleaseApiResponseCache[$cacheKey]
    }

    try {
        $release = Invoke-RestMethod -Uri (Get-GitHubReleaseApiUri -ResolvedVersion $ResolvedVersion) -Headers @{ 'User-Agent' = 'wpf-devtools-online-installer' } -TimeoutSec 15
        $script:GitHubReleaseApiResponseCache[$cacheKey] = $release
        return $release
    }
    catch {
        return $null
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
    param([switch]$CreateRoot)

    $stateRoot = Resolve-AbsolutePath -Path (Join-Path $env:APPDATA 'WpfDevToolsMcp')
    if ($CreateRoot) {
        New-Item -ItemType Directory -Force -Path $stateRoot | Out-Null
    }

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

    $cachePath = Resolve-LatestVersionCachePath -CreateRoot
    [ordered]@{
        version = $VersionValue
        refreshedUtc = [DateTime]::UtcNow.ToString('o')
    } | ConvertTo-Json -Depth 3 | Set-Content -Path $cachePath -Encoding UTF8
}
function Resolve-RequestedReleaseVersion {
    param([Parameter(Mandatory)] [string]$RequestedVersion)

    if ($RequestedVersion -ne 'latest') {
        return $RequestedVersion
    }

    if (-not [string]::IsNullOrWhiteSpace($script:ResolvedOnlineReleaseVersion)) {
        return $script:ResolvedOnlineReleaseVersion
    }

    $script:ResolvedOnlineReleaseVersion = Get-LatestInstallerVersion
    if ([string]::IsNullOrWhiteSpace($script:ResolvedOnlineReleaseVersion)) {
        throw 'Failed to resolve the latest WPF DevTools release version.'
    }

    return $script:ResolvedOnlineReleaseVersion
}
function ConvertTo-PowerShellEncodedCommand {
    param([Parameter(Mandatory)] [string]$CommandText)

    return [Convert]::ToBase64String([System.Text.Encoding]::Unicode.GetBytes($CommandText))
}
function Resolve-InstallerMode {
    if (Test-PackageArchiveRequested) { return 'offline' }
    if (-not [string]::IsNullOrWhiteSpace((Resolve-LocalPackageRoot))) { return 'offline' }
    return 'online'
}
function Get-ReleaseAssetDownloadDetails {
    param(
        [Parameter(Mandatory)] [string]$ResolvedVersion,
        [Parameter(Mandatory)] [string]$ResolvedArchitecture
    )

    $assetName = Get-ReleaseAssetName -ResolvedVersion $ResolvedVersion -ResolvedArchitecture $ResolvedArchitecture
    $fallbackUri = Get-ReleaseDownloadUri -ResolvedVersion $ResolvedVersion -ResolvedArchitecture $ResolvedArchitecture

    $release = Get-GitHubReleaseApiResponse -ResolvedVersion $ResolvedVersion
    if ($null -ne $release) {
        $asset = @($release.assets) | Where-Object { $_.name -eq $assetName } | Select-Object -First 1
        if ($null -ne $asset) {
            return [ordered]@{
                AssetName = $assetName
                DownloadUri = [string]$asset.browser_download_url
                ResolvedVersion = ([string]$release.tag_name).TrimStart('v')
            }
        }
    }

    return [ordered]@{
        AssetName = $assetName
        DownloadUri = $fallbackUri
        ResolvedVersion = $ResolvedVersion
    }
}
function Resolve-PackageSession {
    param(
        [Parameter(Mandatory)] [string]$Mode,
        [Parameter(Mandatory)] [string]$ResolvedVersion,
        [Parameter(Mandatory)] [string]$ResolvedArchitecture
    )

    foreach ($helperPath in @(Get-InstallerSharedModulePaths)) {
        . $helperPath
    }

    $workingRootPath = Resolve-AbsoluteDirectory -Path $WorkingRoot
    $sessionRoot = Join-Path $workingRootPath ([Guid]::NewGuid().ToString('N'))
    $extractRoot = Join-Path $sessionRoot 'package'

    if ($Mode -eq 'offline' -and (Test-PackageArchiveRequested)) {
        $archivePath = (Resolve-Path $PackageArchivePath).Path
        $integrity = Assert-ArchiveIntegrity -ArchivePath $archivePath -DownloadSource 'local-package' -ResolvedVersion $ResolvedVersion -ResolvedArchitecture $ResolvedArchitecture
        New-Item -ItemType Directory -Force -Path $extractRoot | Out-Null
        Expand-Archive -Path $archivePath -DestinationPath $extractRoot -Force
        return [ordered]@{
            PackageDirectory = $extractRoot
            SessionRoot = $sessionRoot
            CleanupSession = $true
            DownloadSource = 'local-package'
            DownloadUri = [string]$integrity.DownloadUri
            PackageAssetName = [string]$integrity.PackageAssetName
            ResolvedVersion = [string]$integrity.ResolvedVersion
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

    $downloadVersion = Resolve-RequestedReleaseVersion -RequestedVersion $ResolvedVersion
    $downloadDetails = Get-ReleaseAssetDownloadDetails -ResolvedVersion $downloadVersion -ResolvedArchitecture $ResolvedArchitecture
    $archivePath = Join-Path $workingRootPath ([string]$downloadDetails.AssetName)
    Invoke-WebRequest -Uri ([string]$downloadDetails.DownloadUri) -OutFile $archivePath
    $integrity = Assert-ArchiveIntegrity -ArchivePath $archivePath -DownloadSource 'github-release' -ResolvedVersion ([string]$downloadDetails.ResolvedVersion) -ResolvedArchitecture $ResolvedArchitecture
    New-Item -ItemType Directory -Force -Path $extractRoot | Out-Null
    Expand-Archive -Path $archivePath -DestinationPath $extractRoot -Force
    return [ordered]@{
        PackageDirectory = $extractRoot
        SessionRoot = $sessionRoot
        CleanupSession = $true
        DownloadSource = 'github-release'
        DownloadUri = if (-not [string]::IsNullOrWhiteSpace([string]$integrity.DownloadUri)) { [string]$integrity.DownloadUri } else { [string]$downloadDetails.DownloadUri }
        PackageAssetName = if (-not [string]::IsNullOrWhiteSpace([string]$integrity.PackageAssetName)) { [string]$integrity.PackageAssetName } else { [string]$downloadDetails.AssetName }
        ResolvedVersion = if (-not [string]::IsNullOrWhiteSpace([string]$integrity.ResolvedVersion)) { [string]$integrity.ResolvedVersion } else { [string]$downloadDetails.ResolvedVersion }
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
    $defaultInstallRoot = $InstallRoot
    try {
        foreach ($helperPath in @(Get-InstallerSharedModulePaths)) {
            . $helperPath
        }

        $defaultInstallRoot = Resolve-PreferredInstallRoot
    }
    catch {
    }

    $defaultAction = $Action
    $defaultArchitecture = if ([string]::IsNullOrWhiteSpace($Architecture)) { Get-SystemDefaultArchitecture } else { $Architecture }
    $defaultClient = if ([string]::IsNullOrWhiteSpace($Client)) { Get-DefaultClient } else { $Client }

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
    $resolvedClient = Read-ValidatedChoice -Prompt 'Client (claude-code/codex/cursor/vscode/visual-studio/claude-desktop/other)' -DefaultValue $defaultClient -AllowedValues @('claude-code', 'codex', 'cursor', 'vscode', 'visual-studio', 'claude-desktop', 'other')
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
function Start-LatestInstallerVersionRefresh {
    if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_TEST_REMOTE_LATEST_VERSION)) {
        return [ordered]@{
            Mode = 'test'
            Version = $env:WPFDEVTOOLS_INSTALLER_TEST_REMOTE_LATEST_VERSION
        }
    }

    $refreshDirectory = Resolve-AbsoluteDirectory -Path (Join-Path $env:TEMP 'wpf-devtools-online-installer\latest-version-refresh')
    $refreshOutputPath = Join-Path $refreshDirectory ("latest-version-" + [Guid]::NewGuid().ToString('N') + '.json')
    $releaseApiUri = Get-GitHubReleaseApiUri -ResolvedVersion 'latest'
    $encodedCommand = ConvertTo-PowerShellEncodedCommand -CommandText @"
\$ProgressPreference = 'SilentlyContinue'
try {
    \$latestVersion = [string](Invoke-RestMethod -Uri '$releaseApiUri' -Headers @{ 'User-Agent' = 'wpf-devtools-online-installer' } -TimeoutSec 10).tag_name.TrimStart('v')
    if (-not [string]::IsNullOrWhiteSpace(\$latestVersion)) {
        [ordered]@{ version = \$latestVersion } | ConvertTo-Json -Depth 3 | Set-Content -Path '$refreshOutputPath' -Encoding UTF8
    }
}
catch {
}
"@

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = New-Object System.Diagnostics.ProcessStartInfo
    $process.StartInfo.FileName = (Get-Process -Id $PID).Path
    $process.StartInfo.Arguments = "-NoProfile -ExecutionPolicy Bypass -EncodedCommand $encodedCommand"
    $process.StartInfo.UseShellExecute = $false
    $process.StartInfo.RedirectStandardOutput = $false
    $process.StartInfo.RedirectStandardError = $false
    $process.StartInfo.CreateNoWindow = $true
    $null = $process.Start()

    return [ordered]@{
        Mode = 'process'
        Process = $process
        OutputPath = $refreshOutputPath
    }
}
function Receive-LatestInstallerVersionRefresh {
    param([Parameter(Mandatory)] $RefreshHandle)

    if ([string]$RefreshHandle.Mode -eq 'test') {
        return [ordered]@{
            IsCompleted = $true
            Version = [string]$RefreshHandle.Version
        }
    }

    $process = $RefreshHandle.Process
    if ($null -eq $process) {
        return [ordered]@{
            IsCompleted = $true
            Version = $null
        }
    }

    if (-not $process.HasExited) {
        return [ordered]@{
            IsCompleted = $false
            Version = $null
        }
    }

    $resolvedVersion = $null
    if (Test-Path ([string]$RefreshHandle.OutputPath)) {
        try {
            $parsed = Get-Content -Path ([string]$RefreshHandle.OutputPath) -Raw | ConvertFrom-Json
            $resolvedVersion = [string]$parsed.version
        }
        catch {
        }

        Remove-PathIfExists -Path ([string]$RefreshHandle.OutputPath)
    }

    try {
        $process.Dispose()
    }
    catch {
    }

    if (-not [string]::IsNullOrWhiteSpace($resolvedVersion)) {
        Save-LatestInstallerVersionCache -VersionValue $resolvedVersion
    }

    return [ordered]@{
        IsCompleted = $true
        Version = $resolvedVersion
    }
}
function Stop-LatestInstallerVersionRefresh {
    param($RefreshHandle)

    if ($null -eq $RefreshHandle) {
        return
    }

    if ([string]$RefreshHandle.Mode -eq 'test') {
        return
    }

    $process = $RefreshHandle.Process
    if ($null -ne $process) {
        if (-not $process.HasExited) {
            try {
                $process.Kill($true)
            }
            catch {
                try {
                    $process.Kill()
                }
                catch {
                }
            }

            try {
                $null = $process.WaitForExit(250)
            }
            catch {
            }
        }

        try {
            $process.Dispose()
        }
        catch {
        }
    }

    Remove-PathIfExists -Path ([string]$RefreshHandle.OutputPath)
}
function Test-TuiSupport {
    if ($NonInteractive -or $OutputJson) {
        Close-TuiBootstrapScreen
        return $false
    }

    $script:LastTuiBootstrapMessage = $null
    $script:LastTuiBootstrapFailureReason = $null
    try {
        Write-TuiBootstrapScreen 'Preparing installer UI...' | Out-Host
        $null = Ensure-TuiHelpersAvailable
    }
    catch {
        $script:LastTuiBootstrapFailureReason = $_.Exception.Message
        Write-TuiBootstrapScreen 'Preparing installer UI... (fallback)' | Out-Host
        Close-TuiBootstrapScreen
        Write-InstallerMessage 'Installer UI bootstrap failed. Falling back to plain CLI.'
        return $false
    }

    if ([string]::IsNullOrWhiteSpace($script:TuiHelperResolvedRoot)) {
        Close-TuiBootstrapScreen
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

    if ($NonInteractive -or $OutputJson) {
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
    foreach ($helperPath in @(Get-InstallerSharedModulePaths)) {
        . $helperPath
    }
    return (Invoke-InstallerActionCore `
            -ResolvedAction $ResolvedAction `
            -ResolvedArchitecture $ResolvedArchitecture `
            -ResolvedClient $ResolvedClient `
            -ResolvedInstallRoot $ResolvedInstallRoot `
            -RequestedVersion $RequestedVersion `
            -UseLatestRelease:$UseLatestRelease)
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

    $global:WpfDevToolsInstallerBootstrapSession = $script:TuiBootstrapTerminalSession
    try {
        return (Invoke-WithTuiHelpers -ScriptBlock { Start-TuiInstallerCore `
                -DefaultAction $DefaultAction `
                -DefaultArchitecture $DefaultArchitecture `
                -DefaultClient $DefaultClient `
                -DefaultInstallRoot $DefaultInstallRoot `
                -InstallerState $InstallerState `
                -VersionHint $VersionHint `
                -LatestVersion $LatestVersion })
    }
    finally {
        if ($null -ne $global:WpfDevToolsInstallerBootstrapSession) {
            $script:TuiBootstrapTerminalSession = $global:WpfDevToolsInstallerBootstrapSession
            Close-TuiBootstrapScreen
        }

        $script:TuiBootstrapTerminalSession = $null
        Remove-Variable -Name WpfDevToolsInstallerBootstrapSession -Scope Global -ErrorAction SilentlyContinue
    }
}
function Resolve-Selection {
    $defaultArchitecture = if ([string]::IsNullOrWhiteSpace($Architecture)) { Get-SystemDefaultArchitecture } else { $Architecture }
    $defaultClient = if ([string]::IsNullOrWhiteSpace($Client)) { Get-DefaultClient } else { $Client }

    if (Test-TuiSupport) {
        foreach ($helperPath in @(Get-InstallerSharedModulePaths)) {
            . $helperPath
        }

        $installerState = Get-InstallerState
        $defaultInstallRoot = Resolve-PreferredInstallRoot
        $mode = Resolve-InstallerMode
        $versionHint = Get-OfflineVersionHint -Mode $mode
        $latestVersion = Get-LatestInstallerVersion -UseCacheOnly
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

    Close-TuiBootstrapScreen
    $mode = Resolve-InstallerMode
    $versionHint = $null
    try {
        foreach ($helperPath in @(Get-InstallerSharedModulePaths)) {
            . $helperPath
        }

        $versionHint = Get-OfflineVersionHint -Mode $mode
    }
    catch {
    }

    return [ordered]@{
        Cancelled = $false
        Selection = (Get-CliSelection)
        VersionHint = $versionHint
        HandledInWindow = $false
    }
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
