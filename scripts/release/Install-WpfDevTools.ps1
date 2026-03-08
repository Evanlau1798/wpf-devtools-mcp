param(
    [Parameter(Mandatory)]
    [string]$PackagePath,

    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA 'WpfDevToolsMcp'),

    [switch]$RegisterClaudeCode,
    [switch]$RegisterCodex,
    [switch]$Force
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

$packageDir = (Resolve-Path $PackagePath).Path
$manifestPath = Join-Path $packageDir 'manifest.json'
if (-not (Test-Path $manifestPath)) {
    throw "manifest.json was not found under package path: $packageDir"
}

$manifest = Get-Content -Path $manifestPath -Raw | ConvertFrom-Json
$architecture = [string]$manifest.architecture
if ([string]::IsNullOrWhiteSpace($architecture)) {
    throw 'manifest.json does not define architecture'
}

$installBase = Join-Path $InstallRoot $architecture
$currentDir = Join-Path $installBase 'current'
if (Test-Path $currentDir) {
    if (-not $Force) {
        throw "Install target already exists: $currentDir. Re-run with -Force to overwrite."
    }

    Remove-Item -Path $currentDir -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $installBase | Out-Null
Copy-Item -Path $packageDir -Destination $currentDir -Recurse -Force

$installedExecutable = Join-Path $currentDir 'WpfDevTools.Mcp.Server.exe'
$installManifest = [ordered]@{
    name = 'wpf-devtools'
    architecture = $architecture
    version = [string]$manifest.version
    installDir = $currentDir
    installedUtc = [DateTime]::UtcNow.ToString('o')
    executable = $installedExecutable
}

$installManifest | ConvertTo-Json -Depth 5 | Set-Content -Path (Join-Path $installBase 'install-manifest.json') -Encoding UTF8

if ($RegisterClaudeCode) {
    Invoke-OptionalCommand -Command 'claude' -Arguments @('mcp', 'add', '--transport', 'stdio', 'wpf-devtools', '--', $installedExecutable)
}

if ($RegisterCodex) {
    Invoke-OptionalCommand -Command 'codex' -Arguments @('mcp', 'add', 'wpf-devtools', '--', $installedExecutable)
}

Write-Host "Installed to: $currentDir"
Write-Host "Executable: $installedExecutable"
Write-Host 'Next steps:'
Write-Host "  Claude Code: claude mcp add --transport stdio wpf-devtools -- '$installedExecutable'"
Write-Host "  Codex CLI : codex mcp add wpf-devtools -- '$installedExecutable'"
