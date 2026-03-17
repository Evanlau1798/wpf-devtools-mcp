param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidateSet('x64', 'x86', 'arm64')]
    [string[]]$Architectures = @('x64', 'x86', 'arm64'),

    [string]$OutputRoot = (Join-Path $PSScriptRoot '..\release'),

    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'

$publishScript = $env:WPFDEVTOOLS_BUILD_RELEASE_PUBLISH_SCRIPT
if ([string]::IsNullOrWhiteSpace($publishScript)) {
    $publishScript = Join-Path $PSScriptRoot 'release\Publish-Release.ps1'
}

if (-not (Test-Path $publishScript)) {
    throw "Publish-Release.ps1 was not found: $publishScript"
}

& $publishScript -Configuration $Configuration -Architectures $Architectures -OutputRoot $OutputRoot -SkipBuild:$SkipBuild
$publishExitCode = if ($null -eq $LASTEXITCODE) { 0 } else { $LASTEXITCODE }
if ($publishExitCode -ne 0) {
    throw "Release build failed with exit code $publishExitCode"
}
