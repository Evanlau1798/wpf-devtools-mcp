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

. (Join-Path $PSScriptRoot 'E2EVisualJudge.Inputs.ps1')

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
        [System.Text.Json.JsonElement] $Value,
        [Parameter(Mandatory = $true)]
        [string] $Name
    )

    foreach ($property in $Value.EnumerateObject()) {
        if ($property.Name -ceq $Name) {
            return $property.Value.Clone()
        }
    }
    throw "Judge result is missing '$Name'."
}

function Assert-ExactProperties {
    param(
        [System.Text.Json.JsonElement] $Value,
        [string[]] $Names,
        [string] $Description
    )

    if ($Value.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) {
        throw "$Description must be a JSON object."
    }
    $actual = @($Value.EnumerateObject() | ForEach-Object { $_.Name })
    foreach ($name in $Names) {
        if ($actual -cnotcontains $name) {
            throw "$Description is missing '$name'."
        }
    }
    if ($actual.Count -ne $Names.Count) {
        throw "$Description contains unsupported properties."
    }
}

function Get-RequiredString {
    param([System.Text.Json.JsonElement] $Value, [string] $Name)

    $property = Get-RequiredProperty $Value $Name
    if ($property.ValueKind -ne [System.Text.Json.JsonValueKind]::String -or
        [string]::IsNullOrWhiteSpace($property.GetString())) {
        throw "Judge result '$Name' must be a non-empty string."
    }
    return $property.GetString()
}

function Assert-Axes {
    param([System.Text.Json.JsonElement] $Axes, [string[]] $Names)

    Assert-ExactProperties $Axes $Names 'Judge axes'
    foreach ($name in $Names) {
        $raw = Get-RequiredProperty $Axes $name
        $score = 0.0
        if ($raw.ValueKind -ne [System.Text.Json.JsonValueKind]::Number -or
            -not $raw.TryGetDouble([ref] $score) -or $score -lt 0 -or $score -gt 10) {
            throw "Judge axis '$name' must be between 0 and 10."
        }
    }
}

function Assert-DefectBounds {
    param([System.Text.Json.JsonElement] $Defect)

    Assert-ExactProperties $Defect @('severity', 'category', 'evidence', 'bounds') 'Visual defect'
    $severity = Get-RequiredString $Defect 'severity'
    if ($severity -notin @('blocking', 'material', 'minor')) {
        throw "Unsupported defect severity '$severity'."
    }
    Get-RequiredString $Defect 'category' | Out-Null
    Get-RequiredString $Defect 'evidence' | Out-Null

    $bounds = Get-RequiredProperty $Defect 'bounds'
    Assert-ExactProperties $bounds @('x', 'y', 'width', 'height') 'Visual defect bounds'
    $values = @{}
    foreach ($name in @('x', 'y', 'width', 'height')) {
        $raw = Get-RequiredProperty $bounds $name
        $number = 0.0
        if ($raw.ValueKind -ne [System.Text.Json.JsonValueKind]::Number -or
            -not $raw.TryGetDouble([ref] $number)) {
            throw "Visual defect bound '$name' must be numeric."
        }
        $values[$name] = $number
    }
    $x = $values.x
    $y = $values.y
    $width = $values.width
    $height = $values.height
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

        $document = [System.Text.Json.JsonDocument]::Parse($line)
        try {
            $event = $document.RootElement
            $eventType = Get-RequiredString $event 'type'
            if ($eventType -match 'compact') {
                throw "Visual judge emitted unexpected context compaction event '$eventType'."
            }
            foreach ($property in $event.EnumerateObject()) {
                if ($property.Name -ceq 'item' -and
                    $property.Value.ValueKind -eq [System.Text.Json.JsonValueKind]::Object) {
                    $itemType = Get-RequiredString $property.Value 'type'
                    if ($itemType -notin @('agent_message', 'reasoning')) {
                        throw "Visual judge emitted forbidden tool event '$itemType'."
                    }
                    $sawAgentMessage = $sawAgentMessage -or $itemType -ceq 'agent_message'
                }
            }
        }
        finally {
            $document.Dispose()
        }
    }

    if (-not $sawAgentMessage) {
        throw 'Visual judge event stream did not contain an agent message.'
    }
}

function Write-ValidatedResult {
    param(
        [Parameter(Mandatory = $true)]
        [string] $ResultPath,
        [Parameter(Mandatory = $true)]
        [string] $OutputPath,
        [Parameter(Mandatory = $true)]
        [ValidateSet('reference', 'standalone')]
        [string] $RequiredMode
    )

    $document = [System.Text.Json.JsonDocument]::Parse([System.IO.File]::ReadAllText($ResultPath))
    try {
        $result = $document.RootElement
        Assert-ExactProperties $result @('mode', 'qualityAxes', 'referenceAxes', 'defects', 'summary') 'Judge result'
        $mode = Get-RequiredString $result 'mode'
        if ($mode -notin @('reference', 'standalone') -or $mode -cne $RequiredMode) {
            throw "Judge mode '$mode' does not match expected image-input mode '$RequiredMode'."
        }
        Assert-Axes (Get-RequiredProperty $result 'qualityAxes') @(
            'layoutBalance', 'visualHierarchy', 'readabilityContrast', 'controlStateCoherence', 'visualPolish')
        $referenceAxes = Get-RequiredProperty $result 'referenceAxes'
        if ($mode -ceq 'reference') {
            Assert-Axes $referenceAxes @(
                'regionGeometry', 'densityRhythm', 'navigationBrowseRhythm', 'mediaCardComposition')
        }
        elseif ($referenceAxes.ValueKind -ne [System.Text.Json.JsonValueKind]::Null) {
            throw 'Standalone mode must return null referenceAxes.'
        }
        $defects = Get-RequiredProperty $result 'defects'
        if ($defects.ValueKind -ne [System.Text.Json.JsonValueKind]::Array) {
            throw 'Judge result defects must be an array.'
        }
        foreach ($defect in $defects.EnumerateArray()) {
            Assert-DefectBounds $defect
        }
        Get-RequiredString $result 'summary' | Out-Null

        $json = $result.GetRawText()
        [System.IO.File]::WriteAllText(
            [System.IO.Path]::GetFullPath($OutputPath),
            $json,
            [System.Text.UTF8Encoding]::new($false))
        [Console]::Out.WriteLine($json)
    }
    finally {
        $document.Dispose()
    }
}

if ($ValidateOnly) {
    $resolvedResultPath = Require-File -Path $JudgeResultPath -ParameterName 'JudgeResultPath'
    if ([string]::IsNullOrWhiteSpace($DecisionPath)) {
        throw 'DecisionPath is required with ValidateOnly.'
    }
    if ([string]::IsNullOrWhiteSpace($ExpectedMode)) {
        throw 'ExpectedMode is required with ValidateOnly.'
    }

    Write-ValidatedResult -ResultPath $resolvedResultPath -OutputPath $DecisionPath -RequiredMode $ExpectedMode
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
    $DecisionPath = Join-Path $evidenceDirectory 'visual-judge-validated.json'
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

    New-Item -ItemType Directory -Path (Split-Path -Parent $outputPath) -Force | Out-Null
}

$frozenInputs = Freeze-VisualJudgeInputs `
    -EvidenceRoot $evidenceDirectory `
    -CandidatePath $resolvedFinalImage `
    -ReferencePath $resolvedReferenceImage

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
- blocking: unreadable, accidental clipping that removes meaningful content,
  overlapping, missing, or visibly broken state.
- material: conspicuous imbalance, accidental empty region, inconsistent sizing
  or alignment, unintegrated default controls, unexplained scrollbars, or blank
  repeated surfaces.
- minor: localized polish issue that does not undermine the overall composition.

In reference mode, compare broad spatial architecture, dominant proportions,
information density, navigation and browse rhythm, and media/card composition.
Do not classify a partial continuation as clipping when the reference shows a
comparable partial item and the final image does not cut meaningful content or
an action; judge its framing, spacing, and affordance against the reference.
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
    $codexArguments += @('--image', $frozenInputs.ReferencePath)
}
$codexArguments += @('--image', $frozenInputs.CandidatePath)

try {
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        $eventLines = @(& $CodexExecutable @codexArguments 2> $stderrPath)
        $judgeExitCode = $LASTEXITCODE
        [System.IO.File]::WriteAllLines(
            $eventsPath,
            [string[]] $eventLines,
            [System.Text.UTF8Encoding]::new($false))
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
    Write-ValidatedResult `
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
