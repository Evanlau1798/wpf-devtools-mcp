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
