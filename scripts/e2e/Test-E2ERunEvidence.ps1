[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateSet('PreJudge', 'Final')]
    [string] $Phase,
    [Parameter(Mandatory = $true)]
    [string] $EvidenceRoot,
    [Parameter(Mandatory = $true)]
    [string] $ManifestPath,
    [string] $DecisionPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

. (Join-Path $PSScriptRoot 'E2ERunEvidence.Common.ps1')
. (Join-Path $PSScriptRoot 'E2ERunEvidence.Interactive.ps1')
. (Join-Path $PSScriptRoot 'E2ERunEvidence.Final.ps1')
. (Join-Path $PSScriptRoot 'E2ERunEvidence.Receipt.ps1')
if (-not (Test-Path -LiteralPath $EvidenceRoot -PathType Container)) {
    throw 'EvidenceRoot must identify an existing directory.'
}
if (-not (Test-Path -LiteralPath $ManifestPath -PathType Leaf)) {
    throw 'ManifestPath must identify an existing file.'
}

$manifestFullPath = [System.IO.Path]::GetFullPath($ManifestPath)
$evidenceFullPath = (Resolve-Path -LiteralPath $EvidenceRoot).Path.TrimEnd('\', '/')
$manifestPrefix = $evidenceFullPath + [System.IO.Path]::DirectorySeparatorChar
if (-not $manifestFullPath.StartsWith($manifestPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'ManifestPath must be contained by EvidenceRoot.'
}

$document = [System.Text.Json.JsonDocument]::Parse([System.IO.File]::ReadAllText($manifestFullPath))
try {
    $root = $document.RootElement.Clone()
}
finally {
    $document.Dispose()
}

if ($Phase -ceq 'PreJudge') {
    $artifacts = Assert-PreJudgeEvidence -Root $root -EvidenceRoot $evidenceFullPath
    New-PreJudgeReceipt -Root $root -Artifacts $artifacts -EvidenceRoot $evidenceFullPath
    $result = [ordered]@{ phase = 'PreJudge'; passed = $true; reasons = @() }
    $json = [System.Text.Json.JsonSerializer]::Serialize(
        $result,
        $result.GetType(),
        [System.Text.Json.JsonSerializerOptions]::new())
    [Console]::Out.WriteLine($json)
    exit 0
}

if ([string]::IsNullOrWhiteSpace($DecisionPath)) {
    throw 'DecisionPath is required for Final validation.'
}
$decisionFullPath = [System.IO.Path]::GetFullPath($DecisionPath)
if (-not $decisionFullPath.StartsWith($manifestPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'DecisionPath must be contained by EvidenceRoot.'
}
if (Test-Path -LiteralPath $decisionFullPath) {
    throw 'DecisionPath must not identify an existing file.'
}
[System.IO.Directory]::CreateDirectory([System.IO.Path]::GetDirectoryName($decisionFullPath)) | Out-Null

$reasons = [System.Collections.Generic.List[string]]::new()
$artifacts = $null
try {
    $artifacts = Assert-EvidenceArtifacts -Root $root -EvidenceRoot $evidenceFullPath
}
catch {
    $reasons.Add($_.Exception.Message)
}

$runnerCompleted = $false
try {
    $runnerCompleted = Get-RunnerCompleted $root
    if (-not $runnerCompleted) {
        $reasons.Add('Runner did not complete successfully.')
    }
}
catch {
    $reasons.Add($_.Exception.Message)
}

$operational = $null -ne $artifacts
if ($operational) {
    foreach ($gate in @(
            { Assert-PreJudgeReceipt $root $artifacts $evidenceFullPath },
            { Assert-PreJudgeEvidence $root $evidenceFullPath -RequireJudgeArtifacts | Out-Null },
            { Assert-RunnerEvents $root $artifacts },
            { Assert-InputMappings $root $artifacts },
            { Assert-ReportAndCleanup $root $artifacts })) {
        try {
            & $gate
        }
        catch {
            $operational = $false
            $reasons.Add($_.Exception.Message)
        }
    }
}

$visualQualified = $false
if ($null -ne $artifacts) {
    try {
        $visualQualified = Test-VisualQualification $root $artifacts $reasons
    }
    catch {
        $reasons.Add($_.Exception.Message)
    }
}

$attemptCount = @((Get-JsonArray $root 'attempts').EnumerateArray()).Count
$repairBudgetExhausted = $attemptCount -eq 2 -and -not $visualQualified
Write-FinalDecision $decisionFullPath $runnerCompleted $operational $visualQualified $reasons $repairBudgetExhausted
if (-not ($runnerCompleted -and $operational -and $visualQualified)) {
    exit 1
}
