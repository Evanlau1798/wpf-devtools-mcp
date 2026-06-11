param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string[]]$Architectures = @('x64', 'x86', 'arm64'),

    [string]$OutputRoot = (Join-Path $PSScriptRoot '..\..\release'),

    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$supportedArchitectures = @('x64', 'x86', 'arm64')

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

$resolvedArchitectures = Resolve-ArchitectureList -InputArchitectures $Architectures

$publishScript = $env:WPFDEVTOOLS_BUILD_RELEASE_PUBLISH_SCRIPT
if ([string]::IsNullOrWhiteSpace($publishScript)) {
    $publishScript = Join-Path $PSScriptRoot 'packaging\Publish-Release.ps1'
}

if (-not (Test-Path $publishScript)) {
    throw "Publish-Release.ps1 was not found: $publishScript"
}

& $publishScript -Configuration $Configuration -Architectures $resolvedArchitectures -OutputRoot $OutputRoot -SkipBuild:$SkipBuild
$publishExitCode = if ($null -eq $LASTEXITCODE) { 0 } else { $LASTEXITCODE }
if ($publishExitCode -ne 0) {
    throw "Release build failed with exit code $publishExitCode"
}
