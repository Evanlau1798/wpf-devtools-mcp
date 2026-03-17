param(
    [string]$Version = 'latest',

    [ValidateSet('x64', 'x86', 'arm64')]
    [string]$Architecture,

    [ValidateSet('claude-code', 'codex', 'codex-cli', 'visual-studio', 'claude-desktop', 'cursor-vscode', 'github-copilot-vscode', 'other')]
    [string]$Client,

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

function Get-GitHubReleaseApiUri {
    param([Parameter(Mandatory)] [string]$ResolvedVersion)

    $apiBase = 'https://api.github.com/repos/Evanlau1798/wpf-devtools-mcp/releases'
    if ($ResolvedVersion -eq 'latest') {
        return "$apiBase/latest"
    }

    $tag = if ($ResolvedVersion.StartsWith('v')) { $ResolvedVersion } else { "v$ResolvedVersion" }
    return "$apiBase/tags/$tag"
}

function Get-DefaultArchitecture {
    $processorArchitecture = [string]$env:PROCESSOR_ARCHITECTURE
    switch ($processorArchitecture.ToUpperInvariant()) {
        'ARM64' { return 'arm64' }
        'X86' { return 'x86' }
        default { return 'x64' }
    }
}

function Resolve-SelectedValue {
    param(
        [string]$CurrentValue,
        [Parameter(Mandatory)] [string]$Prompt,
        [Parameter(Mandatory)] [string]$DefaultValue,
        [string[]]$AllowedValues
    )

    if (-not [string]::IsNullOrWhiteSpace($CurrentValue)) {
        return $CurrentValue
    }

    if ($NonInteractive) {
        return $DefaultValue
    }

    $allowedLiteral = if ($null -eq $AllowedValues -or $AllowedValues.Count -eq 0) {
        $DefaultValue
    }
    else {
        $AllowedValues -join '/'
    }

    $selection = Read-Host "$Prompt [$DefaultValue] ($allowedLiteral)"
    if ([string]::IsNullOrWhiteSpace($selection)) {
        return $DefaultValue
    }

    return $selection.Trim()
}

function Get-SelectedClientList {
    param([Parameter(Mandatory)] [string]$SelectedClient)

    switch ($SelectedClient) {
        'claude-code' { return @('claude-code') }
        'codex' { return @('codex') }
        'codex-cli' { return @('codex') }
        'visual-studio' { return @('visual-studio') }
        'claude-desktop' { return @('claude-desktop') }
        'cursor-vscode' { return @('cursor') }
        'github-copilot-vscode' { return @('none') }
        'other' { return @('none') }
        default { throw "Unsupported client option: $SelectedClient" }
    }
}

function Get-ReleaseAssetDownloadDetails {
    param(
        [Parameter(Mandatory)] [string]$ResolvedVersion,
        [Parameter(Mandatory)] [string]$ResolvedArchitecture
    )

    $assetName = Get-ReleaseAssetName -ResolvedVersion $ResolvedVersion -ResolvedArchitecture $ResolvedArchitecture
    $fallbackUri = Get-ReleaseDownloadUri -ResolvedVersion $ResolvedVersion -ResolvedArchitecture $ResolvedArchitecture
    $apiUri = Get-GitHubReleaseApiUri -ResolvedVersion $ResolvedVersion

    try {
        $release = Invoke-RestMethod -Uri $apiUri -Headers @{ 'User-Agent' = 'wpf-devtools-online-installer' }
        if ($null -ne $release) {
            $asset = @($release.assets) | Where-Object { $_.name -eq $assetName } | Select-Object -First 1
            if ($null -ne $asset) {
                return @{
                    AssetName = $assetName
                    DownloadUri = [string]$asset.browser_download_url
                    ResolvedVersion = [string]$release.tag_name
                }
            }
        }
    }
    catch {
    }

    return @{
        AssetName = $assetName
        DownloadUri = $fallbackUri
        ResolvedVersion = $ResolvedVersion
    }
}

function Remove-PathIfExists {
    param([string]$Path)

    if (-not [string]::IsNullOrWhiteSpace($Path) -and (Test-Path $Path)) {
        Remove-Item -Path $Path -Recurse -Force
    }
}

function Resolve-InstallerScriptPath {
    param([Parameter(Mandatory)] [string]$ExtractRoot)

    $packageSetup = Join-Path $ExtractRoot 'setup.ps1'
    if (Test-Path $packageSetup) {
        return $packageSetup
    }

    $packageInstaller = Join-Path $ExtractRoot 'install.ps1'
    if (Test-Path $packageInstaller) {
        return $packageInstaller
    }

    if (-not [string]::IsNullOrWhiteSpace($PSScriptRoot)) {
        $repoInstaller = Join-Path $PSScriptRoot 'tools\release\Setup-WpfDevTools.ps1'
        if (Test-Path $repoInstaller) {
            return $repoInstaller
        }
    }

    throw "A package setup/install script was not found in extracted package or relative to scripts/online-installer.ps1. ExtractRoot: $ExtractRoot"
}

$selectedVersion = Resolve-SelectedValue -CurrentValue $Version -Prompt 'Release version' -DefaultValue 'latest'
$selectedArchitecture = Resolve-SelectedValue -CurrentValue $Architecture -Prompt 'Architecture' -DefaultValue (Get-DefaultArchitecture) -AllowedValues @('x64', 'x86', 'arm64')
$selectedClient = Resolve-SelectedValue -CurrentValue $Client -Prompt 'Client' -DefaultValue 'claude-code' -AllowedValues @('claude-code', 'codex', 'visual-studio')
$workingRootPath = Resolve-AbsoluteDirectory -Path $WorkingRoot
$downloadDetails = Get-ReleaseAssetDownloadDetails -ResolvedVersion $selectedVersion -ResolvedArchitecture $selectedArchitecture
$assetName = [string]$downloadDetails.AssetName
$downloadUri = [string]$downloadDetails.DownloadUri
$archivePath = if ([string]::IsNullOrWhiteSpace($PackageArchivePath)) { Join-Path $workingRootPath $assetName } else { (Resolve-Path $PackageArchivePath).Path }
$sessionRoot = Join-Path $workingRootPath ([Guid]::NewGuid().ToString('N'))
$extractRoot = Join-Path $sessionRoot 'package'

try {
    if ([string]::IsNullOrWhiteSpace($PackageArchivePath)) {
        Invoke-WebRequest -Uri $downloadUri -OutFile $archivePath
    }

    New-Item -ItemType Directory -Force -Path $extractRoot | Out-Null
    Expand-Archive -Path $archivePath -DestinationPath $extractRoot -Force
    $installerScript = Resolve-InstallerScriptPath -ExtractRoot $extractRoot

    $arguments = @{
        PackagePath = $extractRoot
        InstallRoot = $InstallRoot
        Force = $Force
        NonInteractive = $true
        OutputJson = $OutputJson
    }

    $arguments.Clients = $selectedClient

    if ($OutputJson) {
        $null = & $installerScript @arguments
    }
    else {
        & $installerScript @arguments
    }

    $installManifestPath = Join-Path (Join-Path (Resolve-Path $InstallRoot).Path $selectedArchitecture) 'install-manifest.json'
    $installManifest = if (Test-Path $installManifestPath) {
        Get-Content -Path $installManifestPath -Raw | ConvertFrom-Json
    }
    else {
        $null
    }

    if ($OutputJson) {
        [ordered]@{
            version = $selectedVersion
            architecture = $selectedArchitecture
            client = $selectedClient
            packageAssetName = $assetName
            downloadUri = $downloadUri
            installRoot = (Resolve-Path $InstallRoot).Path
            installedExecutable = [string]$installManifest.executable
            selectedClients = @(Get-SelectedClientList -SelectedClient $selectedClient)
        } | ConvertTo-Json -Depth 6
    }
}
finally {
    Remove-PathIfExists -Path $sessionRoot
    if ([string]::IsNullOrWhiteSpace($PackageArchivePath)) {
        Remove-PathIfExists -Path $archivePath
    }
}
