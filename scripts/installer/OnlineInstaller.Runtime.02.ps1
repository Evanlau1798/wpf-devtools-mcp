function Import-OnlineInstallerReleaseAssetModule {
    param([switch]$AllowRemote)

    $candidatePaths = New-Object System.Collections.Generic.List[string]
    $scriptRoot = Resolve-InstallerScriptRoot
    if (-not [string]::IsNullOrWhiteSpace($scriptRoot)) {
        $candidatePaths.Add((Join-Path $scriptRoot "installer/$script:InstallerReleaseAssetModuleLeafName"))
    }

    if ([bool]$script:WpfDevToolsInstallerTestModeEnabled) {
        if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_HELPER_DIRECTORY)) {
            $candidatePaths.Add((Join-Path $env:WPFDEVTOOLS_INSTALLER_HELPER_DIRECTORY $script:InstallerReleaseAssetModuleLeafName))
        }

        if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_SOURCE_ROOT)) {
            $candidatePaths.Add((Join-Path $env:WPFDEVTOOLS_INSTALLER_SOURCE_ROOT $script:InstallerReleaseAssetModuleRepositoryRelativePath))
        }

        $candidatePaths.Add((Join-Path (Get-Location).Path $script:InstallerReleaseAssetModuleRepositoryRelativePath))
    }

    foreach ($candidatePath in @($candidatePaths.ToArray() | Select-Object -Unique)) {
        if ([string]::IsNullOrWhiteSpace($candidatePath) -or -not (Test-Path -LiteralPath $candidatePath -PathType Leaf)) {
            continue
        }

        $trustedPath = (Resolve-Path -LiteralPath (Assert-InstallerLocalPathTrusted -Path $candidatePath)).Path
        $actualHash = Get-InstallerFileSha256Hex -Path $trustedPath
        if (-not [string]::Equals($actualHash, $script:InstallerReleaseAssetModuleSha256, [System.StringComparison]::OrdinalIgnoreCase)) {
            throw "Installer release asset module integrity verification failed for $trustedPath."
        }

        return [ordered]@{
            Path = $trustedPath
            Content = $null
        }
    }

    if (-not $AllowRemote) {
        return $null
    }

    if ([bool]$script:WpfDevToolsInstallerTestModeEnabled) {
        throw "Installer release asset module was not found: $script:InstallerReleaseAssetModuleRepositoryRelativePath"
    }

    $moduleUri = Get-InstallerReleaseAssetModuleUri
    $moduleContent = [string](Invoke-InstallerWebRequest -Uri $moduleUri -Headers @{ 'User-Agent' = 'wpf-devtools-online-installer' } -TimeoutSec 15).Content
    $actualRemoteHash = Get-InstallerTextSha256Hex -Content $moduleContent
    if (-not [string]::Equals($actualRemoteHash, $script:InstallerReleaseAssetModuleSha256, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Installer release asset module integrity verification failed for $moduleUri."
    }

    return [ordered]@{
        Path = $null
        Content = $moduleContent
    }
}
function Assert-OnlineInstallerTestOnlyOverrides {
    if ([bool]$script:WpfDevToolsInstallerTestModeEnabled) {
        return
    }

    if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_HELPER_DIRECTORY)) {
        throw 'WPFDEVTOOLS_INSTALLER_HELPER_DIRECTORY is supported only when WPFDEVTOOLS_INSTALLER_TEST_MODE=1.'
    }

    if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_INSTALLER_HELPER_BASE_URI)) {
        throw 'WPFDEVTOOLS_INSTALLER_HELPER_BASE_URI is supported only when WPFDEVTOOLS_INSTALLER_TEST_MODE=1.'
    }
}
$script:OnlineInstallerReleaseAssetModuleLoaded = $false
if ($Action -eq 'install') {
    Assert-OnlineInstallerTestOnlyOverrides
}
$script:OnlineInstallerReleaseAssetModule = Import-OnlineInstallerReleaseAssetModule -AllowRemote:($Action -eq 'install')
if ($null -ne $script:OnlineInstallerReleaseAssetModule -and
    -not [string]::IsNullOrWhiteSpace([string]$script:OnlineInstallerReleaseAssetModule.Path)) {
    . ([string]$script:OnlineInstallerReleaseAssetModule.Path)
    $script:OnlineInstallerReleaseAssetModuleLoaded = $true
}
elseif ($null -ne $script:OnlineInstallerReleaseAssetModule -and
    -not [string]::IsNullOrWhiteSpace([string]$script:OnlineInstallerReleaseAssetModule.Content)) {
    . ([scriptblock]::Create([string]$script:OnlineInstallerReleaseAssetModule.Content))
    $script:OnlineInstallerReleaseAssetModuleLoaded = $true
}
function Get-TuiHelperRuntimeRoot {
    $runtimeRoot = Join-Path (Resolve-AbsoluteDirectory -Path $WorkingRoot) 'tui-helpers'
    New-Item -ItemType Directory -Force -Path $runtimeRoot | Out-Null
    return $runtimeRoot
}
function Get-TuiHelperCacheKeyPath {
    param([Parameter(Mandatory)] [string]$RuntimeRoot)

    return (Join-Path $RuntimeRoot 'helper-cache-key.txt')
}
function Get-TuiHelperManifestPath {
    param([Parameter(Mandatory)] [string]$RootPath)

    return (Join-Path $RootPath $script:InstallerHelperManifestFileName)
}
function Find-InstallerHelperArchiveEntry {
    param(
        [Parameter(Mandatory)] $Archive,
        [Parameter(Mandatory)] [string]$LeafName
    )

    foreach ($candidatePath in @(
            "bin/installer/$LeafName"
            "installer/$LeafName"
            "bin\installer\$LeafName"
            "installer\$LeafName"
        )) {
        $entry = $Archive.GetEntry($candidatePath)
        if ($null -ne $entry) {
            return $entry
        }
    }

    return $null
}
function Copy-InstallerHelperBundleFromArchive {
    param(
        [Parameter(Mandatory)] [string]$ArchivePath,
        [Parameter(Mandatory)] [string]$DestinationRoot,
        [Parameter(Mandatory)] [string[]]$HelperFiles
    )

    Add-Type -AssemblyName System.IO.Compression.FileSystem
    $resolvedArchivePath = (Resolve-Path -LiteralPath (Assert-InstallerLocalPathTrusted -Path $ArchivePath)).Path
    $trustedDestinationRoot = Assert-InstallerLocalPathTrusted -Path $DestinationRoot
    New-Item -ItemType Directory -Force -Path $trustedDestinationRoot | Out-Null
    Assert-InstallerLocalPathTrusted -Path $trustedDestinationRoot | Out-Null
    $archive = [System.IO.Compression.ZipFile]::OpenRead($resolvedArchivePath)
    try {
        foreach ($leafName in @($script:InstallerHelperManifestFileName) + @($HelperFiles)) {
            $entry = Find-InstallerHelperArchiveEntry -Archive $archive -LeafName $leafName
            if ($null -eq $entry) {
                throw "Installer helper archive entry was not found: $leafName"
            }

            $destinationPath = Assert-InstallerLocalPathTrusted -Path (Join-Path $trustedDestinationRoot $leafName)
            $destinationDirectory = Split-Path -Parent $destinationPath
            if (-not [string]::IsNullOrWhiteSpace($destinationDirectory)) {
                New-Item -ItemType Directory -Force -Path $destinationDirectory | Out-Null
                Assert-InstallerLocalPathTrusted -Path $destinationDirectory | Out-Null
            }

            $entryStream = $entry.Open()
            $fileStream = [System.IO.File]::Open($destinationPath, [System.IO.FileMode]::Create, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
            try {
                $entryStream.CopyTo($fileStream)
            }
            finally {
                $fileStream.Dispose()
                $entryStream.Dispose()
            }
        }
    }
    finally {
        $archive.Dispose()
    }
}
function Test-PackageArchiveRequested {
    return $script:PackageArchivePathWasSpecified -and -not [string]::IsNullOrWhiteSpace([string]$PackageArchivePath)
}
function Add-InstallerHelperRootCandidate {
    param(
        [System.Collections.Generic.List[string]]$Roots,
        [string]$CandidateRoot
    )

    if ([string]::IsNullOrWhiteSpace($CandidateRoot)) {
        return
    }

    try {
        $trustedCandidateRoot = Assert-InstallerLocalPathTrusted -Path $CandidateRoot
    }
    catch {
        return
    }

    if (-not $Roots.Contains($trustedCandidateRoot)) {
        $Roots.Add($trustedCandidateRoot)
    }
}
function Get-LocalInstallerHelperRoots {
    param([switch]$IncludeInstalledRoots)

    $candidateRoots = New-Object System.Collections.Generic.List[string]

    $localScriptRoot = Resolve-InstallerScriptRoot
    if (-not [string]::IsNullOrWhiteSpace($localScriptRoot)) {
        Add-InstallerHelperRootCandidate -Roots $candidateRoots -CandidateRoot (Join-Path $localScriptRoot 'installer')
    }

    $overrideDirectory = Get-TuiHelperOverrideDirectory
    Add-InstallerHelperRootCandidate -Roots $candidateRoots -CandidateRoot $overrideDirectory

    if ($IncludeInstalledRoots) {
        foreach ($helperRoot in @(Get-InstalledInstallerHelperRoots)) {
            Add-InstallerHelperRootCandidate -Roots $candidateRoots -CandidateRoot $helperRoot
        }
    }

    return @($candidateRoots.ToArray())
}
function Get-StandaloneInstallerStateSnapshot {
    $statePath = Resolve-StandaloneInstallerStatePath
    if (-not (Test-Path -LiteralPath $statePath)) {
        return $null
    }

    for ($attempt = 0; $attempt -lt 6; $attempt++) {
        try {
            Assert-InstallerLocalPathTrusted -Path $statePath | Out-Null
            return (Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json)
        }
        catch {
            if ((Test-StandaloneTransientFileSystemError -Exception $_.Exception) -and $attempt -lt 5) {
                Start-Sleep -Milliseconds ([Math]::Min(75 * ($attempt + 1), 400))
                continue
            }

            Move-StandaloneCorruptInstallerStateFile -Path $statePath | Out-Null
            return $null
        }
    }

    return $null
}
function Move-StandaloneCorruptInstallerStateFile {
    param([Parameter(Mandatory)] [string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return $null
    }

    $resolvedPath = Assert-InstallerLocalPathTrusted -Path $Path
    if (-not (Test-Path -LiteralPath $resolvedPath)) {
        return $null
    }

    try {
        $directory = Split-Path -Parent $resolvedPath
        $fileName = Split-Path -Leaf $resolvedPath
        $quarantinePath = Assert-InstallerLocalPathTrusted -Path (Join-Path $directory ("{0}.corrupt-{1}" -f $fileName, ([guid]::NewGuid().ToString('N'))))
        Assert-InstallerLocalPathTrusted -Path $resolvedPath | Out-Null
        Assert-InstallerLocalPathTrusted -Path $quarantinePath | Out-Null
        Move-Item -LiteralPath $resolvedPath -Destination $quarantinePath -Force
        return $quarantinePath
    }
    catch {
        return $null
    }
}
function Test-StandaloneTransientFileSystemError {
    param([System.Exception]$Exception)

    $candidate = $Exception
    while ($null -ne $candidate) {
        if ($candidate -is [System.IO.IOException] -or $candidate -is [System.UnauthorizedAccessException]) {
            return $true
        }

        $candidate = $candidate.InnerException
    }

    return $false
}
function Move-StandalonePathWithRetry {
    param(
        [Parameter(Mandatory)] [string]$SourcePath,
        [Parameter(Mandatory)] [string]$DestinationPath
    )

    for ($attempt = 0; $attempt -lt 6; $attempt++) {
        try {
            $resolvedSourcePath = Assert-InstallerLocalPathTrusted -Path $SourcePath
            $resolvedDestinationPath = Assert-InstallerLocalPathTrusted -Path $DestinationPath -RejectHardLinks
            if (Test-Path -LiteralPath $resolvedDestinationPath) {
                Remove-PathIfExists -Path $resolvedDestinationPath -RetryCount 1
            }

            Move-Item -LiteralPath $resolvedSourcePath -Destination $resolvedDestinationPath -Force
            return
        }
        catch {
            if (-not (Test-StandaloneTransientFileSystemError -Exception $_.Exception) -or $attempt -ge 5) {
                throw (Get-StandaloneInstallerFileSystemRecoveryMessage -Operation 'Move installer path' -Path $SourcePath -Exception $_.Exception)
            }

            Start-Sleep -Milliseconds ([Math]::Min(75 * ($attempt + 1), 400))
        }
    }
}
function Resolve-StandaloneInstallerStatePath {
    param([switch]$CreateRoot)

    $stateRoot = Assert-InstallerLocalPathTrusted -Path (Join-Path $env:APPDATA 'WpfDevToolsMcp')
    if ($CreateRoot) {
        New-Item -ItemType Directory -Force -Path $stateRoot | Out-Null
        Assert-InstallerLocalPathTrusted -Path $stateRoot | Out-Null
    }

    return (Join-Path $stateRoot 'installer-state.json')
}
function Get-StandaloneEmptyInstallerState {
    return [ordered]@{
        lastInstallRoot = $null
        architectures = [ordered]@{}
        registrations = [ordered]@{}
    }
}
function Get-StandaloneInstallerState {
    $snapshot = Get-StandaloneInstallerStateSnapshot
    $state = Get-StandaloneEmptyInstallerState

    if ($null -eq $snapshot) {
        return $state
    }

    $state.lastInstallRoot = [string]$snapshot.lastInstallRoot

    if ($null -ne $snapshot.architectures) {
        foreach ($property in $snapshot.architectures.PSObject.Properties) {
            $state.architectures[$property.Name] = [ordered]@{
                version = [string]$property.Value.version
                executable = [string]$property.Value.executable
                installRoot = [string]$property.Value.installRoot
            }
        }
    }

    if ($null -ne $snapshot.registrations) {
        foreach ($property in $snapshot.registrations.PSObject.Properties) {
            $state.registrations[$property.Name] = [ordered]@{
                architecture = [string]$property.Value.architecture
                installRoot = [string]$property.Value.installRoot
                mode = [string]$property.Value.mode
                target = [string]$property.Value.target
                resolvedVersion = [string]$property.Value.resolvedVersion
                installedExecutable = [string]$property.Value.installedExecutable
                lastVerifiedUtc = [string]$property.Value.lastVerifiedUtc
            }
        }
    }

    return $state
}
function Save-StandaloneInstallerState {
    param([Parameter(Mandatory)] $State)

    $statePath = Resolve-StandaloneInstallerStatePath -CreateRoot
    $tempStatePath = "$statePath.tmp-$([guid]::NewGuid().ToString('N'))"
    try {
        Assert-InstallerLocalPathTrusted -Path $tempStatePath | Out-Null
        $State | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $tempStatePath -Encoding UTF8
        if ((Get-InstallerTestEnvironmentValue -Name 'WPFDEVTOOLS_INSTALLER_TEST_FAIL_SAVE_STANDALONE_STATE') -eq '1') {
            throw 'Simulated standalone state save failure.'
        }
        Move-StandalonePathWithRetry -SourcePath $tempStatePath -DestinationPath $statePath
    }
    finally {
        if (Test-Path -LiteralPath $tempStatePath) {
            Remove-PathIfExists -Path $tempStatePath
        }
    }

    return $statePath
}
function Get-StandaloneExistingConfigMap {
    param([Parameter(Mandatory)] [string]$Path)

    $resolvedPath = Assert-InstallerLocalPathTrusted -Path $Path
    $map = [ordered]@{}
    if (-not (Test-Path -LiteralPath $resolvedPath)) {
        return $map
    }

    $raw = Get-Content -LiteralPath $resolvedPath -Raw
    if ([string]::IsNullOrWhiteSpace($raw)) {
        return $map
    }

    try {
        $parsed = $raw | ConvertFrom-Json -ErrorAction Stop
    }
    catch {
        throw "Failed to parse JSON config file '$resolvedPath'. Fix the malformed JSON and retry. The installer did not modify the file or update registration state. Parser error: $($_.Exception.Message)"
    }

    foreach ($property in $parsed.PSObject.Properties) {
        $map[$property.Name] = $property.Value
    }

    return $map
}
function Get-StandaloneConfigCollectionMap {
    param(
        [Parameter(Mandatory)] $Root,
        [Parameter(Mandatory)] [string]$CollectionName
    )

    $servers = [ordered]@{}
    if ($Root.Contains($CollectionName) -and $null -ne $Root[$CollectionName]) {
        foreach ($property in $Root[$CollectionName].PSObject.Properties) {
            $servers[$property.Name] = $property.Value
        }
    }

    return $servers
}
function Test-StandaloneJsonConfigRegistration {
    param(
        [Parameter(Mandatory)] [string]$CollectionName,
        [Parameter(Mandatory)] [string]$ConfigPath
    )

    if ([string]::IsNullOrWhiteSpace($ConfigPath)) {
        return $false
    }

    $resolvedConfigPath = Assert-InstallerLocalPathTrusted -Path $ConfigPath
    if (-not (Test-Path -LiteralPath $resolvedConfigPath)) {
        return $false
    }

    $root = Get-StandaloneExistingConfigMap -Path $resolvedConfigPath
    $servers = Get-StandaloneConfigCollectionMap -Root $root -CollectionName $CollectionName
    return $servers.Contains('wpf-devtools')
}
