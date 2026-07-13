function Get-ComputedInstallerHelperRecordCacheKey {
    param(
        [Parameter(Mandatory)] [hashtable]$RecordMap,
        [Parameter(Mandatory)] [string[]]$HelperFiles
    )

    $records = New-Object System.Collections.Generic.List[string]
    foreach ($helperFile in ($HelperFiles | Sort-Object)) {
        if (-not $RecordMap.ContainsKey($helperFile)) {
            throw "Installer helper manifest is missing integrity metadata for $helperFile."
        }

        $records.Add("${helperFile}:$([string]$RecordMap[$helperFile].Sha256)")
    }

    $utf8 = [System.Text.Encoding]::UTF8.GetBytes(($records -join '|'))
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hashBytes = $sha256.ComputeHash($utf8)
    }
    finally {
        $sha256.Dispose()
    }

    return 'sha256:' + (($hashBytes | ForEach-Object { $_.ToString('x2') }) -join '')
}
function Get-InstallerHelperFileSha256 {
    param([Parameter(Mandatory)] [string]$Path)

    return Get-Sha256FileHashHex -Path $Path
}
function Assert-InstallerHelperFileRecord {
    param(
        [Parameter(Mandatory)] [string]$HelperPath,
        [Parameter(Mandatory)] $HelperRecord
    )

    if (-not (Test-Path -LiteralPath $HelperPath)) {
        throw "Installer helper file was not found: $HelperPath"
    }

    $expectedPath = [string]$HelperRecord.Path
    $expectedHash = [string]$HelperRecord.Sha256
    $expectedSize = [long]$HelperRecord.SizeBytes
    $actualSize = (Get-Item -LiteralPath $HelperPath).Length
    if ($actualSize -ne $expectedSize) {
        throw "Installer helper integrity verification failed for $expectedPath. Expected size $expectedSize but found $actualSize."
    }

    $actualHash = Get-InstallerHelperFileSha256 -Path $HelperPath
    if (-not [string]::Equals($actualHash, $expectedHash, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Installer helper integrity verification failed for $expectedPath. Expected SHA-256 $expectedHash but found $actualHash."
    }
}
function Get-InstallerHelperRecordMap {
    param($Manifest)

    $recordMap = @{}
    if ($null -eq $Manifest -or $null -eq $Manifest.HelperFileRecords) {
        return $recordMap
    }

    foreach ($record in @($Manifest.HelperFileRecords)) {
        if ($null -eq $record) {
            continue
        }

        $path = [string]$record.Path
        if ([string]::IsNullOrWhiteSpace($path)) {
            continue
        }

        $recordMap[$path] = $record
    }

    return $recordMap
}
function Assert-InstallerHelperManifestIntegrity {
    param(
        [Parameter(Mandatory)] [string]$HelperDirectory,
        [Parameter(Mandatory)] $Manifest,
        [switch]$RequirePinnedCacheKey,
        [string[]]$RequiredHelperFiles
    )

    $expectedHelperFiles = @(Get-HelperLeafNames | Sort-Object)
    $requiredHelperFiles = if ($PSBoundParameters.ContainsKey('RequiredHelperFiles')) {
        @($RequiredHelperFiles | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | Sort-Object -Unique)
    }
    else {
        $expectedHelperFiles
    }
    $manifestHelperFiles = @($Manifest.HelperFiles | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | Sort-Object -Unique)
    $difference = Compare-Object -ReferenceObject $expectedHelperFiles -DifferenceObject $manifestHelperFiles
    if ($difference.Count -gt 0) {
        throw 'Installer helper manifest must exactly match the expected helper file set.'
    }

    $recordMap = Get-InstallerHelperRecordMap -Manifest $Manifest
    foreach ($helperFile in $expectedHelperFiles) {
        if (-not $recordMap.ContainsKey($helperFile)) {
            throw "Installer helper manifest is missing integrity metadata for $helperFile."
        }
    }

    foreach ($helperFile in $requiredHelperFiles) {
        Assert-InstallerHelperFileRecord -HelperPath (Join-Path $HelperDirectory $helperFile) -HelperRecord $recordMap[$helperFile]
    }

    $computedCacheKey = Get-ComputedInstallerHelperRecordCacheKey -RecordMap $recordMap -HelperFiles $expectedHelperFiles
    if (-not [string]::Equals([string]$Manifest.CacheKey, $computedCacheKey, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Installer helper manifest cache key does not match the helper file records. Expected $computedCacheKey but found $([string]$Manifest.CacheKey)."
    }

    if ($RequirePinnedCacheKey -and
        -not [string]::Equals($computedCacheKey, $script:InstallerHelperManifestCacheKey, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Installer helper manifest cache key does not match the pinned installer helper manifest cache key."
    }
}
function Get-InstallerHelperRuntimeCacheKey {
    param([Parameter(Mandatory)] $Manifest)

    $seedParts = @(
        [string]$Manifest.CacheKey
        ((Get-Item 'function:Test-TuiSupport').ScriptBlock.ToString())
        ((Get-Item 'function:Resolve-Selection').ScriptBlock.ToString())
        ((Get-Item 'function:Invoke-InstallerAction').ScriptBlock.ToString())
        ($script:InstallerHelperSourcePaths -join '|')
    )

    $utf8 = [System.Text.Encoding]::UTF8.GetBytes(($seedParts -join '|'))
    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        $hashBytes = $sha256.ComputeHash($utf8)
    }
    finally {
        $sha256.Dispose()
    }

    return 'runtime-sha256:' + (($hashBytes | ForEach-Object { $_.ToString('x2') }) -join '')
}
function Read-TuiHelperManifest {
    param(
        [Parameter(Mandatory)] [string]$ManifestPath,
        [Parameter(Mandatory)] [string]$HelperDirectory
    )

    try {
        $trustedManifestPath = Assert-InstallerLocalPathTrusted -Path $ManifestPath
        $trustedHelperDirectory = Assert-InstallerLocalPathTrusted -Path $HelperDirectory
    }
    catch {
        return $null
    }

    if (-not (Test-Path -LiteralPath $trustedManifestPath)) {
        return $null
    }

    $parsed = Get-Content -LiteralPath $trustedManifestPath -Raw | ConvertFrom-Json
    $helperFiles = @()
    $helperFileRecords = New-Object System.Collections.Generic.List[object]
    if ($null -ne $parsed.helperFiles) {
        foreach ($entry in $parsed.helperFiles) {
            if ($entry -is [string]) {
                if (-not [string]::IsNullOrWhiteSpace([string]$entry)) {
                    $helperFiles += [string]$entry
                }
                continue
            }

            if ($null -eq $entry) {
                continue
            }

            $path = [string]$entry.path
            $sha256 = [string]$entry.sha256
            $sizeBytes = [long]$entry.sizeBytes
            if (-not [string]::IsNullOrWhiteSpace($path)) {
                $helperFiles += $path
                if (-not [string]::IsNullOrWhiteSpace($sha256) -and $sizeBytes -gt 0) {
                    $helperFileRecords.Add([ordered]@{
                            Path = $path
                            Sha256 = $sha256.ToLowerInvariant()
                            SizeBytes = $sizeBytes
                        })
                }
            }
        }
    }

    if ($helperFiles.Count -eq 0) {
        $helperFiles = @(Get-HelperLeafNames)
    }

    $cacheKey = [string]$parsed.cacheKey
    if ([string]::IsNullOrWhiteSpace($cacheKey)) {
        $cacheKey = Get-ComputedInstallerHelperCacheKey -HelperDirectory $trustedHelperDirectory -HelperFiles $helperFiles
    }

    return [ordered]@{
        CacheKey = $cacheKey
        HelperFiles = @($helperFiles)
        HelperFileRecords = @($helperFileRecords.ToArray())
    }
}
if (-not [string]::IsNullOrWhiteSpace($script:InstallerBootstrapUiPath)) {
    $helperDirectory = Split-Path -Parent $script:InstallerBootstrapUiPath
    $manifest = Read-TuiHelperManifest -ManifestPath (Get-TuiHelperManifestPath -RootPath $helperDirectory) -HelperDirectory $helperDirectory
    if ($null -ne $manifest) {
        Assert-InstallerHelperManifestIntegrity -HelperDirectory $helperDirectory -Manifest $manifest -RequirePinnedCacheKey
        $recordMap = Get-InstallerHelperRecordMap -Manifest $manifest
        if (-not $recordMap.ContainsKey('Installer.BootstrapUi.ps1')) {
            throw 'Installer helper manifest is missing integrity metadata for Installer.BootstrapUi.ps1.'
        }
        Assert-InstallerHelperFileRecord -HelperPath $script:InstallerBootstrapUiPath -HelperRecord $recordMap['Installer.BootstrapUi.ps1']
        . $script:InstallerBootstrapUiPath
    }
}
function Get-TuiHelperManifest {
    param(
        [switch]$SuppressBootstrapOutput,
        [switch]$IncludeInstalledRoots
    )

    if ($null -ne $script:TuiHelperManifest) {
        return $script:TuiHelperManifest
    }

    foreach ($candidateRoot in @(Get-LocalInstallerHelperRoots -IncludeInstalledRoots:$IncludeInstalledRoots)) {
        if ([string]::IsNullOrWhiteSpace($candidateRoot) -or -not (Test-Path $candidateRoot)) {
            continue
        }

        $helperFiles = @(Get-HelperLeafNames)
        $allPresent = $true
        foreach ($helperFile in $helperFiles) {
            if (-not (Test-Path (Join-Path $candidateRoot $helperFile))) {
                $allPresent = $false
                break
            }
        }

        if (-not $allPresent) {
            continue
        }

        $manifestPath = Get-TuiHelperManifestPath -RootPath $candidateRoot
        $manifest = Read-TuiHelperManifest -ManifestPath $manifestPath -HelperDirectory $candidateRoot
        if ($null -eq $manifest) {
            continue
        }

        try {
            Assert-InstallerHelperManifestIntegrity -HelperDirectory $candidateRoot -Manifest $manifest -RequirePinnedCacheKey
        }
        catch {
            if (Test-InstalledInstallerHelperRootCandidate -CandidateRoot $candidateRoot) {
                continue
            }

            throw
        }

        $script:TuiHelperManifest = $manifest
        return $manifest
    }

    if ((Resolve-InstallerMode) -ne 'online') {
        return $null
    }

    $downloadBaseUri = Resolve-TuiHelperDownloadBaseUri
    if ([string]::IsNullOrWhiteSpace($downloadBaseUri)) {
        return $null
    }

    $runtimeRoot = Get-TuiHelperRuntimeRoot
    $manifestPath = Get-TuiHelperManifestPath -RootPath $runtimeRoot

    $manifestUri = "$downloadBaseUri/$($script:InstallerHelperManifestFileName)"
    $temporaryManifestPath = "$manifestPath.download"
    try {
        if (-not $SuppressBootstrapOutput) {
            Write-TuiBootstrapScreen 'Preparing installer UI... (manifest)' | Out-Host
        }
        Invoke-InstallerWebRequest -Uri $manifestUri -OutFile $temporaryManifestPath -Headers @{ 'User-Agent' = 'wpf-devtools-online-installer' } -TimeoutSec (Get-TuiHelperRequestTimeoutSeconds)
        Move-StandalonePathWithRetry -SourcePath $temporaryManifestPath -DestinationPath $manifestPath
    }
    catch {
        Remove-PathIfExists -Path $temporaryManifestPath
        return $null
    }

    $script:TuiHelperManifest = Read-TuiHelperManifest -ManifestPath $manifestPath -HelperDirectory $runtimeRoot
    if ($null -eq $script:TuiHelperManifest) {
        throw "Installer helper manifest was not found after download: $manifestPath"
    }

    Assert-InstallerHelperManifestIntegrity -HelperDirectory $runtimeRoot -Manifest $script:TuiHelperManifest -RequirePinnedCacheKey -RequiredHelperFiles @()
    return $script:TuiHelperManifest
}
function Resolve-TuiHelpersFromReleaseArchive {
    param(
        [Parameter(Mandatory)] [string]$RuntimeRoot,
        [Parameter(Mandatory)] [string]$CacheKeyPath,
        [Parameter(Mandatory)] [string[]]$HelperFiles,
        [switch]$SuppressBootstrapOutput
    )

    $archiveDownload = Get-TuiHelperArchiveDownloadDetails
    $archivePath = Assert-InstallerLocalPathTrusted -Path (Join-Path $RuntimeRoot 'helper-bootstrap-package.zip')
    $temporaryArchivePath = Assert-InstallerLocalPathTrusted -Path "$archivePath.download"
    try {
        if (-not $SuppressBootstrapOutput) {
            Write-TuiBootstrapScreen 'Preparing installer UI... (archive)' | Out-Host
        }

        Invoke-InstallerWebRequest -Uri ([string]$archiveDownload.DownloadUri) -OutFile $temporaryArchivePath -Headers @{ 'User-Agent' = 'wpf-devtools-online-installer' } -TimeoutSec (Get-TuiHelperRequestTimeoutSeconds)
        Move-StandalonePathWithRetry -SourcePath $temporaryArchivePath -DestinationPath $archivePath
        Assert-TuiHelperArchiveIntegrity -ArchivePath $archivePath -DownloadDetails $archiveDownload
        Copy-InstallerHelperBundleFromArchive -ArchivePath $archivePath -DestinationRoot $RuntimeRoot -HelperFiles $HelperFiles
    }
    catch {
        Remove-PathIfExists -Path $temporaryArchivePath
        throw "Failed to download installer UI runtime from $([string]$archiveDownload.DownloadUri). $($_.Exception.Message)"
    }

    $manifestPath = Get-TuiHelperManifestPath -RootPath $RuntimeRoot
    $manifest = Read-TuiHelperManifest -ManifestPath $manifestPath -HelperDirectory $RuntimeRoot
    if ($null -eq $manifest) {
        throw "Installer helper manifest was not found in helper bootstrap archive: $manifestPath"
    }

    $script:TuiHelperManifest = $manifest
    Assert-InstallerHelperManifestIntegrity -HelperDirectory $RuntimeRoot -Manifest $manifest -RequirePinnedCacheKey -RequiredHelperFiles $HelperFiles
    Set-Content -LiteralPath $CacheKeyPath -Value (Get-InstallerHelperRuntimeCacheKey -Manifest $manifest) -Encoding UTF8
    $script:TuiHelperBootstrapArchive = [ordered]@{
        ArchivePath = $archivePath
        DownloadUri = [string]$archiveDownload.DownloadUri
        AssetName = [string]$archiveDownload.AssetName
        ResolvedVersion = [string]$archiveDownload.ResolvedVersion
        ResolvedArchitecture = [string](Resolve-TuiHelperBootstrapArchitecture)
    }
    $script:TuiHelperResolvedRoot = $RuntimeRoot
    return $RuntimeRoot
}
