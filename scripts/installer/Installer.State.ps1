if (-not (Get-Command Write-InstallerUtf8NoBomFile -ErrorAction SilentlyContinue)) {
    $encodingHelperPath = Join-Path $PSScriptRoot 'Installer.Encoding.ps1'
    if (Test-Path -LiteralPath $encodingHelperPath) {
        . $encodingHelperPath
    }
}

function Resolve-InstallerStateAbsolutePath {
    param([Parameter(Mandatory)] [string]$Path)

    $resolver = Get-Command Resolve-AbsolutePath -ErrorAction SilentlyContinue
    if ($null -ne $resolver) {
        return (Resolve-AbsolutePath -Path $Path)
    }

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path (Get-Location).Path $Path))
}

if (-not (Get-Command Test-InstallerUncOrDevicePath -ErrorAction SilentlyContinue)) {
    function Test-InstallerUncOrDevicePath {
        param([Parameter(Mandatory)] [string]$Path)

        return $Path.StartsWith('\\', [System.StringComparison]::Ordinal) -or
            $Path.StartsWith('\\?\', [System.StringComparison]::OrdinalIgnoreCase) -or
            $Path.StartsWith('\\.\', [System.StringComparison]::OrdinalIgnoreCase)
    }
}

if (-not (Get-Command Assert-InstallerLocalPathTrusted -ErrorAction SilentlyContinue)) {
    function Assert-InstallerLocalPathTrusted {
        param(
            [Parameter(Mandatory)] [string]$Path,
            [switch]$RejectHardLinks
        )

        $resolvedPath = Resolve-InstallerStateAbsolutePath -Path $Path
        if (Test-InstallerUncOrDevicePath -Path $resolvedPath) {
            throw "Installer path '$resolvedPath' is blocked because elevated installer file operations require a local path."
        }

        $root = [System.IO.Path]::GetPathRoot($resolvedPath)
        if ([string]::IsNullOrWhiteSpace($root)) {
            throw "Installer path '$resolvedPath' is blocked because elevated installer file operations require an absolute local path."
        }

        try {
            $drive = [System.IO.DriveInfo]::new($root)
            if ($drive.DriveType -eq [System.IO.DriveType]::Network) {
                throw "Installer path '$resolvedPath' is blocked because elevated installer file operations require a local path."
            }
        }
        catch [System.ArgumentException] {
            throw "Installer path '$resolvedPath' is blocked because elevated installer file operations require a local path."
        }

        $relativePath = $resolvedPath.Substring($root.Length).Trim('\', '/')
        if ([string]::IsNullOrWhiteSpace($relativePath)) {
            return $resolvedPath
        }

        $currentPath = $root
        foreach ($segment in $relativePath -split '[\\/]') {
            if ([string]::IsNullOrWhiteSpace($segment)) {
                continue
            }

            $currentPath = Join-Path $currentPath $segment
            if (-not (Test-Path -LiteralPath $currentPath)) {
                break
            }

            $item = Get-Item -LiteralPath $currentPath -Force
            if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Installer path '$resolvedPath' is blocked because '$currentPath' is a reparse point."
            }
        }

        if ($RejectHardLinks -and (Test-Path -LiteralPath $resolvedPath -PathType Leaf) -and (Get-Command Get-InstallerHardLinkCount -ErrorAction SilentlyContinue)) {
            $linkCount = Get-InstallerHardLinkCount -Path $resolvedPath
            if ($linkCount -gt 1) {
                throw "Installer path '$resolvedPath' is blocked because hardlinked installer write targets are not trusted."
            }
        }

        return $resolvedPath
    }
}

function Resolve-InstallerStatePath {
    param([switch]$CreateRoot)

    $stateRoot = Assert-InstallerLocalPathTrusted -Path (Join-Path $env:APPDATA 'WpfDevToolsMcp')
    if ($CreateRoot) {
        New-Item -ItemType Directory -Force -Path $stateRoot | Out-Null
        Assert-InstallerLocalPathTrusted -Path $stateRoot | Out-Null
    }

    return (Join-Path $stateRoot 'installer-state.json')
}

function Get-EmptyInstallerState {
    return [ordered]@{
        lastInstallRoot = $null
        architectures = [ordered]@{}
        registrations = [ordered]@{}
    }
}

function Move-CorruptInstallerStateFile {
    param([Parameter(Mandatory)] [string]$StatePath)

    $resolvedStatePath = Assert-InstallerLocalPathTrusted -Path $StatePath
    if (-not (Test-Path -LiteralPath $resolvedStatePath)) {
        return $null
    }

    $timestamp = [DateTime]::UtcNow.ToString('yyyyMMddHHmmssfff')
    $corruptPath = Assert-InstallerLocalPathTrusted -Path "$resolvedStatePath.corrupt-$timestamp"
    try {
        Assert-InstallerLocalPathTrusted -Path $resolvedStatePath | Out-Null
        Assert-InstallerLocalPathTrusted -Path $corruptPath | Out-Null
        Move-Item -LiteralPath $resolvedStatePath -Destination $corruptPath -Force
        return $corruptPath
    }
    catch {
        return $null
    }
}

function Get-InstallerState {
    $statePath = Resolve-InstallerStatePath
    $state = Get-EmptyInstallerState

    if (-not (Test-Path -LiteralPath $statePath)) {
        return $state
    }

    $raw = Get-Content -LiteralPath $statePath -Raw
    if ([string]::IsNullOrWhiteSpace($raw)) {
        return $state
    }

    try {
        $parsed = $raw | ConvertFrom-Json
    }
    catch {
        Move-CorruptInstallerStateFile -StatePath $statePath | Out-Null
        return $state
    }

    $state.lastInstallRoot = [string]$parsed.lastInstallRoot

    if ($null -ne $parsed.architectures) {
        foreach ($property in $parsed.architectures.PSObject.Properties) {
            $state.architectures[$property.Name] = [ordered]@{
                version = [string]$property.Value.version
                executable = [string]$property.Value.executable
                installRoot = [string]$property.Value.installRoot
            }
        }
    }

    if ($null -ne $parsed.registrations) {
        foreach ($property in $parsed.registrations.PSObject.Properties) {
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

function Save-InstallerState {
    param([Parameter(Mandatory)] $State)

    $statePath = Resolve-InstallerStatePath -CreateRoot
    $tempStatePath = "$statePath.tmp-$([guid]::NewGuid().ToString('N'))"
    try {
        Assert-InstallerLocalPathTrusted -Path $tempStatePath | Out-Null
        Write-InstallerUtf8NoBomFile -Path $tempStatePath -Content ($State | ConvertTo-Json -Depth 10)
        Assert-InstallerLocalPathTrusted -Path $tempStatePath | Out-Null
        Assert-InstallerLocalPathTrusted -Path $statePath -RejectHardLinks | Out-Null
        Move-Item -LiteralPath $tempStatePath -Destination $statePath -Force
    }
    finally {
        if (Test-Path -LiteralPath $tempStatePath) {
            Assert-InstallerLocalPathTrusted -Path $tempStatePath | Out-Null
            Remove-Item -LiteralPath $tempStatePath -Force
        }
    }
    return $statePath
}

function Test-InstallerStateHasData {
    param([Parameter(Mandatory)] $State)

    return (
        -not [string]::IsNullOrWhiteSpace([string]$State.lastInstallRoot) -or
        $State.architectures.Count -gt 0 -or
        $State.registrations.Count -gt 0
    )
}

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
