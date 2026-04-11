param(
    [Parameter(Mandatory)] [string]$InputRoot,
    [Parameter(Mandatory)] [string]$OutputRoot,
    [Parameter(Mandatory)] [string]$Tag,
    [switch]$OutputJson
)

$ErrorActionPreference = 'Stop'
$sidecarWriter = Join-Path $PSScriptRoot 'Write-ReleaseSidecars.ps1'
if (-not (Test-Path $sidecarWriter)) {
    throw "Write-ReleaseSidecars.ps1 was not found: $sidecarWriter"
}

function Resolve-Directory {
    param([Parameter(Mandatory)] [string]$Path)

    New-Item -ItemType Directory -Force -Path $Path | Out-Null
    return (Resolve-Path $Path).Path
}

function Get-ReleaseAssets {
    param([Parameter(Mandatory)] [string]$Root)

    if (-not (Test-Path $Root)) {
        throw "Input root does not exist: $Root"
    }

    return @(Get-ChildItem -Path $Root -Filter 'release_*.zip' -File -Recurse | Sort-Object Name)
}

function New-UploadScriptContent {
    param(
        [Parameter(Mandatory)] [string]$ReleaseTag,
        [Parameter(Mandatory)] [string[]]$AssetNames
    )

    $assetBlock = ($AssetNames | ForEach-Object { "    `"$_`"" }) -join [Environment]::NewLine
    return @"
param(
    [string]`$ReleaseTag = '$ReleaseTag'
)

`$ErrorActionPreference = 'Stop'
`$assets = @(
$assetBlock
)

# Default command: gh release upload $ReleaseTag @assets --clobber
gh release upload `$ReleaseTag @assets --clobber
"@
}

$inputRootFullPath = (Resolve-Path $InputRoot).Path
$outputRootFullPath = Resolve-Directory -Path $OutputRoot
$stagingRoot = Join-Path $outputRootFullPath $Tag

if (Test-Path $stagingRoot) {
    Remove-Item -Path $stagingRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $stagingRoot | Out-Null
$assets = Get-ReleaseAssets -Root $inputRootFullPath
if ($assets.Count -eq 0) {
    throw "No release_*.zip archives were found under: $inputRootFullPath"
}

foreach ($asset in $assets) {
    $destinationPath = Join-Path $stagingRoot $asset.Name
    Copy-Item -Path $asset.FullName -Destination $destinationPath -Force
}

$manifest = & $sidecarWriter -ArchiveRoot $stagingRoot -Tag $Tag -OutputJson | ConvertFrom-Json

$uploadScriptPath = Join-Path $stagingRoot 'upload-gh-release.ps1'
$uploadAssetNames = @($manifest.assets | ForEach-Object { [string]$_.name }) + 'SHA256SUMS.txt' + 'release-assets.json'
New-UploadScriptContent -ReleaseTag $Tag -AssetNames ([string[]]$uploadAssetNames) | Set-Content -Path $uploadScriptPath -Encoding UTF8

if ($OutputJson) {
    $manifest | ConvertTo-Json -Depth 5
}
