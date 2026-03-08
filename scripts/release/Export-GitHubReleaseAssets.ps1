param(
    [Parameter(Mandatory)] [string]$InputRoot,
    [Parameter(Mandatory)] [string]$OutputRoot,
    [Parameter(Mandatory)] [string]$Tag,
    [switch]$OutputJson
)

$ErrorActionPreference = 'Stop'

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

    return @(Get-ChildItem -Path $Root -Filter 'WpfDevTools*.zip' -File -Recurse | Sort-Object Name)
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
    throw "No WpfDevTools release archives were found under: $inputRootFullPath"
}

$copiedAssets = New-Object System.Collections.Generic.List[psobject]
foreach ($asset in $assets) {
    $destinationPath = Join-Path $stagingRoot $asset.Name
    Copy-Item -Path $asset.FullName -Destination $destinationPath -Force
    $hash = (Get-FileHash -Path $destinationPath -Algorithm SHA256).Hash.ToLowerInvariant()
    $copiedAssets.Add([pscustomobject]@{
        name = $asset.Name
        path = $destinationPath
        sizeBytes = (Get-Item $destinationPath).Length
        sha256 = $hash
    }) | Out-Null
}

$checksumPath = Join-Path $stagingRoot 'SHA256SUMS.txt'
$checksumLines = $copiedAssets | ForEach-Object { "$($_.sha256)  $($_.name)" }
$checksumLines | Set-Content -Path $checksumPath -Encoding UTF8

$manifest = [pscustomobject]@{
    tag = $Tag
    generatedUtc = [DateTimeOffset]::UtcNow.ToString('o')
    inputRoot = $inputRootFullPath
    outputRoot = $stagingRoot
    assetCount = $copiedAssets.Count
    assets = $copiedAssets.ToArray()
}

$manifestPath = Join-Path $stagingRoot 'release-assets.json'
$manifest | ConvertTo-Json -Depth 5 | Set-Content -Path $manifestPath -Encoding UTF8

$uploadScriptPath = Join-Path $stagingRoot 'upload-gh-release.ps1'
$uploadAssetNames = @($copiedAssets | ForEach-Object { $_.name }) + 'SHA256SUMS.txt' + 'release-assets.json'
New-UploadScriptContent -ReleaseTag $Tag -AssetNames ([string[]]$uploadAssetNames) | Set-Content -Path $uploadScriptPath -Encoding UTF8

if ($OutputJson) {
    $manifest | ConvertTo-Json -Depth 5
}
