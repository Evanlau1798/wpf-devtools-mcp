param(
    [Parameter(Mandatory)] [string]$OutputPath,
    [string]$RepoRoot = '',
    [string]$Configuration = 'Release',
    [string]$ResultsDirectory = 'artifacts/release/security-tests'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Resolve-BaseRelativePath {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$BasePath
    )

    if ([System.IO.Path]::IsPathRooted($Path)) {
        return [System.IO.Path]::GetFullPath($Path)
    }

    return [System.IO.Path]::GetFullPath((Join-Path $BasePath $Path))
}

$scriptRoot = if ([string]::IsNullOrWhiteSpace($PSScriptRoot)) {
    Split-Path -Parent $MyInvocation.MyCommand.Path
}
else {
    $PSScriptRoot
}

$repoRootInput = if ([string]::IsNullOrWhiteSpace($RepoRoot)) {
    Join-Path $scriptRoot '..\..\..'
}
else {
    $RepoRoot
}

$repoRootPath = (Resolve-Path -LiteralPath $repoRootInput).Path
$unitTestProject = Join-Path $repoRootPath 'tests/WpfDevTools.Tests.Unit/WpfDevTools.Tests.Unit.csproj'
if (-not (Test-Path -LiteralPath $unitTestProject -PathType Leaf)) {
    throw "Unit test project is missing: $unitTestProject"
}

$resultsDirectoryPath = Resolve-BaseRelativePath -Path $ResultsDirectory -BasePath $repoRootPath
$outputPathValue = Resolve-BaseRelativePath -Path $OutputPath -BasePath $repoRootPath
$securityTestFilter = 'FullyQualifiedName~NamedPipeMitmAdversarialMatrixTests|FullyQualifiedName~ScreenshotResourceIntegrityTests'

Push-Location $repoRootPath
try {
    dotnet build $unitTestProject --configuration $Configuration -v minimal
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet build failed for release security evidence with exit code $LASTEXITCODE."
    }

    New-Item -ItemType Directory -Force -Path $resultsDirectoryPath | Out-Null
    dotnet test $unitTestProject `
        --configuration $Configuration `
        --no-build `
        --filter "$securityTestFilter" `
        --logger 'trx;LogFileName=release-security-evidence.trx' `
        --results-directory $resultsDirectoryPath
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet test failed for release security evidence with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}

$outputDirectory = Split-Path -Parent $outputPathValue
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

$evidence = [ordered]@{
    generatedUtc = [DateTimeOffset]::UtcNow.ToString('O')
    testProject = $unitTestProject
    testFilter = $securityTestFilter
    testResultsDirectory = $resultsDirectoryPath
    security = [ordered]@{
        mitmMatrixPassed = $true
        screenshotIntegrityPassed = $true
    }
}

$evidence | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $outputPathValue -Encoding UTF8
