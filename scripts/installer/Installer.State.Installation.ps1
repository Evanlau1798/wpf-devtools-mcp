function Get-DefaultInstallRootPath {
    return (Join-Path $env:APPDATA 'WpfDevToolsMcp')
}

function Normalize-InstallerPathCore {
    param([string]$PathValue)

    if ([string]::IsNullOrWhiteSpace($PathValue)) {
        return $null
    }

    $trimmed = [string]$PathValue.Trim().Trim('"')
    if ([string]::IsNullOrWhiteSpace($trimmed)) {
        return $null
    }

    $normalizedSeparators = $trimmed.Replace('/', '\')
    try {
        return [System.IO.Path]::GetFullPath($normalizedSeparators)
    }
    catch {
        return $normalizedSeparators
    }
}

function Test-InstallerPathEqualsCore {
    param(
        [string]$Left,
        [string]$Right
    )

    $normalizedLeft = Normalize-InstallerPathCore -PathValue $Left
    $normalizedRight = Normalize-InstallerPathCore -PathValue $Right
    if ([string]::IsNullOrWhiteSpace($normalizedLeft) -or [string]::IsNullOrWhiteSpace($normalizedRight)) {
        return $false
    }

    return [string]::Equals($normalizedLeft, $normalizedRight, [System.StringComparison]::OrdinalIgnoreCase)
}

function Resolve-InstallerOwnershipFromExecutable {
    param([string]$InstalledExecutable)

    $result = [ordered]@{
        InstallerOwned = $false
        InstalledExecutable = $InstalledExecutable
        InstallBase = $null
        InstallRoot = $null
        Architecture = $null
        ResolvedVersion = $null
    }

    if ([string]::IsNullOrWhiteSpace($InstalledExecutable)) {
        return $result
    }

    try {
        $trustedInstalledExecutable = Assert-InstallerLocalPathTrusted -Path $InstalledExecutable
    }
    catch {
        return $result
    }

    if (-not (Test-Path -LiteralPath $trustedInstalledExecutable)) {
        return $result
    }

    $architectureMatch = [regex]::Match($trustedInstalledExecutable, 'wpf-devtools-(x64|x86|arm64)\.exe', 'IgnoreCase')
    if ($architectureMatch.Success) {
        $result.Architecture = [string]$architectureMatch.Groups[1].Value.ToLowerInvariant()
    }

    $binDirectory = Split-Path -Parent $trustedInstalledExecutable
    if ([string]::IsNullOrWhiteSpace($binDirectory)) {
        return $result
    }

    $currentDirectory = Split-Path -Parent $binDirectory
    if ([string]::IsNullOrWhiteSpace($currentDirectory)) {
        return $result
    }

    $installBase = Split-Path -Parent $currentDirectory
    if ([string]::IsNullOrWhiteSpace($installBase)) {
        return $result
    }

    try {
        $manifestPath = Assert-InstallerLocalPathTrusted -Path (Join-Path $installBase 'install-manifest.json')
    }
    catch {
        return $result
    }

    if (-not (Test-Path -LiteralPath $manifestPath)) {
        return $result
    }

    try {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
        $manifestExecutable = [string]$manifest.executable
        if (-not [string]::IsNullOrWhiteSpace($manifestExecutable) -and (Test-InstallerPathEqualsCore -Left $manifestExecutable -Right $trustedInstalledExecutable)) {
            $result.InstallerOwned = $true
            $result.InstallBase = $installBase
            $result.InstallRoot = [string]$manifest.installRoot
            if ([string]::IsNullOrWhiteSpace($result.InstallRoot)) {
                $result.InstallRoot = Split-Path -Parent $installBase
            }

            if ([string]::IsNullOrWhiteSpace([string]$result.Architecture)) {
                $result.Architecture = [string]$manifest.architecture
            }

            $result.ResolvedVersion = [string]$manifest.version
        }
    }
    catch {
    }

    return $result
}

function Get-InstallerRecordStringValueCore {
    param(
        $Record,
        [Parameter(Mandatory)] [string[]]$PropertyNames
    )

    if ($null -eq $Record) {
        return $null
    }

    if ($Record -is [System.Collections.IDictionary]) {
        foreach ($propertyName in $PropertyNames) {
            if ($Record.Contains($propertyName) -and -not [string]::IsNullOrWhiteSpace([string]$Record[$propertyName])) {
                return [string]$Record[$propertyName]
            }
        }
    }

    foreach ($propertyName in $PropertyNames) {
        $property = $Record.PSObject.Properties[$propertyName]
        if ($null -ne $property -and -not [string]::IsNullOrWhiteSpace([string]$property.Value)) {
            return [string]$property.Value
        }
    }

    return $null
}

function Get-InstallerKnownArchitecturesCore {
    return @('x64', 'x86', 'arm64')
}

function Get-LiveInstallerManifestEvidence {
    param(
        [string]$InstallRoot,
        [string]$Architecture
    )

    if ([string]::IsNullOrWhiteSpace($InstallRoot) -or [string]::IsNullOrWhiteSpace($Architecture)) {
        return $null
    }

    try {
        $installBase = Assert-InstallerLocalPathTrusted -Path (Join-Path $InstallRoot $Architecture)
        $manifestPath = Assert-InstallerLocalPathTrusted -Path (Join-Path $installBase 'install-manifest.json')
    }
    catch {
        return $null
    }

    if (-not (Test-Path -LiteralPath $manifestPath)) {
        return $null
    }

    try {
        $manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
    }
    catch {
        return $null
    }

    $manifestInstallRoot = [string]$manifest.installRoot
    if (-not [string]::IsNullOrWhiteSpace($manifestInstallRoot) -and -not (Test-InstallerPathEqualsCore -Left $manifestInstallRoot -Right $InstallRoot)) {
        return $null
    }

    $installedExecutable = [string]$manifest.executable
    if ([string]::IsNullOrWhiteSpace($installedExecutable)) {
        $installedExecutable = Join-Path $installBase "current\\bin\\wpf-devtools-$Architecture.exe"
    }

    $ownership = Resolve-InstallerOwnershipFromExecutable -InstalledExecutable $installedExecutable
    if (-not [bool]$ownership.InstallerOwned) {
        return $null
    }

    if (-not (Test-InstallerPathEqualsCore -Left ([string]$ownership.InstallRoot) -Right $InstallRoot)) {
        return $null
    }

    return [ordered]@{
        Architecture = $Architecture
        InstalledExecutable = [string]$ownership.InstalledExecutable
        ResolvedVersion = [string]$ownership.ResolvedVersion
    }
}

function Test-InstallRootHasLiveInstallerEvidence {
    param([string]$InstallRoot)

    foreach ($architecture in @(Get-InstallerKnownArchitecturesCore)) {
        if ($null -ne (Get-LiveInstallerManifestEvidence -InstallRoot $InstallRoot -Architecture $architecture)) {
            return $true
        }
    }

    return $false
}

function Test-StateRecordHasLiveInstallEvidence {
    param(
        $Record,
        [string]$ExpectedInstallRoot
    )

    if ([string]::IsNullOrWhiteSpace($ExpectedInstallRoot) -or $null -eq $Record) {
        return $false
    }

    $recordInstallRoot = Get-InstallerRecordStringValueCore -Record $Record -PropertyNames @('installRoot', 'InstallRoot')
    if (-not (Test-InstallerPathEqualsCore -Left $recordInstallRoot -Right $ExpectedInstallRoot)) {
        return $false
    }

    $installedExecutable = Get-InstallerRecordStringValueCore -Record $Record -PropertyNames @('installedExecutable', 'InstalledExecutable', 'executable', 'Executable')
    if ([string]::IsNullOrWhiteSpace($installedExecutable)) {
        return $false
    }

    $ownership = Resolve-InstallerOwnershipFromExecutable -InstalledExecutable $installedExecutable
    if (-not [bool]$ownership.InstallerOwned) {
        return $false
    }

    return (Test-InstallerPathEqualsCore -Left ([string]$ownership.InstallRoot) -Right $ExpectedInstallRoot)
}

function Resolve-PreferredInstallRoot {
    if ($script:InstallRootWasSpecified) {
        return $InstallRoot
    }

    $state = Get-InstallerState
    if (-not [string]::IsNullOrWhiteSpace($state.lastInstallRoot)) {
        $lastInstallRoot = [string]$state.lastInstallRoot
        $defaultInstallRoot = Get-DefaultInstallRootPath
        if (Test-InstallerPathEqualsCore -Left $lastInstallRoot -Right $defaultInstallRoot) {
            return $defaultInstallRoot
        }

        $hasArchitectureEvidence = @($state.architectures.GetEnumerator() | Where-Object {
                Test-StateRecordHasLiveInstallEvidence -Record $_.Value -ExpectedInstallRoot $lastInstallRoot
            }).Count -gt 0
        $hasRegistrationEvidence = @($state.registrations.GetEnumerator() | Where-Object {
                Test-StateRecordHasLiveInstallEvidence -Record $_.Value -ExpectedInstallRoot $lastInstallRoot
            }).Count -gt 0
        $hasFilesystemEvidence = Test-InstallRootHasLiveInstallerEvidence -InstallRoot $lastInstallRoot
        if ($hasArchitectureEvidence -or $hasRegistrationEvidence -or $hasFilesystemEvidence) {
            return $lastInstallRoot
        }
    }

    return (Get-DefaultInstallRootPath)
}

function Resolve-InstallBasePath {
    param(
        [Parameter(Mandatory)] [string]$ResolvedInstallRoot,
        [Parameter(Mandatory)] [string]$ResolvedArchitecture
    )

    return (Join-Path (Resolve-AbsoluteDirectory -Path $ResolvedInstallRoot) $ResolvedArchitecture)
}

function Get-StandardInstallJson {
    param(
        [Parameter(Mandatory)] [string]$ResolvedInstallRoot,
        [Parameter(Mandatory)] [string]$ResolvedArchitecture
    )

    $installBase = Resolve-InstallBasePath -ResolvedInstallRoot $ResolvedInstallRoot -ResolvedArchitecture $ResolvedArchitecture
    $installedExecutable = Join-Path $installBase "current\\bin\\wpf-devtools-$ResolvedArchitecture.exe"
    $serverNode = [ordered]@{
        type = 'stdio'
        command = $installedExecutable
        args = @()
    }

    return ([ordered]@{
            mcpServers = [ordered]@{
                'wpf-devtools' = $serverNode
            }
        } | ConvertTo-Json -Depth 5)
}

function Get-RegistrationsForArchitecture {
    param(
        [Parameter(Mandatory)] $State,
        [Parameter(Mandatory)] [string]$ResolvedArchitecture,
        [Parameter(Mandatory)] [string]$ResolvedInstallRoot,
        [string]$ExcludeClient
    )

    $remaining = @()
    foreach ($property in $State.registrations.GetEnumerator()) {
        if ($property.Key -eq $ExcludeClient) {
            continue
        }

        if (($property.Value.architecture -eq $ResolvedArchitecture) -and ($property.Value.installRoot -eq $ResolvedInstallRoot)) {
            $remaining += $property.Key
        }
    }

    return @($remaining)
}
