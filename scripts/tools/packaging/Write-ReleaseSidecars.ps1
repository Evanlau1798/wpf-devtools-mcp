param(
    [Parameter(Mandatory)] [string]$ArchiveRoot,
    [Parameter(Mandatory)] [string]$Tag,
    [switch]$OutputJson
)

$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue | Out-Null

function Normalize-SignerThumbprint {
    param([string]$Thumbprint)

    if ([string]::IsNullOrWhiteSpace($Thumbprint)) {
        return $null
    }

    return $Thumbprint.Replace(' ', '').ToUpperInvariant()
}

function Get-ArchiveSignerMetadata {
    param([Parameter(Mandatory)] [string]$ArchivePath)

    try {
        $archive = [System.IO.Compression.ZipFile]::OpenRead($ArchivePath)
        try {
            $manifestEntry = $archive.GetEntry('bin/manifest.json')
            if ($null -eq $manifestEntry) {
                $manifestEntry = $archive.GetEntry('manifest.json')
            }

            if ($null -eq $manifestEntry) {
                return $null
            }

            $reader = New-Object System.IO.StreamReader($manifestEntry.Open())
            try {
                $manifest = ($reader.ReadToEnd() | ConvertFrom-Json)
            }
            finally {
                $reader.Dispose()
            }

            $thumbprint = Normalize-SignerThumbprint -Thumbprint ([string]$manifest.signerThumbprint)
            $subject = if ([string]::IsNullOrWhiteSpace([string]$manifest.signerSubject)) { $null } else { [string]$manifest.signerSubject }
            if ([string]::IsNullOrWhiteSpace($thumbprint) -and [string]::IsNullOrWhiteSpace($subject)) {
                return $null
            }

            return [ordered]@{
                signerThumbprint = $thumbprint
                signerSubject = $subject
            }
        }
        finally {
            $archive.Dispose()
        }
    }
    catch {
        return $null
    }
}

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
    $signerMetadata = Get-ArchiveSignerMetadata -ArchivePath $archive.FullName
    [pscustomobject]@{
        name = $archive.Name
        sizeBytes = $archive.Length
        sha256 = $hash
        signerThumbprint = if ($null -ne $signerMetadata) { [string]$signerMetadata.signerThumbprint } else { $null }
        signerSubject = if ($null -ne $signerMetadata) { [string]$signerMetadata.signerSubject } else { $null }
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
