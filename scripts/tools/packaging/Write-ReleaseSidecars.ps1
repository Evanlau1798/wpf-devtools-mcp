param(
    [Parameter(Mandatory)] [string]$ArchiveRoot,
    [Parameter(Mandatory)] [string]$Tag,
    [switch]$OutputJson
)

$ErrorActionPreference = 'Stop'

function Get-ReleaseArchives {
    param([Parameter(Mandatory)] [string]$Root)

    if (-not (Test-Path $Root)) {
        throw "Archive root does not exist: $Root"
    }

    return @(Get-ChildItem -Path $Root -Filter 'release_*.zip' -File | Sort-Object Name)
}

$archiveRootFullPath = (Resolve-Path $ArchiveRoot).Path
$archives = Get-ReleaseArchives -Root $archiveRootFullPath
if ($archives.Count -eq 0) {
    throw "No release_*.zip archives were found under: $archiveRootFullPath"
}

$assets = foreach ($archive in $archives) {
    $hash = (Get-FileHash -Path $archive.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
    [pscustomobject]@{
        name = $archive.Name
        sizeBytes = $archive.Length
        sha256 = $hash
    }
}

$checksumPath = Join-Path $archiveRootFullPath 'SHA256SUMS.txt'
$checksumLines = $assets | ForEach-Object { "$($_.sha256)  $($_.name)" }
$checksumLines | Set-Content -Path $checksumPath -Encoding UTF8

$manifest = [pscustomobject]@{
    tag = $Tag
    assetCount = @($assets).Count
    assets = @($assets)
}

$manifestPath = Join-Path $archiveRootFullPath 'release-assets.json'
$manifest | ConvertTo-Json -Depth 5 | Set-Content -Path $manifestPath -Encoding UTF8

if ($OutputJson) {
    $manifest | ConvertTo-Json -Depth 5
}
