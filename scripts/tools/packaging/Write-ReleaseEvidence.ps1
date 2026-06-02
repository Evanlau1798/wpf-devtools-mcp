param(
    [Parameter(Mandatory)] [string]$OutputPath,
    [string]$Repository = $env:GITHUB_REPOSITORY,
    [string]$Branch = $env:GITHUB_REF_NAME,
    [string]$CommitSha = $env:GITHUB_SHA,
    [string]$WorkflowRunId = $env:GITHUB_RUN_ID,
    [string[]]$RunnerMatrix = @(),
    [Parameter(Mandatory)] [string[]]$RuntimeEvidencePath,
    [Parameter(Mandatory)] [string]$DocFxEvidencePath,
    [string[]]$SecurityEvidencePath = @(),
    [Parameter(Mandatory)] [string]$Sha256SumsPath,
    [Parameter(Mandatory)] [string]$ReleaseAssetsPath,
    [Parameter(Mandatory)] [string]$ReleaseSbomPath,
    [Parameter(Mandatory)] [string]$PackageSbomPath,
    [string]$DotnetSdkVersion = '',
    [string]$PowerShellVersion = '',
    [string]$WorkflowSha = $env:GITHUB_WORKFLOW_SHA,
    [string]$ExpectedThumbprintHash = '',
    [string]$ObservedThumbprintHash = '',
    [string]$TrustedSignerThumbprint = '',
    [switch]$UninstallResiduePassed,
    [switch]$PublicReleaseStrict
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-Sha256HexForBytes {
    param([Parameter(Mandatory)] [byte[]]$Bytes)

    $sha256 = [System.Security.Cryptography.SHA256]::Create()
    try {
        return (($sha256.ComputeHash($Bytes) | ForEach-Object { $_.ToString('x2') }) -join '')
    }
    finally {
        $sha256.Dispose()
    }
}

function Get-Sha256HexForFile {
    param([Parameter(Mandatory)] [string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required release evidence input is missing: $Path"
    }

    return Get-Sha256HexForBytes -Bytes ([System.IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $Path).Path))
}

function Get-Sha256HexForString {
    param([Parameter(Mandatory)] [string]$Value)

    return Get-Sha256HexForBytes -Bytes ([System.Text.Encoding]::UTF8.GetBytes($Value))
}

function Resolve-GitValue {
    param(
        [Parameter(Mandatory)] [string]$Value,
        [Parameter(Mandatory)] [string]$FallbackCommand
    )

    if (-not [string]::IsNullOrWhiteSpace($Value)) {
        return $Value
    }

    $result = & git $FallbackCommand.Split(' ') 2>$null
    if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace([string]$result)) {
        return ([string]$result).Trim()
    }

    return 'unknown'
}

function Read-RuntimeEvidence {
    param([Parameter(Mandatory)] [string[]]$Paths)

    $expandedPaths = @($Paths |
        ForEach-Object { [string]$_ -split ',' } |
        ForEach-Object { $_.Trim() } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) })

    if ($expandedPaths.Count -eq 0) {
        throw 'At least one runtime evidence JSON path is required.'
    }

    foreach ($path in $expandedPaths) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Runtime evidence JSON is missing: $path"
        }

        Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    }
}

function Get-RequiredJsonProperty {
    param(
        [Parameter(Mandatory)] [object]$Object,
        [Parameter(Mandatory)] [string]$Name,
        [Parameter(Mandatory)] [string]$Source
    )

    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        throw "$Source is missing required property '$Name'."
    }

    return $property.Value
}

function Read-DocFxEvidence {
    param([Parameter(Mandatory)] [string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "DocFX evidence JSON is missing: $Path"
    }

    $source = Get-Content -LiteralPath $Path -Raw | ConvertFrom-Json
    return [ordered]@{
        englishParity = [bool](Get-RequiredJsonProperty -Object $source -Name 'englishParity' -Source 'DocFX evidence')
        zhTwParity = [bool](Get-RequiredJsonProperty -Object $source -Name 'zhTwParity' -Source 'DocFX evidence')
        brokenLinks = [int](Get-RequiredJsonProperty -Object $source -Name 'brokenLinks' -Source 'DocFX evidence')
    }
}

function Read-SecurityEvidence {
    param([string[]]$Paths = @())

    $expandedPaths = @($Paths |
        ForEach-Object { [string]$_ -split ',' } |
        ForEach-Object { $_.Trim() } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) })

    foreach ($path in $expandedPaths) {
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
            throw "Security evidence JSON is missing: $path"
        }

        Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    }
}

function Test-AllTrue {
    param(
        [Parameter(Mandatory)] [object[]]$Evidence,
        [Parameter(Mandatory)] [string]$Section,
        [Parameter(Mandatory)] [string]$Property
    )

    foreach ($item in $Evidence) {
        $sectionProperty = $item.PSObject.Properties[$Section]
        if ($null -eq $sectionProperty) {
            return $false
        }

        $sectionValue = $sectionProperty.Value
        if ($null -eq $sectionValue) {
            return $false
        }

        $valueProperty = $sectionValue.PSObject.Properties[$Property]
        if ($null -eq $valueProperty -or $valueProperty.Value -ne $true) {
            return $false
        }
    }

    return $true
}

function Get-FirstValue {
    param(
        [Parameter(Mandatory)] [object[]]$Evidence,
        [Parameter(Mandatory)] [string]$Section,
        [Parameter(Mandatory)] [string]$Property
    )

    foreach ($item in $Evidence) {
        $sectionProperty = $item.PSObject.Properties[$Section]
        if ($null -eq $sectionProperty) {
            continue
        }

        $sectionValue = $sectionProperty.Value
        if ($null -ne $sectionValue) {
            $valueProperty = $sectionValue.PSObject.Properties[$Property]
            if ($null -ne $valueProperty -and $null -ne $valueProperty.Value) {
                return $valueProperty.Value
            }
        }
    }

    throw "Runtime evidence is missing $Section.$Property."
}

function Get-MergedStatus {
    param(
        [Parameter(Mandatory)] [object[]]$Evidence,
        [Parameter(Mandatory)] [string]$Property
    )

    $values = @()
    foreach ($item in $Evidence) {
        $packageSmokeProperty = $item.PSObject.Properties['packageSmoke']
        if ($null -eq $packageSmokeProperty) {
            continue
        }

        $packageSmoke = $packageSmokeProperty.Value
        if ($null -ne $packageSmoke) {
            $valueProperty = $packageSmoke.PSObject.Properties[$Property]
            if ($null -ne $valueProperty -and $null -ne $valueProperty.Value) {
                $values += [string]$valueProperty.Value
            }
        }
    }

    if ($values -contains 'passed') {
        return 'passed'
    }

    if ($values -contains 'failed') {
        return 'failed'
    }

    if ($values -contains 'not-run') {
        return 'not-run'
    }

    return 'passed-or-not-public'
}

function Assert-PublicReleaseStrictEvidence {
    param(
        [Parameter(Mandatory)] [System.Collections.Specialized.OrderedDictionary]$Evidence,
        [Parameter(Mandatory)] [string[]]$Runners
    )

    $failures = @()
    $docfx = $Evidence['docfx']
    $security = $Evidence['security']
    $packageSmoke = $Evidence['packageSmoke']
    $liveSmoke = $Evidence['liveSmoke']

    if ($docfx['englishParity'] -ne $true) {
        $failures += 'docfx.englishParity'
    }
    if ($docfx['zhTwParity'] -ne $true) {
        $failures += 'docfx.zhTwParity'
    }
    if ([int]$docfx['brokenLinks'] -ne 0) {
        $failures += 'docfx.brokenLinks'
    }

    foreach ($property in @('mitmMatrixPassed', 'stdoutPurityPassed', 'screenshotIntegrityPassed')) {
        if ($security[$property] -ne $true) {
            $failures += "security.$property"
        }
    }

    foreach ($property in @('connect', 'ping', 'getUiSummary', 'safeRead', 'mutationRestore', 'uninstallResidue')) {
        if ($liveSmoke[$property] -ne $true) {
            $failures += "liveSmoke.$property"
        }
    }

    $requiredPackageSmoke = [ordered]@{
        'windows-x64' = @('x64PackageLocal', 'x64OnlineInstaller')
        'windows-x86' = @('x86PackageLocal', 'x86OnlineInstaller')
        'windows-arm64' = @('arm64PackageLocal', 'arm64OnlineInstaller')
    }

    foreach ($runner in $Runners) {
        if (-not $requiredPackageSmoke.Contains($runner)) {
            continue
        }

        foreach ($property in $requiredPackageSmoke[$runner]) {
            if ([string]$packageSmoke[$property] -ne 'passed') {
                $failures += "packageSmoke.$property"
            }
        }
    }

    if ($failures.Count -gt 0) {
        throw "Public release evidence strict mode failed: $($failures -join ', ')"
    }
}

function Normalize-ThumbprintHash {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value)) {
        return $null
    }

    if ($Value -match '^[a-fA-F0-9]{64}$') {
        return $Value.ToLowerInvariant()
    }

    return Get-Sha256HexForString -Value $Value.Trim()
}

function Resolve-ToolVersion {
    param(
        [string]$Value,
        [Parameter(Mandatory)] [scriptblock]$Command,
        [Parameter(Mandatory)] [string]$Fallback
    )

    if (-not [string]::IsNullOrWhiteSpace($Value)) {
        return $Value
    }

    try {
        $output = & $Command 2>$null
        if ($LASTEXITCODE -eq 0 -and -not [string]::IsNullOrWhiteSpace([string]$output)) {
            return ([string]$output).Trim()
        }
    }
    catch {
    }

    return $Fallback
}

function Get-PinnedGitHubActions {
    $repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..\..')).Path
    $workflowRoot = Join-Path $repoRoot '.github\workflows'
    if (-not (Test-Path -LiteralPath $workflowRoot -PathType Container)) {
        return @()
    }

    Get-ChildItem -LiteralPath $workflowRoot -Filter '*.yml' -File |
        Sort-Object Name |
        ForEach-Object {
            $workflow = $_
            $lineNumber = 0
            Get-Content -LiteralPath $workflow.FullName | ForEach-Object {
                $lineNumber++
                $match = [regex]::Match($_, '^\s*uses:\s*(?<ref>[^\s#]+)')
                if (-not $match.Success) {
                    return
                }

                $reference = $match.Groups['ref'].Value
                $parts = $reference.Split('@', 2)
                if ($parts.Count -ne 2) {
                    return
                }

                [pscustomobject]@{
                    workflow = $workflow.Name
                    line = $lineNumber
                    action = $parts[0]
                    reference = $parts[1]
                    pinnedToSha = $parts[1] -match '^[a-fA-F0-9]{40}$'
                }
            }
        }
}

$runtimeEvidence = @(Read-RuntimeEvidence -Paths $RuntimeEvidencePath)
$docFxEvidence = Read-DocFxEvidence -Path $DocFxEvidencePath
$securityEvidence = @(Read-SecurityEvidence -Paths $SecurityEvidencePath)
$repositoryValue = if ([string]::IsNullOrWhiteSpace($Repository)) { 'Evanlau1798/wpf-devtools-mcp' } else { $Repository }
$branchValue = Resolve-GitValue -Value $Branch -FallbackCommand 'branch --show-current'
$commitShaValue = Resolve-GitValue -Value $CommitSha -FallbackCommand 'rev-parse HEAD'
$dotnetSdkVersionValue = Resolve-ToolVersion -Value $DotnetSdkVersion -Command { dotnet --version } -Fallback 'unknown'
$powerShellVersionValue = Resolve-ToolVersion -Value $PowerShellVersion -Command { $PSVersionTable.PSVersion.ToString() } -Fallback 'unknown'
$workflowShaValue = if ([string]::IsNullOrWhiteSpace($WorkflowSha)) { 'unknown' } else { $WorkflowSha }
$pinnedActions = @(Get-PinnedGitHubActions)
$workflowRunIds = @()
if (-not [string]::IsNullOrWhiteSpace($WorkflowRunId)) {
    $workflowRunIds += [long]$WorkflowRunId
}

$runnerValues = @($RunnerMatrix |
    ForEach-Object { [string]$_ -split ',' } |
    ForEach-Object { $_.Trim() } |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
if ($runnerValues.Count -eq 0) {
    $runnerValues = @('windows-x64')
}

$signerHash = Normalize-ThumbprintHash -Value $TrustedSignerThumbprint
$expectedSignerHash = Normalize-ThumbprintHash -Value $ExpectedThumbprintHash
$observedSignerHash = Normalize-ThumbprintHash -Value $ObservedThumbprintHash
if ($null -eq $expectedSignerHash) {
    $expectedSignerHash = $signerHash
}

if ($null -eq $observedSignerHash) {
    $observedSignerHash = $signerHash
}

$mitmMatrixPassed = if ($securityEvidence.Count -gt 0) {
    Test-AllTrue -Evidence $securityEvidence -Section 'security' -Property 'mitmMatrixPassed'
}
else {
    Test-AllTrue -Evidence $runtimeEvidence -Section 'security' -Property 'mitmMatrixPassed'
}

$screenshotIntegrityPassed = if ($securityEvidence.Count -gt 0) {
    Test-AllTrue -Evidence $securityEvidence -Section 'security' -Property 'screenshotIntegrityPassed'
}
else {
    Test-AllTrue -Evidence $runtimeEvidence -Section 'security' -Property 'screenshotIntegrityPassed'
}

$evidence = [ordered]@{
    repository = $repositoryValue
    branch = $branchValue
    commitSha = $commitShaValue
    workflowRunIds = $workflowRunIds
    runnerMatrix = $runnerValues
    toolsList = [ordered]@{
        count = [int](Get-FirstValue -Evidence $runtimeEvidence -Section 'toolsList' -Property 'count')
        nameSetHash = [string](Get-FirstValue -Evidence $runtimeEvidence -Section 'toolsList' -Property 'nameSetHash')
        schemaSnapshotHash = [string](Get-FirstValue -Evidence $runtimeEvidence -Section 'toolsList' -Property 'schemaSnapshotHash')
    }
    docfx = $docFxEvidence
    security = [ordered]@{
        mitmMatrixPassed = $mitmMatrixPassed
        stdoutPurityPassed = Test-AllTrue -Evidence $runtimeEvidence -Section 'security' -Property 'stdoutPurityPassed'
        screenshotIntegrityPassed = $screenshotIntegrityPassed
    }
    packageSmoke = [ordered]@{
        x64PackageLocal = Get-MergedStatus -Evidence $runtimeEvidence -Property 'x64PackageLocal'
        x64OnlineInstaller = Get-MergedStatus -Evidence $runtimeEvidence -Property 'x64OnlineInstaller'
        x86PackageLocal = Get-MergedStatus -Evidence $runtimeEvidence -Property 'x86PackageLocal'
        x86OnlineInstaller = Get-MergedStatus -Evidence $runtimeEvidence -Property 'x86OnlineInstaller'
        arm64PackageLocal = Get-MergedStatus -Evidence $runtimeEvidence -Property 'arm64PackageLocal'
        arm64OnlineInstaller = Get-MergedStatus -Evidence $runtimeEvidence -Property 'arm64OnlineInstaller'
    }
    liveSmoke = [ordered]@{
        connect = Test-AllTrue -Evidence $runtimeEvidence -Section 'liveSmoke' -Property 'connect'
        ping = Test-AllTrue -Evidence $runtimeEvidence -Section 'liveSmoke' -Property 'ping'
        getUiSummary = Test-AllTrue -Evidence $runtimeEvidence -Section 'liveSmoke' -Property 'getUiSummary'
        safeRead = Test-AllTrue -Evidence $runtimeEvidence -Section 'liveSmoke' -Property 'safeRead'
        mutationRestore = Test-AllTrue -Evidence $runtimeEvidence -Section 'liveSmoke' -Property 'mutationRestore'
        uninstallResidue = [bool]$UninstallResiduePassed -or (Test-AllTrue -Evidence $runtimeEvidence -Section 'liveSmoke' -Property 'uninstallResidue')
    }
    releaseAssets = [ordered]@{
        sha256SumsHash = Get-Sha256HexForFile -Path $Sha256SumsPath
        releaseAssetsJsonHash = Get-Sha256HexForFile -Path $ReleaseAssetsPath
        releaseSbomHash = Get-Sha256HexForFile -Path $ReleaseSbomPath
        packageSbomHash = Get-Sha256HexForFile -Path $PackageSbomPath
    }
    signing = [ordered]@{
        expectedThumbprintHash = $expectedSignerHash
        observedThumbprintHash = $observedSignerHash
    }
    runnerEnvironment = [ordered]@{
        dotnetSdkVersion = $dotnetSdkVersionValue
        powerShellVersion = $powerShellVersionValue
        workflowSha = $workflowShaValue
        pinnedActionCount = $pinnedActions.Count
        pinnedActions = $pinnedActions
    }
}

if ($PublicReleaseStrict) {
    Assert-PublicReleaseStrictEvidence -Evidence $evidence -Runners $runnerValues
}

$outputDirectory = Split-Path -Parent ([System.IO.Path]::GetFullPath($OutputPath))
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null
$evidence | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $OutputPath -Encoding UTF8
