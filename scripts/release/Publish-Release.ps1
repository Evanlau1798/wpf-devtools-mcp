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
    $packageDir = Join-Path $outputRootFullPath "WpfDevTools-$runtimeId"

    if (Test-Path $packageDir) {
        Remove-Item -Path $packageDir -Recurse -Force
    }

    New-Item -ItemType Directory -Force -Path $packageDir | Out-Null
    $inspectorNet8Dir = Join-Path $packageDir 'inspectors\net8.0-windows'
    $inspectorNet48Dir = Join-Path $packageDir 'inspectors\net48'
    $bootstrapperDir = Join-Path $packageDir (Join-Path 'bootstrapper' $architecture)
    New-Item -ItemType Directory -Force -Path $inspectorNet8Dir, $inspectorNet48Dir, $bootstrapperDir | Out-Null

    Invoke-Step -FilePath 'dotnet' -Arguments @(
        'publish', $serverProject,
        '-c', $Configuration,
        '-r', $runtimeId,
        '--self-contained', 'false',
        '-o', $packageDir
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

    $manifest = [ordered]@{
        name = 'wpf-devtools'
        version = $version
        architecture = $architecture
        runtimeId = $runtimeId
        createdUtc = [DateTime]::UtcNow.ToString('o')
        entryExecutable = 'WpfDevTools.Mcp.Server.exe'
        inspector = [ordered]@{
            net8 = 'inspectors/net8.0-windows/WpfDevTools.Inspector.dll'
            net48 = 'inspectors/net48/WpfDevTools.Inspector.dll'
        }
        bootstrapper = "bootstrapper/$architecture/WpfDevTools.Bootstrapper.$architecture.dll"
    }

    $manifest | ConvertTo-Json -Depth 5 | Set-Content -Path (Join-Path $packageDir 'manifest.json') -Encoding UTF8
    Write-Host "Created package: $packageDir"
}
