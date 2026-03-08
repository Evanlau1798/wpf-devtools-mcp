param(
    [string]$InstallRoot = (Join-Path $env:LOCALAPPDATA 'WpfDevToolsMcp'),
    [ValidateSet('x64', 'x86', 'arm64')]
    [string]$Architecture,
    [switch]$RemoveClaudeCode,
    [switch]$RemoveCodex
)

$ErrorActionPreference = 'Stop'

function Invoke-OptionalCommand {
    param(
        [Parameter(Mandatory)] [string]$Command,
        [Parameter(Mandatory)] [string[]]$Arguments
    )

    $resolved = Get-Command $Command -ErrorAction SilentlyContinue
    if ($null -eq $resolved) {
        Write-Warning "$Command is not installed. Skipping removal."
        return
    }

    Write-Host "> $Command $($Arguments -join ' ')"
    & $Command @Arguments
}

if ([string]::IsNullOrWhiteSpace($Architecture)) {
    $installedArchitectures = Get-ChildItem -Path $InstallRoot -Directory -ErrorAction SilentlyContinue |
        Select-Object -ExpandProperty Name

    if ($installedArchitectures.Count -eq 1) {
        $Architecture = $installedArchitectures[0]
    }
    else {
        throw 'Specify -Architecture when multiple or zero installations exist.'
    }
}

$installBase = Join-Path $InstallRoot $Architecture
if (-not (Test-Path $installBase)) {
    throw "Install path does not exist: $installBase"
}

Remove-Item -Path $installBase -Recurse -Force

if ($RemoveClaudeCode) {
    Invoke-OptionalCommand -Command 'claude' -Arguments @('mcp', 'remove', 'wpf-devtools')
}

if ($RemoveCodex) {
    Invoke-OptionalCommand -Command 'codex' -Arguments @('mcp', 'remove', 'wpf-devtools')
}

Write-Host "Removed installation: $installBase"
