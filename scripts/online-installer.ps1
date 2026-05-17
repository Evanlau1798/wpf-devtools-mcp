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
    [string]$TrustedReleaseMetadataDirectory,
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
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
$script:WpfDevToolsInstallerTestModeEnabled = [bool]$script:WpfDevToolsInstallerTestModeEnabled -and [bool]$script:WpfDevToolsInstallerTestModeHarnessEnabled
$script:InstallRootWasSpecified = $PSBoundParameters.ContainsKey('InstallRoot')
$script:PackageArchivePathWasSpecified = $PSBoundParameters.ContainsKey('PackageArchivePath')
$script:TrustedReleaseMetadataDirectoryWasSpecified = $PSBoundParameters.ContainsKey('TrustedReleaseMetadataDirectory')
$script:InstallerTestResponses = New-Object System.Collections.Generic.Queue[string]

if ($script:TrustedReleaseMetadataDirectoryWasSpecified) {
    if ([string]::IsNullOrWhiteSpace($TrustedReleaseMetadataDirectory)) {
        throw 'TrustedReleaseMetadataDirectory must not be empty when specified.'
    }

    $env:WPFDEVTOOLS_TRUSTED_RELEASE_METADATA_DIRECTORY = [string]$TrustedReleaseMetadataDirectory
}
else {
    Remove-Item Env:WPFDEVTOOLS_TRUSTED_RELEASE_METADATA_DIRECTORY -ErrorAction SilentlyContinue
}

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

    if ($script:WpfDevToolsInstallerTestModeEnabled -and
        -not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_TEST_TUI_KEYS) -and
        -not [string]::IsNullOrWhiteSpace($DefaultValue)) {
        return $DefaultValue
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

    $resolvedPath = Assert-InstallerLocalPathTrusted -Path $Path
    New-Item -ItemType Directory -Force -Path $resolvedPath | Out-Null
    Assert-InstallerLocalPathTrusted -Path $resolvedPath | Out-Null
    return $resolvedPath
}
function Resolve-AbsolutePath {
    param([Parameter(Mandatory)] [string]$Path)

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location).Path $Path))
}
function Test-InstallerUncOrDevicePath {
    param([Parameter(Mandatory)] [string]$Path)

    return $Path.StartsWith('\\', [System.StringComparison]::Ordinal) -or
        $Path.StartsWith('\\?\', [System.StringComparison]::OrdinalIgnoreCase) -or
        $Path.StartsWith('\\.\', [System.StringComparison]::OrdinalIgnoreCase)
}
function Get-InstallerHardLinkCount {
    param([Parameter(Mandatory)] [string]$Path)

    if ($PSVersionTable.PSVersion.Major -lt 5) {
        return 1
    }

    if (-not ('WpfDevToolsInstallerFileIdentity' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

public static class WpfDevToolsInstallerFileIdentity
{
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint FILE_SHARE_DELETE = 0x00000004;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateFileW(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out BY_HANDLE_FILE_INFORMATION fileInformation);

    [StructLayout(LayoutKind.Sequential)]
    private struct BY_HANDLE_FILE_INFORMATION
    {
        public uint FileAttributes;
        public System.Runtime.InteropServices.ComTypes.FILETIME CreationTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastAccessTime;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }

    public static uint GetHardLinkCount(string path)
    {
        using (SafeFileHandle handle = CreateFileW(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero))
        {
            if (handle.IsInvalid)
            {
                throw new IOException("Failed to open installer path for identity validation.", Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error()));
            }

            BY_HANDLE_FILE_INFORMATION fileInformation;
            if (!GetFileInformationByHandle(handle, out fileInformation))
            {
                throw new IOException("Failed to read installer path identity metadata.", Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error()));
            }

            return fileInformation.NumberOfLinks;
        }
    }
}
'@
    }

    return [WpfDevToolsInstallerFileIdentity]::GetHardLinkCount($Path)
}
function Assert-InstallerLocalPathTrusted {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [switch]$RejectHardLinks
    )

    $resolvedPath = Resolve-AbsolutePath -Path $Path
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

        if ($RejectHardLinks -and -not $item.PSIsContainer) {
            $hardLinkCount = Get-InstallerHardLinkCount -Path $currentPath
            if ($hardLinkCount -gt 1) {
                throw "Installer path '$resolvedPath' is blocked because '$currentPath' has multiple hard links."
            }
        }
    }

    return $resolvedPath
}
function Remove-PathIfExists {
    param(
        [string]$Path,
        [switch]$BestEffort,
        [int]$RetryCount = 3,
        [int]$RetryDelayMilliseconds = 200
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return
    }

    $resolvedPath = Assert-InstallerLocalPathTrusted -Path $Path
    if (-not (Test-Path -LiteralPath $resolvedPath)) {
        return
    }

    $attempts = [Math]::Max(1, $RetryCount)
    for ($attempt = 1; $attempt -le $attempts; $attempt++) {
        try {
            Assert-InstallerLocalPathTrusted -Path $resolvedPath | Out-Null
            Remove-Item -LiteralPath $resolvedPath -Recurse -Force
            return
        }
        catch {
            if ($attempt -ge $attempts) {
                if ($BestEffort) {
                    return
                }

                throw
            }

            Start-Sleep -Milliseconds $RetryDelayMilliseconds
        }
    }
}
function ConvertTo-SingleQuotedPowerShellLiteral {
    param([AllowEmptyString()] [string]$Value)

    if ($null -eq $Value) {
        return ''
    }

    return $Value.Replace("'", "''")
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
    'scripts/installer/Tui.Layout.ps1', 'scripts/installer/Tui.State.ps1'
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
    'scripts/installer/Installer.Discovery.Detection.ps1'
    'scripts/installer/Installer.Uninstall.Standalone.ps1'
    'scripts/installer/Installer.Uninstall.ps1'
    'scripts/installer/Installer.Release.ps1'
    'scripts/installer/Installer.PackageIntegrity.ps1'
    'scripts/installer/Installer.Encoding.ps1'
    'scripts/installer/Installer.State.ps1'
    'scripts/installer/Installer.State.Installation.ps1'
    'scripts/installer/Installer.Registration.Paths.ps1'
    'scripts/installer/Installer.Registration.Json.ps1'
    'scripts/installer/Installer.Registration.TrustedTargets.ps1'
    'scripts/installer/Installer.Registration.Cursor.ps1'
    'scripts/installer/Installer.Registration.Commands.ps1'
    'scripts/installer/Installer.Registration.Clients.ps1'
    'scripts/installer/Installer.Registration.ps1'
    'scripts/installer/Installer.Verification.Commands.ps1'
    'scripts/installer/Installer.Verification.ps1'
    'scripts/installer/Installer.Actions.Paths.ps1'
    'scripts/installer/Installer.Actions.Payload.ps1'
    'scripts/installer/Installer.Actions.Rollback.ps1'
    'scripts/installer/Installer.Actions.State.ps1'
    'scripts/installer/Installer.Actions.Core.ps1'
    'scripts/installer/Installer.Actions.ps1'
)
$script:InstallerHelperRepositoryRelativePath = 'scripts/installer'
# Shared installer modules own Resolve-InstallerStatePath, Save-InstallerState,
# installer-state.json handling, Get-AvailableInstallerUpdates, and the rest of
# the persistent state/update flow.
$script:InstallerSharedHelperLeafNames = @(
    'Installer.Discovery.ps1'
    'Installer.Discovery.Detection.ps1'
    'Installer.Uninstall.Standalone.ps1'
    'Installer.Uninstall.ps1'
    'Installer.Release.ps1'
    'Installer.PackageIntegrity.ps1'
    'Installer.Encoding.ps1'
    'Installer.State.ps1'
    'Installer.State.Installation.ps1'
    'Installer.Registration.Paths.ps1'
    'Installer.Registration.Json.ps1'
    'Installer.Registration.TrustedTargets.ps1'
    'Installer.Registration.Cursor.ps1'
    'Installer.Registration.Commands.ps1'
    'Installer.Registration.Clients.ps1'
    'Installer.Registration.ps1'
    'Installer.Verification.Commands.ps1'
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
$script:TuiHelperBootstrapArchive = $null
$script:TrustedLocalPackageArchivePath = $null
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
            "bin\installer\$LeafName"
            "installer\$LeafName"
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
    $resolvedArchivePath = (Resolve-Path -LiteralPath (Assert-InstallerLocalPathTrusted -Path $ArchivePath)).Path
    $trustedDestinationRoot = Assert-InstallerLocalPathTrusted -Path $DestinationRoot
    New-Item -ItemType Directory -Force -Path $trustedDestinationRoot | Out-Null
    Assert-InstallerLocalPathTrusted -Path $trustedDestinationRoot | Out-Null
    $archive = [System.IO.Compression.ZipFile]::OpenRead($resolvedArchivePath)
    try {
        foreach ($leafName in @($script:InstallerHelperManifestFileName) + @($HelperFiles)) {
            $entry = Find-InstallerHelperArchiveEntry -Archive $archive -LeafName $leafName
            if ($null -eq $entry) {
                throw "Installer helper archive entry was not found: $leafName"
            }

            $destinationPath = Assert-InstallerLocalPathTrusted -Path (Join-Path $trustedDestinationRoot $leafName)
            $destinationDirectory = Split-Path -Parent $destinationPath
            if (-not [string]::IsNullOrWhiteSpace($destinationDirectory)) {
                New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null
                Assert-InstallerLocalPathTrusted -Path $destinationDirectory | Out-Null
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

    try {
        $trustedCandidateRoot = Assert-InstallerLocalPathTrusted -Path $CandidateRoot
    }
    catch {
        return
    }

    if (-not $Roots.Contains($trustedCandidateRoot)) {
        $Roots.Add($trustedCandidateRoot)
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

    if ($Action -ne 'install' -and (Test-InstallerTestModeEnabled)) {
        foreach ($helperRoot in @(Get-InstalledInstallerHelperRoots)) {
            Add-InstallerHelperRootCandidate -Roots $candidateRoots -CandidateRoot $helperRoot
        }
    }

    return @($candidateRoots.ToArray())
}
function Get-StandaloneInstallerStateSnapshot {
    $statePath = Resolve-StandaloneInstallerStatePath
    if (-not (Test-Path -LiteralPath $statePath)) {
        return $null
    }

    for ($attempt = 0; $attempt -lt 6; $attempt++) {
        try {
            Assert-InstallerLocalPathTrusted -Path $statePath | Out-Null
            return (Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json)
        }
        catch {
            if ((Test-StandaloneTransientFileSystemError -Exception $_.Exception) -and $attempt -lt 5) {
                Start-Sleep -Milliseconds ([Math]::Min(75 * ($attempt + 1), 400))
                continue
            }

            Move-StandaloneCorruptInstallerStateFile -Path $statePath | Out-Null
            return $null
        }
    }

    return $null
}
function Move-StandaloneCorruptInstallerStateFile {
    param([Parameter(Mandatory)] [string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $null
    }

    $resolvedPath = Assert-InstallerLocalPathTrusted -Path $Path
    if (-not (Test-Path -LiteralPath $resolvedPath)) {
        return $null
    }

    try {
        $directory = Split-Path -Parent $resolvedPath
        $fileName = Split-Path -Leaf $resolvedPath
        $quarantinePath = Assert-InstallerLocalPathTrusted -Path (Join-Path $directory ("{0}.corrupt-{1}" -f $fileName, ([guid]::NewGuid().ToString('N'))))
        Assert-InstallerLocalPathTrusted -Path $resolvedPath | Out-Null
        Assert-InstallerLocalPathTrusted -Path $quarantinePath | Out-Null
        Move-Item -LiteralPath $resolvedPath -Destination $quarantinePath -Force
        return $quarantinePath
    }
    catch {
        return $null
    }
}
function Test-StandaloneTransientFileSystemError {
    param([System.Exception]$Exception)

    $candidate = $Exception
    while ($null -ne $candidate) {
        if ($candidate -is [System.IO.IOException] -or $candidate -is [System.UnauthorizedAccessException]) {
            return $true
        }

        $candidate = $candidate.InnerException
    }

    return $false
}
function Move-StandalonePathWithRetry {
    param(
        [Parameter(Mandatory)] [string]$SourcePath,
        [Parameter(Mandatory)] [string]$DestinationPath
    )

    for ($attempt = 0; $attempt -lt 6; $attempt++) {
        try {
            $resolvedSourcePath = Assert-InstallerLocalPathTrusted -Path $SourcePath
            $resolvedDestinationPath = Assert-InstallerLocalPathTrusted -Path $DestinationPath -RejectHardLinks
            if (Test-Path -LiteralPath $resolvedDestinationPath) {
                Remove-PathIfExists -Path $resolvedDestinationPath -RetryCount 1
            }

            Move-Item -LiteralPath $resolvedSourcePath -Destination $resolvedDestinationPath -Force
            return
        }
        catch {
            if (-not (Test-StandaloneTransientFileSystemError -Exception $_.Exception) -or $attempt -ge 5) {
                throw
            }

            Start-Sleep -Milliseconds ([Math]::Min(75 * ($attempt + 1), 400))
        }
    }
}
function Resolve-StandaloneInstallerStatePath {
    param([switch]$CreateRoot)

    $stateRoot = Assert-InstallerLocalPathTrusted -Path (Join-Path $env:APPDATA 'WpfDevToolsMcp')
    if ($CreateRoot) {
        New-Item -ItemType Directory -Force -Path $stateRoot | Out-Null
        Assert-InstallerLocalPathTrusted -Path $stateRoot | Out-Null
    }

    return (Join-Path $stateRoot 'installer-state.json')
}
function Get-StandaloneEmptyInstallerState {
    return [ordered]@{
        lastInstallRoot = $null
        architectures = [ordered]@{}
        registrations = [ordered]@{}
    }
}
function Get-StandaloneInstallerState {
    $snapshot = Get-StandaloneInstallerStateSnapshot
    $state = Get-StandaloneEmptyInstallerState

    if ($null -eq $snapshot) {
        return $state
    }

    $state.lastInstallRoot = [string]$snapshot.lastInstallRoot

    if ($null -ne $snapshot.architectures) {
        foreach ($property in $snapshot.architectures.PSObject.Properties) {
            $state.architectures[$property.Name] = [ordered]@{
                version = [string]$property.Value.version
                executable = [string]$property.Value.executable
                installRoot = [string]$property.Value.installRoot
            }
        }
    }

    if ($null -ne $snapshot.registrations) {
        foreach ($property in $snapshot.registrations.PSObject.Properties) {
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
function Save-StandaloneInstallerState {
    param([Parameter(Mandatory)] $State)

    $statePath = Resolve-StandaloneInstallerStatePath -CreateRoot
    $tempStatePath = "$statePath.tmp-$([guid]::NewGuid().ToString('N'))"
    try {
        Assert-InstallerLocalPathTrusted -Path $tempStatePath | Out-Null
        $State | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $tempStatePath -Encoding UTF8
        if ($env:WPFDEVTOOLS_INSTALLER_TEST_FAIL_SAVE_STANDALONE_STATE -eq '1') {
            throw 'Simulated standalone state save failure.'
        }
        Move-StandalonePathWithRetry -SourcePath $tempStatePath -DestinationPath $statePath
    }
    finally {
        if (Test-Path -LiteralPath $tempStatePath) {
            Remove-PathIfExists -Path $tempStatePath
        }
    }

    return $statePath
}
function Get-StandaloneExistingConfigMap {
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
        throw "Failed to parse JSON config file '$resolvedPath'. Fix the malformed JSON and retry. The installer did not modify the file or update registration state. Parser error: $($_.Exception.Message)"
    }

    foreach ($property in $parsed.PSObject.Properties) {
        $map[$property.Name] = $property.Value
    }

    return $map
}
function Get-StandaloneConfigCollectionMap {
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
function Test-StandaloneJsonConfigRegistration {
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

    $root = Get-StandaloneExistingConfigMap -Path $resolvedConfigPath
    $servers = Get-StandaloneConfigCollectionMap -Root $root -CollectionName $CollectionName
    return $servers.Contains('wpf-devtools')
}
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
        if ($manifestClientBaseId -ne 'other' -and $manifestClientBaseId -ne 'claude-code' -and $manifestClientBaseId -ne 'codex') {
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

        $changeTarget = if ($registrationChange.Contains('target')) { [string]$registrationChange.target } else { [string]$registrationChange.Target }
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
function Get-StandaloneDetectedInstallerRegistrations {
    param([Parameter(Mandatory)] $State)

    $registrationMap = [ordered]@{}
    foreach ($entry in $State.registrations.GetEnumerator()) {
        $record = $entry.Value
        $installedExecutable = Get-StandaloneRecordStringValue -Record $record -PropertyNames @('installedExecutable', 'InstalledExecutable')
        $installRoot = Get-StandaloneRecordStringValue -Record $record -PropertyNames @('installRoot', 'InstallRoot')
        $architecture = Get-StandaloneRecordStringValue -Record $record -PropertyNames @('architecture', 'Architecture')
        $resolvedVersion = Get-StandaloneRecordStringValue -Record $record -PropertyNames @('resolvedVersion', 'ResolvedVersion')
        $registrationMode = Get-StandaloneRecordStringValue -Record $record -PropertyNames @('mode', 'Mode', 'RegistrationMode')
        $registrationTarget = Get-StandaloneRecordStringValue -Record $record -PropertyNames @('target', 'Target', 'RegistrationTarget')
        $installerOwned = $false

        if (-not [string]::IsNullOrWhiteSpace($installedExecutable)) {
            $ownership = Resolve-StandaloneInstallerOwnershipFromExecutable -InstalledExecutable $installedExecutable
            $installerOwned = [bool]$ownership.InstallerOwned
            if ([string]::IsNullOrWhiteSpace($installRoot)) {
                $installRoot = [string]$ownership.InstallRoot
            }

            if ([string]::IsNullOrWhiteSpace($architecture)) {
                $architecture = [string]$ownership.Architecture
            }

            if ([string]::IsNullOrWhiteSpace($resolvedVersion)) {
                $resolvedVersion = [string]$ownership.ResolvedVersion
            }
        }

        $registrationMap[[string]$entry.Key] = [ordered]@{
            ClientId = [string]$entry.Key
            RegistrationMode = $registrationMode
            RegistrationTarget = $registrationTarget
            InstalledExecutable = $installedExecutable
            InstallRoot = $installRoot
            Architecture = $architecture
            InstallerOwned = $installerOwned
            ResolvedVersion = $resolvedVersion
        }
    }

    foreach ($registration in @(Get-StandaloneDetectedConfigRegistrations)) {
        $stateKey = Resolve-ClientStateKey -ClientId ([string]$registration.ClientId) -RegistrationMode ([string]$registration.RegistrationMode)
        if ($registrationMap.Contains($stateKey)) {
            $existing = $registrationMap[$stateKey]
            $clientBaseId = Resolve-ClientBaseId -ClientId ([string]$registration.ClientId)
            $collectionName = Get-StandaloneJsonCollectionName -ClientBaseId $clientBaseId
            $existingTarget = [string]$existing.RegistrationTarget
            $liveTarget = [string]$registration.RegistrationTarget
            $existingTargetHasRegistration = $false
            $liveTargetHasRegistration = $false
            if (-not [string]::IsNullOrWhiteSpace($collectionName)) {
                if (-not [string]::IsNullOrWhiteSpace($existingTarget)) {
                    $existingTargetHasRegistration = Test-StandaloneJsonConfigRegistration -CollectionName $collectionName -ConfigPath $existingTarget
                }

                if (-not [string]::IsNullOrWhiteSpace($liveTarget)) {
                    $liveTargetHasRegistration = Test-StandaloneJsonConfigRegistration -CollectionName $collectionName -ConfigPath $liveTarget
                }
            }

            if ([string]::IsNullOrWhiteSpace([string]$existing.RegistrationTarget)) {
                $existing.RegistrationTarget = [string]$registration.RegistrationTarget
            }
            elseif ($liveTargetHasRegistration -and -not $existingTargetHasRegistration) {
                $existing.RegistrationTarget = $liveTarget
                $existing.RegistrationMode = [string]$registration.RegistrationMode
                $existing.InstalledExecutable = [string]$registration.InstalledExecutable
            }
            if ([string]::IsNullOrWhiteSpace([string]$existing.InstalledExecutable)) {
                $existing.InstalledExecutable = [string]$registration.InstalledExecutable
            }
            if (-not [bool]$existing.InstallerOwned -and [bool]$registration.InstallerOwned) {
                $existing.InstallerOwned = $true
                $existing.InstallRoot = [string]$registration.InstallRoot
                $existing.Architecture = [string]$registration.Architecture
                $existing.ResolvedVersion = [string]$registration.ResolvedVersion
            }
            continue
        }

        $registrationMap[$stateKey] = $registration
    }

    return @($registrationMap.Values)
}
function Get-StandaloneDetectedInstallerRegistrationMap {
    param([Parameter(Mandatory)] $State)

    $registrationMap = [ordered]@{}
    foreach ($registration in @(Get-StandaloneDetectedInstallerRegistrations -State $State)) {
        $stateKey = Resolve-ClientStateKey -ClientId ([string]$registration.ClientId) -RegistrationMode ([string]$registration.RegistrationMode)
        $registrationMap[$stateKey] = $registration
        if (-not $registrationMap.Contains([string]$registration.ClientId)) {
            $registrationMap[[string]$registration.ClientId] = $registration
        }
    }

    return $registrationMap
}
function Get-StandaloneDetectedInstallerInstallations {
    param(
        [Parameter(Mandatory)] $State,
        [string]$ExpectedInstallRoot
    )

    $installations = [ordered]@{}
    foreach ($registration in @(Get-StandaloneDetectedInstallerRegistrations -State $State)) {
        if (-not [bool]$registration.InstallerOwned) {
            continue
        }

        if ([string]::IsNullOrWhiteSpace([string]$registration.InstallRoot) -or [string]::IsNullOrWhiteSpace([string]$registration.Architecture)) {
            continue
        }

        try {
            $trustedInstallRoot = Assert-InstallerLocalPathTrusted -Path ([string]$registration.InstallRoot)
            $trustedInstallBase = Assert-InstallerLocalPathTrusted -Path (Resolve-StandaloneInstallBasePath -ResolvedInstallRoot $trustedInstallRoot -ResolvedArchitecture ([string]$registration.Architecture))
        }
        catch {
            continue
        }

        $key = "{0}|{1}" -f $trustedInstallRoot.ToLowerInvariant(), ([string]$registration.Architecture).ToLowerInvariant()
        $installations[$key] = [ordered]@{
            InstallRoot = $trustedInstallRoot
            Architecture = [string]$registration.Architecture
            InstallBase = $trustedInstallBase
            InstalledExecutable = [string]$registration.InstalledExecutable
            ResolvedVersion = [string]$registration.ResolvedVersion
            InstallerOwned = $true
        }
    }

    foreach ($architectureEntry in $State.architectures.GetEnumerator()) {
        $arch = [string]$architectureEntry.Key
        $record = $architectureEntry.Value
        $executable = [string]$record.executable
        if ([string]::IsNullOrWhiteSpace($executable)) {
            continue
        }

        $ownership = Resolve-StandaloneInstallerOwnershipFromExecutable -InstalledExecutable $executable
        if (-not [bool]$ownership.InstallerOwned) {
            continue
        }

        $installRoot = [string]$record.installRoot
        if ([string]::IsNullOrWhiteSpace($installRoot)) {
            $installRoot = [string]$ownership.InstallRoot
        }

        if ([string]::IsNullOrWhiteSpace($installRoot)) {
            continue
        }

        try {
            $trustedInstallRoot = Assert-InstallerLocalPathTrusted -Path $installRoot
            $trustedInstallBase = Assert-InstallerLocalPathTrusted -Path (Resolve-StandaloneInstallBasePath -ResolvedInstallRoot $trustedInstallRoot -ResolvedArchitecture $arch)
        }
        catch {
            continue
        }

        $key = "{0}|{1}" -f $trustedInstallRoot.ToLowerInvariant(), $arch.ToLowerInvariant()
        $installations[$key] = [ordered]@{
            InstallRoot = $trustedInstallRoot
            Architecture = $arch
            InstallBase = $trustedInstallBase
            InstalledExecutable = $executable
            ResolvedVersion = [string]$record.version
            InstallerOwned = $true
        }
    }

    $candidateRoots = New-Object System.Collections.Generic.List[string]
    foreach ($candidateRoot in @(
            $ExpectedInstallRoot
            [string]$State.lastInstallRoot
        )) {
        if ([string]::IsNullOrWhiteSpace($candidateRoot)) {
            continue
        }

        try {
            $trustedCandidateRoot = Assert-InstallerLocalPathTrusted -Path $candidateRoot
        }
        catch {
            continue
        }

        if (-not $candidateRoots.Contains($trustedCandidateRoot)) {
            $candidateRoots.Add($trustedCandidateRoot)
        }
    }

    foreach ($candidateRoot in $candidateRoots) {
        foreach ($architecture in @(Get-StandaloneKnownArchitectures)) {
            $evidence = Get-StandaloneLiveInstallerManifestEvidence -InstallRoot $candidateRoot -Architecture $architecture
            if ($null -eq $evidence) {
                continue
            }

            $key = "{0}|{1}" -f $candidateRoot.ToLowerInvariant(), $architecture.ToLowerInvariant()
            $installations[$key] = [ordered]@{
                InstallRoot = $candidateRoot
                Architecture = $architecture
                InstallBase = Resolve-StandaloneInstallBasePath -ResolvedInstallRoot $candidateRoot -ResolvedArchitecture $architecture
                InstalledExecutable = [string]$evidence.InstalledExecutable
                ResolvedVersion = [string]$evidence.ResolvedVersion
                InstallerOwned = $true
            }
        }
    }

    return @($installations.Values)
}
function Get-StandaloneFallbackRegistrationRecord {
    param(
        [Parameter(Mandatory)] [string]$SelectedClient,
        [Parameter(Mandatory)] [string]$ResolvedInstallRoot,
        [Parameter(Mandatory)] [string]$ResolvedArchitecture
    )

    $clientBaseId = Resolve-ClientBaseId -ClientId $SelectedClient
    $liveEvidence = Get-StandaloneLiveInstallerManifestEvidence -InstallRoot $ResolvedInstallRoot -Architecture $ResolvedArchitecture
    $fallbackExecutable = if ($null -ne $liveEvidence) {
        [string]$liveEvidence.InstalledExecutable
    }
    else {
        try {
            $candidateExecutable = Assert-InstallerLocalPathTrusted -Path (Join-Path (Resolve-StandaloneInstallBasePath -ResolvedInstallRoot $ResolvedInstallRoot -ResolvedArchitecture $ResolvedArchitecture) "current\bin\wpf-devtools-$ResolvedArchitecture.exe")
        }
        catch {
            $candidateExecutable = $null
        }

        if (-not [string]::IsNullOrWhiteSpace($candidateExecutable) -and (Test-Path -LiteralPath $candidateExecutable)) { $candidateExecutable } else { $null }
    }
    $managedRegistrations = @(Get-StandaloneManagedRegistrationsFromInstall -ResolvedInstallRoot $ResolvedInstallRoot -ResolvedArchitecture $ResolvedArchitecture)

    switch ($clientBaseId) {
        'vscode' {
            $registration = $managedRegistrations | Where-Object { [string]$_.ClientId -eq 'vscode' } | Select-Object -First 1
            if ($null -ne $registration) { return $registration }
            break
        }
        'visual-studio' {
            $registration = $managedRegistrations | Where-Object { [string]$_.ClientId -eq 'visual-studio' } | Select-Object -First 1
            if ($null -ne $registration) { return $registration }
            break
        }
        'claude-desktop' {
            $registration = $managedRegistrations | Where-Object { [string]$_.ClientId -eq 'claude-desktop' } | Select-Object -First 1
            if ($null -ne $registration) { return $registration }
            break
        }
        'cursor' {
            $preferredClientId = if ($SelectedClient -eq 'cursor-global') {
                'cursor-global'
            }
            elseif ($SelectedClient -eq 'cursor-project') {
                'cursor-project'
            }
            elseif ($CursorMode -eq 'project') {
                'cursor-project'
            }
            else {
                'cursor-global'
            }

            $registration = $managedRegistrations | Where-Object { [string]$_.ClientId -eq $preferredClientId } | Select-Object -First 1
            if ($null -eq $registration) {
                $registration = $managedRegistrations | Where-Object { [string]$_.ClientId -like 'cursor-*' } | Select-Object -First 1
            }

            if ($null -ne $registration) { return $registration }
            break
        }
    }

    switch ($clientBaseId) {
        'other' {
            return [ordered]@{
                ClientId = 'other'
                RegistrationMode = 'artifact-only'
                RegistrationTarget = (Join-Path (Resolve-StandaloneInstallBasePath -ResolvedInstallRoot $ResolvedInstallRoot -ResolvedArchitecture $ResolvedArchitecture) 'client-registration\other.mcpServers.json')
                InstalledExecutable = $fallbackExecutable
                InstallRoot = $ResolvedInstallRoot
                Architecture = $ResolvedArchitecture
            }
        }
        'claude-code' {
            return [ordered]@{
                ClientId = 'claude-code'
                RegistrationMode = 'cli'
                RegistrationTarget = 'claude'
                InstalledExecutable = $fallbackExecutable
                InstallRoot = $ResolvedInstallRoot
                Architecture = $ResolvedArchitecture
            }
        }
        'codex' {
            return [ordered]@{
                ClientId = 'codex'
                RegistrationMode = 'cli'
                RegistrationTarget = 'codex'
                InstalledExecutable = $fallbackExecutable
                InstallRoot = $ResolvedInstallRoot
                Architecture = $ResolvedArchitecture
            }
        }
        default { return $null }
    }
}
function Test-StandaloneInstallerRunningElevated {
    if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_ASSUME_ELEVATED)) {
        $overrideValue = ([string]$env:WPFDEVTOOLS_INSTALLER_ASSUME_ELEVATED).Trim().ToLowerInvariant()
        return @('1', 'true', 'yes', 'on') -contains $overrideValue
    }

    try {
        $identity = [System.Security.Principal.WindowsIdentity]::GetCurrent()
        $principal = [System.Security.Principal.WindowsPrincipal]::new($identity)
        return $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)
    }
    catch {
        return $false
    }
}
function Resolve-StandaloneExecutableCommandPath {
    param([Parameter(Mandatory)] [string]$Command)

    if (Test-StandaloneInstallerRunningElevated) {
        return $null
    }

    $resolvedCommand = Get-Command $Command -CommandType Application,ExternalScript -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $resolvedCommand) {
        return $null
    }

    if (-not [string]::IsNullOrWhiteSpace([string]$resolvedCommand.Path)) {
        return [string]$resolvedCommand.Path
    }

    if (-not [string]::IsNullOrWhiteSpace([string]$resolvedCommand.Source)) {
        return [string]$resolvedCommand.Source
    }

    return $null
}
function Invoke-StandaloneUninstallVerification {
    param(
        [Parameter(Mandatory)] [string]$SelectedClient,
        $RegistrationRecord,
        $RegistrationChanges
    )

    $clientBaseId = Resolve-ClientBaseId -ClientId $SelectedClient
    $verificationSucceeded = switch ($clientBaseId) {
        'claude-code' {
            (Invoke-VerificationCommand -Command 'claude' -Arguments @('mcp', 'list') -ExpectedToken 'wpf-devtools' -ExpectPresent $false).Succeeded
            break
        }
        'codex' {
            (Invoke-VerificationCommand -Command 'codex' -Arguments @('mcp', 'list') -ExpectedToken 'wpf-devtools' -ExpectPresent $false).Succeeded
            break
        }
        'cursor' {
            $verificationSucceeded = $true
            foreach ($targetPath in @(Get-StandaloneJsonVerificationTargets -SelectedClient $SelectedClient -RegistrationRecord $RegistrationRecord -RegistrationChanges $RegistrationChanges)) {
                if (Test-StandaloneJsonConfigRegistration -CollectionName 'mcpServers' -ConfigPath $targetPath) {
                    $verificationSucceeded = $false
                    break
                }
            }
            $verificationSucceeded
            break
        }
        'vscode' {
            $verificationSucceeded = $true
            foreach ($targetPath in @(Get-StandaloneJsonVerificationTargets -SelectedClient $SelectedClient -RegistrationRecord $RegistrationRecord -RegistrationChanges $RegistrationChanges)) {
                if (Test-StandaloneJsonConfigRegistration -CollectionName 'servers' -ConfigPath $targetPath) {
                    $verificationSucceeded = $false
                    break
                }
            }
            $verificationSucceeded
            break
        }
        'visual-studio' {
            $verificationSucceeded = $true
            foreach ($targetPath in @(Get-StandaloneJsonVerificationTargets -SelectedClient $SelectedClient -RegistrationRecord $RegistrationRecord -RegistrationChanges $RegistrationChanges)) {
                if (Test-StandaloneJsonConfigRegistration -CollectionName 'servers' -ConfigPath $targetPath) {
                    $verificationSucceeded = $false
                    break
                }
            }
            $verificationSucceeded
            break
        }
        'claude-desktop' {
            $verificationSucceeded = $true
            foreach ($targetPath in @(Get-StandaloneJsonVerificationTargets -SelectedClient $SelectedClient -RegistrationRecord $RegistrationRecord -RegistrationChanges $RegistrationChanges)) {
                if (Test-StandaloneJsonConfigRegistration -CollectionName 'mcpServers' -ConfigPath $targetPath) {
                    $verificationSucceeded = $false
                    break
                }
            }
            $verificationSucceeded
            break
        }
        'other' {
            $verificationTargets = New-Object System.Collections.Generic.List[string]
            foreach ($candidateTarget in @(Get-StandaloneTrustedOtherRegistrationArtifactTargets -RegistrationRecord $RegistrationRecord)) {
                Add-StandaloneTrustedTargetCandidate -Targets $verificationTargets -Candidate $candidateTarget
            }

            if ($verificationTargets.Count -eq 0) {
                $recordedTarget = Get-StandaloneRecordStringValue -Record $RegistrationRecord -PropertyNames @('target', 'Target', 'RegistrationTarget')
                $trustedRecordedTarget = $null
                if (-not [string]::IsNullOrWhiteSpace($recordedTarget)) {
                    try {
                        $trustedRecordedTarget = Assert-InstallerLocalPathTrusted -Path $recordedTarget
                    }
                    catch {
                        $trustedRecordedTarget = $null
                    }
                }

                if (-not [string]::IsNullOrWhiteSpace($trustedRecordedTarget) -and (Test-Path -LiteralPath $trustedRecordedTarget)) {
                    $false
                }
                else {
                    $true
                }
            }
            else {
                @($verificationTargets.ToArray()).Where({
                        $trustedVerificationTarget = $null
                        try {
                            $trustedVerificationTarget = Assert-InstallerLocalPathTrusted -Path $_
                        }
                        catch {
                            $trustedVerificationTarget = $null
                        }

                        -not [string]::IsNullOrWhiteSpace($trustedVerificationTarget) -and (Test-Path -LiteralPath $trustedVerificationTarget)
                    }).Count -eq 0
            }
            break
        }
        default {
            $true
        }
    }

    return [ordered]@{
        Succeeded = [bool]$verificationSucceeded
        VerificationMessage = "Verified uninstall state for $SelectedClient."
    }
}
function Invoke-StandaloneInstallerActionCore {
    param(
        [Parameter(Mandatory)] [ValidateSet('uninstall', 'full-uninstall')] [string]$ResolvedAction,
        [Parameter(Mandatory)] [string]$ResolvedArchitecture,
        [Parameter(Mandatory)] [string]$ResolvedClient,
        [Parameter(Mandatory)] [AllowEmptyString()] [AllowNull()] [string]$ResolvedInstallRoot,
        [Parameter(Mandatory)] [string]$RequestedVersion,
        [switch]$UseLatestRelease
    )

    $state = Get-StandaloneInstallerState

    if ($ResolvedAction -eq 'full-uninstall') {
        $detectedInstallations = @(Get-StandaloneDetectedInstallerInstallations -State $state -ExpectedInstallRoot $ResolvedInstallRoot)
        $detectedRegistrations = @(Get-StandaloneDetectedInstallerRegistrations -State $state)
        $registrationMap = [ordered]@{}
        foreach ($registration in $detectedRegistrations) {
            $stateKey = Resolve-ClientStateKey -ClientId ([string]$registration.ClientId) -RegistrationMode ([string]$registration.RegistrationMode)
            $registrationMap[$stateKey] = $registration
        }

        foreach ($installation in $detectedInstallations) {
            foreach ($registration in @(Get-StandaloneManagedRegistrationsFromInstall -ResolvedInstallRoot ([string]$installation.InstallRoot) -ResolvedArchitecture ([string]$installation.Architecture))) {
                $stateKey = Resolve-ClientStateKey -ClientId ([string]$registration.ClientId) -RegistrationMode ([string]$registration.RegistrationMode)
                if (-not $registrationMap.Contains($stateKey)) {
                    $registrationMap[$stateKey] = $registration
                }
            }
        }

        $detectedRegistrations = @($registrationMap.Values)
        $registrationOperations = @()
        $installationBackups = @()
        $removedInstallations = @()
        $stateRestoreRequired = $false
        $statePath = Resolve-StandaloneInstallerStatePath -CreateRoot
        $hadOriginalStateFile = Test-Path -LiteralPath $statePath
        $originalStateJson = if ($hadOriginalStateFile) { Get-Content -LiteralPath $statePath -Raw } else { $null }
        try {
            foreach ($record in $detectedRegistrations) {
                $clientId = [string]$record.ClientId
                $rawRegistrationMode = Get-StandaloneRecordStringValue -Record $record -PropertyNames @('RegistrationMode', 'mode', 'Mode')
                $registrationMode = Get-StandaloneNormalizedRegistrationMode -RegistrationMode $rawRegistrationMode
                $targetPath = Get-StandaloneTrustedRecordedTarget -SelectedClient $clientId -RegistrationRecord $record
                $clientBaseId = Resolve-ClientBaseId -ClientId $clientId
                $operation = [ordered]@{
                    ClientId = $clientId
                    RegistrationMode = $registrationMode
                    TargetPath = $targetPath
                    BackupPath = $null
                    Applied = $false
                    InstalledExecutable = (Get-StandaloneRecordStringValue -Record $record -PropertyNames @('InstalledExecutable', 'installedExecutable'))
                }

                if ([string]::IsNullOrWhiteSpace($targetPath) -and [string]::Equals($registrationMode, 'json-file', [System.StringComparison]::OrdinalIgnoreCase)) {
                    if ($clientBaseId -eq 'cursor') {
                        $targetPath = Get-StandaloneTrustedCursorManifestTarget -SelectedClient $clientId -RegistrationRecord $record
                    }
                    else {
                        $targetPath = Get-StandaloneTrustedManagedJsonRegistrationTarget -SelectedClient $clientId -RegistrationRecord $record
                    }

                    $operation.TargetPath = $targetPath
                }

                if ([string]::Equals($registrationMode, 'artifact-only', [System.StringComparison]::OrdinalIgnoreCase) -and [string]::IsNullOrWhiteSpace($targetPath)) {
                    $artifactTargets = @(Get-StandaloneTrustedOtherRegistrationArtifactTargets -RegistrationRecord $record)
                    if ($artifactTargets.Count -gt 0) {
                        $targetPath = [string]$artifactTargets[0]
                        $operation.TargetPath = $targetPath
                    }
                }

                if ([string]::Equals($registrationMode, 'json-file', [System.StringComparison]::OrdinalIgnoreCase)) {
                    $collectionName = switch ($clientBaseId) {
                        'vscode' { 'servers' }
                        'visual-studio' { 'servers' }
                        'claude-desktop' { 'mcpServers' }
                        'cursor' { 'mcpServers' }
                        default { $null }
                    }

                    if (-not [string]::IsNullOrWhiteSpace($collectionName) -and -not [string]::IsNullOrWhiteSpace($targetPath)) {
                        $removal = Remove-StandaloneJsonConfigRegistration -CollectionName $collectionName -ConfigPath $targetPath
                        $operation.BackupPath = [string]$removal.backupPath
                        $operation.Applied = [bool]$removal.applied
                    }
                }
                elseif ([string]::Equals($registrationMode, 'artifact-only', [System.StringComparison]::OrdinalIgnoreCase)) {
                    $artifactTargets = @(Get-StandaloneTrustedOtherRegistrationArtifactTargets -RegistrationRecord $record)
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

                        if ([string]::IsNullOrWhiteSpace([string]$operation.BackupPath)) {
                            $targetPath = $trustedCandidateTarget
                            $operation.TargetPath = $targetPath
                            $operation.BackupPath = Assert-InstallerLocalPathTrusted -Path "$targetPath.bak-$([guid]::NewGuid().ToString('N'))"
                            Copy-Item -LiteralPath $targetPath -Destination ([string]$operation.BackupPath) -Force
                        }

                        Remove-PathIfExists -Path $trustedCandidateTarget
                        $operation.Applied = $true
                    }
                }
                elseif ([string]::Equals($registrationMode, 'cli', [System.StringComparison]::OrdinalIgnoreCase)) {
                    $commandName = if ($clientBaseId -eq 'claude-code') { 'claude' } else { 'codex' }
                    $resolvedCommandPath = Resolve-StandaloneExecutableCommandPath -Command $commandName
                    if (-not [string]::IsNullOrWhiteSpace($resolvedCommandPath)) {
                        & $resolvedCommandPath mcp remove wpf-devtools | Out-Null
                        $operation.Applied = ($LASTEXITCODE -eq 0)
                    }
                }

                $registrationOperations += $operation
                $verification = Invoke-StandaloneUninstallVerification -SelectedClient $clientId -RegistrationRecord $record -RegistrationChanges @($registrationOperations | Where-Object { [string]$_.ClientId -eq $clientId })
                if (-not $verification.Succeeded) {
                    throw $verification.VerificationMessage
                }
            }

            foreach ($installation in $detectedInstallations) {
                $architecture = [string]$installation.Architecture
                $installRoot = [string]$installation.InstallRoot
                $installBase = [string]$installation.InstallBase
                try {
                    $trustedInstallBase = Assert-InstallerLocalPathTrusted -Path $installBase
                }
                catch {
                    continue
                }

                if (Test-Path -LiteralPath $trustedInstallBase) {
                    $installBase = $trustedInstallBase
                    $rollbackPath = Assert-InstallerLocalPathTrusted -Path "$installBase.rollback-$([guid]::NewGuid().ToString('N'))"
                    Move-StandalonePathWithRetry -SourcePath $installBase -DestinationPath $rollbackPath
                    $installationBackups += [ordered]@{
                        InstallBase = $installBase
                        RollbackPath = $rollbackPath
                    }
                    $removedInstallations += [ordered]@{
                        InstallRoot = $installRoot
                        Architecture = $architecture
                        InstallBase = $installBase
                        InstalledExecutable = [string]$installation.InstalledExecutable
                        ResolvedVersion = [string]$installation.ResolvedVersion
                        InstallerOwned = $true
                    }
                }
            }

            foreach ($installation in $removedInstallations) {
                $trustedInstallBase = $null
                try {
                    $trustedInstallBase = Assert-InstallerLocalPathTrusted -Path ([string]$installation.InstallBase)
                }
                catch {
                    $trustedInstallBase = $null
                }

                if (-not [string]::IsNullOrWhiteSpace($trustedInstallBase) -and (Test-Path -LiteralPath $trustedInstallBase)) {
                    throw "Installation root still exists: $([string]$installation.InstallBase)"
                }
            }

            $state.registrations.Clear()
            $state.architectures.Clear()
            $statePath = Save-StandaloneInstallerState -State $state
            $stateRestoreRequired = $true
            foreach ($operation in $registrationOperations) {
                Remove-PathIfExists -Path ([string]$operation.BackupPath)
            }

            foreach ($backup in $installationBackups) {
                Remove-PathIfExists -Path ([string]$backup.RollbackPath)
            }
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
                statePath = $statePath
                removedInstallation = ($removedInstallations.Count -gt 0)
                removedInstallations = @($removedInstallations)
                registrations = @($registrationOperations | Where-Object { [bool]$_.Applied })
                verificationMessage = "Verified removal of $($registrationOperations.Count) registration(s) and $($removedInstallations.Count) installer-owned server location(s)."
            }
        }
        catch {
            $backupsInReverse = @($installationBackups)
            [array]::Reverse($backupsInReverse)
            foreach ($backup in $backupsInReverse) {
                if (-not [string]::IsNullOrWhiteSpace([string]$backup.RollbackPath)) {
                    try {
                        $trustedRollbackPath = Assert-InstallerLocalPathTrusted -Path ([string]$backup.RollbackPath)
                        $trustedInstallBase = Assert-InstallerLocalPathTrusted -Path ([string]$backup.InstallBase)
                    }
                    catch {
                        continue
                    }

                    if (Test-Path -LiteralPath $trustedRollbackPath) {
                        Move-StandalonePathWithRetry -SourcePath $trustedRollbackPath -DestinationPath $trustedInstallBase
                    }
                }
            }

            $operationsInReverse = @($registrationOperations)
            [array]::Reverse($operationsInReverse)
            foreach ($operation in $operationsInReverse) {
                if (-not [bool]$operation.Applied) {
                    continue
                }

                if ([string]::Equals([string]$operation.RegistrationMode, 'json-file', [System.StringComparison]::OrdinalIgnoreCase)) {
                    if (-not [string]::IsNullOrWhiteSpace([string]$operation.BackupPath)) {
                        try {
                            $trustedBackupPath = Assert-InstallerLocalPathTrusted -Path ([string]$operation.BackupPath)
                            $trustedTargetPath = Assert-InstallerLocalPathTrusted -Path ([string]$operation.TargetPath) -RejectHardLinks
                        }
                        catch {
                            continue
                        }

                        if (Test-Path -LiteralPath $trustedBackupPath) {
                            Copy-Item -LiteralPath $trustedBackupPath -Destination $trustedTargetPath -Force
                            Remove-PathIfExists -Path $trustedBackupPath
                        }
                    }
                    continue
                }

                if ([string]::Equals([string]$operation.RegistrationMode, 'artifact-only', [System.StringComparison]::OrdinalIgnoreCase)) {
                    if (-not [string]::IsNullOrWhiteSpace([string]$operation.BackupPath)) {
                        try {
                            $trustedBackupPath = Assert-InstallerLocalPathTrusted -Path ([string]$operation.BackupPath)
                            $trustedTargetPath = Assert-InstallerLocalPathTrusted -Path ([string]$operation.TargetPath) -RejectHardLinks
                        }
                        catch {
                            continue
                        }

                        if (Test-Path -LiteralPath $trustedBackupPath) {
                            Copy-Item -LiteralPath $trustedBackupPath -Destination $trustedTargetPath -Force
                            Remove-PathIfExists -Path $trustedBackupPath
                        }
                    }
                    continue
                }

                if ([string]::Equals([string]$operation.RegistrationMode, 'cli', [System.StringComparison]::OrdinalIgnoreCase) -and -not [string]::IsNullOrWhiteSpace([string]$operation.InstalledExecutable)) {
                    $clientBaseId = Resolve-ClientBaseId -ClientId ([string]$operation.ClientId)
                    $commandName = if ($clientBaseId -eq 'claude-code') { 'claude' } else { 'codex' }
                    $resolvedCommandPath = Resolve-StandaloneExecutableCommandPath -Command $commandName
                    if (-not [string]::IsNullOrWhiteSpace($resolvedCommandPath)) {
                        & $resolvedCommandPath mcp add wpf-devtools -- ([string]$operation.InstalledExecutable) | Out-Null
                    }
                }
            }

            if ($stateRestoreRequired) {
                try {
                    if ($hadOriginalStateFile) {
                        $originalStateJson | Set-Content -LiteralPath $statePath -Encoding UTF8
                    }
                    else {
                        Remove-PathIfExists -Path $statePath
                    }
                }
                catch {
                    throw ($_.Exception.Message + ' Failed to restore standalone installer state after rollback. ' + $_.Exception.Message)
                }
            }

            throw
        }
    }

    $detectedRegistrationMap = Get-StandaloneDetectedInstallerRegistrationMap -State $state
    $registrationKey = if ($detectedRegistrationMap.Contains($ResolvedClient)) {
        $ResolvedClient
    }
    elseif ($ResolvedClient -eq 'cursor') {
        ($detectedRegistrationMap.Keys | Where-Object { $_ -like 'cursor-*' } | Select-Object -First 1)
    }
    else {
        $null
    }

    $registrationRecord = if (-not [string]::IsNullOrWhiteSpace([string]$registrationKey) -and $detectedRegistrationMap.Contains($registrationKey)) {
        $detectedRegistrationMap[$registrationKey]
    }
    else {
        Get-StandaloneFallbackRegistrationRecord -SelectedClient $ResolvedClient -ResolvedInstallRoot $ResolvedInstallRoot -ResolvedArchitecture $ResolvedArchitecture
    }

    $registrations = @()
    try {
        if ($null -ne $registrationRecord) {
            $rawMode = Get-StandaloneRecordStringValue -Record $registrationRecord -PropertyNames @('mode', 'Mode', 'RegistrationMode')
            $mode = Get-StandaloneNormalizedRegistrationMode -RegistrationMode $rawMode
            $targetPath = Get-StandaloneTrustedRecordedTarget -SelectedClient $ResolvedClient -RegistrationRecord $registrationRecord
            $clientBaseId = Resolve-ClientBaseId -ClientId $ResolvedClient

            if ([string]::IsNullOrWhiteSpace($targetPath) -and [string]::Equals($mode, 'json-file', [System.StringComparison]::OrdinalIgnoreCase)) {
                if ($clientBaseId -eq 'cursor') {
                    $targetPath = Get-StandaloneTrustedCursorManifestTarget -SelectedClient $ResolvedClient -RegistrationRecord $registrationRecord
                }
                else {
                    $targetPath = Get-StandaloneTrustedManagedJsonRegistrationTarget -SelectedClient $ResolvedClient -RegistrationRecord $registrationRecord
                }
            }

            if ([string]::Equals($mode, 'artifact-only', [System.StringComparison]::OrdinalIgnoreCase) -and [string]::IsNullOrWhiteSpace($targetPath)) {
                $artifactTargets = @(Get-StandaloneTrustedOtherRegistrationArtifactTargets -RegistrationRecord $registrationRecord)
                if ($artifactTargets.Count -gt 0) {
                    $targetPath = [string]$artifactTargets[0]
                }
            }

            if ([string]::Equals($mode, 'json-file', [System.StringComparison]::OrdinalIgnoreCase)) {
                $collectionName = switch ($clientBaseId) {
                    'vscode' { 'servers' }
                    'visual-studio' { 'servers' }
                    'claude-desktop' { 'mcpServers' }
                    'cursor' { 'mcpServers' }
                    default { $null }
                }

                if (-not [string]::IsNullOrWhiteSpace($collectionName) -and -not [string]::IsNullOrWhiteSpace($targetPath)) {
                    $removal = Remove-StandaloneJsonConfigRegistration -CollectionName $collectionName -ConfigPath $targetPath
                    $registrations += [ordered]@{
                        client = $clientBaseId
                        mode = 'json-file'
                        target = $targetPath
                        backupPath = [string]$removal.backupPath
                        installedExecutable = (Get-StandaloneRecordStringValue -Record $registrationRecord -PropertyNames @('InstalledExecutable', 'installedExecutable'))
                        applied = [bool]$removal.applied
                    }
                }
            }
            elseif ([string]::Equals($mode, 'artifact-only', [System.StringComparison]::OrdinalIgnoreCase)) {
                $backupPath = $null
                $artifactTargets = @(Get-StandaloneTrustedOtherRegistrationArtifactTargets -RegistrationRecord $registrationRecord)
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
                }
                $registrations += [ordered]@{
                    client = $clientBaseId
                    mode = 'artifact-only'
                    target = $targetPath
                    backupPath = $backupPath
                    installedExecutable = (Get-StandaloneRecordStringValue -Record $registrationRecord -PropertyNames @('InstalledExecutable', 'installedExecutable'))
                    applied = (-not [string]::IsNullOrWhiteSpace($backupPath))
                }
            }
            elseif ([string]::Equals($mode, 'cli', [System.StringComparison]::OrdinalIgnoreCase)) {
                $command = if ($clientBaseId -eq 'claude-code') { 'claude' } else { 'codex' }
                $resolvedCommandPath = Resolve-StandaloneExecutableCommandPath -Command $command
                if (-not [string]::IsNullOrWhiteSpace($resolvedCommandPath)) {
                    & $resolvedCommandPath mcp remove wpf-devtools | Out-Null
                }

                $registrations += [ordered]@{
                    client = $clientBaseId
                    mode = 'cli'
                    target = $command
                    backupPath = $null
                    installedExecutable = (Get-StandaloneRecordStringValue -Record $registrationRecord -PropertyNames @('InstalledExecutable', 'installedExecutable'))
                    applied = ($LASTEXITCODE -eq 0)
                }
            }
        }

        $verification = Invoke-StandaloneUninstallVerification -SelectedClient $ResolvedClient -RegistrationRecord $registrationRecord -RegistrationChanges @($registrations)
        if (-not $verification.Succeeded) {
            throw $verification.VerificationMessage
        }

        if (-not [string]::IsNullOrWhiteSpace([string]$registrationKey) -and $state.registrations.Contains($registrationKey)) {
            [void]$state.registrations.Remove($registrationKey)
        }

        $statePath = Save-StandaloneInstallerState -State $state
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
            installedExecutable = [string]$registrationRecord.installedExecutable
            selectedClients = @($ResolvedClient)
            statePath = $statePath
            removedInstallation = $false
            registrations = @($registrations)
            verificationMessage = [string]$verification.VerificationMessage
        }
    }
    catch {
        $registrationsInReverse = @($registrations)
        [array]::Reverse($registrationsInReverse)
        foreach ($registration in $registrationsInReverse) {
            if (-not [bool]$registration.applied) {
                continue
            }

            if ([string]::Equals([string]$registration.mode, 'json-file', [System.StringComparison]::OrdinalIgnoreCase) -or
                [string]::Equals([string]$registration.mode, 'artifact-only', [System.StringComparison]::OrdinalIgnoreCase)) {
                if (-not [string]::IsNullOrWhiteSpace([string]$registration.backupPath) -and
                    -not [string]::IsNullOrWhiteSpace([string]$registration.target)) {
                    try {
                        $trustedBackupPath = Assert-InstallerLocalPathTrusted -Path ([string]$registration.backupPath)
                        $trustedTargetPath = Assert-InstallerLocalPathTrusted -Path ([string]$registration.target) -RejectHardLinks
                    }
                    catch {
                        continue
                    }

                    if (Test-Path -LiteralPath $trustedBackupPath) {
                        Copy-Item -LiteralPath $trustedBackupPath -Destination $trustedTargetPath -Force
                        Remove-PathIfExists -Path $trustedBackupPath
                    }
                }
                continue
            }

            if ([string]::Equals([string]$registration.mode, 'cli', [System.StringComparison]::OrdinalIgnoreCase) -and
                -not [string]::IsNullOrWhiteSpace([string]$registration.installedExecutable)) {
                $command = if ([string]$registration.client -eq 'claude-code') { 'claude' } else { 'codex' }
                $resolvedCommandPath = Resolve-StandaloneExecutableCommandPath -Command $command
                if (-not [string]::IsNullOrWhiteSpace($resolvedCommandPath)) {
                    & $resolvedCommandPath mcp add wpf-devtools -- ([string]$registration.installedExecutable) | Out-Null
                }
            }
        }

        throw
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

    return @($helperRoots.ToArray())
}
function Test-InstallerTestModeEnabled {
    return [bool]$script:WpfDevToolsInstallerTestModeEnabled
}
function Get-TuiHelperOverrideDirectory {
    if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_HELPER_DIRECTORY)) {
        if (-not (Test-InstallerTestModeEnabled)) {
            throw 'WPFDEVTOOLS_INSTALLER_HELPER_DIRECTORY is supported only when WPFDEVTOOLS_INSTALLER_TEST_MODE=1.'
        }

        return $env:WPFDEVTOOLS_INSTALLER_HELPER_DIRECTORY
    }

    return $null
}
function Get-TuiHelperOverrideDownloadBaseUri {
    if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_HELPER_BASE_URI)) {
        if (-not (Test-InstallerTestModeEnabled)) {
            throw 'WPFDEVTOOLS_INSTALLER_HELPER_BASE_URI is supported only when WPFDEVTOOLS_INSTALLER_TEST_MODE=1.'
        }

        return $env:WPFDEVTOOLS_INSTALLER_HELPER_BASE_URI.TrimEnd('/')
    }

    return $null
}
function Get-HelperLeafNames {
    return @($script:InstallerHelperSourcePaths | ForEach-Object { Split-Path $_ -Leaf })
}
function Resolve-InstallerBootstrapUiPath {
    foreach ($candidateRoot in @(Get-LocalInstallerHelperRoots)) {
        if ([string]::IsNullOrWhiteSpace($candidateRoot)) {
            continue
        }

        try {
            $trustedCandidateRoot = Assert-InstallerLocalPathTrusted -Path $candidateRoot
        }
        catch {
            continue
        }

        if (-not (Test-Path -LiteralPath $trustedCandidateRoot)) {
            continue
        }

        $bootstrapHelperPath = Assert-InstallerLocalPathTrusted -Path (Join-Path $trustedCandidateRoot 'Installer.BootstrapUi.ps1')
        if (Test-Path -LiteralPath $bootstrapHelperPath) {
            return $bootstrapHelperPath
        }
    }

    return $null
}

$script:InstallerBootstrapUiPath = Resolve-InstallerBootstrapUiPath
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
    function Write-TuiBootstrapScreen { param([Parameter(Mandatory)] [AllowEmptyString()] [string]$Message); if ([string]::IsNullOrWhiteSpace($Message)) { return '' }; return $Message }
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
function Get-ReleaseArchiveDownloadTimeoutSeconds {
    return (Get-InstallerTimeoutSeconds -EnvironmentVariable 'WPFDEVTOOLS_INSTALLER_DOWNLOAD_TIMEOUT_SEC' -DefaultValue 30 -MinimumValue 5 -MaximumValue 300)
}
function Get-TuiHelperBootstrapTimeoutSeconds {
    return (Get-InstallerTimeoutSeconds -EnvironmentVariable 'WPFDEVTOOLS_INSTALLER_HELPER_BOOTSTRAP_TIMEOUT_SEC' -DefaultValue 20 -MinimumValue 3 -MaximumValue 120)
}
function Get-InstallerVerificationTimeoutSeconds {
    return (Get-InstallerTimeoutSeconds -EnvironmentVariable 'WPFDEVTOOLS_INSTALLER_VERIFICATION_TIMEOUT_SEC' -DefaultValue 2 -MinimumValue 1 -MaximumValue 30)
}
function Get-Sha256FileHashHex {
    param([Parameter(Mandatory)] [string]$Path)

    if (Get-Command Get-FileHash -ErrorAction SilentlyContinue) {
        return (Get-FileHash -Algorithm SHA256 -LiteralPath $Path).Hash.ToLowerInvariant()
    }

    $stream = [System.IO.File]::OpenRead($Path)
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hashBytes = $sha256.ComputeHash($stream)
    }
    finally {
        $sha256.Dispose()
        $stream.Dispose()
    }

    return (($hashBytes | ForEach-Object { $_.ToString('x2') }) -join '')
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

        $fileHash = Get-Sha256FileHashHex -Path $helperPath
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
function Get-InstallerHelperFileSha256 {
    param([Parameter(Mandatory)] [string]$Path)

    return Get-Sha256FileHashHex -Path $Path
}
function Assert-InstallerHelperFileRecord {
    param(
        [Parameter(Mandatory)] [string]$HelperPath,
        [Parameter(Mandatory)] $HelperRecord
    )

    if (-not (Test-Path -LiteralPath $HelperPath)) {
        throw "Installer helper file was not found: $HelperPath"
    }

    $expectedPath = [string]$HelperRecord.Path
    $expectedHash = [string]$HelperRecord.Sha256
    $expectedSize = [long]$HelperRecord.SizeBytes
    $actualSize = (Get-Item -LiteralPath $HelperPath).Length
    if ($actualSize -ne $expectedSize) {
        throw "Installer helper integrity verification failed for $expectedPath. Expected size $expectedSize but found $actualSize."
    }

    $actualHash = Get-InstallerHelperFileSha256 -Path $HelperPath
    if (-not [string]::Equals($actualHash, $expectedHash, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Installer helper integrity verification failed for $expectedPath. Expected SHA-256 $expectedHash but found $actualHash."
    }
}
function Get-InstallerHelperRecordMap {
    param($Manifest)

    $recordMap = @{}
    if ($null -eq $Manifest -or $null -eq $Manifest.HelperFileRecords) {
        return $recordMap
    }

    foreach ($record in @($Manifest.HelperFileRecords)) {
        if ($null -eq $record) {
            continue
        }

        $path = [string]$record.Path
        if ([string]::IsNullOrWhiteSpace($path)) {
            continue
        }

        $recordMap[$path] = $record
    }

    return $recordMap
}
function Assert-InstallerHelperManifestIntegrity {
    param(
        [Parameter(Mandatory)] [string]$HelperDirectory,
        [Parameter(Mandatory)] $Manifest
    )

    $expectedHelperFiles = @(Get-HelperLeafNames | Sort-Object)
    $manifestHelperFiles = @($Manifest.HelperFiles | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | Sort-Object -Unique)
    $difference = Compare-Object -ReferenceObject $expectedHelperFiles -DifferenceObject $manifestHelperFiles
    if ($difference.Count -gt 0) {
        throw 'Installer helper manifest must exactly match the expected helper file set.'
    }

    $recordMap = Get-InstallerHelperRecordMap -Manifest $Manifest
    foreach ($helperFile in $expectedHelperFiles) {
        if (-not $recordMap.ContainsKey($helperFile)) {
            throw "Installer helper manifest is missing integrity metadata for $helperFile."
        }

        Assert-InstallerHelperFileRecord -HelperPath (Join-Path $HelperDirectory $helperFile) -HelperRecord $recordMap[$helperFile]
    }
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

    try {
        $trustedManifestPath = Assert-InstallerLocalPathTrusted -Path $ManifestPath
        $trustedHelperDirectory = Assert-InstallerLocalPathTrusted -Path $HelperDirectory
    }
    catch {
        return $null
    }

    if (-not (Test-Path -LiteralPath $trustedManifestPath)) {
        return $null
    }

    $parsed = Get-Content -LiteralPath $trustedManifestPath -Raw | ConvertFrom-Json
    $helperFiles = @()
    $helperFileRecords = New-Object System.Collections.Generic.List[object]
    if ($null -ne $parsed.helperFiles) {
        foreach ($entry in $parsed.helperFiles) {
            if ($entry -is [string]) {
                if (-not [string]::IsNullOrWhiteSpace([string]$entry)) {
                    $helperFiles += [string]$entry
                }
                continue
            }

            if ($null -eq $entry) {
                continue
            }

            $path = [string]$entry.path
            $sha256 = [string]$entry.sha256
            $sizeBytes = [long]$entry.sizeBytes
            if (-not [string]::IsNullOrWhiteSpace($path)) {
                $helperFiles += $path
                if (-not [string]::IsNullOrWhiteSpace($sha256) -and $sizeBytes -gt 0) {
                    $helperFileRecords.Add([ordered]@{
                            Path = $path
                            Sha256 = $sha256.ToLowerInvariant()
                            SizeBytes = $sizeBytes
                        })
                }
            }
        }
    }

    if ($helperFiles.Count -eq 0) {
        $helperFiles = @(Get-HelperLeafNames)
    }

    $cacheKey = [string]$parsed.cacheKey
    if ([string]::IsNullOrWhiteSpace($cacheKey)) {
        $cacheKey = Get-ComputedInstallerHelperCacheKey -HelperDirectory $trustedHelperDirectory -HelperFiles $helperFiles
    }

    return [ordered]@{
        CacheKey = $cacheKey
        HelperFiles = @($helperFiles)
        HelperFileRecords = @($helperFileRecords.ToArray())
    }
}
if (-not [string]::IsNullOrWhiteSpace($script:InstallerBootstrapUiPath)) {
    $helperDirectory = Split-Path -Parent $script:InstallerBootstrapUiPath
    $manifest = Read-TuiHelperManifest -ManifestPath (Get-TuiHelperManifestPath -RootPath $helperDirectory) -HelperDirectory $helperDirectory
    if ($null -ne $manifest) {
        $recordMap = Get-InstallerHelperRecordMap -Manifest $manifest
        if (-not $recordMap.ContainsKey('Installer.BootstrapUi.ps1')) {
            throw 'Installer helper manifest is missing integrity metadata for Installer.BootstrapUi.ps1.'
        }
        Assert-InstallerHelperFileRecord -HelperPath $script:InstallerBootstrapUiPath -HelperRecord $recordMap['Installer.BootstrapUi.ps1']
        . $script:InstallerBootstrapUiPath
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
            continue
        }

        Assert-InstallerHelperManifestIntegrity -HelperDirectory $candidateRoot -Manifest $manifest

        $script:TuiHelperManifest = $manifest
        return $manifest
    }

    if (Resolve-InstallerMode -ne 'online') {
        return $null
    }

    $downloadBaseUri = Resolve-TuiHelperDownloadBaseUri
    if ([string]::IsNullOrWhiteSpace($downloadBaseUri)) {
        return $null
    }

    $runtimeRoot = Get-TuiHelperRuntimeRoot
    $manifestPath = Get-TuiHelperManifestPath -RootPath $runtimeRoot

    $manifestUri = "$downloadBaseUri/$($script:InstallerHelperManifestFileName)"
    $temporaryManifestPath = "$manifestPath.download"
    try {
        if (-not $SuppressBootstrapOutput) {
            Write-TuiBootstrapScreen 'Preparing installer UI... (manifest)' | Out-Host
        }
        Invoke-WebRequest -Uri $manifestUri -OutFile $temporaryManifestPath -Headers @{ 'User-Agent' = 'wpf-devtools-online-installer' } -TimeoutSec (Get-TuiHelperRequestTimeoutSeconds)
        Move-StandalonePathWithRetry -SourcePath $temporaryManifestPath -DestinationPath $manifestPath
    }
    catch {
        Remove-PathIfExists -Path $temporaryManifestPath
        throw "Failed to download installer helper manifest from $manifestUri. $($_.Exception.Message)"
    }

    $script:TuiHelperManifest = Read-TuiHelperManifest -ManifestPath $manifestPath -HelperDirectory $runtimeRoot
    if ($null -eq $script:TuiHelperManifest) {
        throw "Installer helper manifest was not found after download: $manifestPath"
    }

    Assert-InstallerHelperManifestIntegrity -HelperDirectory $runtimeRoot -Manifest $script:TuiHelperManifest
    return $script:TuiHelperManifest
}
function Ensure-TuiHelpersAvailable {
    param([switch]$SuppressBootstrapOutput)

    if (-not [string]::IsNullOrWhiteSpace($script:TuiHelperResolvedRoot)) {
        return $script:TuiHelperResolvedRoot
    }

    $manifest = Get-TuiHelperManifest -SuppressBootstrapOutput:$SuppressBootstrapOutput
    $helperFiles = @(Get-HelperLeafNames)
    foreach ($candidateRoot in @(Get-LocalInstallerHelperRoots)) {
        if ([string]::IsNullOrWhiteSpace($candidateRoot)) {
            continue
        }

        try {
            $trustedCandidateRoot = Assert-InstallerLocalPathTrusted -Path $candidateRoot
        }
        catch {
            continue
        }

        $allPresent = $true
        foreach ($helperFile in $helperFiles) {
            $helperPath = Assert-InstallerLocalPathTrusted -Path (Join-Path $trustedCandidateRoot $helperFile)
            if (-not (Test-Path -LiteralPath $helperPath)) {
                $allPresent = $false
                break
            }
        }

        if ($allPresent) {
            if ($null -ne $manifest) {
                Assert-InstallerHelperManifestIntegrity -HelperDirectory $trustedCandidateRoot -Manifest $manifest
            }
            $script:TuiHelperResolvedRoot = $trustedCandidateRoot
            return $trustedCandidateRoot
        }
    }

    if (Resolve-InstallerMode -eq 'offline' -and (Test-PackageArchiveRequested)) {
        $runtimeRoot = Get-TuiHelperRuntimeRoot
        $helperFiles = @(Get-HelperLeafNames)
        $archivePath = [string]$PackageArchivePath

        if ([string]::IsNullOrWhiteSpace($archivePath)) {
            return $null
        }

        Remove-PathIfExists -Path $runtimeRoot
        New-Item -ItemType Directory -Force -Path $runtimeRoot | Out-Null
        $trustedArchivePath = Initialize-TrustedLocalPackageArchiveCopy `
            -ArchivePath $archivePath `
            -DestinationRoot $runtimeRoot `
            -HelperFiles $helperFiles `
            -ResolvedVersion $Version `
            -ResolvedArchitecture (Resolve-TuiHelperBootstrapArchitecture)

        $manifestPath = Get-TuiHelperManifestPath -RootPath $runtimeRoot
        $manifest = Read-TuiHelperManifest -ManifestPath $manifestPath -HelperDirectory $runtimeRoot
        if ($null -eq $manifest) {
            throw "Installer helper manifest was not found in package runtime: $manifestPath"
        }

        $script:TuiHelperManifest = $manifest
        Assert-InstallerHelperManifestIntegrity -HelperDirectory $runtimeRoot -Manifest $manifest
        $script:TuiHelperResolvedRoot = $runtimeRoot
        return $runtimeRoot
    }

    if (Resolve-InstallerMode -ne 'online') {
        return $null
    }

    $runtimeRoot = Get-TuiHelperRuntimeRoot
    $cacheKeyPath = Get-TuiHelperCacheKeyPath -RuntimeRoot $runtimeRoot
    $downloadBaseUri = Resolve-TuiHelperDownloadBaseUri
    Remove-PathIfExists -Path $runtimeRoot
    New-Item -ItemType Directory -Force -Path $runtimeRoot | Out-Null
    $runtimeRoot = Assert-InstallerLocalPathTrusted -Path $runtimeRoot
    $cacheKeyPath = Assert-InstallerLocalPathTrusted -Path $cacheKeyPath

    if (-not [string]::IsNullOrWhiteSpace($downloadBaseUri)) {
        if ($null -eq $manifest) {
            throw "Installer helper manifest could not be resolved from $downloadBaseUri"
        }

        $runtimeManifestPath = Assert-InstallerLocalPathTrusted -Path (Get-TuiHelperManifestPath -RootPath $runtimeRoot)
        $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $runtimeManifestPath -Encoding UTF8

        $requestTimeoutSeconds = Get-TuiHelperRequestTimeoutSeconds
        $bootstrapDeadline = [DateTimeOffset]::UtcNow.AddSeconds((Get-TuiHelperBootstrapTimeoutSeconds))
        $totalHelperCount = $helperFiles.Count
        $downloadIndex = 0
        $helperRecordMap = Get-InstallerHelperRecordMap -Manifest $manifest

        foreach ($helperFile in $helperFiles) {
            if ([DateTimeOffset]::UtcNow -gt $bootstrapDeadline) {
                throw 'Installer UI bootstrap timed out before the runtime assets finished downloading.'
            }

            $destinationPath = Assert-InstallerLocalPathTrusted -Path (Join-Path $runtimeRoot $helperFile)
            $downloadIndex += 1
            if (-not $SuppressBootstrapOutput) {
                Write-TuiBootstrapScreen "Preparing installer UI... ($downloadIndex/$totalHelperCount)" | Out-Host
            }
            $downloadUri = "$downloadBaseUri/$helperFile"
            $temporaryPath = Assert-InstallerLocalPathTrusted -Path "$destinationPath.download"
            try {
                Invoke-WebRequest -Uri $downloadUri -OutFile $temporaryPath -Headers @{ 'User-Agent' = 'wpf-devtools-online-installer' } -TimeoutSec $requestTimeoutSeconds
                if ($helperRecordMap.ContainsKey($helperFile)) {
                    Assert-InstallerHelperFileRecord -HelperPath $temporaryPath -HelperRecord $helperRecordMap[$helperFile]
                }
                Move-StandalonePathWithRetry -SourcePath $temporaryPath -DestinationPath $destinationPath
            }
            catch {
                Remove-PathIfExists -Path $temporaryPath
                throw "Failed to download installer UI runtime from $downloadUri. $($_.Exception.Message)"
            }
        }

        Set-Content -LiteralPath $cacheKeyPath -Value (Get-InstallerHelperRuntimeCacheKey -Manifest $manifest) -Encoding UTF8
        $script:TuiHelperResolvedRoot = $runtimeRoot
        return $runtimeRoot
    }

    $archiveDownload = Get-TuiHelperArchiveDownloadDetails
    $archivePath = Assert-InstallerLocalPathTrusted -Path (Join-Path $runtimeRoot 'helper-bootstrap-package.zip')
    $temporaryArchivePath = Assert-InstallerLocalPathTrusted -Path "$archivePath.download"
    try {
        if (-not $SuppressBootstrapOutput) {
            Write-TuiBootstrapScreen 'Preparing installer UI... (archive)' | Out-Host
        }

        Invoke-WebRequest -Uri ([string]$archiveDownload.DownloadUri) -OutFile $temporaryArchivePath -Headers @{ 'User-Agent' = 'wpf-devtools-online-installer' } -TimeoutSec (Get-TuiHelperRequestTimeoutSeconds)
        Move-StandalonePathWithRetry -SourcePath $temporaryArchivePath -DestinationPath $archivePath
        Assert-TuiHelperArchiveIntegrity -ArchivePath $archivePath -DownloadDetails $archiveDownload
        Copy-InstallerHelperBundleFromArchive -ArchivePath $archivePath -DestinationRoot $runtimeRoot -HelperFiles $helperFiles
    }
    catch {
        Remove-PathIfExists -Path $temporaryArchivePath
        throw "Failed to download installer UI runtime from $([string]$archiveDownload.DownloadUri). $($_.Exception.Message)"
    }

    $manifestPath = Get-TuiHelperManifestPath -RootPath $runtimeRoot
    $manifest = Read-TuiHelperManifest -ManifestPath $manifestPath -HelperDirectory $runtimeRoot
    if ($null -eq $manifest) {
        throw "Installer helper manifest was not found in helper bootstrap archive: $manifestPath"
    }

    $script:TuiHelperManifest = $manifest
    Assert-InstallerHelperManifestIntegrity -HelperDirectory $runtimeRoot -Manifest $manifest
    Set-Content -LiteralPath $cacheKeyPath -Value (Get-InstallerHelperRuntimeCacheKey -Manifest $manifest) -Encoding UTF8
    $script:TuiHelperBootstrapArchive = [ordered]@{
        ArchivePath = $archivePath
        DownloadUri = [string]$archiveDownload.DownloadUri
        AssetName = [string]$archiveDownload.AssetName
        ResolvedVersion = [string]$archiveDownload.ResolvedVersion
        ResolvedArchitecture = [string](Resolve-TuiHelperBootstrapArchitecture)
    }
    $script:TuiHelperResolvedRoot = $runtimeRoot
    return $runtimeRoot
}
function Get-InstallerSharedModulePaths {
    param([switch]$AllowMissing)

    try {
        $helperRoot = Ensure-TuiHelpersAvailable -SuppressBootstrapOutput
    }
    catch {
        if ($AllowMissing) {
            return @()
        }

        throw
    }

    if ([string]::IsNullOrWhiteSpace($helperRoot)) {
        if ($AllowMissing) {
            return @()
        }

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
function Get-ReleaseArchiveIdentity {
    param(
        [Parameter(Mandatory)] [string]$ArchivePath,
        [string]$ResolvedVersion,
        [string]$ResolvedArchitecture
    )

    $archiveName = Split-Path -Leaf $ArchivePath
    $match = [regex]::Match($archiveName, '^release_(?<version>.+)_win-(?<architecture>x64|x86|arm64)(?: \(\d+\))?\.zip$', 'IgnoreCase')
    if ($match.Success) {
        $versionFromName = [string]$match.Groups['version'].Value
        $architectureFromName = [string]$match.Groups['architecture'].Value.ToLowerInvariant()
        return [ordered]@{
            AssetName = (Get-ReleaseAssetName -ResolvedVersion $versionFromName -ResolvedArchitecture $architectureFromName)
            ResolvedVersion = $versionFromName
            ResolvedArchitecture = $architectureFromName
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($ResolvedVersion) -and
        $ResolvedVersion -ne 'latest' -and
        -not [string]::IsNullOrWhiteSpace($ResolvedArchitecture)) {
        return [ordered]@{
            AssetName = (Get-ReleaseAssetName -ResolvedVersion $ResolvedVersion -ResolvedArchitecture $ResolvedArchitecture)
            ResolvedVersion = $ResolvedVersion
            ResolvedArchitecture = $ResolvedArchitecture
        }
    }

    return $null
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
    return (Get-TuiHelperOverrideDownloadBaseUri)
}
function Resolve-TuiHelperBootstrapArchitecture {
    if ([string]::IsNullOrWhiteSpace($Architecture)) {
        return (Get-SystemDefaultArchitecture)
    }

    return [string]$Architecture
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
function Get-TuiHelperReleaseAssetRecordsFromManifestObject {
    param($ManifestObject)

    $records = @()
    if ($null -eq $ManifestObject -or $null -eq $ManifestObject.assets) {
        return $records
    }

    foreach ($asset in @($ManifestObject.assets)) {
        $assetName = [string]$asset.name
        $sha256 = [string]$asset.sha256
        if ([string]::IsNullOrWhiteSpace($assetName) -or [string]::IsNullOrWhiteSpace($sha256)) {
            continue
        }

        $records += ,([ordered]@{
                AssetName = $assetName
                Sha256 = $sha256.ToLowerInvariant()
            })
    }

    return $records
}
function Get-TuiHelperReleaseAssetRecordsFromChecksumContent {
    param([AllowEmptyString()] [string]$Content)

    $records = @()
    foreach ($rawLine in ($Content -split "`r?`n")) {
        $line = [string]$rawLine
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        $match = [regex]::Match($line.Trim(), '^(?<sha>[0-9A-Fa-f]{64})\s+\*?(?<name>.+)$')
        if (-not $match.Success) {
            continue
        }

        $records += ,([ordered]@{
                AssetName = [string]$match.Groups['name'].Value.Trim()
                Sha256 = [string]$match.Groups['sha'].Value.ToLowerInvariant()
            })
    }

    return $records
}
function Get-TuiHelperReleaseAssetRecordsFromGitHub {
    param([Parameter(Mandatory)] [string]$ResolvedVersion)

    $cacheKey = [string]$ResolvedVersion
    if ($script:GitHubReleaseChecksumRecordCache.Contains($cacheKey)) {
        return @($script:GitHubReleaseChecksumRecordCache[$cacheKey])
    }

    $records = @()
    $release = Get-GitHubReleaseApiResponse -ResolvedVersion $ResolvedVersion
    if ($null -ne $release -and $null -ne $release.assets) {
        $manifestAsset = @($release.assets) | Where-Object { $_.name -eq 'release-assets.json' } | Select-Object -First 1
        if ($null -ne $manifestAsset) {
            try {
                $manifest = Invoke-RestMethod -Uri ([string]$manifestAsset.browser_download_url) -Headers @{ 'User-Agent' = 'wpf-devtools-online-installer' } -TimeoutSec 15
                $records = @(Get-TuiHelperReleaseAssetRecordsFromManifestObject -ManifestObject $manifest)
            }
            catch {
            }
        }

        if ($records.Count -eq 0) {
            $checksumAsset = @($release.assets) | Where-Object { $_.name -eq 'SHA256SUMS.txt' } | Select-Object -First 1
            if ($null -ne $checksumAsset) {
                try {
                    $checksumResponse = Invoke-WebRequest -Uri ([string]$checksumAsset.browser_download_url) -Headers @{ 'User-Agent' = 'wpf-devtools-online-installer' } -TimeoutSec 15
                    $records = @(Get-TuiHelperReleaseAssetRecordsFromChecksumContent -Content ([string]$checksumResponse.Content))
                }
                catch {
                }
            }
        }
    }

    $script:GitHubReleaseChecksumRecordCache[$cacheKey] = @($records)
    return @($records)
}
function Get-TuiHelperReleaseAssetRecord {
    param(
        [Parameter(Mandatory)] [string]$ResolvedVersion,
        [string]$AssetName,
        [string]$ArchiveHash
    )

    $records = @(Get-TuiHelperReleaseAssetRecordsFromGitHub -ResolvedVersion $ResolvedVersion)
    if (-not [string]::IsNullOrWhiteSpace($AssetName)) {
        $namedRecord = @($records | Where-Object { [string]$_.AssetName -eq $AssetName } | Select-Object -First 1)
        if ($namedRecord.Count -gt 0) {
            return $namedRecord[0]
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($ArchiveHash)) {
        $hashRecord = @($records | Where-Object { [string]$_.Sha256 -eq $ArchiveHash } | Select-Object -First 1)
        if ($hashRecord.Count -gt 0) {
            return $hashRecord[0]
        }
    }

    return $null
}
function Find-LocalReleaseAssetRecord {
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [object[]]$Records,
        [string]$AssetName,
        [string]$ArchiveHash
    )

    if (-not [string]::IsNullOrWhiteSpace($AssetName)) {
        $namedRecord = @($Records | Where-Object { [string]$_.AssetName -eq $AssetName } | Select-Object -First 1)
        if ($namedRecord.Count -gt 0) {
            return $namedRecord[0]
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($ArchiveHash)) {
        $hashRecord = @($Records | Where-Object { [string]$_.Sha256 -eq $ArchiveHash } | Select-Object -First 1)
        if ($hashRecord.Count -gt 0) {
            return $hashRecord[0]
        }
    }

    return $null
}
function Get-LocalReleaseAssetRecordFromDirectory {
    param(
        [Parameter(Mandatory)] [string]$DirectoryPath,
        [string]$AssetName,
        [string]$ArchiveHash
    )

    if ([string]::IsNullOrWhiteSpace($DirectoryPath) -or -not (Test-Path -LiteralPath $DirectoryPath -PathType Container)) {
        return $null
    }

    $manifestPath = Join-Path $DirectoryPath 'release-assets.json'
    if (Test-Path -LiteralPath $manifestPath) {
        try {
            $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
            $manifestRecord = Find-LocalReleaseAssetRecord `
                -Records @(Get-TuiHelperReleaseAssetRecordsFromManifestObject -ManifestObject $manifest) `
                -AssetName $AssetName `
                -ArchiveHash $ArchiveHash
            if ($null -ne $manifestRecord) {
                return $manifestRecord
            }
        }
        catch {
        }
    }

    $checksumPath = Join-Path $DirectoryPath 'SHA256SUMS.txt'
    if (Test-Path -LiteralPath $checksumPath) {
        return (Find-LocalReleaseAssetRecord `
                -Records @(Get-TuiHelperReleaseAssetRecordsFromChecksumContent -Content (Get-Content -LiteralPath $checksumPath -Raw)) `
                -AssetName $AssetName `
                -ArchiveHash $ArchiveHash)
    }

    return $null
}
function Resolve-TrustedLocalPackageMetadataDirectory {
    param([Parameter(Mandatory)] [string]$ArchivePath)

    $trustedDirectoryEntry = Get-Item Env:WPFDEVTOOLS_TRUSTED_RELEASE_METADATA_DIRECTORY -ErrorAction SilentlyContinue
    if ($null -ne $trustedDirectoryEntry) {
        $trustedDirectory = [string]$trustedDirectoryEntry.Value
        if ([string]::IsNullOrWhiteSpace($trustedDirectory)) {
            throw 'TrustedReleaseMetadataDirectory must not be empty when specified.'
        }

        $resolvedDirectory = Assert-InstallerLocalPathTrusted -Path $trustedDirectory
        if (-not (Test-Path -LiteralPath $resolvedDirectory -PathType Container)) {
            throw "TrustedReleaseMetadataDirectory was not found: $resolvedDirectory"
        }

        return $resolvedDirectory
    }

    if ((Test-InstallerTestModeEnabled) -and
        [string]::Equals([string]$env:WPFDEVTOOLS_TEST_TRUST_LOCAL_ARCHIVE_RELEASE_METADATA, '1', [System.StringComparison]::Ordinal)) {
        return (Split-Path -Parent $ArchivePath)
    }

    return $null
}
function Assert-LocalPackageArchiveTrustedForHelperBootstrap {
    param(
        [Parameter(Mandatory)] [string]$ArchivePath,
        [string]$MetadataArchivePath,
        [string]$ResolvedVersion,
        [string]$ResolvedArchitecture
    )

    $resolvedArchivePath = (Resolve-Path -LiteralPath (Assert-InstallerLocalPathTrusted -Path $ArchivePath)).Path
    $metadataArchive = if ([string]::IsNullOrWhiteSpace($MetadataArchivePath)) {
        $resolvedArchivePath
    }
    else {
        (Resolve-Path -LiteralPath (Assert-InstallerLocalPathTrusted -Path $MetadataArchivePath)).Path
    }
    $archiveHash = Get-Sha256FileHashHex -Path $resolvedArchivePath
    $archiveIdentity = Get-ReleaseArchiveIdentity -ArchivePath $metadataArchive -ResolvedVersion $ResolvedVersion -ResolvedArchitecture $ResolvedArchitecture
    $canonicalAssetName = if ($null -ne $archiveIdentity) { [string]$archiveIdentity.AssetName } else { $null }
    if ([string]::IsNullOrWhiteSpace($canonicalAssetName)) {
        throw "Archive integrity could not be verified for local package archive '$metadataArchive' because the canonical release asset identity could not be resolved from the archive name."
    }

    $metadataDirectory = Resolve-TrustedLocalPackageMetadataDirectory -ArchivePath $metadataArchive
    if ([string]::IsNullOrWhiteSpace($metadataDirectory)) {
        throw "Archive integrity could not be verified for local package $canonicalAssetName because no trusted release metadata was found for the local artifact. Provide release-assets.json or SHA256SUMS.txt via WPFDEVTOOLS_TRUSTED_RELEASE_METADATA_DIRECTORY, or use the GitHub release download workflow instead of a local package archive."
    }

    $releaseRecord = Get-LocalReleaseAssetRecordFromDirectory `
        -DirectoryPath $metadataDirectory `
        -AssetName $canonicalAssetName `
        -ArchiveHash $archiveHash
    if ($null -eq $releaseRecord) {
        throw "Archive integrity could not be verified for $canonicalAssetName because no matching release checksum metadata was found."
    }

    if ([string]$releaseRecord.Sha256 -ne $archiveHash) {
        throw "Archive integrity verification failed for $([string]$releaseRecord.AssetName). Expected SHA256 $([string]$releaseRecord.Sha256) but got $archiveHash."
    }
}
function Initialize-TrustedLocalPackageArchiveCopy {
    param(
        [Parameter(Mandatory)] [string]$ArchivePath,
        [Parameter(Mandatory)] [string]$DestinationRoot,
        [Parameter(Mandatory)] [string[]]$HelperFiles,
        [string]$ResolvedVersion,
        [string]$ResolvedArchitecture
    )

    if (-not [string]::IsNullOrWhiteSpace($script:TrustedLocalPackageArchivePath) -and
        (Test-Path -LiteralPath $script:TrustedLocalPackageArchivePath)) {
        return $script:TrustedLocalPackageArchivePath
    }

    $sourceArchivePath = (Resolve-Path -LiteralPath (Assert-InstallerLocalPathTrusted -Path $ArchivePath)).Path
    $trustedDestinationRoot = Assert-InstallerLocalPathTrusted -Path $DestinationRoot
    New-Item -ItemType Directory -Force -Path $trustedDestinationRoot | Out-Null
    Assert-InstallerLocalPathTrusted -Path $trustedDestinationRoot | Out-Null
    $trustedArchivePath = Assert-InstallerLocalPathTrusted -Path (Join-Path $trustedDestinationRoot (Split-Path -Leaf $sourceArchivePath))
    Copy-Item -LiteralPath $sourceArchivePath -Destination $trustedArchivePath -Force -ErrorAction Stop

    $trustedArchiveStream = [System.IO.File]::Open(
        $trustedArchivePath,
        [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Read,
        [System.IO.FileShare]::Read)
    try {
        Assert-LocalPackageArchiveTrustedForHelperBootstrap `
            -ArchivePath $trustedArchivePath `
            -MetadataArchivePath $sourceArchivePath `
            -ResolvedVersion $ResolvedVersion `
            -ResolvedArchitecture $ResolvedArchitecture

        $script:TrustedLocalPackageArchivePath = $trustedArchivePath
        $script:PackageArchivePath = $trustedArchivePath
        Copy-InstallerHelperBundleFromArchive -ArchivePath $trustedArchivePath -DestinationRoot $trustedDestinationRoot -HelperFiles $HelperFiles
    }
    finally {
        $trustedArchiveStream.Dispose()
    }

    return $trustedArchivePath
}
function Get-TuiHelperArchiveSha256 {
    param([Parameter(Mandatory)] [string]$ArchivePath)

    return Get-Sha256FileHashHex -Path $ArchivePath
}
function Get-TuiHelperArchiveDownloadDetails {
    $resolvedArchitecture = Resolve-TuiHelperBootstrapArchitecture
    $downloadVersion = Resolve-RequestedReleaseVersion -RequestedVersion $Version
    return (Get-ReleaseAssetDownloadDetails -ResolvedVersion $downloadVersion -ResolvedArchitecture $resolvedArchitecture)
}
function Assert-TuiHelperArchiveIntegrity {
    param(
        [Parameter(Mandatory)] [string]$ArchivePath,
        [Parameter(Mandatory)] $DownloadDetails
    )

    $archiveHash = Get-TuiHelperArchiveSha256 -ArchivePath $ArchivePath
    $releaseRecord = Get-TuiHelperReleaseAssetRecord `
        -ResolvedVersion ([string]$DownloadDetails.ResolvedVersion) `
        -AssetName ([string]$DownloadDetails.AssetName) `
        -ArchiveHash $archiveHash

    if ($null -eq $releaseRecord) {
        throw "Installer helper bootstrap archive integrity could not be verified for $([string]$DownloadDetails.AssetName) because no matching release checksum metadata was found."
    }

    if ([string]$releaseRecord.Sha256 -ne $archiveHash) {
        throw "Installer helper bootstrap archive integrity verification failed for $([string]$releaseRecord.AssetName). Expected SHA256 $([string]$releaseRecord.Sha256) but got $archiveHash."
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

    $stateRoot = Assert-InstallerLocalPathTrusted -Path (Join-Path $env:APPDATA 'WpfDevToolsMcp')
    if ($CreateRoot) {
        New-Item -ItemType Directory -Force -Path $stateRoot | Out-Null
        Assert-InstallerLocalPathTrusted -Path $stateRoot | Out-Null
    }

    return (Join-Path $stateRoot 'latest-release-cache.json')
}
function Get-CachedLatestInstallerVersion {
    $cachePath = Resolve-LatestVersionCachePath
    if (-not (Test-Path -LiteralPath $cachePath)) {
        return $null
    }

    try {
        $parsed = Get-Content -LiteralPath $cachePath -Raw | ConvertFrom-Json
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
    Assert-InstallerLocalPathTrusted -Path $cachePath -RejectHardLinks | Out-Null
    [ordered]@{
        version = $VersionValue
        refreshedUtc = [DateTime]::UtcNow.ToString('o')
    } | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $cachePath -Encoding UTF8
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
        Assert-ArchiveSafeEntries -ArchivePath $archivePath -DestinationPath $extractRoot
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
    New-Item -ItemType Directory -Force -Path $sessionRoot | Out-Null
    $archivePath = $null
    if ($null -ne $script:TuiHelperBootstrapArchive -and
        [string]::Equals([string]$script:TuiHelperBootstrapArchive.ResolvedVersion, [string]$downloadDetails.ResolvedVersion, [System.StringComparison]::OrdinalIgnoreCase) -and
        [string]::Equals([string]$script:TuiHelperBootstrapArchive.ResolvedArchitecture, [string]$ResolvedArchitecture, [System.StringComparison]::OrdinalIgnoreCase) -and
        -not [string]::IsNullOrWhiteSpace([string]$script:TuiHelperBootstrapArchive.ArchivePath) -and
        (Test-Path -LiteralPath ([string]$script:TuiHelperBootstrapArchive.ArchivePath))) {
        $archivePath = [string]$script:TuiHelperBootstrapArchive.ArchivePath
    }
    else {
        $archivePath = Join-Path $sessionRoot ([string]$downloadDetails.AssetName)
        Invoke-WebRequest -Uri ([string]$downloadDetails.DownloadUri) -OutFile $archivePath -Headers @{ 'User-Agent' = 'wpf-devtools-online-installer' } -TimeoutSec (Get-ReleaseArchiveDownloadTimeoutSeconds)
    }

    $integrity = Assert-ArchiveIntegrity -ArchivePath $archivePath -DownloadSource 'github-release' -ResolvedVersion ([string]$downloadDetails.ResolvedVersion) -ResolvedArchitecture $ResolvedArchitecture
    New-Item -ItemType Directory -Force -Path $extractRoot | Out-Null
    Assert-ArchiveSafeEntries -ArchivePath $archivePath -DestinationPath $extractRoot
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
function Read-ValidatedVersion {
    param(
        [Parameter(Mandatory)] [string]$Prompt,
        [Parameter(Mandatory)] [string]$DefaultValue
    )

    while ($true) {
        $response = Read-InstallerInput -Prompt $Prompt -DefaultValue $DefaultValue
        if ([string]::IsNullOrWhiteSpace($response)) {
            return $DefaultValue
        }

        $normalized = $response.Trim()
        if ($normalized -eq 'latest') {
            return $normalized
        }

        if ($normalized -match '^v?\d+\.\d+\.\d+(-[0-9A-Za-z][0-9A-Za-z.-]*)?$') {
            return $normalized.TrimStart('v', 'V')
        }

        Write-InstallerMessage 'Allowed values: latest or a SemVer release such as 0.1.0'
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
    $defaultVersion = $Version
    $defaultArchitecture = if ([string]::IsNullOrWhiteSpace($Architecture)) { Get-SystemDefaultArchitecture } else { $Architecture }
    $defaultClient = if ([string]::IsNullOrWhiteSpace($Client)) { Get-DefaultClient } else { $Client }

    if ($NonInteractive -or $OutputJson) {
        $selectedInstallRoot = $defaultInstallRoot
        if (-not $script:InstallRootWasSpecified -and $defaultAction -ne 'install') {
            $selectedInstallRoot = $null
        }

        return [ordered]@{
            Action = $defaultAction
            Version = $defaultVersion
            Architecture = $defaultArchitecture
            Client = $defaultClient
            InstallRoot = $selectedInstallRoot
        }
    }

    $resolvedAction = Read-ValidatedChoice -Prompt 'Action (install/uninstall)' -DefaultValue $defaultAction -AllowedValues @('install', 'uninstall', 'full-uninstall')
    $resolvedVersion = if ($resolvedAction -eq 'install' -and -not $script:InteractiveReleaseVersionWasPrompted) {
        Read-ValidatedVersion -Prompt 'Release version' -DefaultValue $defaultVersion
    }
    else {
        $defaultVersion
    }
    $resolvedArchitecture = Read-ValidatedChoice -Prompt 'Architecture (x64/x86/arm64)' -DefaultValue $defaultArchitecture -AllowedValues @('x64', 'x86', 'arm64')
    $resolvedClient = Read-ValidatedChoice -Prompt 'Client (claude-code/codex/cursor/vscode/visual-studio/claude-desktop/other)' -DefaultValue $defaultClient -AllowedValues @('claude-code', 'codex', 'cursor', 'vscode', 'visual-studio', 'claude-desktop', 'other')
    $installRootPrompt = Read-InstallerInput -Prompt 'Install root' -DefaultValue $defaultInstallRoot
    if ([string]::IsNullOrWhiteSpace($installRootPrompt)) {
        $installRootPrompt = $defaultInstallRoot
    }

    return [ordered]@{
        Action = $resolvedAction
        Version = $resolvedVersion
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
    Assert-InstallerLocalPathTrusted -Path $refreshDirectory | Out-Null
    $releaseApiUri = Get-GitHubReleaseApiUri -ResolvedVersion 'latest'
    $escapedReleaseApiUri = ConvertTo-SingleQuotedPowerShellLiteral -Value $releaseApiUri
    $encodedCommand = ConvertTo-PowerShellEncodedCommand -CommandText @"
\$ProgressPreference = 'SilentlyContinue'
try {
    \$latestVersion = [string](Invoke-RestMethod -Uri '$escapedReleaseApiUri' -Headers @{ 'User-Agent' = 'wpf-devtools-online-installer' } -TimeoutSec 10).tag_name.TrimStart('v')
    if (-not [string]::IsNullOrWhiteSpace(\$latestVersion)) {
        [ordered]@{ version = \$latestVersion; error = \$null; exitCode = 0 } | ConvertTo-Json -Depth 3 -Compress
        exit 0
    }
    [ordered]@{ version = \$null; error = 'Latest release metadata did not return a tag_name.'; exitCode = 2 } | ConvertTo-Json -Depth 3 -Compress
    exit 2
}
catch {
    [ordered]@{ version = \$null; error = [string]\$_.Exception.Message; exitCode = 1 } | ConvertTo-Json -Depth 3 -Compress
    exit 1
}
"@

    $process = New-Object System.Diagnostics.Process
    $process.StartInfo = New-Object System.Diagnostics.ProcessStartInfo
    $process.StartInfo.FileName = (Get-Process -Id $PID).Path
    $process.StartInfo.Arguments = "-NoProfile -ExecutionPolicy Bypass -EncodedCommand $encodedCommand"
    $process.StartInfo.UseShellExecute = $false
    $process.StartInfo.RedirectStandardOutput = $true
    $process.StartInfo.RedirectStandardError = $true
    $process.StartInfo.CreateNoWindow = $true
    $null = $process.Start()

    return [ordered]@{
        Mode = 'process'
        Process = $process
    }
}
function Receive-LatestInstallerVersionRefresh {
    param([Parameter(Mandatory)] $RefreshHandle)

    if ([string]$RefreshHandle.Mode -eq 'test') {
        return [ordered]@{
            IsCompleted = $true
            Version = [string]$RefreshHandle.Version
            ErrorMessage = $null
            ExitCode = 0
        }
    }

    $process = $RefreshHandle.Process
    if ($null -eq $process) {
        return [ordered]@{
            IsCompleted = $true
            Version = $null
            ErrorMessage = $null
            ExitCode = 0
        }
    }

    if (-not $process.HasExited) {
        return [ordered]@{
            IsCompleted = $false
            Version = $null
            ErrorMessage = $null
            ExitCode = $null
        }
    }

    $resolvedVersion = $null
    $errorMessage = $null
    $exitCode = $process.ExitCode
    try {
        $outputJson = [string]$process.StandardOutput.ReadToEnd()
        $standardError = [string]$process.StandardError.ReadToEnd()
        if (-not [string]::IsNullOrWhiteSpace($outputJson)) {
            $parsed = $outputJson | ConvertFrom-Json
            $resolvedVersion = [string]$parsed.version
            $errorMessage = [string]$parsed.error
            if ($parsed.PSObject.Properties.Name -contains 'exitCode') {
                $exitCode = [int]$parsed.exitCode
            }
        }
        elseif (-not [string]::IsNullOrWhiteSpace($standardError)) {
            $errorMessage = $standardError.Trim()
        }
    }
    catch {
        $errorMessage = [string]$_.Exception.Message
    }

    try {
        $process.Dispose()
    }
    catch {
    }

    if (-not [string]::IsNullOrWhiteSpace($resolvedVersion)) {
        Save-LatestInstallerVersionCache -VersionValue $resolvedVersion
    }
    elseif ([string]::IsNullOrWhiteSpace($errorMessage) -and $exitCode -ne 0) {
        $errorMessage = "Background metadata refresh exited with code $exitCode."
    }

    return [ordered]@{
        IsCompleted = $true
        Version = $resolvedVersion
        ErrorMessage = $errorMessage
        ExitCode = $exitCode
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
        if (Resolve-InstallerMode -eq 'offline') {
            Close-TuiBootstrapScreen
            throw "The installer runtime bundled with this package failed integrity or bootstrap validation. $script:LastTuiBootstrapFailureReason"
        }

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
        [Parameter(Mandatory)] [AllowEmptyString()] [AllowNull()] [string]$ResolvedInstallRoot,
        [Parameter(Mandatory)] [string]$RequestedVersion,
        [switch]$UseLatestRelease
    )

    Assert-InstallerHelperRuntimeAvailable -ResolvedAction $ResolvedAction
    $sharedModulePaths = if ($ResolvedAction -eq 'install') {
        @(Get-InstallerSharedModulePaths)
    }
    else {
        @(Get-InstallerSharedModulePaths -AllowMissing)
    }
    $shouldUseStandaloneFallback = ($ResolvedAction -ne 'install' -and $sharedModulePaths.Count -eq 0)

    foreach ($helperPath in $sharedModulePaths) {
        . $helperPath
    }

    if ($shouldUseStandaloneFallback) {
        return (Invoke-StandaloneInstallerActionCore `
                -ResolvedAction $ResolvedAction `
                -ResolvedArchitecture $ResolvedArchitecture `
                -ResolvedClient $ResolvedClient `
                -ResolvedInstallRoot $ResolvedInstallRoot `
                -RequestedVersion $RequestedVersion `
                -UseLatestRelease:$UseLatestRelease)
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

    $script:InteractiveReleaseVersionWasPrompted = $false
    if (-not $NonInteractive -and -not $OutputJson -and $Action -eq 'install') {
        $script:Version = Read-ValidatedVersion -Prompt 'Release version' -DefaultValue $Version
        $script:InteractiveReleaseVersionWasPrompted = $true
    }

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

# TEST_BOUNDARY_MARKER: definition-only loading stops before the main entrypoint.
$selectionContext = Resolve-Selection
if ($selectionContext.Cancelled) {
    return
}
if ($selectionContext.HandledInWindow) {
    return
}

$interactiveSelection = $selectionContext.Selection
$resolvedAction = [string]$interactiveSelection.Action
$resolvedVersion = if ($interactiveSelection.PSObject.Properties.Name -contains 'Version' -and
    -not [string]::IsNullOrWhiteSpace([string]$interactiveSelection.Version)) {
    [string]$interactiveSelection.Version
}
else {
    [string]$Version
}
$resolvedArchitecture = [string]$interactiveSelection.Architecture
$resolvedClient = [string]$interactiveSelection.Client
$resolvedInstallRoot = [string]$interactiveSelection.InstallRoot
$versionHint = [string]$selectionContext.VersionHint

$result = Invoke-InstallerAction -ResolvedAction $resolvedAction -ResolvedArchitecture $resolvedArchitecture -ResolvedClient $resolvedClient -ResolvedInstallRoot $resolvedInstallRoot -RequestedVersion $resolvedVersion

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
