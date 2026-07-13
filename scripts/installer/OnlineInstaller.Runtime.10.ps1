function Ensure-TuiHelpersAvailable {
    param(
        [switch]$SuppressBootstrapOutput,
        [switch]$IncludeInstalledRoots,
        [string[]]$RequiredHelperFiles
    )

    $allHelperFiles = @(Get-HelperLeafNames)
    $helperFiles = if ($PSBoundParameters.ContainsKey('RequiredHelperFiles')) {
        @($RequiredHelperFiles | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) } | Sort-Object -Unique)
    }
    else {
        $allHelperFiles
    }

    if (-not [string]::IsNullOrWhiteSpace($script:TuiHelperResolvedRoot)) {
        $cachedRootHasRequiredFiles = $true
        foreach ($helperFile in $helperFiles) {
            if (-not (Test-Path -LiteralPath (Join-Path $script:TuiHelperResolvedRoot $helperFile))) {
                $cachedRootHasRequiredFiles = $false
                break
            }
        }

        if ($cachedRootHasRequiredFiles) {
            return $script:TuiHelperResolvedRoot
        }
    }

    $manifest = Get-TuiHelperManifest -SuppressBootstrapOutput:$SuppressBootstrapOutput -IncludeInstalledRoots:$IncludeInstalledRoots
    foreach ($candidateRoot in @(Get-LocalInstallerHelperRoots -IncludeInstalledRoots:$IncludeInstalledRoots)) {
        if ([string]::IsNullOrWhiteSpace($candidateRoot)) {
            continue
        }

        try {
            $trustedCandidateRoot = Assert-InstallerLocalPathTrusted -Path $candidateRoot
        }
        catch {
            continue
        }

        $allPresent = $true
        foreach ($helperFile in $helperFiles) {
            $helperPath = Assert-InstallerLocalPathTrusted -Path (Join-Path $trustedCandidateRoot $helperFile)
            if (-not (Test-Path -LiteralPath $helperPath)) {
                $allPresent = $false
                break
            }
        }

        if ($allPresent) {
            if ($null -ne $manifest) {
                Assert-InstallerHelperManifestIntegrity -HelperDirectory $trustedCandidateRoot -Manifest $manifest -RequirePinnedCacheKey -RequiredHelperFiles $helperFiles
            }
            $script:TuiHelperResolvedRoot = $trustedCandidateRoot
            return $trustedCandidateRoot
        }
    }

    if ((Resolve-InstallerMode) -eq 'offline' -and (Test-PackageArchiveRequested)) {
        $runtimeRoot = Get-TuiHelperRuntimeRoot
        $archivePath = [string]$PackageArchivePath

        if ([string]::IsNullOrWhiteSpace($archivePath)) {
            return $null
        }

        Remove-PathIfExists -Path $runtimeRoot
        New-Item -ItemType Directory -Force -Path $runtimeRoot | Out-Null
        $trustedArchivePath = Initialize-TrustedLocalPackageArchiveCopy `
            -ArchivePath $archivePath `
            -DestinationRoot $runtimeRoot `
            -HelperFiles $helperFiles `
            -ResolvedVersion $Version `
            -ResolvedArchitecture (Resolve-TuiHelperBootstrapArchitecture)

        $manifestPath = Get-TuiHelperManifestPath -RootPath $runtimeRoot
        $manifest = Read-TuiHelperManifest -ManifestPath $manifestPath -HelperDirectory $runtimeRoot
        if ($null -eq $manifest) {
            throw "Installer helper manifest was not found in package runtime: $manifestPath"
        }

        $script:TuiHelperManifest = $manifest
        Assert-InstallerHelperManifestIntegrity -HelperDirectory $runtimeRoot -Manifest $manifest -RequirePinnedCacheKey -RequiredHelperFiles $helperFiles
        $script:TuiHelperResolvedRoot = $runtimeRoot
        return $runtimeRoot
    }

    if ((Resolve-InstallerMode) -ne 'online') {
        return $null
    }

    $runtimeRoot = Get-TuiHelperRuntimeRoot
    $cacheKeyPath = Get-TuiHelperCacheKeyPath -RuntimeRoot $runtimeRoot
    $downloadBaseUri = Resolve-TuiHelperDownloadBaseUri
    Remove-PathIfExists -Path $runtimeRoot
    New-Item -ItemType Directory -Force -Path $runtimeRoot | Out-Null
    $runtimeRoot = Assert-InstallerLocalPathTrusted -Path $runtimeRoot
    $cacheKeyPath = Assert-InstallerLocalPathTrusted -Path $cacheKeyPath

    if (-not [string]::IsNullOrWhiteSpace($downloadBaseUri) -and $null -ne $manifest) {
        $runtimeManifestPath = Assert-InstallerLocalPathTrusted -Path (Get-TuiHelperManifestPath -RootPath $runtimeRoot)
        $manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $runtimeManifestPath -Encoding UTF8

        $requestTimeoutSeconds = Get-TuiHelperRequestTimeoutSeconds
        $bootstrapDeadline = [DateTimeOffset]::UtcNow.AddSeconds((Get-TuiHelperBootstrapTimeoutSeconds))
        $totalHelperCount = $helperFiles.Count
        $downloadIndex = 0
        $helperRecordMap = Get-InstallerHelperRecordMap -Manifest $manifest

        foreach ($helperFile in $helperFiles) {
            if ([DateTimeOffset]::UtcNow -gt $bootstrapDeadline) {
                throw 'Installer UI bootstrap timed out before the runtime assets finished downloading.'
            }

            $destinationPath = Assert-InstallerLocalPathTrusted -Path (Join-Path $runtimeRoot $helperFile)
            $downloadIndex += 1
            if (-not $SuppressBootstrapOutput) {
                Write-TuiBootstrapScreen "Preparing installer UI... ($downloadIndex/$totalHelperCount)" | Out-Host
            }
            $downloadUri = "$downloadBaseUri/$helperFile"
            $temporaryPath = Assert-InstallerLocalPathTrusted -Path "$destinationPath.download"
            try {
                Invoke-InstallerWebRequest -Uri $downloadUri -OutFile $temporaryPath -Headers @{ 'User-Agent' = 'wpf-devtools-online-installer' } -TimeoutSec $requestTimeoutSeconds
                if ($helperRecordMap.ContainsKey($helperFile)) {
                    Assert-InstallerHelperFileRecord -HelperPath $temporaryPath -HelperRecord $helperRecordMap[$helperFile]
                }
                Move-StandalonePathWithRetry -SourcePath $temporaryPath -DestinationPath $destinationPath
            }
            catch {
                Remove-PathIfExists -Path $temporaryPath
                Remove-PathIfExists -Path $runtimeRoot
                New-Item -ItemType Directory -Force -Path $runtimeRoot | Out-Null
                $runtimeRoot = Assert-InstallerLocalPathTrusted -Path $runtimeRoot
                $cacheKeyPath = Assert-InstallerLocalPathTrusted -Path $cacheKeyPath
                return Resolve-TuiHelpersFromReleaseArchive -RuntimeRoot $runtimeRoot -CacheKeyPath $cacheKeyPath -HelperFiles $helperFiles -SuppressBootstrapOutput:$SuppressBootstrapOutput
            }
        }

        Set-Content -LiteralPath $cacheKeyPath -Value (Get-InstallerHelperRuntimeCacheKey -Manifest $manifest) -Encoding UTF8
        $script:TuiHelperResolvedRoot = $runtimeRoot
        return $runtimeRoot
    }

    return Resolve-TuiHelpersFromReleaseArchive -RuntimeRoot $runtimeRoot -CacheKeyPath $cacheKeyPath -HelperFiles $helperFiles -SuppressBootstrapOutput:$SuppressBootstrapOutput
}
function Get-InstallerSharedModulePaths {
    param(
        [switch]$AllowMissing,
        [switch]$IncludeInstalledRoots
    )

    if ($null -ne $script:InstallerSharedModulePathsCache -and
        [bool]$script:InstallerSharedModulePathsCacheIncludesInstalledRoots -eq [bool]$IncludeInstalledRoots) {
        return @($script:InstallerSharedModulePathsCache)
    }

    try {
        $helperRoot = Ensure-TuiHelpersAvailable -SuppressBootstrapOutput -IncludeInstalledRoots:$IncludeInstalledRoots -RequiredHelperFiles (Get-InstallerSharedRuntimeHelperLeafNames)
    }
    catch {
        if ($AllowMissing) {
            return @()
        }

        throw
    }

    if ([string]::IsNullOrWhiteSpace($helperRoot)) {
        if ($AllowMissing) {
            return @()
        }

        throw 'Shared installer helper scripts are unavailable in the current execution context.'
    }

    $helperPaths = New-Object System.Collections.Generic.List[string]
    foreach ($helperFile in $script:InstallerSharedHelperLeafNames) {
        $helperPath = Join-Path $helperRoot $helperFile
        if (-not (Test-Path $helperPath)) {
            throw "Shared installer helper script was not found: $helperPath"
        }

        $helperPaths.Add($helperPath)
    }

    $script:InstallerSharedModulePathsCache = @($helperPaths.ToArray())
    $script:InstallerSharedModulePathsCacheIncludesInstalledRoots = [bool]$IncludeInstalledRoots
    return @($script:InstallerSharedModulePathsCache)
}
function Import-TuiHelpers {
    $helperRoot = Ensure-TuiHelpersAvailable
    if ([string]::IsNullOrWhiteSpace($helperRoot)) {
        throw 'TUI helper scripts are unavailable in the current execution context.'
    }

    $helperPaths = New-Object System.Collections.Generic.List[string]
    foreach ($repoRelativePath in $script:InstallerHelperSourcePaths) {
        $leafName = Split-Path $repoRelativePath -Leaf
        $runtimePath = Join-Path $helperRoot $leafName
        if (-not (Test-Path $runtimePath)) {
            throw "TUI helper script was not found: $runtimePath"
        }

        $helperPaths.Add($runtimePath)
    }

    return @($helperPaths)
}
function Invoke-WithTuiHelpers {
    param([Parameter(Mandatory)] [scriptblock]$ScriptBlock)

    foreach ($helperPath in @(Import-TuiHelpers)) {
        . $helperPath
    }

    return (. $ScriptBlock)
}
function Get-NextArchitecture {
    param(
        [Parameter(Mandatory)] [string]$Current,
        [Parameter(Mandatory)] [int]$Direction
    )

    $architectures = @('x64', 'x86', 'arm64')
    $index = [Array]::IndexOf($architectures, $Current)
    if ($index -lt 0) {
        $index = [Array]::IndexOf($architectures, (Get-SystemDefaultArchitecture))
    }

    $index = ($index + $Direction) % $architectures.Count
    if ($index -lt 0) {
        $index += $architectures.Count
    }

    return $architectures[$index]
}
function Get-SupportedClients {
    return @(
        [pscustomobject]@{ Id = 'claude-code'; Label = 'Claude Code'; ConfigType = 'cli' }
        [pscustomobject]@{ Id = 'codex'; Label = 'Codex/Codex CLI'; ConfigType = 'cli' }
        [pscustomobject]@{ Id = 'grok'; Label = 'Grok Build CLI'; ConfigType = 'cli' }
        [pscustomobject]@{ Id = 'cursor'; Label = 'Cursor'; ConfigType = 'json-file' }
        [pscustomobject]@{ Id = 'vscode'; Label = 'VS Code'; ConfigType = 'json-file' }
        [pscustomobject]@{ Id = 'visual-studio'; Label = 'Visual Studio'; ConfigType = 'json-file' }
        [pscustomobject]@{ Id = 'claude-desktop'; Label = 'Claude Desktop'; ConfigType = 'json-file' }
        [pscustomobject]@{ Id = 'other'; Label = 'Other'; ConfigType = 'artifact-only' }
    )
}
function Resolve-ClientBaseId {
    param([Parameter(Mandatory)] [string]$ClientId)

    if ($ClientId -like 'cursor-*') {
        return 'cursor'
    }

    return $ClientId
}
function Resolve-ClientStateKey {
    param(
        [Parameter(Mandatory)] [string]$ClientId,
        [string]$RegistrationMode
    )

    if ((Resolve-ClientBaseId -ClientId $ClientId) -ne 'cursor') {
        return $ClientId
    }

    if ($ClientId -in @('cursor-global', 'cursor-project')) {
        return $ClientId
    }

    switch ([string]$RegistrationMode) {
        'cursor-project' { return 'cursor-project' }
        default { return 'cursor-global' }
    }
}
function Resolve-ClientLabel {
    param([Parameter(Mandatory)] [string]$ClientId)

    switch ($ClientId) {
        'cursor-global' { return 'Cursor (Global)' }
        'cursor-project' { return 'Cursor (Project)' }
    }

    $client = Get-SupportedClients | Where-Object { $_.Id -eq (Resolve-ClientBaseId -ClientId $ClientId) } | Select-Object -First 1
    if ($null -ne $client) {
        return [string]$client.Label
    }

    return $ClientId
}
function Get-DefaultClient {
    if ($null -ne (Get-Command 'claude' -ErrorAction SilentlyContinue)) { return 'claude-code' }
    if ($null -ne (Get-Command 'codex' -ErrorAction SilentlyContinue)) { return 'codex' }
    if ($null -ne (Get-Command 'grok' -ErrorAction SilentlyContinue)) { return 'grok' }
    if ($null -ne (Get-Command 'cursor-agent' -ErrorAction SilentlyContinue)) { return 'cursor' }
    if (Test-InstallerPathExists -Root $env:USERPROFILE -ChildPath '.cursor') { return 'cursor' }
    if (Test-InstallerPathExists -Root $env:APPDATA -ChildPath 'Code\User') { return 'vscode' }
    if (Test-InstallerPathExists -Root $env:USERPROFILE -ChildPath '.mcp.json') { return 'visual-studio' }
    return 'other'
}
function Test-InstallerPathExists {
    param(
        [string]$Root,
        [Parameter(Mandatory)] [string]$ChildPath
    )

    if ([string]::IsNullOrWhiteSpace($Root)) {
        return $false
    }

    return (Test-Path -LiteralPath (Join-Path $Root $ChildPath))
}
function Get-DetectedInstallerClients {
    $detectedClients = @()

    foreach ($client in @(Get-SupportedClients)) {
        $clientId = [string]$client.Id
        $available = $false
        $evidence = @()

        switch ($clientId) {
            'claude-code' {
                $available = $null -ne (Get-Command 'claude' -ErrorAction SilentlyContinue)
                if ($available) { $evidence += 'claude command' }
                break
            }
            'codex' {
                $available = $null -ne (Get-Command 'codex' -ErrorAction SilentlyContinue)
                if ($available) { $evidence += 'codex command' }
                break
            }
            'grok' {
                $available = $null -ne (Get-Command 'grok' -ErrorAction SilentlyContinue)
                if ($available) { $evidence += 'grok command' }
                break
            }
            'cursor' {
                $hasCommand = $null -ne (Get-Command 'cursor-agent' -ErrorAction SilentlyContinue)
                $hasConfigRoot = Test-InstallerPathExists -Root $env:USERPROFILE -ChildPath '.cursor'
                $available = $hasCommand -or $hasConfigRoot
                if ($hasCommand) { $evidence += 'cursor-agent command' }
                if ($hasConfigRoot) { $evidence += '%USERPROFILE%\.cursor' }
                break
            }
            'vscode' {
                $available = Test-InstallerPathExists -Root $env:APPDATA -ChildPath 'Code\User'
                if ($available) { $evidence += '%APPDATA%\Code\User' }
                break
            }
            'visual-studio' {
                $available = Test-InstallerPathExists -Root $env:USERPROFILE -ChildPath '.mcp.json'
                if ($available) { $evidence += '%USERPROFILE%\.mcp.json' }
                break
            }
            'claude-desktop' {
                $available = Test-InstallerPathExists -Root $env:APPDATA -ChildPath 'Claude\claude_desktop_config.json'
                if ($available) { $evidence += '%APPDATA%\Claude\claude_desktop_config.json' }
                break
            }
            'other' {
                $available = $true
                $evidence += 'artifact-only fallback'
                break
            }
        }

        $detectedClients += [ordered]@{
            client = $clientId
            available = [bool]$available
            registrationStyle = [string]$client.ConfigType
            evidence = @($evidence)
        }
    }

    return @($detectedClients)
}
function Get-InstallerPlanFallbackRoot {
    if (-not [string]::IsNullOrWhiteSpace($InstallRoot)) {
        return [string]$InstallRoot
    }

    if (-not [string]::IsNullOrWhiteSpace($env:APPDATA)) {
        return (Join-Path $env:APPDATA 'WpfDevToolsMcp')
    }

    return (Join-Path ([System.IO.Path]::GetTempPath()) 'WpfDevToolsMcp')
}
function Get-InstallerPlanStateSnapshot {
    if ([string]::IsNullOrWhiteSpace($env:APPDATA)) {
        return [ordered]@{
            LastInstallRoot = $null
            ArchitectureRecords = @()
            RegistrationRecords = @()
        }
    }

    $statePath = Join-Path (Join-Path $env:APPDATA 'WpfDevToolsMcp') 'installer-state.json'
    if (-not (Test-Path -LiteralPath $statePath)) {
        return [ordered]@{
            LastInstallRoot = $null
            ArchitectureRecords = @()
            RegistrationRecords = @()
        }
    }

    try {
        $parsed = Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
    }
    catch {
        return [ordered]@{
            LastInstallRoot = $null
            ArchitectureRecords = @()
            RegistrationRecords = @()
        }
    }

    $architectureRecords = @()
    if ($null -ne $parsed.architectures) {
        foreach ($property in $parsed.architectures.PSObject.Properties) {
            $architectureRecords += $property.Value
        }
    }

    $registrationRecords = @()
    if ($null -ne $parsed.registrations) {
        foreach ($property in $parsed.registrations.PSObject.Properties) {
            $registrationRecords += $property.Value
        }
    }

    return [ordered]@{
        LastInstallRoot = [string]$parsed.lastInstallRoot
        ArchitectureRecords = @($architectureRecords)
        RegistrationRecords = @($registrationRecords)
    }
}
function Test-InstallerPlanPathEquals {
    param(
        [string]$Left,
        [string]$Right
    )

    if ([string]::IsNullOrWhiteSpace($Left) -or [string]::IsNullOrWhiteSpace($Right)) {
        return $false
    }

    if (Get-Command 'Test-StandaloneInstallerPathEquals' -ErrorAction SilentlyContinue) {
        return (Test-StandaloneInstallerPathEquals -Left $Left -Right $Right)
    }

    return [string]::Equals(
        [System.IO.Path]::GetFullPath($Left).TrimEnd('\'),
        [System.IO.Path]::GetFullPath($Right).TrimEnd('\'),
        [System.StringComparison]::OrdinalIgnoreCase)
}
