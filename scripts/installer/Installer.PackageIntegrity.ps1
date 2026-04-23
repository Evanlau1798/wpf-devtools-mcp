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

    return (Get-FileHash -Algorithm SHA256 -Path $ArchivePath).Hash.ToLowerInvariant()
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

function Assert-ArchiveIntegrity {
    param(
        [Parameter(Mandatory)] [string]$ArchivePath,
        [Parameter(Mandatory)] [string]$DownloadSource,
        [string]$ResolvedVersion,
        [string]$ResolvedArchitecture
    )

    $archiveHash = Get-ArchiveSha256 -ArchivePath $ArchivePath
    $archiveIdentity = Get-ReleaseArchiveIdentity -ArchivePath $ArchivePath -ResolvedVersion $ResolvedVersion -ResolvedArchitecture $ResolvedArchitecture
    $canonicalAssetName = if ($null -ne $archiveIdentity) { [string]$archiveIdentity.AssetName } else { $null }

    if ($DownloadSource -eq 'local-package' -and [string]::IsNullOrWhiteSpace($canonicalAssetName)) {
        throw "Archive integrity could not be verified for local package archive '$ArchivePath' because the canonical release asset identity could not be resolved from the archive name."
    }

    $releaseRecord = $null
    if ($DownloadSource -eq 'github-release' -or
        ($DownloadSource -eq 'local-package' -and (Test-AllowLocalArchiveReleaseMetadataInTestMode))) {
        $releaseRecord = Get-ReleaseAssetRecordFromDirectory `
            -DirectoryPath (Split-Path -Parent $ArchivePath) `
            -AssetName $canonicalAssetName `
            -ArchiveHash $archiveHash
    }

    if ($null -eq $releaseRecord -and
        ($DownloadSource -eq 'github-release' -or $DownloadSource -eq 'local-package') -and
        $null -ne $archiveIdentity) {
        $releaseRecord = Get-ReleaseAssetRecordFromGitHub `
            -ResolvedVersion ([string]$archiveIdentity.ResolvedVersion) `
            -AssetName $canonicalAssetName `
            -ArchiveHash $archiveHash
    }

    if ($null -eq $releaseRecord -and
        ($DownloadSource -eq 'github-release' -or $DownloadSource -eq 'local-package') -and
        -not [string]::IsNullOrWhiteSpace($canonicalAssetName)) {
        throw "Archive integrity could not be verified for $canonicalAssetName because no matching release checksum metadata was found."
    }

    if ($null -ne $releaseRecord) {
        if ([string]$releaseRecord.Sha256 -ne $archiveHash) {
            throw "Archive integrity verification failed for $([string]$releaseRecord.AssetName). Expected SHA256 $([string]$releaseRecord.Sha256) but got $archiveHash."
        }

        $recordIdentity = Get-ReleaseAssetIdentity -AssetName ([string]$releaseRecord.AssetName)
        $finalIdentity = if ($null -ne $recordIdentity) { $recordIdentity } else { $archiveIdentity }
        return [ordered]@{
            HasTrustedReleaseMetadata = $true
            PackageAssetName = [string]$releaseRecord.AssetName
            DownloadUri = if ($null -ne $finalIdentity) { Get-ReleaseDownloadUri -ResolvedVersion ([string]$finalIdentity.ResolvedVersion) -ResolvedArchitecture ([string]$finalIdentity.ResolvedArchitecture) } else { $null }
            ResolvedVersion = if ($null -ne $finalIdentity) { [string]$finalIdentity.ResolvedVersion } else { $ResolvedVersion }
            ResolvedArchitecture = if ($null -ne $finalIdentity) { [string]$finalIdentity.ResolvedArchitecture } else { $ResolvedArchitecture }
            Sha256 = $archiveHash
            TrustedSignerThumbprint = [string]$releaseRecord.SignerThumbprint
            TrustedSignerSubject = [string]$releaseRecord.SignerSubject
        }
    }

    return [ordered]@{
        HasTrustedReleaseMetadata = $false
        PackageAssetName = if ($null -ne $archiveIdentity) { [string]$archiveIdentity.AssetName } else { (Split-Path -Leaf $ArchivePath) }
        DownloadUri = if ($null -ne $archiveIdentity) { Get-ReleaseDownloadUri -ResolvedVersion ([string]$archiveIdentity.ResolvedVersion) -ResolvedArchitecture ([string]$archiveIdentity.ResolvedArchitecture) } else { $null }
        ResolvedVersion = if ($null -ne $archiveIdentity) { [string]$archiveIdentity.ResolvedVersion } else { $ResolvedVersion }
        ResolvedArchitecture = if ($null -ne $archiveIdentity) { [string]$archiveIdentity.ResolvedArchitecture } else { $ResolvedArchitecture }
        Sha256 = $archiveHash
        TrustedSignerThumbprint = $null
        TrustedSignerSubject = $null
    }
}

function Assert-ArchiveSafeEntries {
    <#
    .SYNOPSIS
        Validate a zip archive's entry names do not escape the intended
        destination directory (zip-slip, CVE-2018-1002200 pattern).

    .DESCRIPTION
        Enumerates every entry of the archive and requires that its
        canonical full path, resolved relative to the destination root,
        stays inside the destination root. Absolute paths, drive-qualified
        paths, and traversal segments ("..") are rejected.

    .PARAMETER ArchivePath
        Path to the zip archive to inspect.

    .PARAMETER DestinationPath
        Intended extraction root. Does not need to exist yet.

    .OUTPUTS
        None. Throws on any unsafe entry. Returns silently when the
        archive is well-formed.
    #>
    param(
        [Parameter(Mandatory)] [string]$ArchivePath,
        [Parameter(Mandatory)] [string]$DestinationPath
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction SilentlyContinue | Out-Null

    $resolvedDestination = [System.IO.Path]::GetFullPath($DestinationPath)
    if (-not $resolvedDestination.EndsWith([System.IO.Path]::DirectorySeparatorChar)) {
        $resolvedDestination = $resolvedDestination + [System.IO.Path]::DirectorySeparatorChar
    }

    $archive = [System.IO.Compression.ZipFile]::OpenRead($ArchivePath)
    try {
        foreach ($entry in $archive.Entries) {
            $entryName = [string]$entry.FullName
            if ([string]::IsNullOrEmpty($entryName)) {
                continue
            }

            if ($entryName -match '^[a-zA-Z]:[\\/]' -or $entryName.StartsWith('/') -or $entryName.StartsWith('\')) {
                throw "Unsafe archive entry rejected (absolute path): '$entryName' in $ArchivePath"
            }

            $normalizedEntry = $entryName.Replace('/', [System.IO.Path]::DirectorySeparatorChar)
            $candidate = [System.IO.Path]::Combine($resolvedDestination, $normalizedEntry)
            $fullCandidate = [System.IO.Path]::GetFullPath($candidate)

            if (-not $fullCandidate.StartsWith($resolvedDestination, [System.StringComparison]::OrdinalIgnoreCase)) {
                throw "Unsafe archive entry rejected (path traversal): '$entryName' in $ArchivePath"
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Resolve-PackageManifestPath {
    param([Parameter(Mandatory)] [string]$PackageDirectory)

    foreach ($candidate in @((Join-Path $PackageDirectory 'manifest.json'), (Join-Path $PackageDirectory 'bin\manifest.json'))) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    throw "manifest.json was not found under package path: $PackageDirectory"
}

function Resolve-PackageExecutable {
    param(
        [Parameter(Mandatory)] [string]$PackageDirectory,
        [Parameter(Mandatory)] [string]$ResolvedArchitecture
    )

    foreach ($candidate in @(
            (Join-Path $PackageDirectory "bin\wpf-devtools-$ResolvedArchitecture.exe"),
            (Join-Path $PackageDirectory "wpf-devtools-$ResolvedArchitecture.exe"),
            (Join-Path $PackageDirectory 'bin\WpfDevTools.Mcp.Server.exe'))) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    throw "Package does not contain an executable for architecture '$ResolvedArchitecture'."
}

function Test-InstallerTestModeEnabled {
    return [string]::Equals([string]$env:WPFDEVTOOLS_INSTALLER_TEST_MODE, '1', [System.StringComparison]::Ordinal)
}

function Test-AllowLocalArchiveReleaseMetadataInTestMode {
    return (Test-InstallerTestModeEnabled) -and
        [string]::Equals([string]$env:WPFDEVTOOLS_TEST_TRUST_LOCAL_ARCHIVE_RELEASE_METADATA, '1', [System.StringComparison]::Ordinal)
}

function Normalize-SignerThumbprint {
    param([string]$Thumbprint)

    if ([string]::IsNullOrWhiteSpace($Thumbprint)) {
        return $null
    }

    return $Thumbprint.Replace(' ', '').ToUpperInvariant()
}

function Get-PackageExpectedSignerMetadata {
    param(
        [Parameter(Mandatory)] [psobject]$PackageManifest,
        [string]$TrustedSignerThumbprint,
        [string]$TrustedSignerSubject
    )

    $thumbprint = if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT)) {
        [string]$env:WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT
    }
    else {
        [string]$TrustedSignerThumbprint
    }

    $subject = if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT)) {
        [string]$env:WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT
    }
    else {
        [string]$TrustedSignerSubject
    }

    return [ordered]@{
        Thumbprint = Normalize-SignerThumbprint -Thumbprint $thumbprint
        Subject = if ([string]::IsNullOrWhiteSpace($subject)) { $null } else { $subject }
    }
}

function Get-PackagePayloadSignature {
    param([Parameter(Mandatory)] [string]$Path)

    if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_TEST_SIGNATURE_STATUS)) {
        if (-not (Test-InstallerTestModeEnabled)) {
            throw 'WPFDEVTOOLS_TEST_SIGNATURE_STATUS is supported only when WPFDEVTOOLS_INSTALLER_TEST_MODE=1.'
        }

        $forcedStatus = [string]$env:WPFDEVTOOLS_TEST_SIGNATURE_STATUS
        if ([string]::Equals($forcedStatus, 'Valid', [System.StringComparison]::OrdinalIgnoreCase)) {
            return [pscustomobject]@{
                Status = [System.Management.Automation.SignatureStatus]::Valid
                SignerCertificate = [pscustomobject]@{
                    Thumbprint = 'TESTSIGNER00000000000000000000000000000000'
                    Subject = 'CN=WPFDEVTOOLS TEST SIGNER'
                }
            }
        }

        return [pscustomobject]@{
            Status = $forcedStatus
            SignerCertificate = $null
        }
    }

    return Get-AuthenticodeSignature -FilePath $Path
}

function Get-PackagePayloadSignatureTargets {
    param(
        [Parameter(Mandatory)] [string]$PackageDirectory,
        [Parameter(Mandatory)] [psobject]$PackageManifest
    )

    $payloadRoot = if (Test-Path (Join-Path $PackageDirectory 'bin')) {
        Join-Path $PackageDirectory 'bin'
    }
    else {
        $PackageDirectory
    }

    if (-not (Test-Path $payloadRoot)) {
        return @()
    }

    $targets = New-Object System.Collections.Generic.List[string]
    foreach ($relativePath in @(
            [string]$PackageManifest.entryExecutable
            [string]$PackageManifest.inspector.net8
            [string]$PackageManifest.inspector.net48
            [string]$PackageManifest.bootstrapper
        )) {
        if (-not [string]::IsNullOrWhiteSpace($relativePath)) {
            $candidate = Join-Path $PackageDirectory $relativePath
            if (Test-Path $candidate) {
                $targets.Add((Resolve-Path $candidate).Path)
            }
        }
    }

    $knownPayloads = @(Get-ChildItem -Path $payloadRoot -Recurse -File -Include @(
                'wpf-devtools-*.exe',
                'WpfDevTools.Mcp.Server.exe',
                'WpfDevTools.Inspector.dll',
                'WpfDevTools.Bootstrapper.*.dll'
            ) | ForEach-Object { $_.FullName })
    foreach ($payload in $knownPayloads) {
        $targets.Add($payload)
    }

    return @($targets | Sort-Object -Unique)
}

<#
.SYNOPSIS
    Verifies that executable payloads inside a package satisfy the current
    signature policy before installation.

.PARAMETER TrustedArchiveManifestPolicy
    Indicates the package directory was extracted from a GitHub release archive
    whose provenance already passed trusted release metadata verification in
    Assert-ArchiveIntegrity plus zip-slip validation in Assert-ArchiveSafeEntries.
    This is a provenance hint only; archive-controlled manifest fields must not
    use it to relax shipping payload signature requirements. Local
    PackageArchivePath installs never set this flag because archive-adjacent
    release-assets.json/SHA256SUMS.txt are not a trusted root.
#>
function Assert-PackagePayloadIntegrity {
    param(
        [Parameter(Mandatory)] [string]$PackageDirectory,
        [Parameter(Mandatory)] [psobject]$PackageManifest,
        [string]$TrustedSignerThumbprint,
        [string]$TrustedSignerSubject,
        [switch]$TrustedArchiveManifestPolicy
    )

    $signaturePolicy = [string]$PackageManifest.signaturePolicy
    $installerMode = Resolve-InstallerMode

    # TrustedArchiveManifestPolicy records that archive provenance was verified,
    # but shipping payload signature requirements still must not be downgraded
    # by archive-controlled manifest fields such as DebugTrustedRootSkip.

    $requiresSignedPayload = $false

    if ($installerMode -eq 'offline') {
        $requiresSignedPayload = $true
    }
    elseif ($installerMode -eq 'online') {
        $requiresSignedPayload = $true
    }
    elseif ($signaturePolicy -eq 'RequireAuthenticodeSignature') {
        $requiresSignedPayload = $true
    }

    if (-not $requiresSignedPayload) {
        return
    }

    if (-not (Test-InstallerTestModeEnabled) -and
        -not [string]::IsNullOrWhiteSpace([string]$env:WPFDEVTOOLS_TEST_SIGNATURE_STATUS)) {
        throw 'WPFDEVTOOLS_TEST_SIGNATURE_STATUS is supported only when WPFDEVTOOLS_INSTALLER_TEST_MODE=1.'
    }

    $targets = @(Get-PackagePayloadSignatureTargets -PackageDirectory $PackageDirectory -PackageManifest $PackageManifest)
    if ($targets.Count -eq 0) {
        throw 'Package payload signature verification could not locate any executable payloads.'
    }

    $expectedSigner = Get-PackageExpectedSignerMetadata `
        -PackageManifest $PackageManifest `
        -TrustedSignerThumbprint $TrustedSignerThumbprint `
        -TrustedSignerSubject $TrustedSignerSubject
    if (-not (Test-InstallerTestModeEnabled) -and
        [string]::IsNullOrWhiteSpace([string]$expectedSigner.Thumbprint) -and
        [string]::IsNullOrWhiteSpace([string]$expectedSigner.Subject)) {
        throw 'Package payload signature verification requires pinned signer metadata from a trusted release source or WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT/WPFDEVTOOLS_RELEASE_SIGNER_SUBJECT.'
    }

    foreach ($targetPath in $targets) {
        $signature = Get-PackagePayloadSignature -Path $targetPath
        if ($null -eq $signature -or $signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
            $signatureStatus = if ($null -eq $signature) { 'Unknown' } else { [string]$signature.Status }
            throw "Package payload signature verification failed for $targetPath. Authenticode status: $signatureStatus."
        }

        $signerCertificate = $signature.SignerCertificate
        if ($null -eq $signerCertificate) {
            throw "Package payload signature verification failed for $targetPath. Signer certificate metadata was missing."
        }

        $actualThumbprint = Normalize-SignerThumbprint -Thumbprint ([string]$signerCertificate.Thumbprint)
        $actualSubject = [string]$signerCertificate.Subject
        if (-not [string]::IsNullOrWhiteSpace([string]$expectedSigner.Thumbprint) -and
            -not [string]::Equals($actualThumbprint, [string]$expectedSigner.Thumbprint, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Package payload signature verification failed for $targetPath. Expected signer thumbprint '$([string]$expectedSigner.Thumbprint)' but got '$actualThumbprint'."
        }

        if (-not [string]::IsNullOrWhiteSpace([string]$expectedSigner.Subject) -and
            -not [string]::Equals($actualSubject, [string]$expectedSigner.Subject, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Package payload signature verification failed for $targetPath. Expected signer subject '$([string]$expectedSigner.Subject)' but got '$actualSubject'."
        }
    }
}
