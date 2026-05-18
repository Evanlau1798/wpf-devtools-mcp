function Invoke-Step {
    param(
        [Parameter(Mandatory)] [string]$FilePath,
        [Parameter(Mandatory)] [string[]]$Arguments
    )

    Write-Host "> $FilePath $($Arguments -join ' ')"
    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($Arguments -join ' ')"
    }
}

function Get-RuntimeId {
    param([Parameter(Mandatory)] [string]$Architecture)

    switch ($Architecture) {
        'x64' { return 'win-x64' }
        'x86' { return 'win-x86' }
        'arm64' { return 'win-arm64' }
        default { throw "Unsupported architecture: $Architecture" }
    }
}

function Get-BootstrapperPlatform {
    param([Parameter(Mandatory)] [string]$Architecture)

    switch ($Architecture) {
        'x64' { return 'x64' }
        'x86' { return 'Win32' }
        'arm64' { return 'ARM64' }
        default { throw "Unsupported architecture: $Architecture" }
    }
}

function ConvertTo-MSBuildPropertyValue {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return ''
    }

    return $Value.TrimEnd(';') -replace ';', '%3B'
}

function Resolve-WindowsSdkDirectory {
    if (-not [string]::IsNullOrWhiteSpace($env:WindowsSDKDir)) {
        return $env:WindowsSDKDir.TrimEnd('\')
    }

    $defaultSdkDirectory = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10'
    if (Test-Path -LiteralPath $defaultSdkDirectory) {
        return $defaultSdkDirectory.TrimEnd('\')
    }

    return ''
}

function Resolve-WindowsSdkVersion {
    param([string]$WindowsSdkDirectory)

    if ([string]::IsNullOrWhiteSpace($WindowsSdkDirectory)) {
        return ''
    }

    $includeRoot = Join-Path $WindowsSdkDirectory 'Include'
    if (-not (Test-Path -LiteralPath $includeRoot)) {
        return ''
    }

    $versionDirectory = Get-ChildItem -LiteralPath $includeRoot -Directory -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' } |
        Sort-Object { [version]$_.Name } -Descending |
        Select-Object -First 1

    if ($null -eq $versionDirectory) {
        return ''
    }

    return [string]$versionDirectory.Name
}

function Get-PackageChannel {
    param([Parameter(Mandatory)] [string]$BuildConfiguration)
    if ($BuildConfiguration -eq 'Debug') {
        return 'dev'
    }

    return 'release'
}

function Get-SignaturePolicy {
    param([Parameter(Mandatory)] [string]$BuildConfiguration)
    if ($BuildConfiguration -eq 'Debug') {
        return 'DebugTrustedRootSkip'
    }

    return 'RequireAuthenticodeSignature'
}

function Normalize-ReleaseTag {
    param([Parameter(Mandatory)] [string]$Tag)

    if ($Tag.StartsWith('v')) {
        return $Tag
    }

    return "v$Tag"
}

function Assert-ExpectedReleaseTagMatchesVersion {
    param(
        [Parameter(Mandatory)] [string]$Version,
        [string]$ExpectedReleaseTag,
        [Parameter(Mandatory)] [string]$ProjectPath
    )

    if ([string]::IsNullOrWhiteSpace($ExpectedReleaseTag)) {
        return
    }

    $normalizedExpectedTag = Normalize-ReleaseTag -Tag $ExpectedReleaseTag
    $normalizedVersionTag = Normalize-ReleaseTag -Tag $Version
    if (-not [string]::Equals($normalizedExpectedTag, $normalizedVersionTag, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Expected release tag '$ExpectedReleaseTag' does not match project version '$Version' from $ProjectPath."
    }
}

function ConvertTo-NativeResourceVersion {
    param([Parameter(Mandatory)] [string]$Version)

    $trimmedVersion = $Version.Trim()
    if ($trimmedVersion.StartsWith('v')) {
        $trimmedVersion = $trimmedVersion.Substring(1)
    }

    $numericVersion = ($trimmedVersion -split '\+', 2)[0]
    $numericVersion = ($numericVersion -split '-', 2)[0]
    $parts = @($numericVersion -split '\.')
    if ($parts.Count -gt 4) {
        throw "Version '$Version' contains more than four numeric components."
    }

    $resolvedParts = @(0, 0, 0, 0)
    for ($index = 0; $index -lt $parts.Count; $index++) {
        if ($parts[$index] -notmatch '^\d+$') {
            throw "Version '$Version' contains a non-numeric resource version component: '$($parts[$index])'."
        }

        $value = [int]$parts[$index]
        if ($value -lt 0 -or $value -gt 65535) {
            throw "Version '$Version' contains a resource version component outside 0..65535: $value."
        }

        $resolvedParts[$index] = $value
    }

    return [ordered]@{
        Numeric = [string]::Join(',', $resolvedParts)
        FileVersionString = [string]::Join('.', $resolvedParts)
        ProductVersionString = $trimmedVersion
    }
}

function Copy-DirectoryContents {
    param(
        [Parameter(Mandatory)] [string]$Source,
        [Parameter(Mandatory)] [string]$Destination
    )

    if (-not (Test-Path $Source)) {
        throw "Source path does not exist: $Source"
    }

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Copy-Item -Path (Join-Path $Source '*') -Destination $Destination -Recurse -Force
}

function Resolve-ArchitectureList {
    param([string[]]$InputArchitectures)

    $resolvedArchitectures = New-Object System.Collections.Generic.List[string]
    foreach ($entry in $InputArchitectures) {
        foreach ($candidate in ($entry -split ',')) {
            $normalized = $candidate.Trim().ToLowerInvariant()
            if ([string]::IsNullOrWhiteSpace($normalized)) {
                continue
            }

            if ($supportedArchitectures -notcontains $normalized) {
                throw "Unsupported architecture: $normalized. Supported values: $($supportedArchitectures -join ', ')"
            }

            if (-not $resolvedArchitectures.Contains($normalized)) {
                $resolvedArchitectures.Add($normalized)
            }
        }
    }

    if ($resolvedArchitectures.Count -eq 0) {
        throw 'At least one architecture must be specified.'
    }

    return @($resolvedArchitectures)
}

function Resolve-ServerOutputSource {
    param(
        [Parameter(Mandatory)] [string]$RepositoryRoot,
        [Parameter(Mandatory)] [string]$BuildConfiguration,
        [Parameter(Mandatory)] [string]$RuntimeId,
        [Parameter(Mandatory)] [bool]$UseExistingBuildOutput
    )

    $runtimeBuildDir = Join-Path $RepositoryRoot "src\WpfDevTools.Mcp.Server\bin\$BuildConfiguration\net8.0\$RuntimeId"
    if (Test-Path $runtimeBuildDir) {
        return [ordered]@{
            Path = $runtimeBuildDir
        }
    }

    $frameworkBuildDir = Join-Path $RepositoryRoot "src\WpfDevTools.Mcp.Server\bin\$BuildConfiguration\net8.0"
    if ($UseExistingBuildOutput) {
        if (Test-Path (Join-Path $frameworkBuildDir 'WpfDevTools.Mcp.Server.exe')) {
            throw "Expected existing server output was not found for runtime '$RuntimeId'. -SkipBuild requires RID-specific publish output under $runtimeBuildDir; framework-only output at $frameworkBuildDir cannot be repackaged safely."
        }

        throw "Expected existing server output was not found for runtime '$RuntimeId': $runtimeBuildDir"
    }

    return $null
}

function Copy-ServerBuildOutput {
    param(
        [Parameter(Mandatory)] $SourceInfo,
        [Parameter(Mandatory)] [string]$Destination
    )

    Copy-DirectoryContents -Source $SourceInfo.Path -Destination $Destination
}

function Copy-DirectoryFilesOnly {
    param(
        [Parameter(Mandatory)] [string]$Source,
        [Parameter(Mandatory)] [string]$Destination
    )

    if (-not (Test-Path $Source)) {
        throw "Source path does not exist: $Source"
    }

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Get-ChildItem -Path $Source -File | ForEach-Object {
        Copy-Item -Path $_.FullName -Destination (Join-Path $Destination $_.Name) -Force
    }
}

function Get-InstallerHelperFiles {
    param([Parameter(Mandatory)] [string]$RepositoryRoot)

    $manifestPath = Join-Path $RepositoryRoot 'scripts\installer\installer-helpers.manifest.json'
    if (-not (Test-Path $manifestPath)) {
        throw "Installer helper manifest was not found: $manifestPath"
    }

    $manifest = Get-Content -Path $manifestPath -Raw | ConvertFrom-Json
    $helperFiles = @(
        $manifest.helperFiles |
            ForEach-Object {
                if ($_ -is [string]) {
                    return [string]$_
                }

                return [string]$_.path
            } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )
    if ($helperFiles.Count -eq 0) {
        throw "Installer helper manifest did not declare any helper files: $manifestPath"
    }

    return @($helperFiles)
}

function Copy-InstallerHelperFiles {
    param(
        [Parameter(Mandatory)] [string]$RepositoryRoot,
        [Parameter(Mandatory)] [string]$Destination
    )

    $installerRoot = Join-Path $RepositoryRoot 'scripts\installer'
    $manifestPath = Join-Path $installerRoot 'installer-helpers.manifest.json'

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Copy-Item -Path $manifestPath -Destination (Join-Path $Destination 'installer-helpers.manifest.json') -Force

    foreach ($helperFile in @(Get-InstallerHelperFiles -RepositoryRoot $RepositoryRoot)) {
        $sourcePath = Join-Path $installerRoot $helperFile
        if (-not (Test-Path $sourcePath)) {
            throw "Installer helper file declared in manifest was not found: $sourcePath"
        }

        Copy-Item -Path $sourcePath -Destination (Join-Path $Destination $helperFile) -Force
    }
}

function Remove-PathIfExists {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return
    }

    $retryDelayMilliseconds = 250
    $maxAttempts = 40
    for ($attempt = 1; $attempt -le $maxAttempts; $attempt++) {
        if (-not (Test-Path -LiteralPath $Path)) {
            return
        }

        try {
            Remove-Item -LiteralPath $Path -Recurse -Force -ErrorAction Stop
            if (-not (Test-Path -LiteralPath $Path)) {
                return
            }
        }
        catch {
            if ($attempt -eq $maxAttempts) {
                throw
            }
        }

        Start-Sleep -Milliseconds $retryDelayMilliseconds
    }

    if (Test-Path -LiteralPath $Path) {
        throw "Failed to remove path after retries: $Path"
    }
}

function Write-ReleaseSidecars {
    param(
        [Parameter(Mandatory)] [string]$PackagingScriptRoot,
        [Parameter(Mandatory)] [string]$ArchiveRoot,
        [Parameter(Mandatory)] [string]$Version
    )

    $sidecarWriter = Join-Path $PackagingScriptRoot 'Write-ReleaseSidecars.ps1'
    if (-not (Test-Path $sidecarWriter)) {
        throw "Write-ReleaseSidecars.ps1 was not found: $sidecarWriter"
    }

    $tag = if ($Version.StartsWith('v')) { $Version } else { "v$Version" }
    & $sidecarWriter -ArchiveRoot $ArchiveRoot -Tag $tag | Out-Null
}
