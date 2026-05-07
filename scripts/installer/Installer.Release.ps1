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

function Get-ReleaseAssetIdentity {
    param([string]$AssetName)

    if ([string]::IsNullOrWhiteSpace($AssetName)) {
        return $null
    }

    $match = [regex]::Match($AssetName, '^release_(?<version>.+)_win-(?<architecture>x64|x86|arm64)\.zip$', 'IgnoreCase')
    if (-not $match.Success) {
        return $null
    }

    $resolvedVersion = [string]$match.Groups['version'].Value
    $resolvedArchitecture = [string]$match.Groups['architecture'].Value.ToLowerInvariant()
    return [ordered]@{
        AssetName = (Get-ReleaseAssetName -ResolvedVersion $resolvedVersion -ResolvedArchitecture $resolvedArchitecture)
        ResolvedVersion = $resolvedVersion
        ResolvedArchitecture = $resolvedArchitecture
    }
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

function Get-ArchiveSha256 {
    param([Parameter(Mandatory)] [string]$ArchivePath)

    if (Get-Command Get-FileHash -ErrorAction SilentlyContinue) {
        return (Get-FileHash -Algorithm SHA256 -LiteralPath $ArchivePath).Hash.ToLowerInvariant()
    }

    $stream = [System.IO.File]::OpenRead($ArchivePath)
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

function Normalize-SignerThumbprint {
    param([string]$Thumbprint)

    if ([string]::IsNullOrWhiteSpace($Thumbprint)) {
        return $null
    }

    return $Thumbprint.Replace(' ', '').ToUpperInvariant()
}

function Get-ReleaseAssetRecordsFromManifestObject {
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

        $signerThumbprint = Normalize-SignerThumbprint -Thumbprint ([string]$asset.signerThumbprint)
        $signerSubject = if ([string]::IsNullOrWhiteSpace([string]$asset.signerSubject)) { $null } else { [string]$asset.signerSubject }

        $records += ,([ordered]@{
                AssetName = $assetName
                Sha256 = $sha256.ToLowerInvariant()
                SignerThumbprint = $signerThumbprint
                SignerSubject = $signerSubject
            })
    }

    return $records
}

function Get-ReleaseAssetRecordsFromManifestPath {
    param([Parameter(Mandatory)] $ManifestPath)

    $resolvedManifestPath = [string]$ManifestPath
    if ([string]::IsNullOrWhiteSpace($resolvedManifestPath) -or -not (Test-Path -LiteralPath $resolvedManifestPath)) {
        return @()
    }

    try {
        $manifestJson = [System.IO.File]::ReadAllText((Resolve-Path -LiteralPath $resolvedManifestPath).Path)
        $manifest = $manifestJson | ConvertFrom-Json
    }
    catch {
        return @()
    }

    return @(Get-ReleaseAssetRecordsFromManifestObject -ManifestObject $manifest)
}

function Get-ReleaseAssetRecordsFromChecksumContent {
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

function Get-ReleaseAssetRecordsFromChecksumPath {
    param([Parameter(Mandatory)] $ChecksumPath)

    $resolvedChecksumPath = [string]$ChecksumPath
    if ([string]::IsNullOrWhiteSpace($resolvedChecksumPath) -or -not (Test-Path -LiteralPath $resolvedChecksumPath)) {
        return @()
    }

    $checksumContent = [System.IO.File]::ReadAllText((Resolve-Path -LiteralPath $resolvedChecksumPath).Path)
    return @(Get-ReleaseAssetRecordsFromChecksumContent -Content $checksumContent)
}

function Find-ReleaseAssetRecord {
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

function Get-ReleaseAssetRecordFromDirectory {
    param(
        [Parameter(Mandatory)] [string]$DirectoryPath,
        [string]$AssetName,
        [string]$ArchiveHash
    )

    if ([string]::IsNullOrWhiteSpace($DirectoryPath) -or -not (Test-Path $DirectoryPath)) {
        return $null
    }

    $manifestPath = Join-Path -Path $DirectoryPath -ChildPath 'release-assets.json'
    $manifestRecords = @(Get-ReleaseAssetRecordsFromManifestPath -ManifestPath $manifestPath)
    $manifestRecord = Find-ReleaseAssetRecord `
        -Records $manifestRecords `
        -AssetName $AssetName `
        -ArchiveHash $ArchiveHash
    if ($null -ne $manifestRecord) {
        return $manifestRecord
    }

    $checksumPath = Join-Path -Path $DirectoryPath -ChildPath 'SHA256SUMS.txt'
    $checksumRecords = @(Get-ReleaseAssetRecordsFromChecksumPath -ChecksumPath $checksumPath)
    return (Find-ReleaseAssetRecord `
            -Records $checksumRecords `
            -AssetName $AssetName `
            -ArchiveHash $ArchiveHash)
}

function Get-ReleaseAssetRecordsFromGitHub {
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
                $records = @(Get-ReleaseAssetRecordsFromManifestObject -ManifestObject $manifest)
            }
            catch {
            }
        }

        if ($records.Count -eq 0) {
            $checksumAsset = @($release.assets) | Where-Object { $_.name -eq 'SHA256SUMS.txt' } | Select-Object -First 1
            if ($null -ne $checksumAsset) {
                try {
                    $checksumResponse = Invoke-WebRequest -Uri ([string]$checksumAsset.browser_download_url) -Headers @{ 'User-Agent' = 'wpf-devtools-online-installer' } -TimeoutSec 15
                    $records = @(Get-ReleaseAssetRecordsFromChecksumContent -Content ([string]$checksumResponse.Content))
                }
                catch {
                }
            }
        }
    }

    $script:GitHubReleaseChecksumRecordCache[$cacheKey] = @($records)
    return @($records)
}

function Get-ReleaseAssetRecordFromGitHub {
    param(
        [Parameter(Mandatory)] [string]$ResolvedVersion,
        [string]$AssetName,
        [string]$ArchiveHash
    )

    return (Find-ReleaseAssetRecord `
            -Records @(Get-ReleaseAssetRecordsFromGitHub -ResolvedVersion $ResolvedVersion) `
            -AssetName $AssetName `
            -ArchiveHash $ArchiveHash)
}

function Get-ReleaseArchiveDownloadTimeoutSeconds {
    $resolver = Get-Command 'Get-InstallerTimeoutSeconds' -ErrorAction SilentlyContinue
    if ($null -ne $resolver) {
        return (Get-InstallerTimeoutSeconds -EnvironmentVariable 'WPFDEVTOOLS_INSTALLER_DOWNLOAD_TIMEOUT_SEC' -DefaultValue 30 -MinimumValue 5 -MaximumValue 300)
    }

    return 30
}

function Get-LocalPackageTrustedReleaseMetadata {
    param(
        [Parameter(Mandatory)] [string]$PackageDirectory,
        [Parameter(Mandatory)] [psobject]$PackageManifest
    )

    $resolvedVersion = [string]$PackageManifest.version
    $resolvedArchitecture = [string]$PackageManifest.architecture
    if ([string]::IsNullOrWhiteSpace($resolvedVersion) -or [string]::IsNullOrWhiteSpace($resolvedArchitecture)) {
        return [ordered]@{
            TrustedSignerThumbprint = $null
            TrustedSignerSubject = $null
            PackageAssetName = $null
            DownloadUri = $null
            HasTrustedReleaseMetadata = $false
        }
    }

    $assetName = Get-ReleaseAssetName -ResolvedVersion $resolvedVersion -ResolvedArchitecture $resolvedArchitecture
    $candidateDirectories = @((Split-Path -Parent $PackageDirectory)) |
        Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } |
        Select-Object -Unique

    foreach ($candidateDirectory in $candidateDirectories) {
        $adjacentArchivePath = Join-Path -Path $candidateDirectory -ChildPath $assetName
        if (-not (Test-Path -LiteralPath $adjacentArchivePath -PathType Leaf)) {
            continue
        }

        $archiveHash = Get-ArchiveSha256 -ArchivePath $adjacentArchivePath
        $releaseRecord = Get-ReleaseAssetRecordFromDirectory `
            -DirectoryPath $candidateDirectory `
            -AssetName $assetName `
            -ArchiveHash $archiveHash
        if ($null -ne $releaseRecord -and [string]$releaseRecord.Sha256 -eq $archiveHash) {
            return [ordered]@{
                TrustedSignerThumbprint = [string]$releaseRecord.SignerThumbprint
                TrustedSignerSubject = [string]$releaseRecord.SignerSubject
                PackageAssetName = [string]$releaseRecord.AssetName
                DownloadUri = Get-ReleaseDownloadUri -ResolvedVersion $resolvedVersion -ResolvedArchitecture $resolvedArchitecture
                HasTrustedReleaseMetadata = $true
            }
        }
    }

    return [ordered]@{
        TrustedSignerThumbprint = $null
        TrustedSignerSubject = $null
        PackageAssetName = $assetName
        DownloadUri = Get-ReleaseDownloadUri -ResolvedVersion $resolvedVersion -ResolvedArchitecture $resolvedArchitecture
        HasTrustedReleaseMetadata = $false
    }
}

function Resolve-PackageSession {
    param(
        [Parameter(Mandatory)] [string]$Mode,
        [Parameter(Mandatory)] [string]$ResolvedVersion,
        [Parameter(Mandatory)] [string]$ResolvedArchitecture
    )

    $workingRootPath = Resolve-AbsoluteDirectory -Path $WorkingRoot
    $sessionRoot = Join-Path $workingRootPath ([Guid]::NewGuid().ToString('N'))
    $extractRoot = Join-Path $sessionRoot 'package'

    if ($Mode -eq 'offline' -and (Test-PackageArchiveRequested)) {
        $archivePath = (Resolve-Path $PackageArchivePath).Path
        $integrity = Assert-ArchiveIntegrity -ArchivePath $archivePath -DownloadSource 'local-package' -ResolvedVersion $ResolvedVersion -ResolvedArchitecture $ResolvedArchitecture
        New-Item -ItemType Directory -Force -Path $extractRoot | Out-Null
        Assert-ArchiveSafeEntries -ArchivePath $archivePath -DestinationPath $extractRoot
        Expand-Archive -Path $archivePath -DestinationPath $extractRoot -Force
        # Shipping PackageArchivePath installs accept trusted archive provenance
        # only when release metadata can be resolved from the trusted release
        # source. Archive-adjacent release-assets.json/SHA256SUMS.txt remain a
        # test-only emulation path and are not a production trust root.
        return [ordered]@{
            PackageDirectory = $extractRoot
            SessionRoot = $sessionRoot
            CleanupSession = $true
            TrustedArchiveManifestPolicy = [bool]$integrity.HasTrustedReleaseMetadata
            TrustedSignerThumbprint = [string]$integrity.TrustedSignerThumbprint
            TrustedSignerSubject = [string]$integrity.TrustedSignerSubject
            DownloadSource = 'local-package'
            DownloadUri = [string]$integrity.DownloadUri
            PackageAssetName = [string]$integrity.PackageAssetName
            ResolvedVersion = [string]$integrity.ResolvedVersion
        }
    }

    if ($Mode -eq 'offline') {
        $localRoot = Resolve-LocalPackageRoot
        $manifest = Get-Content -Path (Resolve-PackageManifestPath -PackageDirectory $localRoot) -Raw | ConvertFrom-Json
        $trustedLocalReleaseMetadata = Get-LocalPackageTrustedReleaseMetadata -PackageDirectory $localRoot -PackageManifest $manifest
        # Package-local directories must never trust embedded manifest fields to
        # relax payload signature validation. Manual archive fallback may reuse
        # adjacent release sidecars only when the original verified release zip
        # is still present beside the extracted package so the signer metadata
        # remains hash-bound to a concrete archive.
        return [ordered]@{
            PackageDirectory = $localRoot
            SessionRoot = $null
            CleanupSession = $false
            TrustedArchiveManifestPolicy = $false
            TrustedSignerThumbprint = [string]$trustedLocalReleaseMetadata.TrustedSignerThumbprint
            TrustedSignerSubject = [string]$trustedLocalReleaseMetadata.TrustedSignerSubject
            DownloadSource = 'local-package'
            DownloadUri = [string]$trustedLocalReleaseMetadata.DownloadUri
            PackageAssetName = [string]$trustedLocalReleaseMetadata.PackageAssetName
            ResolvedVersion = [string]$manifest.version
        }
    }

    $downloadVersion = Resolve-RequestedReleaseVersion -RequestedVersion $ResolvedVersion
    $downloadDetails = Get-ReleaseAssetDownloadDetails -ResolvedVersion $downloadVersion -ResolvedArchitecture $ResolvedArchitecture
    New-Item -ItemType Directory -Force -Path $sessionRoot | Out-Null
    $archivePath = Join-Path $sessionRoot ([string]$downloadDetails.AssetName)
    Invoke-WebRequest -Uri ([string]$downloadDetails.DownloadUri) -OutFile $archivePath -TimeoutSec (Get-ReleaseArchiveDownloadTimeoutSeconds)
    $integrity = Assert-ArchiveIntegrity -ArchivePath $archivePath -DownloadSource 'github-release' -ResolvedVersion ([string]$downloadDetails.ResolvedVersion) -ResolvedArchitecture $ResolvedArchitecture
    New-Item -ItemType Directory -Force -Path $extractRoot | Out-Null
    Assert-ArchiveSafeEntries -ArchivePath $archivePath -DestinationPath $extractRoot
    Expand-Archive -Path $archivePath -DestinationPath $extractRoot -Force
    # GitHub release archives only enable DebugTrustedRootSkip after the archive
    # download passes checksum and zip-slip validation.
    return [ordered]@{
        PackageDirectory = $extractRoot
        SessionRoot = $sessionRoot
        CleanupSession = $true
        TrustedArchiveManifestPolicy = $true
        TrustedSignerThumbprint = [string]$integrity.TrustedSignerThumbprint
        TrustedSignerSubject = [string]$integrity.TrustedSignerSubject
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
