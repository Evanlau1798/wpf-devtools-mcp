param(
    [string]$PackagePath,

    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA 'WpfDevToolsMcp'),

    [switch]$RegisterClaudeCode,
    [switch]$RegisterCodex,
    [switch]$Force,
    [switch]$Quiet
)

$ErrorActionPreference = 'Stop'

function Invoke-OptionalCommand {
    param(
        [Parameter(Mandatory)] [string]$Command,
        [Parameter(Mandatory)] [string[]]$Arguments
    )

    $resolved = Get-Command $Command -ErrorAction SilentlyContinue
    if ($null -eq $resolved) {
        Write-Warning "$Command is not installed. Skipping registration."
        return
    }

    Write-Host "> $Command $($Arguments -join ' ')"
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Command registration failed with exit code $LASTEXITCODE"
    }
}

function Write-InstallMessage {
    param([Parameter(Mandatory)] [string]$Message)

    if (-not $Quiet) {
        Write-Host $Message
    }
}

function Write-RegistrationArtifact {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$Content
    )

    $directory = Split-Path -Parent $Path
    if (-not [string]::IsNullOrWhiteSpace($directory)) {
        New-Item -ItemType Directory -Force -Path $directory | Out-Null
    }

    Set-Content -Path $Path -Value $Content -Encoding UTF8
}

function Resolve-PackageDirectory {
    param([string]$ConfiguredPackagePath)

    if (-not [string]::IsNullOrWhiteSpace($ConfiguredPackagePath)) {
        return (Resolve-Path $ConfiguredPackagePath).Path
    }

    $packageRoot = (Resolve-Path $PSScriptRoot).Path
    $currentManifest = Join-Path $packageRoot 'manifest.json'
    $childManifest = Join-Path $packageRoot 'bin\manifest.json'
    $packageParent = Split-Path -Parent $packageRoot

    if ((Split-Path $packageRoot -Leaf) -ieq 'bin' -and
        (Test-Path $currentManifest) -and
        -not [string]::IsNullOrWhiteSpace($packageParent)) {
        return $packageParent
    }

    if ((Test-Path $currentManifest) -or (Test-Path $childManifest)) {
        return $packageRoot
    }

    if (-not [string]::IsNullOrWhiteSpace($packageParent) -and
        ((Test-Path (Join-Path $packageParent 'manifest.json')) -or
         (Test-Path (Join-Path $packageParent 'bin\manifest.json')))) {
        return $packageParent
    }

    throw 'PackagePath was not provided and manifest.json was not found next to install.ps1.'
}

function Resolve-PackageManifestPath {
    param([Parameter(Mandatory)] [string]$PackageDirectory)

    $manifestCandidates = @(
        (Join-Path $PackageDirectory 'manifest.json'),
        (Join-Path $PackageDirectory 'bin\manifest.json')
    )

    foreach ($candidate in $manifestCandidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    throw "manifest.json was not found under package path: $PackageDirectory"
}

function Resolve-PackageExecutable {
    param(
        [Parameter(Mandatory)] [string]$PackageDirectory,
        [Parameter(Mandatory)] [string]$Architecture
    )

    $candidates = @(
        (Join-Path $PackageDirectory "bin\wpf-devtools-$Architecture.exe"),
        (Join-Path $PackageDirectory "wpf-devtools-$Architecture.exe"),
        (Join-Path $PackageDirectory 'bin\WpfDevTools.Mcp.Server.exe'),
        (Join-Path $PackageDirectory 'WpfDevTools.Mcp.Server.exe')
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    $fallback = Get-ChildItem -Path (Join-Path $PackageDirectory 'bin') -Filter 'wpf-devtools-*.exe' -File -ErrorAction SilentlyContinue |
        Select-Object -First 1
    if ($null -ne $fallback) {
        return $fallback.FullName
    }

    throw "Package does not contain a wpf-devtools executable under bin\\ or package root: $PackageDirectory"
}

function Resolve-AbsoluteDirectory {
    param([Parameter(Mandatory)] [string]$Path)

    New-Item -ItemType Directory -Force -Path $Path | Out-Null
    return (Resolve-Path $Path).Path
}

function New-ClientRegistrationArtifacts {
    param(
        [Parameter(Mandatory)] [string]$InstallBase,
        [Parameter(Mandatory)] [string]$InstalledExecutable
    )

    $registrationDir = Join-Path $InstallBase 'client-registration'
    New-Item -ItemType Directory -Force -Path $registrationDir | Out-Null

    $claudeCodeCommand = "claude mcp add --transport stdio wpf-devtools -- `"$InstalledExecutable`""
    $claudeCodeProjectCommand = "claude mcp add --scope project --transport stdio wpf-devtools -- `"$InstalledExecutable`""
    $codexCommand = "codex mcp add wpf-devtools -- `"$InstalledExecutable`""

    Write-RegistrationArtifact -Path (Join-Path $registrationDir 'claude-code.txt') -Content @"
$claudeCodeCommand

Project-scoped alternative:
$claudeCodeProjectCommand

Uninstall:
claude mcp remove wpf-devtools
"@

    Write-RegistrationArtifact -Path (Join-Path $registrationDir 'codex-cli.txt') -Content @"
$codexCommand

Uninstall:
codex mcp remove wpf-devtools
"@

    $claudeDesktop = [ordered]@{
        mcpServers = [ordered]@{
            'wpf-devtools' = [ordered]@{
                command = $InstalledExecutable
                args = @()
            }
        }
    }

    $claudeCodeProject = [ordered]@{
        mcpServers = [ordered]@{
            'wpf-devtools' = [ordered]@{
                command = $InstalledExecutable
                args = @()
            }
        }
    }

    $cursorVsCode = [ordered]@{
        servers = [ordered]@{
            'wpf-devtools' = [ordered]@{
                command = $InstalledExecutable
                args = @()
            }
        }
    }

    $githubCopilotVsCode = [ordered]@{
        servers = [ordered]@{
            'wpf-devtools' = [ordered]@{
                command = $InstalledExecutable
                args = @()
            }
        }
    }

    $otherConfig = [ordered]@{
        mcpServers = [ordered]@{
            'wpf-devtools' = [ordered]@{
                command = $InstalledExecutable
                args = @()
            }
        }
    }

    Write-RegistrationArtifact -Path (Join-Path $registrationDir 'claude-desktop.json') -Content ($claudeDesktop | ConvertTo-Json -Depth 5)
    Write-RegistrationArtifact -Path (Join-Path $registrationDir 'claude-code.project.mcp.json') -Content ($claudeCodeProject | ConvertTo-Json -Depth 5)
    Write-RegistrationArtifact -Path (Join-Path $registrationDir 'cursor-vscode.json') -Content ($cursorVsCode | ConvertTo-Json -Depth 5)
    Write-RegistrationArtifact -Path (Join-Path $registrationDir 'github-copilot-vscode.json') -Content ($githubCopilotVsCode | ConvertTo-Json -Depth 5)
    Write-RegistrationArtifact -Path (Join-Path $registrationDir 'other.mcpServers.json') -Content ($otherConfig | ConvertTo-Json -Depth 5)

    return $registrationDir
}

$packageDir = Resolve-PackageDirectory -ConfiguredPackagePath $PackagePath
$manifestPath = Resolve-PackageManifestPath -PackageDirectory $packageDir
$manifest = Get-Content -Path $manifestPath -Raw | ConvertFrom-Json
$architecture = [string]$manifest.architecture
if ([string]::IsNullOrWhiteSpace($architecture)) {
    throw 'manifest.json does not define architecture'
}

$channel = [string]$manifest.channel
$buildConfiguration = [string]$manifest.buildConfiguration
$signaturePolicy = [string]$manifest.signaturePolicy
if ([string]::IsNullOrWhiteSpace($channel)) {
    $channel = if ($buildConfiguration -eq 'Debug') { 'dev' } else { 'release' }
}
if ([string]::IsNullOrWhiteSpace($buildConfiguration)) {
    $buildConfiguration = if ($channel -eq 'dev') { 'Debug' } else { 'Release' }
}
if ([string]::IsNullOrWhiteSpace($signaturePolicy)) {
    $signaturePolicy = if ($buildConfiguration -eq 'Debug') { 'DebugTrustedRootSkip' } else { 'RequireAuthenticodeSignature' }
}

$packageExecutable = Resolve-PackageExecutable -PackageDirectory $packageDir -Architecture $architecture

$installRootFullPath = Resolve-AbsoluteDirectory -Path $InstallRoot
$installBase = Join-Path $installRootFullPath $architecture
$currentDir = Join-Path $installBase 'current'
if (Test-Path $currentDir) {
    if (-not $Force) {
        throw "Install target already exists: $currentDir. Re-run with -Force to overwrite."
    }

    Remove-Item -Path $currentDir -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $installBase | Out-Null
Copy-Item -Path $packageDir -Destination $currentDir -Recurse -Force

$packageExecutableRelativePath = $packageExecutable.Substring($packageDir.Length).TrimStart('\', '/')
$installedExecutable = Join-Path $currentDir $packageExecutableRelativePath
$installManifest = [ordered]@{
    name = 'wpf-devtools'
    architecture = $architecture
    version = [string]$manifest.version
    channel = $channel
    buildConfiguration = $buildConfiguration
    signaturePolicy = $signaturePolicy
    installDir = $currentDir
    installedUtc = [DateTime]::UtcNow.ToString('o')
    executable = $installedExecutable
}

$installManifest | ConvertTo-Json -Depth 5 | Set-Content -Path (Join-Path $installBase 'install-manifest.json') -Encoding UTF8
$registrationDir = New-ClientRegistrationArtifacts -InstallBase $installBase -InstalledExecutable $installedExecutable

if ($RegisterClaudeCode) {
    Invoke-OptionalCommand -Command 'claude' -Arguments @('mcp', 'add', '--transport', 'stdio', 'wpf-devtools', '--', $installedExecutable)
}

if ($RegisterCodex) {
    Invoke-OptionalCommand -Command 'codex' -Arguments @('mcp', 'add', 'wpf-devtools', '--', $installedExecutable)
}

Write-InstallMessage "Installed to: $currentDir"
Write-InstallMessage "Executable: $installedExecutable"
Write-InstallMessage "Package channel: $channel"
Write-InstallMessage "Build configuration: $buildConfiguration"
Write-InstallMessage "Client registration templates: $registrationDir"
Write-InstallMessage 'Next steps:'
Write-InstallMessage "  Claude Code: claude mcp add --transport stdio wpf-devtools -- '$installedExecutable'"
Write-InstallMessage "  Codex CLI : codex mcp add wpf-devtools -- '$installedExecutable'"
