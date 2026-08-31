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

if ($Phase -ceq 'Final') {
    throw 'Final evidence validation is not implemented yet.'
}
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
    Assert-PreJudgeEvidence -Root $document.RootElement -EvidenceRoot $evidenceFullPath | Out-Null
    $result = [ordered]@{ phase = 'PreJudge'; passed = $true; reasons = @() }
    $json = [System.Text.Json.JsonSerializer]::Serialize(
        $result,
        $result.GetType(),
        [System.Text.Json.JsonSerializerOptions]::new())
    [Console]::Out.WriteLine($json)
}
finally {
    $document.Dispose()
}
