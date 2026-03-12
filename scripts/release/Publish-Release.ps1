param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidateSet('x64', 'x86', 'arm64')]
    [string[]]$Architectures = @('x64'),

    [string]$OutputRoot = (Join-Path $PSScriptRoot '..\..\artifacts\release')
)

$ErrorActionPreference = 'Stop'

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

function Remove-PathIfExists {
    param([string]$Path)

    if (-not [string]::IsNullOrWhiteSpace($Path) -and (Test-Path $Path)) {
        Remove-Item -Path $Path -Recurse -Force
    }
}

function Resolve-MSBuildPath {
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

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$serverProject = Join-Path $repoRoot 'src\WpfDevTools.Mcp.Server\WpfDevTools.Mcp.Server.csproj'
$inspectorProject = Join-Path $repoRoot 'src\WpfDevTools.Inspector\WpfDevTools.Inspector.csproj'
$bootstrapperProject = Join-Path $repoRoot 'src\WpfDevTools.Bootstrapper\WpfDevTools.Bootstrapper.vcxproj'
$installScript = Join-Path $repoRoot 'scripts\release\Install-WpfDevTools.ps1'
$installBatchTemplate = Join-Path $repoRoot 'scripts\release\install-template.bat'
$setupScript = Join-Path $repoRoot 'scripts\release\Setup-WpfDevTools.ps1'
$uninstallScript = Join-Path $repoRoot 'scripts\release\Uninstall-WpfDevTools.ps1'
$outputRootFullPath = (Resolve-Path (New-Item -ItemType Directory -Force -Path $OutputRoot)).Path
$msbuildPath = Resolve-MSBuildPath

[xml]$serverProjectXml = Get-Content -Path $serverProject
$version = $serverProjectXml.Project.PropertyGroup.Version | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($version)) {
    $version = '0.0.0-dev'
}

foreach ($architecture in $Architectures) {
    $runtimeId = Get-RuntimeId -Architecture $architecture
    $bootstrapperPlatform = Get-BootstrapperPlatform -Architecture $architecture
    $channel = Get-PackageChannel -BuildConfiguration $Configuration
    $signaturePolicy = Get-SignaturePolicy -BuildConfiguration $Configuration
    $packageDir = Join-Path $outputRootFullPath "release_${version}_win-$architecture"
    $packageArchiveName = if ($channel -eq 'dev') { "release_${version}_dev_win-$architecture.zip" } else { "release_${version}_win-$architecture.zip" }
    $packageArchivePath = Join-Path $outputRootFullPath $packageArchiveName
    $binDir = Join-Path $packageDir 'bin'

    Remove-PathIfExists -Path $packageDir
    Remove-PathIfExists -Path $packageArchivePath

    New-Item -ItemType Directory -Force -Path $packageDir | Out-Null
    $inspectorNet8Dir = Join-Path $binDir 'inspectors\net8.0-windows'
    $inspectorNet48Dir = Join-Path $binDir 'inspectors\net48'
    $bootstrapperDir = Join-Path $binDir (Join-Path 'bootstrapper' $architecture)
    New-Item -ItemType Directory -Force -Path $binDir, $inspectorNet8Dir, $inspectorNet48Dir, $bootstrapperDir | Out-Null

    Invoke-Step -FilePath 'dotnet' -Arguments @(
        'publish', $serverProject,
        '-c', $Configuration,
        '-r', $runtimeId,
        '--self-contained', 'false',
        '-o', $binDir
    )

    Invoke-Step -FilePath 'dotnet' -Arguments @(
        'build', $inspectorProject,
        '-c', $Configuration,
        '-f', 'net8.0-windows'
    )

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

    Copy-DirectoryContents -Source $inspectorNet8BuildDir -Destination $inspectorNet8Dir
    Copy-DirectoryContents -Source $inspectorNet48BuildDir -Destination $inspectorNet48Dir
    Copy-Item -Path $bootstrapperSource -Destination (Join-Path $bootstrapperDir (Split-Path $bootstrapperSource -Leaf)) -Force
    Copy-Item -Path $installBatchTemplate -Destination (Join-Path $packageDir 'install.bat') -Force
    Copy-Item -Path $installScript -Destination (Join-Path $packageDir 'install.ps1') -Force
    Copy-Item -Path $setupScript -Destination (Join-Path $packageDir 'setup.ps1') -Force
    Copy-Item -Path $uninstallScript -Destination (Join-Path $packageDir 'uninstall.ps1') -Force

    $manifest = [ordered]@{
        name = 'wpf-devtools'
        version = $version
        architecture = $architecture
        runtimeId = $runtimeId
        channel = $channel
        buildConfiguration = $Configuration
        signaturePolicy = $signaturePolicy
        createdUtc = [DateTime]::UtcNow.ToString('o')
        entryExecutable = 'bin/WpfDevTools.Mcp.Server.exe'
        installBatch = 'install.bat'
        installScript = 'install.ps1'
        setupScript = 'setup.ps1'
        uninstallScript = 'uninstall.ps1'
        inspector = [ordered]@{
            net8 = 'bin/inspectors/net8.0-windows/WpfDevTools.Inspector.dll'
            net48 = 'bin/inspectors/net48/WpfDevTools.Inspector.dll'
        }
        bootstrapper = "bin/bootstrapper/$architecture/WpfDevTools.Bootstrapper.$architecture.dll"
    }

    $manifest | ConvertTo-Json -Depth 5 | Set-Content -Path (Join-Path $packageDir 'manifest.json') -Encoding UTF8
    Compress-Archive -Path (Join-Path $packageDir '*') -DestinationPath $packageArchivePath -Force
    Write-Host "Created package: $packageDir"
    Write-Host "Created archive: $packageArchivePath"
}
