function Test-InstallerPlanStateRecordEvidence {
    param(
        [object[]]$Records,
        [string]$ExpectedInstallRoot
    )

    if (-not (Get-Command 'Resolve-StandaloneInstallerOwnershipFromExecutable' -ErrorAction SilentlyContinue)) {
        return $false
    }

    foreach ($record in @($Records)) {
        $installedExecutable = Get-StandaloneRecordStringValue -Record $record -PropertyNames @('installedExecutable', 'InstalledExecutable')
        if ([string]::IsNullOrWhiteSpace($installedExecutable)) {
            continue
        }

        $ownership = Resolve-StandaloneInstallerOwnershipFromExecutable -InstalledExecutable $installedExecutable
        if ([bool]$ownership.InstallerOwned -and
            (Test-InstallerPlanPathEquals -Left ([string]$ownership.InstallRoot) -Right $ExpectedInstallRoot)) {
            return $true
        }
    }

    return $false
}
function Test-InstallerPlanLiveManifestEvidence {
    param([string]$InstallRoot)

    if ([string]::IsNullOrWhiteSpace($InstallRoot)) {
        return $false
    }

    foreach ($architecture in @('x64', 'x86', 'arm64')) {
        if ($null -ne (Get-StandaloneLiveInstallerManifestEvidence -InstallRoot $InstallRoot -Architecture $architecture)) {
            return $true
        }
    }

    return $false
}
function Resolve-InstallerPlanInstallRoot {
    $fallbackInstallRoot = Get-InstallerPlanFallbackRoot

    if ($script:InstallRootWasSpecified -and -not [string]::IsNullOrWhiteSpace($InstallRoot)) {
        return [ordered]@{
            InstallRootDefault = [string]$InstallRoot
            PreferredInstallRoot = [string]$InstallRoot
            FallbackInstallRoot = [string]$fallbackInstallRoot
            InstallRootSource = 'explicit'
        }
    }

    $state = Get-InstallerPlanStateSnapshot
    $lastInstallRoot = [string]$state.LastInstallRoot
    if (-not [string]::IsNullOrWhiteSpace($lastInstallRoot)) {
        if (Test-InstallerPlanPathEquals -Left $lastInstallRoot -Right $fallbackInstallRoot) {
            return [ordered]@{
                InstallRootDefault = [string]$fallbackInstallRoot
                PreferredInstallRoot = [string]$fallbackInstallRoot
                FallbackInstallRoot = [string]$fallbackInstallRoot
                InstallRootSource = 'default'
            }
        }

        $hasStateEvidence =
            (Test-InstallerPlanStateRecordEvidence -Records @($state.ArchitectureRecords) -ExpectedInstallRoot $lastInstallRoot) -or
            (Test-InstallerPlanStateRecordEvidence -Records @($state.RegistrationRecords) -ExpectedInstallRoot $lastInstallRoot)
        $hasFilesystemEvidence = Test-InstallerPlanLiveManifestEvidence -InstallRoot $lastInstallRoot

        if ($hasStateEvidence -or $hasFilesystemEvidence) {
            return [ordered]@{
                InstallRootDefault = [string]$fallbackInstallRoot
                PreferredInstallRoot = [string]$lastInstallRoot
                FallbackInstallRoot = [string]$fallbackInstallRoot
                InstallRootSource = 'previous-live-install'
            }
        }
    }

    return [ordered]@{
        InstallRootDefault = [string]$fallbackInstallRoot
        PreferredInstallRoot = [string]$fallbackInstallRoot
        FallbackInstallRoot = [string]$fallbackInstallRoot
        InstallRootSource = 'default'
    }
}
function Get-InstallerPlan {
    $resolvedArchitecture = if ([string]::IsNullOrWhiteSpace($Architecture)) { Get-SystemDefaultArchitecture } else { $Architecture }
    $resolvedClient = if ([string]::IsNullOrWhiteSpace($Client)) { Get-DefaultClient } else { $Client }
    $supportedClientIds = @(Get-SupportedClients | ForEach-Object { [string]$_.Id })
    $installRootPlan = Resolve-InstallerPlanInstallRoot

    return [ordered]@{
        action = 'plan'
        contractVersion = 1
        platform = 'windows'
        version = [string]$Version
        releaseChannel = Get-InstallerReleaseChannel
        architecture = [string]$resolvedArchitecture
        client = [string]$resolvedClient
        installRootDefault = [string]$installRootPlan.InstallRootDefault
        preferredInstallRoot = [string]$installRootPlan.PreferredInstallRoot
        fallbackInstallRoot = [string]$installRootPlan.FallbackInstallRoot
        installRootSource = [string]$installRootPlan.InstallRootSource
        supportedClients = @($supportedClientIds)
        detectedClients = @(Get-DetectedInstallerClients)
        requiresUserConfirmationBeforeMutation = $true
        mutatesFileSystem = $false
        downloadsReleaseAssets = $false
        runsClientRegistration = $false
        mutationBoundary = 'read-only discovery only; no download, install, registration, or filesystem mutation before user confirmation'
    }
}
function Get-InstallerReleaseChannel {
    if ($Prerelease) {
        return 'prerelease'
    }

    $requestedVersion = [string]$Version
    if (Test-InstallerPrereleaseVersion -VersionValue $requestedVersion) {
        return 'prerelease'
    }

    return 'stable'
}
function Test-InstallerPrereleaseVersion {
    param([AllowNull()] [string]$VersionValue)

    if ([string]::IsNullOrWhiteSpace($VersionValue)) {
        return $false
    }

    $normalizedVersion = ([string]$VersionValue).Trim().TrimStart('v', 'V')
    return ($normalizedVersion -match '^\d+\.\d+\.\d+-[0-9A-Za-z][0-9A-Za-z.-]*$')
}
function Test-InstallerPrereleaseArtifactText {
    param([AllowNull()] [string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $false
    }

    $text = [string]$Value
    $assetMatch = [regex]::Match(
        $text,
        'release_(?<version>[^\\/]+?)_win-(?:x64|x86|arm64)\.zip',
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    if ($assetMatch.Success -and (Test-InstallerPrereleaseVersion -VersionValue ([string]$assetMatch.Groups['version'].Value))) {
        return $true
    }

    $tagMatch = [regex]::Match(
        $text,
        '(?:^|/)v(?<version>\d+\.\d+\.\d+-[0-9A-Za-z][0-9A-Za-z.-]*)(?:/|$)',
        [System.Text.RegularExpressions.RegexOptions]::IgnoreCase)
    return ($tagMatch.Success -and (Test-InstallerPrereleaseVersion -VersionValue ([string]$tagMatch.Groups['version'].Value)))
}
function Get-GitHubReleaseListApiUri {
    return 'https://api.github.com/repos/Evanlau1798/wpf-devtools-mcp/releases?per_page=20'
}
function Select-LatestInstallerPrereleaseVersion {
    param($Releases)

    foreach ($release in @($Releases)) {
        if ($null -eq $release) {
            continue
        }

        $propertyNames = @($release.PSObject.Properties.Name)
        $isDraft = $propertyNames -contains 'draft' -and [bool]$release.draft
        $isPrerelease = $propertyNames -contains 'prerelease' -and [bool]$release.prerelease
        $tagName = if ($propertyNames -contains 'tag_name') { [string]$release.tag_name } else { $null }
        if (-not $isDraft -and $isPrerelease -and -not [string]::IsNullOrWhiteSpace($tagName)) {
            return $tagName.TrimStart('v', 'V')
        }
    }

    return $null
}
function Resolve-LatestVersionCachePath {
    param(
        [switch]$CreateRoot,
        [ValidateSet('stable', 'prerelease')]
        [string]$ReleaseChannel = (Get-InstallerReleaseChannel)
    )

    $stateRoot = Assert-InstallerLocalPathTrusted -Path (Join-Path $env:APPDATA 'WpfDevToolsMcp')
    if ($CreateRoot) {
        New-Item -ItemType Directory -Force -Path $stateRoot | Out-Null
        Assert-InstallerLocalPathTrusted -Path $stateRoot | Out-Null
    }

    $cacheFileName = if ($ReleaseChannel -eq 'prerelease') { 'latest-prerelease-release-cache.json' } else { 'latest-release-cache.json' }
    return (Join-Path $stateRoot $cacheFileName)
}
function Get-CachedLatestInstallerVersion {
    param(
        [ValidateSet('stable', 'prerelease')]
        [string]$ReleaseChannel = (Get-InstallerReleaseChannel)
    )

    $cachePath = Resolve-LatestVersionCachePath -ReleaseChannel $ReleaseChannel
    if (-not (Test-Path -LiteralPath $cachePath)) {
        return $null
    }

    try {
        $parsed = Get-Content -LiteralPath $cachePath -Raw | ConvertFrom-Json
        return [string]$parsed.version
    }
    catch {
        return $null
    }
}
function Save-LatestInstallerVersionCache {
    param(
        [Parameter(Mandatory)] [string]$VersionValue,
        [ValidateSet('stable', 'prerelease')]
        [string]$ReleaseChannel = (Get-InstallerReleaseChannel)
    )

    if ([string]::IsNullOrWhiteSpace($VersionValue)) {
        return
    }

    $cachePath = Resolve-LatestVersionCachePath -CreateRoot -ReleaseChannel $ReleaseChannel
    Assert-InstallerLocalPathTrusted -Path $cachePath -RejectHardLinks | Out-Null
    [ordered]@{
        version = $VersionValue
        releaseChannel = $ReleaseChannel
        refreshedUtc = [DateTime]::UtcNow.ToString('o')
    } | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $cachePath -Encoding UTF8
}
function Resolve-RequestedReleaseVersion {
    param([Parameter(Mandatory)] [string]$RequestedVersion)

    if ($RequestedVersion -ne 'latest') {
        return $RequestedVersion.TrimStart('v', 'V')
    }

    if (-not [string]::IsNullOrWhiteSpace($script:ResolvedOnlineReleaseVersion)) {
        return $script:ResolvedOnlineReleaseVersion
    }

    $script:ResolvedOnlineReleaseVersion = Get-LatestInstallerVersion
    if ([string]::IsNullOrWhiteSpace($script:ResolvedOnlineReleaseVersion)) {
        throw 'Failed to resolve the latest WPF DevTools release version.'
    }

    return $script:ResolvedOnlineReleaseVersion
}
function ConvertTo-PowerShellEncodedCommand {
    param([Parameter(Mandatory)] [string]$CommandText)

    return [Convert]::ToBase64String([System.Text.Encoding]::Unicode.GetBytes($CommandText))
}
function Resolve-InstallerMode {
    if (Test-PackageArchiveRequested) { return 'offline' }
    if (-not [string]::IsNullOrWhiteSpace((Resolve-LocalPackageRoot))) { return 'offline' }
    return 'online'
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
function Resolve-PackageSession {
    param(
        [Parameter(Mandatory)] [string]$Mode,
        [Parameter(Mandatory)] [string]$ResolvedVersion,
        [Parameter(Mandatory)] [string]$ResolvedArchitecture
    )

    foreach ($helperPath in @(Get-InstallerSharedModulePaths)) {
        . $helperPath
    }

    $workingRootPath = Resolve-AbsoluteDirectory -Path $WorkingRoot
    $sessionRoot = Join-Path $workingRootPath ([Guid]::NewGuid().ToString('N'))
    $extractRoot = Join-Path $sessionRoot 'package'

    if ($Mode -eq 'offline' -and (Test-PackageArchiveRequested)) {
        $archivePath = (Resolve-Path $PackageArchivePath).Path
        $integrity = Assert-ArchiveIntegrity -ArchivePath $archivePath -DownloadSource 'local-package' -ResolvedVersion $ResolvedVersion -ResolvedArchitecture $ResolvedArchitecture
        New-Item -ItemType Directory -Force -Path $extractRoot | Out-Null
        Assert-ArchiveSafeEntries -ArchivePath $archivePath -DestinationPath $extractRoot
        Expand-InstallerArchive -ArchivePath $archivePath -DestinationPath $extractRoot
        return [ordered]@{
            PackageDirectory = $extractRoot
            SessionRoot = $sessionRoot
            CleanupSession = $true
            DownloadSource = 'local-package'
            DownloadUri = [string]$integrity.DownloadUri
            PackageAssetName = [string]$integrity.PackageAssetName
            ResolvedVersion = [string]$integrity.ResolvedVersion
        }
    }

    if ($Mode -eq 'offline') {
        $localRoot = Resolve-LocalPackageRoot
        $manifest = Get-Content -Path (Resolve-PackageManifestPath -PackageDirectory $localRoot) -Raw | ConvertFrom-Json
        return [ordered]@{
            PackageDirectory = $localRoot
            SessionRoot = $null
            CleanupSession = $false
            DownloadSource = 'local-package'
            DownloadUri = $null
            PackageAssetName = $null
            ResolvedVersion = [string]$manifest.version
        }
    }

    $downloadVersion = Resolve-RequestedReleaseVersion -RequestedVersion $ResolvedVersion
    $downloadDetails = Get-ReleaseAssetDownloadDetails -ResolvedVersion $downloadVersion -ResolvedArchitecture $ResolvedArchitecture
    New-Item -ItemType Directory -Force -Path $sessionRoot | Out-Null
    $archivePath = $null
    if ($null -ne $script:TuiHelperBootstrapArchive -and
        [string]::Equals([string]$script:TuiHelperBootstrapArchive.ResolvedVersion, [string]$downloadDetails.ResolvedVersion, [System.StringComparison]::OrdinalIgnoreCase) -and
        [string]::Equals([string]$script:TuiHelperBootstrapArchive.ResolvedArchitecture, [string]$ResolvedArchitecture, [System.StringComparison]::OrdinalIgnoreCase) -and
        -not [string]::IsNullOrWhiteSpace([string]$script:TuiHelperBootstrapArchive.ArchivePath) -and
        (Test-Path -LiteralPath ([string]$script:TuiHelperBootstrapArchive.ArchivePath))) {
        $archivePath = [string]$script:TuiHelperBootstrapArchive.ArchivePath
    }
    else {
        $archivePath = Join-Path $sessionRoot ([string]$downloadDetails.AssetName)
        Invoke-InstallerWebRequest -Uri ([string]$downloadDetails.DownloadUri) -OutFile $archivePath -Headers @{ 'User-Agent' = 'wpf-devtools-online-installer' } -TimeoutSec (Get-ReleaseArchiveDownloadTimeoutSeconds)
    }

    $integrity = Assert-ArchiveIntegrity -ArchivePath $archivePath -DownloadSource 'github-release' -ResolvedVersion ([string]$downloadDetails.ResolvedVersion) -ResolvedArchitecture $ResolvedArchitecture
    New-Item -ItemType Directory -Force -Path $extractRoot | Out-Null
    Assert-ArchiveSafeEntries -ArchivePath $archivePath -DestinationPath $extractRoot
    Expand-InstallerArchive -ArchivePath $archivePath -DestinationPath $extractRoot
    return [ordered]@{
        PackageDirectory = $extractRoot
        SessionRoot = $sessionRoot
        CleanupSession = $true
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
function Read-ValidatedChoice {
    param(
        [Parameter(Mandatory)] [string]$Prompt,
        [Parameter(Mandatory)] [string]$DefaultValue,
        [Parameter(Mandatory)] [string[]]$AllowedValues
    )

    while ($true) {
        $response = Read-InstallerInput -Prompt $Prompt -DefaultValue $DefaultValue
        if ([string]::IsNullOrWhiteSpace($response)) {
            return $DefaultValue
        }

        $normalized = $response.Trim().ToLowerInvariant()
        if ($AllowedValues -contains $normalized) {
            return $normalized
        }

        Write-InstallerMessage ("Allowed values: " + ($AllowedValues -join ', '))
    }
}
function Read-ValidatedVersion {
    param(
        [Parameter(Mandatory)] [string]$Prompt,
        [Parameter(Mandatory)] [string]$DefaultValue
    )

    while ($true) {
        $response = Read-InstallerInput -Prompt $Prompt -DefaultValue $DefaultValue
        if ([string]::IsNullOrWhiteSpace($response)) {
            return $DefaultValue
        }

        $normalized = $response.Trim()
        if ($normalized -eq 'latest') {
            return $normalized
        }

        if ($normalized -match '^v?\d+\.\d+\.\d+(-[0-9A-Za-z][0-9A-Za-z.-]*)?$') {
            return $normalized.TrimStart('v', 'V')
        }

        Write-InstallerMessage 'Allowed values: latest or a SemVer release such as 1.0.0-beta.1'
    }
}
