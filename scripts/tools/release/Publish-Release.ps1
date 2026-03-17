param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string[]]$Architectures = @('x64'),

    [string]$OutputRoot = (Join-Path $PSScriptRoot '..\..\artifacts\release'),

    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$supportedArchitectures = @('x64', 'x86', 'arm64')

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
        return $runtimeBuildDir
    }

    if ($UseExistingBuildOutput) {
        throw "Expected existing server output was not found for runtime '$RuntimeId': $runtimeBuildDir"
    }

    return $null
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

function Remove-PathIfExists {
    param([string]$Path)

    if (-not [string]::IsNullOrWhiteSpace($Path) -and (Test-Path $Path)) {
        Remove-Item -Path $Path -Recurse -Force
    }
}

function Resolve-MSBuildPath {
    if (-not [string]::IsNullOrWhiteSpace($env:WPFDEVTOOLS_PUBLISH_RELEASE_MSBUILD_PATH)) {
        return $env:WPFDEVTOOLS_PUBLISH_RELEASE_MSBUILD_PATH
    }

    $command = Get-Command 'msbuild.exe' -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    $vsWhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
    if (Test-Path $vsWhere) {
        $resolved = & $vsWhere -latest -products * -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' |
            Select-Object -First 1
        if (-not [string]::IsNullOrWhiteSpace($resolved)) {
            return $resolved
        }
    }

    throw 'MSBuild.exe was not found. Install Visual Studio Build Tools or add MSBuild.exe to PATH.'
}

function Get-VisualStudioInstallationRoot {
    param([Parameter(Mandatory)] [string]$ResolvedMsBuildPath)

    $msbuildDirectory = Split-Path -Parent $ResolvedMsBuildPath
    if ([string]::IsNullOrWhiteSpace($msbuildDirectory)) {
        return $null
    }

    $currentDirectory = Split-Path -Parent $msbuildDirectory
    if ([string]::IsNullOrWhiteSpace($currentDirectory) -or
        (Split-Path $currentDirectory -Leaf) -ne 'Current') {
        return $null
    }

    $msbuildRoot = Split-Path -Parent $currentDirectory
    if ([string]::IsNullOrWhiteSpace($msbuildRoot) -or
        (Split-Path $msbuildRoot -Leaf) -ne 'MSBuild') {
        return $null
    }

    return Split-Path -Parent $msbuildRoot
}

function Test-Arm64ToolchainInstalled {
    param([Parameter(Mandatory)] [string]$ResolvedMsBuildPath)

    $visualStudioRoot = Get-VisualStudioInstallationRoot -ResolvedMsBuildPath $ResolvedMsBuildPath
    if ([string]::IsNullOrWhiteSpace($visualStudioRoot)) {
        return $true
    }

    $msvcRoot = Join-Path $visualStudioRoot 'VC\Tools\MSVC'
    if (-not (Test-Path $msvcRoot)) {
        return $false
    }

    $toolDirectories = Get-ChildItem -Path $msvcRoot -Directory -ErrorAction SilentlyContinue
    foreach ($toolDirectory in $toolDirectories) {
        $compilerCandidates = @(
            (Join-Path $toolDirectory.FullName 'bin\Hostx64\arm64\cl.exe'),
            (Join-Path $toolDirectory.FullName 'bin\Hostx86\arm64\cl.exe')
        )

        foreach ($compilerCandidate in $compilerCandidates) {
            if (Test-Path $compilerCandidate) {
                return $true
            }
        }
    }

    return $false
}

function Assert-ArchitectureToolchainAvailable {
    param(
        [Parameter(Mandatory)] [string[]]$ResolvedArchitectures,
        [Parameter(Mandatory)] [string]$ResolvedMsBuildPath
    )

    if ($ResolvedArchitectures -notcontains 'arm64') {
        return
    }

    if (-not (Test-Arm64ToolchainInstalled -ResolvedMsBuildPath $ResolvedMsBuildPath)) {
        throw 'ARM64 bootstrapper build requires the Visual Studio v143 ARM64 C++ toolchain. Install component Microsoft.VisualStudio.Component.VC.Tools.ARM64 and rerun scripts/tools/build-release.ps1.'
    }
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$serverProject = Join-Path $repoRoot 'src\WpfDevTools.Mcp.Server\WpfDevTools.Mcp.Server.csproj'
$inspectorProject = Join-Path $repoRoot 'src\WpfDevTools.Inspector\WpfDevTools.Inspector.csproj'
$bootstrapperProject = Join-Path $repoRoot 'src\WpfDevTools.Bootstrapper\WpfDevTools.Bootstrapper.vcxproj'
$installScript = Join-Path $repoRoot 'scripts\tools\release\Install-WpfDevTools.ps1'
$installBatchTemplate = Join-Path $repoRoot 'scripts\tools\release\run-template.bat'
$setupScript = Join-Path $repoRoot 'scripts\tools\release\Setup-WpfDevTools.ps1'
$uninstallScript = Join-Path $repoRoot 'scripts\tools\release\Uninstall-WpfDevTools.ps1'
$outputRootFullPath = (Resolve-Path (New-Item -ItemType Directory -Force -Path $OutputRoot)).Path
$msbuildPath = Resolve-MSBuildPath

[xml]$serverProjectXml = Get-Content -Path $serverProject
$version = $serverProjectXml.Project.PropertyGroup.Version | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($version)) {
    $version = '0.0.0-dev'
}

$resolvedArchitectures = Resolve-ArchitectureList -InputArchitectures $Architectures
Assert-ArchitectureToolchainAvailable -ResolvedArchitectures $resolvedArchitectures -ResolvedMsBuildPath $msbuildPath
foreach ($architecture in $resolvedArchitectures) {
    $runtimeId = Get-RuntimeId -Architecture $architecture
    $bootstrapperPlatform = Get-BootstrapperPlatform -Architecture $architecture
    $channel = Get-PackageChannel -BuildConfiguration $Configuration
    $signaturePolicy = Get-SignaturePolicy -BuildConfiguration $Configuration
    $packageDir = Join-Path $outputRootFullPath "release_${version}_win-$architecture"
    $packageArchiveName = "release_${version}_win-$architecture.zip"
    $packageArchivePath = Join-Path $outputRootFullPath $packageArchiveName
    $binDir = Join-Path $packageDir 'bin'
    $serverBuildSource = Resolve-ServerOutputSource -RepositoryRoot $repoRoot -BuildConfiguration $Configuration -RuntimeId $runtimeId -UseExistingBuildOutput $SkipBuild.IsPresent

    Remove-PathIfExists -Path $packageDir
    Remove-PathIfExists -Path $packageArchivePath

    New-Item -ItemType Directory -Force -Path $packageDir | Out-Null
    $inspectorNet8Dir = Join-Path $binDir 'inspectors\net8.0-windows'
    $inspectorNet48Dir = Join-Path $binDir 'inspectors\net48'
    $bootstrapperDir = Join-Path $binDir (Join-Path 'bootstrapper' $architecture)
    New-Item -ItemType Directory -Force -Path $binDir, $inspectorNet8Dir, $inspectorNet48Dir, $bootstrapperDir | Out-Null

    try {
        if ($SkipBuild) {
            Copy-DirectoryContents -Source $serverBuildSource -Destination $binDir
        }
        else {
            Invoke-Step -FilePath 'dotnet' -Arguments @(
                'publish', $serverProject,
                '-c', $Configuration,
                '-r', $runtimeId,
                '--self-contained', 'false',
                '-o', $binDir
            )
        }

        if (-not $SkipBuild) {
            Invoke-Step -FilePath 'dotnet' -Arguments @(
                'build', $inspectorProject,
                '-c', $Configuration,
                '-f', 'net8.0-windows'
            )
        }

        Invoke-Step -FilePath 'dotnet' -Arguments @(
            'build', $inspectorProject,
            '-c', $Configuration,
            '-f', 'net48'
        )

        Invoke-Step -FilePath $msbuildPath -Arguments @(
            $bootstrapperProject,
            "/p:Configuration=$Configuration",
            "/p:Platform=$bootstrapperPlatform"
        )

        $inspectorNet8BuildDir = Join-Path $repoRoot "src\WpfDevTools.Inspector\bin\$Configuration\net8.0-windows"
        $inspectorNet48BuildDir = Join-Path $repoRoot "src\WpfDevTools.Inspector\bin\$Configuration\net48"
        $bootstrapperSource = Join-Path $repoRoot "artifacts\bootstrapper\$Configuration\$bootstrapperPlatform\WpfDevTools.Bootstrapper.$architecture.dll"

        $packagedExecutableName = "wpf-devtools-$architecture.exe"
        $serverExecutablePath = Join-Path $binDir 'WpfDevTools.Mcp.Server.exe'
        if (Test-Path $serverExecutablePath) {
            Rename-Item -Path $serverExecutablePath -NewName $packagedExecutableName -Force
        }

        Copy-DirectoryFilesOnly -Source $inspectorNet8BuildDir -Destination $inspectorNet8Dir
        Copy-DirectoryContents -Source $inspectorNet48BuildDir -Destination $inspectorNet48Dir
        Copy-Item -Path $bootstrapperSource -Destination (Join-Path $bootstrapperDir (Split-Path $bootstrapperSource -Leaf)) -Force
        Copy-Item -Path $installBatchTemplate -Destination (Join-Path $packageDir 'run.bat') -Force
        Copy-Item -Path $setupScript -Destination (Join-Path $binDir 'install.ps1') -Force
        Copy-Item -Path $installScript -Destination (Join-Path $binDir 'internal-install.ps1') -Force

        $manifest = [ordered]@{
            name = 'wpf-devtools'
            version = $version
            architecture = $architecture
            runtimeId = $runtimeId
            channel = $channel
            buildConfiguration = $Configuration
            signaturePolicy = $signaturePolicy
            createdUtc = [DateTime]::UtcNow.ToString('o')
            entryExecutable = "bin/$packagedExecutableName"
            runBatch = 'run.bat'
            installScript = 'bin\install.ps1'
            inspector = [ordered]@{
                net8 = 'bin/inspectors/net8.0-windows/WpfDevTools.Inspector.dll'
                net48 = 'bin/inspectors/net48/WpfDevTools.Inspector.dll'
            }
            bootstrapper = "bin/bootstrapper/$architecture/WpfDevTools.Bootstrapper.$architecture.dll"
        }

        $manifest | ConvertTo-Json -Depth 5 | Set-Content -Path (Join-Path $binDir 'manifest.json') -Encoding UTF8
        Compress-Archive -Path (Join-Path $packageDir '*') -DestinationPath $packageArchivePath -Force
        Write-Host "Created package: $packageDir"
        Write-Host "Created archive: $packageArchivePath"
    }
    catch {
        Remove-PathIfExists -Path $packageArchivePath
        Remove-PathIfExists -Path $packageDir
        throw "Failed to package architecture $architecture. $($_.Exception.Message)"
    }
}
