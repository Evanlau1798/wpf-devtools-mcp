param(
    [ValidateSet('install', 'uninstall', 'full-uninstall', 'plan')]
    [string]$Action = 'install',

    [string]$Version = 'latest',

    [switch]$Prerelease,

    [ValidateSet('x64', 'x86', 'arm64')]
    [string]$Architecture,

    [ValidateSet('claude-code', 'codex', 'grok', 'cursor', 'vscode', 'visual-studio', 'claude-desktop', 'other')]
    [string]$Client,

    [string]$InstallRoot,
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
    [switch]$OutputJson,
    [switch]$Help
)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
$script:WpfDevToolsInstallerTestModeEnabled = [bool]$script:WpfDevToolsInstallerTestModeEnabled -and [bool]$script:WpfDevToolsInstallerTestModeHarnessEnabled
$script:InstallRootWasSpecified = $PSBoundParameters.ContainsKey('InstallRoot')
$script:PackageArchivePathWasSpecified = $PSBoundParameters.ContainsKey('PackageArchivePath')
$script:TrustedReleaseMetadataDirectoryWasSpecified = $PSBoundParameters.ContainsKey('TrustedReleaseMetadataDirectory')
$script:InstallerTestResponses = New-Object System.Collections.Generic.Queue[string]

function Show-InstallerHelp {
@'
Usage:
  irm https://installer.wpf-mcptools.evanlau1798.com | iex
  & ([scriptblock]::Create((irm https://installer.wpf-mcptools.evanlau1798.com))) [options]
  powershell -NoProfile -File .\install.ps1 [options]

Actions:
  -Action install        Install and register the MCP server. This is the default.
  -Action uninstall      Remove one client registration and installed payload.
  -Action full-uninstall Remove installer-owned registrations and payloads.
  -Action plan           Print the non-mutating install plan as JSON.

Common options:
  -Version <tag|latest>  Release version or tag. Use -Prerelease for preview tags.
  -Architecture <arch>   x64, x86, or arm64.
  -Client <client>       claude-code, codex, grok, cursor, vscode, visual-studio, claude-desktop, or other.
  -InstallRoot <path>    Install root. Defaults to a per-user local app data path.
                         For full-uninstall, this scopes cleanup to that exact root.
                         Omit -InstallRoot to remove all detected installer roots.
  -PackageArchivePath <zip>
                         Install from a reviewed local release archive.
  -TrustedReleaseMetadataDirectory <path>
                         Directory containing SHA256SUMS.txt and release-assets.json for the archive.
  -NonInteractive        Do not prompt for input.
  -Force                 Allow overwrite when the selected action supports it.
  -OutputJson            Print machine-readable JSON for action results.
  -Help                  Print this help text without installing or modifying state.
'@ | Write-Output
}

if ($Help) {
    Show-InstallerHelp
    return
}

if (-not [bool]$script:WpfDevToolsInstallerTestModeEnabled) {
    $testOnlyOverrides = @(Get-ChildItem Env: | Where-Object {
            $_.Name.StartsWith('WPFDEVTOOLS_INSTALLER_TEST_', [StringComparison]::OrdinalIgnoreCase) -and
            -not [string]::Equals(
                $_.Name,
                'WPFDEVTOOLS_INSTALLER_TEST_MODE',
                [StringComparison]::OrdinalIgnoreCase) -and
            -not [string]::IsNullOrWhiteSpace([string]$_.Value)
        } | Select-Object -ExpandProperty Name | Sort-Object -Unique)
    if ($testOnlyOverrides.Count -gt 0) {
        throw "$($testOnlyOverrides -join ', ') are supported only when WPFDEVTOOLS_INSTALLER_TEST_MODE=1 with internal harness authority."
    }
    if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_HELPER_DIRECTORY)) {
        throw 'WPFDEVTOOLS_INSTALLER_HELPER_DIRECTORY is supported only when WPFDEVTOOLS_INSTALLER_TEST_MODE=1.'
    }
    if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_HELPER_BASE_URI)) {
        throw 'WPFDEVTOOLS_INSTALLER_HELPER_BASE_URI is supported only when WPFDEVTOOLS_INSTALLER_TEST_MODE=1.'
    }
}

$script:OnlineInstallerEntryScriptRoot = if (-not [string]::IsNullOrWhiteSpace($PSScriptRoot)) {
    $PSScriptRoot
}
elseif (-not [string]::IsNullOrWhiteSpace($PSCommandPath)) {
    Split-Path -Parent $PSCommandPath
}
else {
    $null
}
$script:InstallerHelperManifestCacheKey = 'sha256:c598394ea2a4b8039b5cfbf812950ab230b75389e293be342717bfb75865a2ad'
$script:OnlineInstallerRuntimeSourcePaths = @(
    'scripts/installer/OnlineInstaller.Runtime.01.ps1'
    'scripts/installer/OnlineInstaller.Runtime.02.ps1'
    'scripts/installer/OnlineInstaller.Runtime.03.ps1'
    'scripts/installer/OnlineInstaller.Runtime.04.ps1'
    'scripts/installer/OnlineInstaller.Runtime.05.ps1'
    'scripts/installer/OnlineInstaller.Runtime.06.ps1'
    'scripts/installer/OnlineInstaller.Runtime.07.ps1'
    'scripts/installer/OnlineInstaller.Runtime.08.ps1'
    'scripts/installer/OnlineInstaller.Runtime.09.ps1'
    'scripts/installer/OnlineInstaller.Runtime.10.ps1'
    'scripts/installer/OnlineInstaller.Runtime.11.ps1'
    'scripts/installer/OnlineInstaller.Runtime.12.ps1'
    'scripts/installer/OnlineInstaller.Runtime.13.ps1'
)

function Get-OnlineInstallerRuntimeSha256 {
    param([Parameter(Mandatory)] [byte[]]$Bytes)

    $sha256 = [Security.Cryptography.SHA256]::Create()
    try { $hash = $sha256.ComputeHash($Bytes) }
    finally { $sha256.Dispose() }
    return (($hash | ForEach-Object { $_.ToString('x2') }) -join '')
}

function Read-OnlineInstallerRuntimeManifest {
    param([Parameter(Mandatory)] [string]$Content)

    $manifest = $Content | ConvertFrom-Json
    $records = @{}
    foreach ($entry in @($manifest.helperFiles)) {
        $path = [string]$entry.path
        $hash = [string]$entry.sha256
        $size = [long]$entry.sizeBytes
        if ([string]::IsNullOrWhiteSpace($path) -or
            $path -ne [IO.Path]::GetFileName($path) -or
            $hash -notmatch '^[a-fA-F0-9]{64}$' -or $size -le 0) {
            throw 'Installer helper manifest contains an invalid integrity record.'
        }

        $records[$path] = [ordered]@{ Path = $path; Sha256 = $hash.ToLowerInvariant(); SizeBytes = $size }
    }

    $cacheRecords = @($records.Keys | ForEach-Object { "${_}:$([string]$records[$_].Sha256)" })
    [Array]::Sort($cacheRecords, [StringComparer]::OrdinalIgnoreCase)
    $computed = 'sha256:' + (Get-OnlineInstallerRuntimeSha256 -Bytes (
        [Text.Encoding]::UTF8.GetBytes(($cacheRecords -join '|'))))
    if (-not [string]::Equals($computed, [string]$manifest.cacheKey, [StringComparison]::OrdinalIgnoreCase) -or
        -not [string]::Equals($computed, $script:InstallerHelperManifestCacheKey, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Installer helper manifest cache key does not match the pinned installer helper manifest cache key.'
    }

    foreach ($sourcePath in $script:OnlineInstallerRuntimeSourcePaths) {
        $leafName = Split-Path $sourcePath -Leaf
        if (-not $records.ContainsKey($leafName)) {
            throw "Installer helper manifest is missing runtime fragment $leafName."
        }
    }

    return $records
}

function Assert-OnlineInstallerRuntimePayload {
    param(
        [Parameter(Mandatory)] [byte[]]$Bytes,
        [Parameter(Mandatory)] $Record
    )

    if ($Bytes.Length -ne [long]$Record.SizeBytes -or
        -not [string]::Equals(
            (Get-OnlineInstallerRuntimeSha256 -Bytes $Bytes),
            [string]$Record.Sha256,
            [StringComparison]::OrdinalIgnoreCase)) {
        throw "Installer runtime fragment integrity verification failed for $([string]$Record.Path)."
    }
}

function Get-OnlineInstallerRuntimeLocalRoots {
    $roots = New-Object Collections.Generic.List[string]
    if (-not [string]::IsNullOrWhiteSpace($script:OnlineInstallerEntryScriptRoot)) {
        $roots.Add((Join-Path $script:OnlineInstallerEntryScriptRoot 'installer'))
    }

    if ([bool]$script:WpfDevToolsInstallerTestModeEnabled) {
        if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_HELPER_DIRECTORY)) {
            $roots.Add($env:WPFDEVTOOLS_INSTALLER_HELPER_DIRECTORY)
        }
        if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_SOURCE_ROOT)) {
            $roots.Add((Join-Path $env:WPFDEVTOOLS_INSTALLER_SOURCE_ROOT 'scripts\installer'))
        }
        $roots.Add((Join-Path (Get-Location).Path 'scripts\installer'))
    }

    return @($roots | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)
}

function Read-OnlineInstallerLocalRuntime {
    foreach ($root in @(Get-OnlineInstallerRuntimeLocalRoots)) {
        $manifestPath = Join-Path $root 'installer-helpers.manifest.json'
        if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) { continue }
        $manifestBytes = [IO.File]::ReadAllBytes($manifestPath)
        $records = Read-OnlineInstallerRuntimeManifest -Content ([Text.Encoding]::UTF8.GetString($manifestBytes))
        $contents = New-Object Collections.Generic.List[string]
        foreach ($sourcePath in $script:OnlineInstallerRuntimeSourcePaths) {
            $leafName = Split-Path $sourcePath -Leaf
            $fragmentPath = Join-Path $root $leafName
            if (-not (Test-Path -LiteralPath $fragmentPath -PathType Leaf)) {
                throw "Installer runtime fragment was not found: $fragmentPath"
            }
            $bytes = [IO.File]::ReadAllBytes($fragmentPath)
            Assert-OnlineInstallerRuntimePayload -Bytes $bytes -Record $records[$leafName]
            $contents.Add([Text.Encoding]::UTF8.GetString($bytes))
        }
        return @($contents.ToArray())
    }

    return @()
}

function Invoke-OnlineInstallerRuntimeWebRequest {
    param([Parameter(Mandatory)] [string]$Uri)

    $parameters = @{ Uri = $Uri; Headers = @{ 'User-Agent' = 'wpf-devtools-online-installer' }; TimeoutSec = 15 }
    if ((Get-Command Invoke-WebRequest).Parameters.ContainsKey('UseBasicParsing')) {
        $parameters['UseBasicParsing'] = $true
    }
    return Invoke-WebRequest @parameters
}

function Read-OnlineInstallerRemoteRuntime {
    if ([bool]$script:WpfDevToolsInstallerTestModeEnabled -and
        [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_HELPER_BASE_URI)) {
        throw 'Installer runtime fragments were not found in the test helper roots.'
    }
    $baseUri = if ([bool]$script:WpfDevToolsInstallerTestModeEnabled) {
        $env:WPFDEVTOOLS_INSTALLER_HELPER_BASE_URI.TrimEnd('/')
    }
    else {
        'https://raw.githubusercontent.com/Evanlau1798/wpf-devtools-mcp/master/scripts/installer'
    }
    $manifestContent = [string](Invoke-OnlineInstallerRuntimeWebRequest -Uri "$baseUri/installer-helpers.manifest.json").Content
    $records = Read-OnlineInstallerRuntimeManifest -Content $manifestContent
    $contents = New-Object Collections.Generic.List[string]
    foreach ($sourcePath in $script:OnlineInstallerRuntimeSourcePaths) {
        $leafName = Split-Path $sourcePath -Leaf
        $content = [string](Invoke-OnlineInstallerRuntimeWebRequest -Uri "$baseUri/$leafName").Content
        $bytes = [Text.Encoding]::UTF8.GetBytes($content)
        Assert-OnlineInstallerRuntimePayload -Bytes $bytes -Record $records[$leafName]
        $contents.Add($content)
    }
    return @($contents.ToArray())
}

function Import-OnlineInstallerRuntime {
    $contents = @(Read-OnlineInstallerLocalRuntime)
    if ($contents.Count -eq 0) { $contents = @(Read-OnlineInstallerRemoteRuntime) }
    return $contents
}

foreach ($runtimeContent in @(Import-OnlineInstallerRuntime)) {
    . ([scriptblock]::Create($runtimeContent))
}
# TEST_BOUNDARY_MARKER: definition-only loading stops before the main entrypoint.
$selectionContext = Resolve-Selection
if ($selectionContext.Cancelled) {
    return
}
if ($selectionContext.HandledInWindow) {
    return
}
if ($selectionContext.Contains('IsPlan') -and [bool]$selectionContext['IsPlan']) {
    $plan = $selectionContext['Selection']
    $plan | ConvertTo-Json -Depth 10
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
    if (-not [string]::IsNullOrWhiteSpace($result.cleanupGuidance)) {
        Write-InstallerMessage $result.cleanupGuidance
    }
    if (-not [string]::IsNullOrWhiteSpace($versionHint)) {
        Write-InstallerMessage $versionHint
    }

    $manualRegistration = @($result.registrations | Where-Object {
            $null -ne $_ -and
            $_.Contains('mode') -and
            [string]::Equals([string]$_.mode, 'manual-cli-artifact', [System.StringComparison]::OrdinalIgnoreCase)
        } | Select-Object -First 1)
    if ($manualRegistration.Count -gt 0) {
        $manualTarget = [string]$manualRegistration[0].target
        Write-InstallerMessage "Manual registration required. Review the generated command file: $manualTarget"
        try {
            $manualCommand = @(Get-Content -LiteralPath $manualTarget | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 1)
            if ($manualCommand.Count -gt 0) {
                Write-InstallerMessage "Manual command: $([string]$manualCommand[0])"
            }
        }
        catch {
            Write-InstallerMessage 'Manual command could not be read from the generated artifact.'
        }
    }
}
