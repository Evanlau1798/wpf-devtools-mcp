[CmdletBinding()]
param(
    [string] $FinalImagePath,
    [string] $ReferenceImagePath,
    [string] $EvidenceRoot,
    [string] $JudgeResultPath,
    [string] $DecisionPath,
    [ValidateSet('reference', 'standalone')]
    [string] $ExpectedMode,
    [string] $CodexExecutable = 'codex',
    [string] $Model = 'gpt-5.6-sol',
    [ValidateSet('low', 'medium', 'high', 'xhigh', 'max', 'ultra')]
    [string] $ReasoningEffort = 'medium',
    [switch] $ValidateOnly
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$qualityAxisNames = @(
    'layoutBalance',
    'visualHierarchy',
    'readabilityContrast',
    'controlStateCoherence',
    'visualPolish'
)
$referenceAxisNames = @(
    'regionGeometry',
    'densityRhythm',
    'navigationBrowseRhythm',
    'mediaCardComposition'
)

function Require-File {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Path,
        [Parameter(Mandatory = $true)]
        [string] $ParameterName
    )

    if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$ParameterName must identify an existing file."
    }

    return (Resolve-Path -LiteralPath $Path).Path
}

function Get-RequiredProperty {
    param(
        [Parameter(Mandatory = $true)]
        [object] $Value,
        [Parameter(Mandatory = $true)]
        [string] $Name
    )

    if ($null -eq $Value -or $Value.PSObject.Properties.Name -notcontains $Name) {
        throw "Judge result is missing '$Name'."
    }

    return $Value.$Name
}

function Get-AxisMinimum {
    param(
        [Parameter(Mandatory = $true)]
        [object] $Axes,
        [Parameter(Mandatory = $true)]
        [string[]] $Names
    )

    $scores = foreach ($name in $Names) {
        $raw = Get-RequiredProperty -Value $Axes -Name $name
        $score = [double] $raw
        if ($score -lt 0 -or $score -gt 10) {
            throw "Judge axis '$name' must be between 0 and 10."
        }

        $score
    }

    return [double] (($scores | Measure-Object -Minimum).Minimum)
}

function Assert-DefectBounds {
    param([Parameter(Mandatory = $true)] [object] $Defect)

    $evidence = [string] (Get-RequiredProperty -Value $Defect -Name 'evidence')
    if ([string]::IsNullOrWhiteSpace($evidence)) {
        throw 'Every defect must include image-grounded evidence.'
    }

    $bounds = Get-RequiredProperty -Value $Defect -Name 'bounds'
    $x = [double] (Get-RequiredProperty -Value $bounds -Name 'x')
    $y = [double] (Get-RequiredProperty -Value $bounds -Name 'y')
    $width = [double] (Get-RequiredProperty -Value $bounds -Name 'width')
    $height = [double] (Get-RequiredProperty -Value $bounds -Name 'height')
    if ($x -lt 0 -or $y -lt 0 -or $width -le 0 -or $height -le 0 -or
        $x -gt 1 -or $y -gt 1 -or $width -gt 1 -or $height -gt 1 -or
        ($x + $width) -gt 1.000001 -or ($y + $height) -gt 1.000001) {
        throw 'Every defect must include normalized bounds contained within the final image.'
    }
}

function Assert-BlindJudgeEvents {
    param([Parameter(Mandatory = $true)] [string] $EventsPath)

    $sawAgentMessage = $false
    foreach ($line in [System.IO.File]::ReadLines($EventsPath)) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        $event = $line | ConvertFrom-Json
        $eventType = [string] (Get-RequiredProperty -Value $event -Name 'type')
        if ($eventType -match 'compact') {
            throw "Visual judge emitted unexpected context compaction event '$eventType'."
        }

        if ($event.PSObject.Properties.Name -contains 'item' -and $null -ne $event.item) {
            $itemType = [string] (Get-RequiredProperty -Value $event.item -Name 'type')
            if ($itemType -notin @('agent_message', 'reasoning')) {
                throw "Visual judge emitted forbidden tool event '$itemType'."
            }

            if ($itemType -eq 'agent_message') {
                $sawAgentMessage = $true
            }
        }
    }

    if (-not $sawAgentMessage) {
        throw 'Visual judge event stream did not contain an agent message.'
    }
}

function Write-Decision {
    param(
        [Parameter(Mandatory = $true)]
        [string] $ResultPath,
        [Parameter(Mandatory = $true)]
        [string] $OutputPath,
        [Parameter(Mandatory = $true)]
        [ValidateSet('reference', 'standalone')]
        [string] $RequiredMode
    )

    $result = [System.IO.File]::ReadAllText($ResultPath) | ConvertFrom-Json
    $mode = [string] (Get-RequiredProperty -Value $result -Name 'mode')
    if ($mode -notin @('reference', 'standalone')) {
        throw "Judge mode must be 'reference' or 'standalone'."
    }
    if ($mode -ne $RequiredMode) {
        throw "Judge mode '$mode' does not match expected image-input mode '$RequiredMode'."
    }

    $visualQuality = Get-AxisMinimum `
        -Axes (Get-RequiredProperty -Value $result -Name 'qualityAxes') `
        -Names $qualityAxisNames
    $referenceFidelity = $null
    $referenceAxes = Get-RequiredProperty -Value $result -Name 'referenceAxes'
    if ($mode -eq 'reference') {
        if ($null -eq $referenceAxes) {
            throw 'Reference mode requires referenceAxes.'
        }

        $referenceFidelity = Get-AxisMinimum -Axes $referenceAxes -Names $referenceAxisNames
    }
    elseif ($null -ne $referenceAxes) {
        throw 'Standalone mode must return null referenceAxes.'
    }

    $severityCap = $null
    $defects = @(Get-RequiredProperty -Value $result -Name 'defects')
    foreach ($defect in $defects) {
        Assert-DefectBounds -Defect $defect
        $severity = [string] (Get-RequiredProperty -Value $defect -Name 'severity')
        switch ($severity) {
            'blocking' {
                $severityCap = if ($null -eq $severityCap) { 9.0 } else { [Math]::Min($severityCap, 9.0) }
            }
            'material' {
                $severityCap = if ($null -eq $severityCap) { 9.5 } else { [Math]::Min($severityCap, 9.5) }
            }
            'minor' {
            }
            default {
                throw "Unsupported defect severity '$severity'."
            }
        }
    }

    if ($null -ne $severityCap) {
        $visualQuality = [Math]::Min($visualQuality, $severityCap)
        if ($null -ne $referenceFidelity) {
            $referenceFidelity = [Math]::Min($referenceFidelity, $severityCap)
        }
    }

    $reasons = [System.Collections.Generic.List[string]]::new()
    if ($visualQuality -le 9.5) {
        $reasons.Add("visualQuality=$visualQuality is not strictly greater than 9.5")
    }
    if ($mode -eq 'reference' -and $referenceFidelity -le 9.5) {
        $reasons.Add("referenceFidelity=$referenceFidelity is not strictly greater than 9.5")
    }

    $decision = [ordered]@{
        mode = $mode
        qualified = $reasons.Count -eq 0
        visualQuality = $visualQuality
        referenceFidelity = $referenceFidelity
        severityCap = $severityCap
        requiresRepair = $reasons.Count -ne 0
        reasons = @($reasons)
        defects = $defects
        judgeResultPath = (Resolve-Path -LiteralPath $ResultPath).Path
    }

    $parent = Split-Path -Parent $OutputPath
    if (-not [string]::IsNullOrWhiteSpace($parent)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }

    $json = $decision | ConvertTo-Json -Depth 8
    [System.IO.File]::WriteAllText(
        [System.IO.Path]::GetFullPath($OutputPath),
        $json,
        [System.Text.UTF8Encoding]::new($false))
    Write-Output ($decision | ConvertTo-Json -Depth 8 -Compress)
}

if ($ValidateOnly) {
    $resolvedResultPath = Require-File -Path $JudgeResultPath -ParameterName 'JudgeResultPath'
    if ([string]::IsNullOrWhiteSpace($DecisionPath)) {
        throw 'DecisionPath is required with ValidateOnly.'
    }
    if ([string]::IsNullOrWhiteSpace($ExpectedMode)) {
        throw 'ExpectedMode is required with ValidateOnly.'
    }

    Write-Decision -ResultPath $resolvedResultPath -OutputPath $DecisionPath -RequiredMode $ExpectedMode
    exit 0
}

$resolvedFinalImage = Require-File -Path $FinalImagePath -ParameterName 'FinalImagePath'
$resolvedReferenceImage = $null
if (-not [string]::IsNullOrWhiteSpace($ReferenceImagePath)) {
    $resolvedReferenceImage = Require-File -Path $ReferenceImagePath -ParameterName 'ReferenceImagePath'
}
if ([string]::IsNullOrWhiteSpace($EvidenceRoot)) {
    throw 'EvidenceRoot is required.'
}

$evidenceDirectory = [System.IO.Path]::GetFullPath($EvidenceRoot)
New-Item -ItemType Directory -Path $evidenceDirectory -Force | Out-Null

if ([string]::IsNullOrWhiteSpace($JudgeResultPath)) {
    $JudgeResultPath = Join-Path $evidenceDirectory 'visual-judge-result.json'
}
if ([string]::IsNullOrWhiteSpace($DecisionPath)) {
    $DecisionPath = Join-Path $evidenceDirectory 'visual-judge-decision.json'
}

$schemaPath = Join-Path $PSScriptRoot 'e2e-visual-judge.schema.json'
$schemaPath = Require-File -Path $schemaPath -ParameterName 'visual judge schema'
$eventsPath = Join-Path $evidenceDirectory 'visual-judge-events.jsonl'
$stderrPath = Join-Path $evidenceDirectory 'visual-judge-stderr.txt'
$mode = if ($null -eq $resolvedReferenceImage) { 'standalone' } else { 'reference' }
if (-not [string]::IsNullOrWhiteSpace($ExpectedMode) -and $ExpectedMode -ne $mode) {
    throw "ExpectedMode '$ExpectedMode' does not match the supplied image inputs '$mode'."
}
$ExpectedMode = $mode

$outputPaths = @($JudgeResultPath, $DecisionPath, $eventsPath, $stderrPath) |
    ForEach-Object { [System.IO.Path]::GetFullPath($_) }
foreach ($outputPath in $outputPaths) {
    if (Test-Path -LiteralPath $outputPath) {
        throw "Visual judge output '$outputPath' already exists. Use a fresh attempt evidence root."
    }
}

$systemTempRoot = [System.IO.Path]::GetFullPath([System.IO.Path]::GetTempPath()).TrimEnd('\') + '\'
$judgeWorkDirectory = [System.IO.Path]::GetFullPath(
    (Join-Path $systemTempRoot ('wpfdevtools-visual-judge-' + [guid]::NewGuid().ToString('N'))))
if (-not $judgeWorkDirectory.StartsWith($systemTempRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Visual judge work directory must stay under the system temporary directory.'
}
New-Item -ItemType Directory -Path $judgeWorkDirectory | Out-Null

$imageOrder = if ($mode -eq 'reference') {
    'Image 1 is the reference. Image 2 is the final application PNG.'
}
else {
    'Image 1 is the final application PNG. There is no reference image.'
}

$prompt = @"
Act as an independent, blind visual-quality judge. Inspect only the attached
pixels. Do not use tools, shell commands, product knowledge, filenames, or
assumptions about how the image was built. $imageOrder

Return mode '$mode' and JSON matching the supplied schema. Score each axis from
0 to 10 using this calibration: 10 is exceptional and virtually flawless; 9 is
polished release quality with only small visible shortcomings; 8 is solid but
has clear design defects; 7 or lower has substantial usability or composition
problems. Judge the actual pixels, not semantic intent. Write every JSON string
value in English even when text inside an image uses another language.

Quality axes:
- layoutBalance: spacing, alignment, proportions, viewport use, and density.
- visualHierarchy: clear primary, secondary, and supporting regions.
- readabilityContrast: legibility, contrast, clipping, and overlap.
- controlStateCoherence: controls, selected states, affordances, and styling fit.
- visualPolish: consistency, completeness, rhythm, and professional finish.

Classify every visible defect and give its normalized final-image bounds:
- blocking: unreadable, clipped, overlapping, missing, or visibly broken state.
- material: conspicuous imbalance, accidental empty region, inconsistent sizing
  or alignment, unintegrated default controls, unexplained scrollbars, or blank
  repeated surfaces.
- minor: localized polish issue that does not undermine the overall composition.

In reference mode, compare broad spatial architecture, dominant proportions,
information density, navigation and browse rhythm, and media/card composition.
Do not penalize original branding, content, palette, or imagery. In standalone
mode, return null referenceAxes. Do not include a pass/fail verdict or infer a
target score. Keep the summary concise and image-grounded.
"@

$codexArguments = @(
    'exec',
    $prompt,
    '--ignore-user-config',
    '--ignore-rules',
    '--strict-config',
    '--skip-git-repo-check',
    '--ephemeral',
    '--disable',
    'apps',
    '--disable',
    'browser_use',
    '--disable',
    'browser_use_external',
    '--disable',
    'computer_use',
    '--disable',
    'hooks',
    '--disable',
    'image_generation',
    '--disable',
    'in_app_browser',
    '--disable',
    'memories',
    '--disable',
    'multi_agent',
    '--disable',
    'multi_agent_v2',
    '--disable',
    'plugins',
    '--disable',
    'remote_plugin',
    '--disable',
    'shell_tool',
    '--disable',
    'skill_mcp_dependency_install',
    '--disable',
    'tool_suggest',
    '--model',
    $Model,
    '-c',
    "model_reasoning_effort=$ReasoningEffort",
    '--sandbox',
    'read-only',
    '--json',
    '--output-schema',
    $schemaPath,
    '--output-last-message',
    [System.IO.Path]::GetFullPath($JudgeResultPath),
    '--cd',
    $judgeWorkDirectory
)
if ($mode -eq 'reference') {
    $codexArguments += @('--image', $resolvedReferenceImage)
}
$codexArguments += @('--image', $resolvedFinalImage)

try {
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $CodexExecutable @codexArguments 2> $stderrPath |
            Set-Content -LiteralPath $eventsPath -Encoding UTF8
        $judgeExitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }
    if ($judgeExitCode -ne 0) {
        throw "Visual judge exited with code $judgeExitCode. See '$stderrPath'."
    }

    $resolvedEventsPath = Require-File -Path $eventsPath -ParameterName 'visual judge events'
    Assert-BlindJudgeEvents -EventsPath $resolvedEventsPath
    $resolvedJudgeResult = Require-File -Path $JudgeResultPath -ParameterName 'JudgeResultPath'
    Write-Decision `
        -ResultPath $resolvedJudgeResult `
        -OutputPath $DecisionPath `
        -RequiredMode $ExpectedMode
}
finally {
    if ($judgeWorkDirectory.StartsWith($systemTempRoot, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $judgeWorkDirectory)) {
        Remove-Item -LiteralPath $judgeWorkDirectory -Recurse -Force -ErrorAction SilentlyContinue
    }
}
