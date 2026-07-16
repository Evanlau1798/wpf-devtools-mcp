function Invoke-InstallerWebRequest { param([Parameter(Mandatory)] [string]$Uri, [string]$OutFile, [hashtable]$Headers, [int]$TimeoutSec) $parameters = @{ Uri = $Uri }; if (-not [string]::IsNullOrWhiteSpace($OutFile)) { $parameters['OutFile'] = $OutFile }; if ($null -ne $Headers) { $parameters['Headers'] = $Headers }; if ($TimeoutSec -gt 0) { $parameters['TimeoutSec'] = $TimeoutSec }; if ((Get-Command Invoke-WebRequest).Parameters.ContainsKey('UseBasicParsing')) { $parameters['UseBasicParsing'] = $true }; return Invoke-WebRequest @parameters }
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
function Get-GitHubReleaseApiUri {
    param([Parameter(Mandatory)] [string]$ResolvedVersion)

    $apiBase = 'https://api.github.com/repos/Evanlau1798/wpf-devtools-mcp/releases'
    if ($ResolvedVersion -eq 'latest') {
        return "$apiBase/latest"
    }

    $tag = if ($ResolvedVersion.StartsWith('v')) { $ResolvedVersion } else { "v$ResolvedVersion" }
    return "$apiBase/tags/$tag"
}
function Get-GitHubTagRef {
    param([Parameter(Mandatory)] [string]$ResolvedVersion)

    $tagVersion = Resolve-RequestedReleaseVersion -RequestedVersion $ResolvedVersion
    if ([string]::IsNullOrWhiteSpace($tagVersion)) {
        throw 'Failed to resolve the installer helper release version.'
    }

    if ($tagVersion.StartsWith('v')) {
        return $tagVersion
    }

    return "v$tagVersion"
}
function Get-ReleaseRawContentBaseUri {
    param(
        [Parameter(Mandatory)] [string]$ResolvedVersion,
        [Parameter(Mandatory)] [string]$RepositoryRelativePath
    )

    $tagRef = Get-GitHubTagRef -ResolvedVersion $ResolvedVersion
    $normalizedPath = $RepositoryRelativePath.Trim([char[]]@('\', '/')).Replace('\', '/')
    return "https://raw.githubusercontent.com/Evanlau1798/wpf-devtools-mcp/$tagRef/$normalizedPath"
}
function Resolve-TuiHelperDownloadBaseUri {
    $override = Get-TuiHelperOverrideDownloadBaseUri
    if (-not [string]::IsNullOrWhiteSpace($override)) { return $override }
    return "https://raw.githubusercontent.com/Evanlau1798/wpf-devtools-mcp/master/$script:InstallerHelperRepositoryRelativePath"
}
function Resolve-TuiHelperBootstrapArchitecture {
    if ([string]::IsNullOrWhiteSpace($Architecture)) {
        return (Get-SystemDefaultArchitecture)
    }

    return [string]$Architecture
}
function Get-GitHubReleaseApiResponse {
    param([Parameter(Mandatory)] [string]$ResolvedVersion)

    $cacheKey = [string]$ResolvedVersion
    if ($script:GitHubReleaseApiResponseCache.Contains($cacheKey)) {
        return $script:GitHubReleaseApiResponseCache[$cacheKey]
    }

    try {
        $release = Invoke-RestMethod -Uri (Get-GitHubReleaseApiUri -ResolvedVersion $ResolvedVersion) -Headers @{ 'User-Agent' = 'wpf-devtools-online-installer' } -TimeoutSec 15
        $script:GitHubReleaseApiResponseCache[$cacheKey] = $release
        return $release
    }
    catch {
        return $null
    }
}
function Remove-TuiHelperReleaseTextPreamble {
    param([AllowEmptyString()] [string]$Content)

    if ($null -eq $Content) {
        return ''
    }

    $text = [string]$Content
    $prefixes = @([string][char]0xFEFF, (-join ([char[]](0x00EF, 0x00BB, 0x00BF))))
    foreach ($prefix in $prefixes) {
        if ($text.StartsWith($prefix, [System.StringComparison]::Ordinal)) {
            return $text.Substring($prefix.Length)
        }
    }

    return $text
}
function Get-TuiHelperReleaseAssetRecordsFromManifestObject {
    param($ManifestObject)

    $records = @()
    $manifest = $ManifestObject
    if ($manifest -is [string]) {
        try {
            $manifest = (Remove-TuiHelperReleaseTextPreamble -Content ([string]$manifest)) | ConvertFrom-Json
        }
        catch {
            return $records
        }
    }

    if ($null -eq $manifest -or $null -eq $manifest.assets) {
        return $records
    }

    foreach ($asset in @($manifest.assets)) {
        $assetName = [string]$asset.name
        $sha256 = [string]$asset.sha256
        if ([string]::IsNullOrWhiteSpace($assetName) -or [string]::IsNullOrWhiteSpace($sha256)) {
            continue
        }

        $records += ,([ordered]@{
                AssetName = $assetName
                Sha256 = $sha256.ToLowerInvariant()
            })
    }

    return $records
}
function Get-TuiHelperReleaseAssetRecordsFromChecksumContent {
    param([AllowEmptyString()] [string]$Content)

    $records = @()
    foreach ($rawLine in ($Content -split "`r?`n")) {
        $line = Remove-TuiHelperReleaseTextPreamble -Content ([string]$rawLine)
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
function Get-TuiHelperReleaseAssetRecordsFromGitHub {
    param([Parameter(Mandatory)] [string]$ResolvedVersion)

    $cacheKey = [string]$ResolvedVersion
    if ($script:GitHubReleaseChecksumRecordCache.Contains($cacheKey)) {
        return @($script:GitHubReleaseChecksumRecordCache[$cacheKey])
    }

    $records = @()
    $releasePath = if ($ResolvedVersion -eq 'latest') { 'latest/download' } else {
        $tag = if ($ResolvedVersion.StartsWith('v')) { $ResolvedVersion } else { "v$ResolvedVersion" }
        "download/$tag"
    }
    $releaseBaseUri = "https://github.com/Evanlau1798/wpf-devtools-mcp/releases/$releasePath"
    try {
        $manifest = Invoke-RestMethod -Uri "$releaseBaseUri/release-assets.json" -Headers @{ 'User-Agent' = 'wpf-devtools-online-installer' } -TimeoutSec 15
        $records = @(Get-TuiHelperReleaseAssetRecordsFromManifestObject -ManifestObject $manifest)
    }
    catch {
    }
    if ($records.Count -eq 0) {
        try {
            $checksumResponse = Invoke-InstallerWebRequest -Uri "$releaseBaseUri/SHA256SUMS.txt" -Headers @{ 'User-Agent' = 'wpf-devtools-online-installer' } -TimeoutSec 15
            $records = @(Get-TuiHelperReleaseAssetRecordsFromChecksumContent -Content ([string]$checksumResponse.Content))
        }
        catch {
        }
    }
    if ($records.Count -eq 0) {
        $release = Get-GitHubReleaseApiResponse -ResolvedVersion $ResolvedVersion
        $manifestAsset = @($release.assets) | Where-Object { $_.name -eq 'release-assets.json' } | Select-Object -First 1
        if ($null -ne $manifestAsset) { try { $manifest = Invoke-RestMethod -Uri ([string]$manifestAsset.browser_download_url) -Headers @{ 'User-Agent' = 'wpf-devtools-online-installer' } -TimeoutSec 15; $records = @(Get-TuiHelperReleaseAssetRecordsFromManifestObject -ManifestObject $manifest) } catch {} }
    }
    $script:GitHubReleaseChecksumRecordCache[$cacheKey] = @($records)
    return @($records)
}
function Get-TuiHelperReleaseAssetRecord {
    param(
        [Parameter(Mandatory)] [string]$ResolvedVersion,
        [string]$AssetName,
        [string]$ArchiveHash
    )

    $records = @(Get-TuiHelperReleaseAssetRecordsFromGitHub -ResolvedVersion $ResolvedVersion)
    if (-not [string]::IsNullOrWhiteSpace($AssetName)) {
        $namedRecord = @($records | Where-Object { [string]$_.AssetName -eq $AssetName } | Select-Object -First 1)
        if ($namedRecord.Count -gt 0) {
            return $namedRecord[0]
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($ArchiveHash)) {
        $hashRecord = @($records | Where-Object { [string]$_.Sha256 -eq $ArchiveHash } | Select-Object -First 1)
        if ($hashRecord.Count -gt 0) {
            return $hashRecord[0]
        }
    }

    return $null
}
function Find-LocalReleaseAssetRecord {
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
function Get-LocalReleaseAssetRecordFromDirectory {
    param(
        [Parameter(Mandatory)] [string]$DirectoryPath,
        [string]$AssetName,
        [string]$ArchiveHash
    )

    if ([string]::IsNullOrWhiteSpace($DirectoryPath) -or -not (Test-Path -LiteralPath $DirectoryPath -PathType Container)) {
        return $null
    }

    $manifestPath = Join-Path $DirectoryPath 'release-assets.json'
    if (Test-Path -LiteralPath $manifestPath) {
        try {
            $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
            $manifestRecord = Find-LocalReleaseAssetRecord `
                -Records @(Get-TuiHelperReleaseAssetRecordsFromManifestObject -ManifestObject $manifest) `
                -AssetName $AssetName `
                -ArchiveHash $ArchiveHash
            if ($null -ne $manifestRecord) {
                return $manifestRecord
            }
        }
        catch {
        }
    }

    $checksumPath = Join-Path $DirectoryPath 'SHA256SUMS.txt'
    if (Test-Path -LiteralPath $checksumPath) {
        return (Find-LocalReleaseAssetRecord `
                -Records @(Get-TuiHelperReleaseAssetRecordsFromChecksumContent -Content (Get-Content -LiteralPath $checksumPath -Raw)) `
                -AssetName $AssetName `
                -ArchiveHash $ArchiveHash)
    }

    return $null
}
function Resolve-TrustedLocalPackageMetadataDirectory {
    param([Parameter(Mandatory)] [string]$ArchivePath)

    $trustedDirectoryEntry = Get-Item Env:WPFDEVTOOLS_TRUSTED_RELEASE_METADATA_DIRECTORY -ErrorAction SilentlyContinue
    if ($null -ne $trustedDirectoryEntry) {
        $trustedDirectory = [string]$trustedDirectoryEntry.Value
        if ([string]::IsNullOrWhiteSpace($trustedDirectory)) {
            throw 'TrustedReleaseMetadataDirectory must not be empty when specified.'
        }

        $resolvedDirectory = Assert-InstallerLocalPathTrusted -Path $trustedDirectory
        if (-not (Test-Path -LiteralPath $resolvedDirectory -PathType Container)) {
            throw "TrustedReleaseMetadataDirectory was not found: $resolvedDirectory"
        }

        return $resolvedDirectory
    }

    if ((Test-InstallerTestModeEnabled) -and
        [string]::Equals([string]$env:WPFDEVTOOLS_TEST_TRUST_LOCAL_ARCHIVE_RELEASE_METADATA, '1', [System.StringComparison]::Ordinal)) {
        return (Split-Path -Parent $ArchivePath)
    }

    return $null
}
function Assert-LocalPackageArchiveTrustedForHelperBootstrap {
    param(
        [Parameter(Mandatory)] [string]$ArchivePath,
        [string]$MetadataArchivePath,
        [string]$ResolvedVersion,
        [string]$ResolvedArchitecture
    )

    $resolvedArchivePath = (Resolve-Path -LiteralPath (Assert-InstallerLocalPathTrusted -Path $ArchivePath)).Path
    $metadataArchive = if ([string]::IsNullOrWhiteSpace($MetadataArchivePath)) {
        $resolvedArchivePath
    }
    else {
        (Resolve-Path -LiteralPath (Assert-InstallerLocalPathTrusted -Path $MetadataArchivePath)).Path
    }
    $archiveHash = Get-Sha256FileHashHex -Path $resolvedArchivePath
    $archiveIdentity = Get-ReleaseArchiveIdentity -ArchivePath $metadataArchive -ResolvedVersion $ResolvedVersion -ResolvedArchitecture $ResolvedArchitecture
    $canonicalAssetName = if ($null -ne $archiveIdentity) { [string]$archiveIdentity.AssetName } else { $null }
    if ([string]::IsNullOrWhiteSpace($canonicalAssetName)) {
        throw "Archive integrity could not be verified for local package archive '$metadataArchive' because the canonical release asset identity could not be resolved from the archive name."
    }

    $metadataDirectory = Resolve-TrustedLocalPackageMetadataDirectory -ArchivePath $metadataArchive
    if ([string]::IsNullOrWhiteSpace($metadataDirectory)) {
        throw "Archive integrity could not be verified for local package $canonicalAssetName because no trusted release metadata was found for the local artifact. Provide release-assets.json or SHA256SUMS.txt via WPFDEVTOOLS_TRUSTED_RELEASE_METADATA_DIRECTORY, or use the GitHub release download workflow instead of a local package archive."
    }

    $releaseRecord = Get-LocalReleaseAssetRecordFromDirectory `
        -DirectoryPath $metadataDirectory `
        -AssetName $canonicalAssetName `
        -ArchiveHash $archiveHash
    if ($null -eq $releaseRecord) {
        throw "Archive integrity could not be verified for $canonicalAssetName because no matching release checksum metadata was found."
    }

    if ([string]$releaseRecord.Sha256 -ne $archiveHash) {
        throw "Archive integrity verification failed for $([string]$releaseRecord.AssetName). Expected SHA256 $([string]$releaseRecord.Sha256) but got $archiveHash."
    }
}
function Initialize-TrustedLocalPackageArchiveCopy {
    param(
        [Parameter(Mandatory)] [string]$ArchivePath,
        [Parameter(Mandatory)] [string]$DestinationRoot,
        [Parameter(Mandatory)] [string[]]$HelperFiles,
        [string]$ResolvedVersion,
        [string]$ResolvedArchitecture
    )

    if (-not [string]::IsNullOrWhiteSpace($script:TrustedLocalPackageArchivePath) -and
        (Test-Path -LiteralPath $script:TrustedLocalPackageArchivePath)) {
        return $script:TrustedLocalPackageArchivePath
    }

    $sourceArchivePath = (Resolve-Path -LiteralPath (Assert-InstallerLocalPathTrusted -Path $ArchivePath)).Path
    $trustedDestinationRoot = Assert-InstallerLocalPathTrusted -Path $DestinationRoot
    New-Item -ItemType Directory -Force -Path $trustedDestinationRoot | Out-Null
    Assert-InstallerLocalPathTrusted -Path $trustedDestinationRoot | Out-Null
    $trustedArchivePath = Assert-InstallerLocalPathTrusted -Path (Join-Path $trustedDestinationRoot (Split-Path -Leaf $sourceArchivePath))
    Copy-Item -LiteralPath $sourceArchivePath -Destination $trustedArchivePath -Force -ErrorAction Stop

    $trustedArchiveStream = [System.IO.File]::Open(
        $trustedArchivePath,
        [System.IO.FileMode]::Open,
        [System.IO.FileAccess]::Read,
        [System.IO.FileShare]::Read)
    try {
        Assert-LocalPackageArchiveTrustedForHelperBootstrap `
            -ArchivePath $trustedArchivePath `
            -MetadataArchivePath $sourceArchivePath `
            -ResolvedVersion $ResolvedVersion `
            -ResolvedArchitecture $ResolvedArchitecture

        $script:TrustedLocalPackageArchivePath = $trustedArchivePath
        $script:PackageArchivePath = $trustedArchivePath
        Copy-TestLocalPackageReleaseMetadataSidecars -SourceArchivePath $sourceArchivePath -DestinationRoot $trustedDestinationRoot
        Copy-InstallerHelperBundleFromArchive -ArchivePath $trustedArchivePath -DestinationRoot $trustedDestinationRoot -HelperFiles $HelperFiles
    }
    finally {
        $trustedArchiveStream.Dispose()
    }

    return $trustedArchivePath
}
function Copy-TestLocalPackageReleaseMetadataSidecars {
    param(
        [Parameter(Mandatory)] [string]$SourceArchivePath,
        [Parameter(Mandatory)] [string]$DestinationRoot
    )

    if (-not ((Test-InstallerTestModeEnabled) -and
            [string]::Equals([string]$env:WPFDEVTOOLS_TEST_TRUST_LOCAL_ARCHIVE_RELEASE_METADATA, '1', [System.StringComparison]::Ordinal))) {
        return
    }

    $sourceRoot = Assert-InstallerLocalPathTrusted -Path (Split-Path -Parent $SourceArchivePath)
    $trustedDestinationRoot = Assert-InstallerLocalPathTrusted -Path $DestinationRoot
    foreach ($sidecarName in @('release-assets.json', 'SHA256SUMS.txt')) {
        $sourcePath = Assert-InstallerLocalPathTrusted -Path (Join-Path $sourceRoot $sidecarName)
        if (Test-Path -LiteralPath $sourcePath -PathType Leaf) {
            Copy-Item -LiteralPath $sourcePath -Destination (Join-Path $trustedDestinationRoot $sidecarName) -Force -ErrorAction Stop
        }
    }
}
function Get-TuiHelperArchiveSha256 {
    param([Parameter(Mandatory)] [string]$ArchivePath)

    return Get-Sha256FileHashHex -Path $ArchivePath
}
function Get-TuiHelperArchiveDownloadDetails {
    $resolvedArchitecture = Resolve-TuiHelperBootstrapArchitecture
    $downloadVersion = Resolve-RequestedReleaseVersion -RequestedVersion $Version
    return (Get-ReleaseAssetDownloadDetails -ResolvedVersion $downloadVersion -ResolvedArchitecture $resolvedArchitecture)
}
function Assert-TuiHelperArchiveIntegrity {
    param(
        [Parameter(Mandatory)] [string]$ArchivePath,
        [Parameter(Mandatory)] $DownloadDetails
    )

    $archiveHash = Get-TuiHelperArchiveSha256 -ArchivePath $ArchivePath
    $releaseRecord = Get-TuiHelperReleaseAssetRecord `
        -ResolvedVersion ([string]$DownloadDetails.ResolvedVersion) `
        -AssetName ([string]$DownloadDetails.AssetName) `
        -ArchiveHash $archiveHash

    if ($null -eq $releaseRecord) {
        throw "Installer helper bootstrap archive integrity could not be verified for $([string]$DownloadDetails.AssetName) because no matching release checksum metadata was found."
    }

    if ([string]$releaseRecord.Sha256 -ne $archiveHash) {
        throw "Installer helper bootstrap archive integrity verification failed for $([string]$releaseRecord.AssetName). Expected SHA256 $([string]$releaseRecord.Sha256) but got $archiveHash."
    }
}
function Resolve-LocalPackageRoot {
    $scriptRoot = Resolve-InstallerScriptRoot
    if ([string]::IsNullOrWhiteSpace($scriptRoot)) {
        return $null
    }

    $binManifestPath = Join-Path $scriptRoot 'manifest.json'
    if (Test-Path $binManifestPath) {
        return (Split-Path -Parent $scriptRoot)
    }

    $packageManifestPath = Join-Path $scriptRoot 'bin\manifest.json'
    if (Test-Path $packageManifestPath) {
        return $scriptRoot
    }

    return $null
}
