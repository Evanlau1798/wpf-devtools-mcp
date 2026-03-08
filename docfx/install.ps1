param(
    [ValidateSet('x64', 'x86', 'arm64')]
    [string]$Architecture,

    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA 'WpfDevToolsMcp'),

    [string[]]$Clients,

    [string]$ClaudeDesktopConfigPath,
    [string]$CursorConfigPath,

    [string]$PackageArchivePath,
    [string]$DownloadBaseUrl = 'https://github.com/Evanlau1798/wpf-devtools-mcp/releases/latest/download',
    [string]$WorkingRoot = (Join-Path ([System.IO.Path]::GetTempPath()) 'wpf-devtools-bootstrap'),

    [switch]$NonInteractive,
    [switch]$Force,
    [switch]$OutputJson
)

$ErrorActionPreference = 'Stop'
$script:IsJsonOutput = $OutputJson.IsPresent

function Write-BootstrapMessage {
    param([Parameter(Mandatory)] [string]$Message)

    if (-not $script:IsJsonOutput) {
        Write-Host $Message
    }
}

function Resolve-TargetArchitecture {
    param([string]$ConfiguredArchitecture)

    if (-not [string]::IsNullOrWhiteSpace($ConfiguredArchitecture)) {
        return $ConfiguredArchitecture
    }

    $detected = @(
        $env:PROCESSOR_ARCHITECTURE,
        $env:PROCESSOR_ARCHITEW6432
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -First 1

    if ($detected -and $detected.ToUpperInvariant().Contains('ARM64')) {
        return 'arm64'
    }

    return 'x64'
}

function Resolve-AbsoluteDirectory {
    param([Parameter(Mandatory)] [string]$Path)

    New-Item -ItemType Directory -Force -Path $Path | Out-Null
    return (Resolve-Path $Path).Path
}

function Resolve-ArchivePath {
    param(
        [Parameter(Mandatory)] [string]$TargetArchitecture,
        [string]$ConfiguredArchivePath,
        [Parameter(Mandatory)] [string]$ResolvedWorkingRoot,
        [Parameter(Mandatory)] [string]$ResolvedDownloadBaseUrl
    )

    if (-not [string]::IsNullOrWhiteSpace($ConfiguredArchivePath)) {
        return [ordered]@{
            archivePath = (Resolve-Path $ConfiguredArchivePath).Path
            downloaded = $false
            assetName = [System.IO.Path]::GetFileName($ConfiguredArchivePath)
        }
    }

    $assetName = "WpfDevTools-win-$TargetArchitecture.zip"
    $archivePath = Join-Path $ResolvedWorkingRoot $assetName
    $downloadUri = ($ResolvedDownloadBaseUrl.TrimEnd('/') + '/' + $assetName)
    Write-BootstrapMessage "Downloading $downloadUri"
    Invoke-WebRequest -Uri $downloadUri -OutFile $archivePath

    return [ordered]@{
        archivePath = $archivePath
        downloaded = $true
        assetName = $assetName
    }
}

function Remove-PathIfExists {
    param([string]$Path)

    if (-not [string]::IsNullOrWhiteSpace($Path) -and (Test-Path $Path)) {
        Remove-Item -Path $Path -Recurse -Force
    }
}

function Invoke-PackageSetup {
    param(
        [Parameter(Mandatory)] [string]$ExtractedPackageRoot,
        [Parameter(Mandatory)] [string]$ResolvedInstallRoot,
        [string[]]$SelectedClients,
        [string]$ResolvedClaudeDesktopConfigPath,
        [string]$ResolvedCursorConfigPath,
        [switch]$RunNonInteractive,
        [switch]$Overwrite,
        [switch]$JsonOutput
    )

    $setupScript = Join-Path $ExtractedPackageRoot 'setup.ps1'
    if (-not (Test-Path $setupScript)) {
        throw "setup.ps1 was not found in extracted package: $ExtractedPackageRoot"
    }

    $setupArguments = @{
        InstallRoot = $ResolvedInstallRoot
        Force = $Overwrite
        OutputJson = $JsonOutput
    }

    if ($RunNonInteractive) {
        $setupArguments.NonInteractive = $true
    }

    if ($null -ne $SelectedClients -and $SelectedClients.Count -gt 0) {
        $setupArguments.Clients = $SelectedClients
    }
    elseif ($null -ne $SelectedClients) {
        $setupArguments.Clients = @('none')
    }

    if (-not [string]::IsNullOrWhiteSpace($ResolvedClaudeDesktopConfigPath)) {
        $setupArguments.ClaudeDesktopConfigPath = $ResolvedClaudeDesktopConfigPath
    }

    if (-not [string]::IsNullOrWhiteSpace($ResolvedCursorConfigPath)) {
        $setupArguments.CursorConfigPath = $ResolvedCursorConfigPath
    }

    & $setupScript @setupArguments
}

$targetArchitecture = Resolve-TargetArchitecture -ConfiguredArchitecture $Architecture
$resolvedWorkingRoot = Resolve-AbsoluteDirectory -Path $WorkingRoot
$archiveInfo = Resolve-ArchivePath -TargetArchitecture $targetArchitecture -ConfiguredArchivePath $PackageArchivePath -ResolvedWorkingRoot $resolvedWorkingRoot -ResolvedDownloadBaseUrl $DownloadBaseUrl
$sessionRoot = Join-Path $resolvedWorkingRoot ([Guid]::NewGuid().ToString('N'))
$extractRoot = Join-Path $sessionRoot 'package'
$resolvedInstallRoot = Resolve-AbsoluteDirectory -Path $InstallRoot

try {
    New-Item -ItemType Directory -Force -Path $extractRoot | Out-Null
    Expand-Archive -Path $archiveInfo.archivePath -DestinationPath $extractRoot -Force

    $selectedClients = $null
    if ($null -ne $Clients) {
        $selectedClients = @($Clients)
    }

    Invoke-PackageSetup -ExtractedPackageRoot $extractRoot -ResolvedInstallRoot $resolvedInstallRoot -SelectedClients $selectedClients -ResolvedClaudeDesktopConfigPath $ClaudeDesktopConfigPath -ResolvedCursorConfigPath $CursorConfigPath -RunNonInteractive:$NonInteractive -Overwrite:$Force -JsonOutput:$OutputJson
}
finally {
    Remove-PathIfExists -Path $sessionRoot
    if ($archiveInfo.downloaded) {
        Remove-PathIfExists -Path $archiveInfo.archivePath
    }
}
