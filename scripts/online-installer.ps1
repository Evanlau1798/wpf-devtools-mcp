param(
    [string]$Version = 'latest',

    [ValidateSet('x64', 'x86', 'arm64')]
    [string]$Architecture = 'x64',

    [ValidateSet('claude-code', 'codex-cli', 'claude-desktop', 'cursor-vscode', 'github-copilot-vscode', 'other')]
    [string]$Client = 'claude-code',

    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA 'WpfDevToolsMcp'),
    [string]$WorkingRoot = (Join-Path ([System.IO.Path]::GetTempPath()) 'wpf-devtools-online-installer'),
    [string]$PackageArchivePath,

    [switch]$NonInteractive,
    [switch]$Force,
    [switch]$OutputJson
)

$ErrorActionPreference = 'Stop'

function Resolve-AbsoluteDirectory {
    param([Parameter(Mandatory)] [string]$Path)

    New-Item -ItemType Directory -Force -Path $Path | Out-Null
    return (Resolve-Path $Path).Path
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

function Get-SelectedClientList {
    param([Parameter(Mandatory)] [string]$SelectedClient)

    switch ($SelectedClient) {
        'claude-code' { return @('claude-code') }
        'codex-cli' { return @('codex') }
        'claude-desktop' { return @('claude-desktop') }
        'cursor-vscode' { return @('cursor') }
        'github-copilot-vscode' { return @('none') }
        'other' { return @('none') }
        default { throw "Unsupported client option: $SelectedClient" }
    }
}

function Remove-PathIfExists {
    param([string]$Path)

    if (-not [string]::IsNullOrWhiteSpace($Path) -and (Test-Path $Path)) {
        Remove-Item -Path $Path -Recurse -Force
    }
}

$workingRootPath = Resolve-AbsoluteDirectory -Path $WorkingRoot
$assetName = Get-ReleaseAssetName -ResolvedVersion $Version -ResolvedArchitecture $Architecture
$downloadUri = Get-ReleaseDownloadUri -ResolvedVersion $Version -ResolvedArchitecture $Architecture
$archivePath = if ([string]::IsNullOrWhiteSpace($PackageArchivePath)) { Join-Path $workingRootPath $assetName } else { (Resolve-Path $PackageArchivePath).Path }
$sessionRoot = Join-Path $workingRootPath ([Guid]::NewGuid().ToString('N'))
$extractRoot = Join-Path $sessionRoot 'package'
$installerScript = Join-Path $PSScriptRoot 'release\Install-WpfDevTools.ps1'

if (-not (Test-Path $installerScript)) {
    throw "Install-WpfDevTools.ps1 was not found relative to scripts/online-installer.ps1: $installerScript"
}

try {
    if ([string]::IsNullOrWhiteSpace($PackageArchivePath)) {
        Invoke-WebRequest -Uri $downloadUri -OutFile $archivePath
    }

    New-Item -ItemType Directory -Force -Path $extractRoot | Out-Null
    Expand-Archive -Path $archivePath -DestinationPath $extractRoot -Force

    $arguments = @{
        PackagePath = $extractRoot
        InstallRoot = $InstallRoot
        Force = $Force
        Quiet = $OutputJson
    }

    $arguments.RegisterClaudeCode = $Client -eq 'claude-code'
    $arguments.RegisterCodex = $Client -eq 'codex-cli'

    & $installerScript @arguments

    $installManifestPath = Join-Path (Join-Path (Resolve-Path $InstallRoot).Path $Architecture) 'install-manifest.json'
    $installManifest = if (Test-Path $installManifestPath) {
        Get-Content -Path $installManifestPath -Raw | ConvertFrom-Json
    }
    else {
        $null
    }

    if ($OutputJson) {
        [ordered]@{
            version = $Version
            architecture = $Architecture
            client = $Client
            packageAssetName = $assetName
            downloadUri = $downloadUri
            installRoot = (Resolve-Path $InstallRoot).Path
            installedExecutable = [string]$installManifest.executable
            selectedClients = @(Get-SelectedClientList -SelectedClient $Client)
        } | ConvertTo-Json -Depth 6
    }
}
finally {
    Remove-PathIfExists -Path $sessionRoot
    if ([string]::IsNullOrWhiteSpace($PackageArchivePath)) {
        Remove-PathIfExists -Path $archivePath
    }
}
