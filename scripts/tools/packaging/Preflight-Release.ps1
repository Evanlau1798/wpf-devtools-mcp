param(
    [ValidateSet('Release')]
    [string]$Configuration = 'Release',

    [string[]]$Architectures = @('x64', 'x86', 'arm64'),

    [string]$VersionTag,

    [string]$OutputRoot = (Join-Path $PSScriptRoot '..\..\artifacts\release\preflight'),

    [switch]$SkipBuild,
    [switch]$SkipTest,

    [switch]$PlanOnly,
    [switch]$OutputJson
)

$ErrorActionPreference = 'Stop'
$supportedArchitectures = @('x64', 'x86', 'arm64')

function Write-StepMessage {
    param([Parameter(Mandatory)] [string]$Message)

    if ($OutputJson) {
        [Console]::Error.WriteLine("> $Message")
        return
    }

    Write-Host "> $Message"
}

function Invoke-Step {
    param(
        [Parameter(Mandatory)] [string]$FilePath,
        [Parameter(Mandatory)] [string[]]$Arguments
    )

    Write-StepMessage -Message "$FilePath $($Arguments -join ' ')"
    if ($OutputJson) {
        & $FilePath @Arguments 2>&1 | ForEach-Object { [Console]::Error.WriteLine($_.ToString()) }
    }
    else {
        & $FilePath @Arguments
    }

    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $FilePath $($Arguments -join ' ')"
    }
}

function Assert-LastExitCodeSucceeded {
    param([Parameter(Mandatory)] [string]$CommandDescription)

    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $CommandDescription"
    }
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

function Resolve-ScriptPath {
    param(
        [Parameter(Mandatory)] [string]$DefaultRelativePath,
        [string]$OverridePath
    )

    $candidatePath = if ([string]::IsNullOrWhiteSpace($OverridePath)) {
        Join-Path $repoRoot $DefaultRelativePath
    }
    elseif ([System.IO.Path]::IsPathRooted($OverridePath)) {
        $OverridePath
    }
    else {
        Join-Path $repoRoot $OverridePath
    }

    return [System.IO.Path]::GetFullPath($candidatePath)
}

$resolvedArchitectures = Resolve-ArchitectureList -InputArchitectures $Architectures
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$outputRootPath = if ([System.IO.Path]::IsPathRooted($OutputRoot)) { $OutputRoot } else { Join-Path $repoRoot $OutputRoot }
$outputRootFullPath = [System.IO.Path]::GetFullPath($outputRootPath)
$packageOutputRoot = Join-Path $outputRootFullPath 'packages'
$assetOutputRoot = Join-Path $outputRootFullPath 'github-assets'
$publishScript = Resolve-ScriptPath -DefaultRelativePath 'scripts\tools\packaging\Publish-Release.ps1' -OverridePath $env:WPFDEVTOOLS_PREFLIGHT_PUBLISH_SCRIPT
$exportScript = Resolve-ScriptPath -DefaultRelativePath 'scripts\tools\packaging\Export-GitHubReleaseAssets.ps1' -OverridePath $env:WPFDEVTOOLS_PREFLIGHT_EXPORT_SCRIPT

if (-not (Test-Path $publishScript)) {
    throw "Publish script does not exist: $publishScript"
}

if (-not (Test-Path $exportScript)) {
    throw "Export script does not exist: $exportScript"
}

$steps = New-Object System.Collections.ArrayList
$architecturesLiteral = "@('" + ($resolvedArchitectures -join "', '") + "')"
if (-not $SkipBuild) {
    $null = $steps.Add("dotnet build WpfDevTools.sln -c $Configuration -m:1 -nodeReuse:false -p:BuildInParallel=false")
}

if (-not $SkipTest) {
    $null = $steps.Add("dotnet test tests/WpfDevTools.Tests.Unit/WpfDevTools.Tests.Unit.csproj -c $Configuration --no-build")
    $null = $steps.Add("dotnet test tests/WpfDevTools.Tests.Unit.Release/WpfDevTools.Tests.Unit.Release.csproj -c $Configuration --no-build")
}

$publishStep = "powershell -ExecutionPolicy Bypass -File $publishScript -Configuration $Configuration -Architectures $architecturesLiteral -OutputRoot $packageOutputRoot"
if (-not [string]::IsNullOrWhiteSpace($VersionTag)) {
    $publishStep += " -ExpectedReleaseTag $VersionTag"
}

$null = $steps.Add($publishStep)
if (-not [string]::IsNullOrWhiteSpace($VersionTag)) {
    $null = $steps.Add("powershell -ExecutionPolicy Bypass -File $exportScript -InputRoot $packageOutputRoot -OutputRoot $assetOutputRoot -Tag $VersionTag")
}

$result = [pscustomobject]@{
    configuration = $Configuration
    architectures = $resolvedArchitectures
    versionTag = $VersionTag
    versionTagProvided = -not [string]::IsNullOrWhiteSpace($VersionTag)
    outputRoot = $outputRootFullPath
    packageOutputRoot = $packageOutputRoot
    assetOutputRoot = $assetOutputRoot
    skipBuild = [bool]$SkipBuild
    skipTest = [bool]$SkipTest
    steps = @($steps)
    uploadedToGitHub = $false
}

if ($PlanOnly) {
    if ($OutputJson) {
        $result | ConvertTo-Json -Depth 5
    }
    else {
        $result
    }

    return
}

New-Item -ItemType Directory -Force -Path $packageOutputRoot | Out-Null
if (-not [string]::IsNullOrWhiteSpace($VersionTag)) {
    New-Item -ItemType Directory -Force -Path $assetOutputRoot | Out-Null
}

if (-not $SkipBuild) {
    Invoke-Step -FilePath 'dotnet' -Arguments @('build', 'WpfDevTools.sln', '-c', $Configuration, '-m:1', '-nodeReuse:false', '-p:BuildInParallel=false')
}

if (-not $SkipTest) {
    Invoke-Step -FilePath 'dotnet' -Arguments @('test', 'tests/WpfDevTools.Tests.Unit/WpfDevTools.Tests.Unit.csproj', '-c', $Configuration, '--no-build')
    Invoke-Step -FilePath 'dotnet' -Arguments @('test', 'tests/WpfDevTools.Tests.Unit.Release/WpfDevTools.Tests.Unit.Release.csproj', '-c', $Configuration, '--no-build')
}

$publishCommandDescription = "$publishScript -Configuration $Configuration -Architectures $($resolvedArchitectures -join ',') -OutputRoot $packageOutputRoot"
if (-not [string]::IsNullOrWhiteSpace($VersionTag)) {
    $publishCommandDescription += " -ExpectedReleaseTag $VersionTag"
}

Write-StepMessage -Message $publishCommandDescription
$global:LASTEXITCODE = 0
if ($OutputJson) {
    if (-not [string]::IsNullOrWhiteSpace($VersionTag)) {
        & $publishScript -Configuration $Configuration -Architectures $resolvedArchitectures -OutputRoot $packageOutputRoot -ExpectedReleaseTag $VersionTag 2>&1 6>&1 |
            ForEach-Object { [Console]::Error.WriteLine($_.ToString()) }
    }
    else {
        & $publishScript -Configuration $Configuration -Architectures $resolvedArchitectures -OutputRoot $packageOutputRoot 2>&1 6>&1 |
            ForEach-Object { [Console]::Error.WriteLine($_.ToString()) }
    }
}
else {
    if (-not [string]::IsNullOrWhiteSpace($VersionTag)) {
        & $publishScript -Configuration $Configuration -Architectures $resolvedArchitectures -OutputRoot $packageOutputRoot -ExpectedReleaseTag $VersionTag
    }
    else {
        & $publishScript -Configuration $Configuration -Architectures $resolvedArchitectures -OutputRoot $packageOutputRoot
    }
}
Assert-LastExitCodeSucceeded -CommandDescription $publishCommandDescription

if (-not [string]::IsNullOrWhiteSpace($VersionTag)) {
    $exportCommandDescription = "$exportScript -InputRoot $packageOutputRoot -OutputRoot $assetOutputRoot -Tag $VersionTag"
    Write-StepMessage -Message $exportCommandDescription
    $global:LASTEXITCODE = 0
    if ($OutputJson) {
        & $exportScript -InputRoot $packageOutputRoot -OutputRoot $assetOutputRoot -Tag $VersionTag 2>&1 6>&1 |
            ForEach-Object { [Console]::Error.WriteLine($_.ToString()) }
    }
    else {
        & $exportScript -InputRoot $packageOutputRoot -OutputRoot $assetOutputRoot -Tag $VersionTag
    }
    Assert-LastExitCodeSucceeded -CommandDescription $exportCommandDescription
}

if ($OutputJson) {
    $result | ConvertTo-Json -Depth 5
}
else {
    $result
}
