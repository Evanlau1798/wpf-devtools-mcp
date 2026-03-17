param(
    [ValidateSet('Release')]
    [string]$Configuration = 'Release',

    [ValidateSet('x64', 'x86', 'arm64')]
    [string[]]$Architectures = @('x64', 'x86', 'arm64'),

    [string]$VersionTag,

    [string]$OutputRoot = (Join-Path $PSScriptRoot '..\..\artifacts\release\preflight'),

    [switch]$SkipBuild,
    [switch]$SkipTest,

    [switch]$PlanOnly,
    [switch]$OutputJson
)

$ErrorActionPreference = 'Stop'

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

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
$outputRootPath = if ([System.IO.Path]::IsPathRooted($OutputRoot)) { $OutputRoot } else { Join-Path $repoRoot $OutputRoot }
$outputRootFullPath = [System.IO.Path]::GetFullPath($outputRootPath)
$packageOutputRoot = Join-Path $outputRootFullPath 'packages'
$assetOutputRoot = Join-Path $outputRootFullPath 'github-assets'
$publishScript = Resolve-ScriptPath -DefaultRelativePath 'scripts\tools\release\Publish-Release.ps1' -OverridePath $env:WPFDEVTOOLS_PREFLIGHT_PUBLISH_SCRIPT
$exportScript = Resolve-ScriptPath -DefaultRelativePath 'scripts\tools\release\Export-GitHubReleaseAssets.ps1' -OverridePath $env:WPFDEVTOOLS_PREFLIGHT_EXPORT_SCRIPT

if (-not (Test-Path $publishScript)) {
    throw "Publish script does not exist: $publishScript"
}

if (-not (Test-Path $exportScript)) {
    throw "Export script does not exist: $exportScript"
}

$steps = New-Object System.Collections.ArrayList
$architecturesLiteral = "@('" + ($Architectures -join "', '") + "')"
if (-not $SkipBuild) {
    $null = $steps.Add("dotnet build WpfDevTools.sln -c $Configuration -m:1 -nodeReuse:false -p:BuildInParallel=false")
}

if (-not $SkipTest) {
    $null = $steps.Add("dotnet test tests/WpfDevTools.Tests.Unit/WpfDevTools.Tests.Unit.csproj -c $Configuration --no-build")
}

$null = $steps.Add("powershell -ExecutionPolicy Bypass -File $publishScript -Configuration $Configuration -Architectures $architecturesLiteral -OutputRoot $packageOutputRoot")
if (-not [string]::IsNullOrWhiteSpace($VersionTag)) {
    $null = $steps.Add("powershell -ExecutionPolicy Bypass -File $exportScript -InputRoot $packageOutputRoot -OutputRoot $assetOutputRoot -Tag $VersionTag")
}

$result = [pscustomobject]@{
    configuration = $Configuration
    architectures = $Architectures
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
}

Write-StepMessage -Message "$publishScript -Configuration $Configuration -Architectures $($Architectures -join ',') -OutputRoot $packageOutputRoot"
if ($OutputJson) {
    & $publishScript -Configuration $Configuration -Architectures $Architectures -OutputRoot $packageOutputRoot 2>&1 6>&1 |
        ForEach-Object { [Console]::Error.WriteLine($_.ToString()) }
}
else {
    & $publishScript -Configuration $Configuration -Architectures $Architectures -OutputRoot $packageOutputRoot
}

if (-not [string]::IsNullOrWhiteSpace($VersionTag)) {
    Write-StepMessage -Message "$exportScript -InputRoot $packageOutputRoot -OutputRoot $assetOutputRoot -Tag $VersionTag"
    if ($OutputJson) {
        & $exportScript -InputRoot $packageOutputRoot -OutputRoot $assetOutputRoot -Tag $VersionTag 2>&1 6>&1 |
            ForEach-Object { [Console]::Error.WriteLine($_.ToString()) }
    }
    else {
        & $exportScript -InputRoot $packageOutputRoot -OutputRoot $assetOutputRoot -Tag $VersionTag
    }
}

if ($OutputJson) {
    $result | ConvertTo-Json -Depth 5
}
else {
    $result
}
