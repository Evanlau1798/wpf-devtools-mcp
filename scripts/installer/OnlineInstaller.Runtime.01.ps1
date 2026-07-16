function Invoke-InstallerWebRequest {
    param(
        [Parameter(Mandatory)] [string]$Uri,
        [string]$OutFile,
        [hashtable]$Headers,
        [int]$TimeoutSec
    )

    $parameters = @{
        Uri = $Uri
    }

    if (-not [string]::IsNullOrWhiteSpace($OutFile)) {
        $parameters['OutFile'] = $OutFile
    }

    if ($null -ne $Headers) {
        $parameters['Headers'] = $Headers
    }

    if ($TimeoutSec -gt 0) {
        $parameters['TimeoutSec'] = $TimeoutSec
    }

    if ((Get-Command Invoke-WebRequest).Parameters.ContainsKey('UseBasicParsing')) {
        $parameters['UseBasicParsing'] = $true
    }

    return Invoke-WebRequest @parameters
}

function Get-InstallerTestEnvironmentValue {
    param([Parameter(Mandatory)] [string]$Name)

    if (-not [bool]$script:WpfDevToolsInstallerTestModeEnabled) {
        return $null
    }

    return [Environment]::GetEnvironmentVariable(
        $Name,
        [EnvironmentVariableTarget]::Process)
}

if ($Action -ne 'plan' -and $script:TrustedReleaseMetadataDirectoryWasSpecified) {
    if ([string]::IsNullOrWhiteSpace($TrustedReleaseMetadataDirectory)) {
        throw 'TrustedReleaseMetadataDirectory must not be empty when specified.'
    }

    $env:WPFDEVTOOLS_TRUSTED_RELEASE_METADATA_DIRECTORY = [string]$TrustedReleaseMetadataDirectory
}
elseif ($Action -ne 'plan') {
    Remove-Item Env:WPFDEVTOOLS_TRUSTED_RELEASE_METADATA_DIRECTORY -ErrorAction SilentlyContinue
}

$testResponses = Get-InstallerTestEnvironmentValue -Name 'WPFDEVTOOLS_INSTALLER_TEST_RESPONSES'
if (-not [string]::IsNullOrWhiteSpace($testResponses)) {
    foreach ($entry in ($testResponses -split '\|\|')) {
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

    try {
        $resolvedPath = Assert-InstallerLocalPathTrusted -Path $Path
        if (-not (Test-Path -LiteralPath $resolvedPath -ErrorAction Stop)) {
            return
        }
    }
    catch {
        if ($BestEffort) { return }
        throw
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

                if (Test-StandaloneTransientFileSystemError -Exception $_.Exception) {
                    throw (Get-StandaloneInstallerFileSystemRecoveryMessage -Operation 'Remove installer path' -Path $resolvedPath -Exception $_.Exception)
                }

                throw
            }

            Start-Sleep -Milliseconds $RetryDelayMilliseconds
        }
    }
}
function Get-StandaloneInstallerFileSystemRecoveryMessage {
    param(
        [Parameter(Mandatory)] [string]$Operation,
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [System.Exception]$Exception
    )

    $message = "$Operation failed for '$Path'. $($Exception.Message)"
    if (Test-StandaloneTransientFileSystemError -Exception $Exception) {
        $message += " Close any running WPF target applications, MCP server processes, and terminals or Explorer windows using this install directory, then retry with -Action full-uninstall."
    }

    return $message
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
    'scripts/installer/online-installer.release-assets.ps1'
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
$script:InstallerReleaseAssetModuleLeafName = 'online-installer.release-assets.ps1'
$script:InstallerReleaseAssetModuleRepositoryRelativePath = 'scripts/installer/online-installer.release-assets.ps1'
$script:InstallerReleaseAssetModuleSha256 = 'b74b90483fbcf2e0eb2ec755c8d0583d1497bd80b1a04bf6e658a3d82fd3ec17'
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
function Get-InstallerSharedRuntimeHelperLeafNames {
    return @($script:InstallerHelperSourcePaths |
        ForEach-Object { Split-Path $_ -Leaf } |
        Where-Object {
            -not $_.StartsWith('Tui.', [System.StringComparison]::Ordinal) -and
            -not [string]::Equals($_, 'Installer.BootstrapUi.ps1', [System.StringComparison]::Ordinal)
        })
}
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
$script:InstallerSharedModulePathsCache = $null
$script:InstallerSharedModulePathsCacheIncludesInstalledRoots = $false
function Resolve-InstallerScriptRoot {
    if (-not [string]::IsNullOrWhiteSpace($script:OnlineInstallerEntryScriptRoot)) {
        return $script:OnlineInstallerEntryScriptRoot
    }

    if (-not [string]::IsNullOrWhiteSpace($PSScriptRoot)) {
        return $PSScriptRoot
    }

    if (-not [string]::IsNullOrWhiteSpace($PSCommandPath)) {
        return (Split-Path -Parent $PSCommandPath)
    }

    return $null
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
function Get-InstallerSha256Hex {
    param([Parameter(Mandatory)] [byte[]]$Bytes)

    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hashBytes = $sha256.ComputeHash($Bytes)
    }
    finally {
        $sha256.Dispose()
    }

    return (($hashBytes | ForEach-Object { $_.ToString('x2') }) -join '')
}
function Get-InstallerTextSha256Hex {
    param([Parameter(Mandatory)] [string]$Content)

    $normalizedContent = $Content.Replace("`r`n", "`n")
    return (Get-InstallerSha256Hex -Bytes ([System.Text.Encoding]::UTF8.GetBytes($normalizedContent)))
}
function Get-InstallerFileSha256Hex {
    param([Parameter(Mandatory)] [string]$Path)

    return (Get-InstallerTextSha256Hex -Content ([System.IO.File]::ReadAllText($Path)))
}
function Get-InstallerReleaseAssetModuleUri {
    $ref = if (-not [string]::IsNullOrWhiteSpace($Version) -and $Version -ne 'latest') {
        if ($Version.StartsWith('v')) { $Version } else { "v$Version" }
    }
    else {
        'master'
    }

    return "https://raw.githubusercontent.com/Evanlau1798/wpf-devtools-mcp/$ref/$script:InstallerReleaseAssetModuleRepositoryRelativePath"
}
