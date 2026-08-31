Set-StrictMode -Version Latest

function Get-JsonProperty {
    param(
        [Parameter(Mandatory = $true)] [System.Text.Json.JsonElement] $Element,
        [Parameter(Mandatory = $true)] [string] $Name
    )

    foreach ($property in $Element.EnumerateObject()) {
        if ($property.Name -ceq $Name) {
            return $property.Value.Clone()
        }
    }

    throw "Evidence is missing '$Name'."
}

function Get-JsonString {
    param([System.Text.Json.JsonElement] $Element, [string] $Name)

    $value = Get-JsonProperty -Element $Element -Name $Name
    if ($value.ValueKind -ne [System.Text.Json.JsonValueKind]::String -or
        [string]::IsNullOrWhiteSpace($value.GetString())) {
        throw "Evidence '$Name' must be a non-empty string."
    }

    return $value.GetString()
}

function Get-JsonBoolean {
    param([System.Text.Json.JsonElement] $Element, [string] $Name)

    $value = Get-JsonProperty -Element $Element -Name $Name
    if ($value.ValueKind -notin @(
            [System.Text.Json.JsonValueKind]::True,
            [System.Text.Json.JsonValueKind]::False)) {
        throw "Evidence '$Name' must be a boolean."
    }

    return $value.GetBoolean()
}

function Get-JsonInteger {
    param([System.Text.Json.JsonElement] $Element, [string] $Name)

    $value = Get-JsonProperty -Element $Element -Name $Name
    $result = 0
    if ($value.ValueKind -ne [System.Text.Json.JsonValueKind]::Number -or
        -not $value.TryGetInt32([ref] $result)) {
        throw "Evidence '$Name' must be an integer."
    }

    return $result
}

function Get-JsonArray {
    param([System.Text.Json.JsonElement] $Element, [string] $Name)

    $value = Get-JsonProperty -Element $Element -Name $Name
    if ($value.ValueKind -ne [System.Text.Json.JsonValueKind]::Array) {
        throw "Evidence '$Name' must be an array."
    }

    return $value
}

function Assert-TrueField {
    param([System.Text.Json.JsonElement] $Element, [string] $Name, [string] $Gate)

    if (-not (Get-JsonBoolean -Element $Element -Name $Name)) {
        throw "$Gate requires '$Name=true'."
    }
}

function Assert-FalseField {
    param([System.Text.Json.JsonElement] $Element, [string] $Name, [string] $Gate)

    if (Get-JsonBoolean -Element $Element -Name $Name) {
        throw "$Gate requires '$Name=false'."
    }
}

function Get-Sha256 {
    param([Parameter(Mandatory = $true)] [string] $Path)

    $stream = [System.IO.File]::OpenRead($Path)
    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try {
        return [Convert]::ToHexString($algorithm.ComputeHash($stream)).ToLowerInvariant()
    }
    finally {
        $algorithm.Dispose()
        $stream.Dispose()
    }
}

function Assert-NoReparsePoint {
    param([string] $Root, [string] $Path)

    $cursor = [System.IO.FileInfo]::new($Path)
    while ($null -ne $cursor -and
        -not $cursor.FullName.Equals($Root, [StringComparison]::OrdinalIgnoreCase)) {
        if (($cursor.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Artifact path '$Path' traverses a reparse point."
        }
        $cursor = if ($cursor -is [System.IO.FileInfo]) { $cursor.Directory } else { $cursor.Parent }
    }
}

function Assert-EvidenceArtifacts {
    param(
        [System.Text.Json.JsonElement] $Root,
        [Parameter(Mandatory = $true)] [string] $EvidenceRoot
    )

    $resolvedRoot = (Resolve-Path -LiteralPath $EvidenceRoot).Path.TrimEnd('\', '/')
    $rootPrefix = $resolvedRoot + [System.IO.Path]::DirectorySeparatorChar
    $result = @{}
    foreach ($artifact in (Get-JsonArray -Element $Root -Name 'artifacts').EnumerateArray()) {
        $id = Get-JsonString -Element $artifact -Name 'id'
        $relativePath = Get-JsonString -Element $artifact -Name 'path'
        $expectedHash = Get-JsonString -Element $artifact -Name 'sha256'
        if ($result.ContainsKey($id)) {
            throw "Artifact id '$id' is duplicated."
        }
        if ([System.IO.Path]::IsPathRooted($relativePath)) {
            throw "Artifact '$id' path must be relative to the evidence root."
        }

        $fullPath = [System.IO.Path]::GetFullPath((Join-Path $resolvedRoot $relativePath))
        if (-not $fullPath.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase) -or
            -not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
            throw "Artifact '$id' must be a file contained by the evidence root."
        }
        Assert-NoReparsePoint -Root $resolvedRoot -Path $fullPath
        if ($expectedHash -notmatch '^[0-9a-fA-F]{64}$' -or
            -not (Get-Sha256 -Path $fullPath).Equals($expectedHash, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Artifact '$id' SHA-256 does not match its manifest entry."
        }

        $result[$id] = $fullPath
    }

    if ($result.Count -eq 0) {
        throw 'Evidence must contain artifacts.'
    }
    return $result
}

function Get-ArtifactPath {
    param([hashtable] $Artifacts, [string] $Id)

    if (-not $Artifacts.ContainsKey($Id)) {
        throw "Referenced artifact '$Id' is not declared."
    }
    return [string] $Artifacts[$Id]
}

function Read-JsonArtifact {
    param([hashtable] $Artifacts, [string] $Id, [string] $EvidenceKind)

    $path = Get-ArtifactPath -Artifacts $Artifacts -Id $Id
    try {
        $document = [System.Text.Json.JsonDocument]::Parse([System.IO.File]::ReadAllText($path))
        try {
            if ($document.RootElement.ValueKind -ne [System.Text.Json.JsonValueKind]::Object) {
                throw 'root must be a JSON object'
            }
            return $document.RootElement.Clone()
        }
        finally {
            $document.Dispose()
        }
    }
    catch {
        throw "$EvidenceKind artifact '$Id' is invalid: $($_.Exception.Message)"
    }
}

function Assert-SuccessfulToolResult {
    param([System.Text.Json.JsonElement] $Envelope, [string] $EvidenceKind)

    $result = Get-JsonProperty $Envelope 'result'
    if ((Get-JsonBoolean $result 'isError') -or
        -not (Get-JsonBoolean (Get-JsonProperty $result 'structuredContent') 'success')) {
        throw "$EvidenceKind did not contain a successful MCP tool result."
    }
    return $result
}

function Assert-ArtifactReference {
    param([System.Text.Json.JsonElement] $Element, [string] $Name, [hashtable] $Artifacts)

    Get-ArtifactPath -Artifacts $Artifacts -Id (Get-JsonString -Element $Element -Name $Name) | Out-Null
}

function Assert-ReleaseIdentity {
    param([System.Text.Json.JsonElement] $Root)

    $release = Get-JsonProperty -Element $Root -Name 'release'
    foreach ($name in @('version', 'tag', 'assetName', 'architecture', 'sourceUrl')) {
        Get-JsonString -Element $release -Name $name | Out-Null
    }
    $packageHash = Get-JsonString -Element $release -Name 'packageSha256'
    if ($packageHash -notmatch '^[0-9a-fA-F]{64}$') {
        throw 'Resolved release packageSha256 must be a SHA-256 value.'
    }
}

function Get-PngSize {
    param([string] $Path)

    try {
        Add-Type -AssemblyName PresentationCore
        $stream = [System.IO.File]::OpenRead($Path)
        try {
            $decoder = [System.Windows.Media.Imaging.PngBitmapDecoder]::new(
                $stream,
                [System.Windows.Media.Imaging.BitmapCreateOptions]::PreservePixelFormat,
                [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad)
            if ($decoder.Frames.Count -ne 1) {
                throw 'PNG must contain exactly one decodable frame.'
            }
            return @($decoder.Frames[0].PixelWidth, $decoder.Frames[0].PixelHeight)
        }
        finally {
            $stream.Dispose()
        }
    }
    catch {
        throw "Image artifact '$Path' is not a decodable PNG: $($_.Exception.Message)"
    }
}

function Assert-CandidateViewportSize {
    param([int[]] $Candidate, [int[]] $Reference, [System.Text.Json.JsonElement] $Viewport)

    $referenceRatio = [double] $Reference[0] / $Reference[1]
    $candidateRatio = [double] $Candidate[0] / $Candidate[1]
    if ([Math]::Abs($candidateRatio - $referenceRatio) / $referenceRatio -gt 0.01) {
        throw 'Candidate screenshot aspect-ratio error exceeds 1%.'
    }

    $workWidth = Get-JsonInteger $Viewport 'workAreaWidth'
    $workHeight = Get-JsonInteger $Viewport 'workAreaHeight'
    $expectedWidth = $workWidth
    $expectedHeight = [Math]::Round($workWidth / $referenceRatio)
    if ($expectedHeight -gt $workHeight) {
        $expectedHeight = $workHeight
        $expectedWidth = [Math]::Round($workHeight * $referenceRatio)
    }
    if ([Math]::Abs($Candidate[0] - $expectedWidth) -gt 1 -or
        [Math]::Abs($Candidate[1] - $expectedHeight) -gt 1) {
        throw 'Candidate screenshot must use the largest reference-ratio size that fits the work area.'
    }
}

function Assert-Viewport {
    param([System.Text.Json.JsonElement] $Root, [hashtable] $Artifacts)

    $viewport = Get-JsonProperty -Element $Root -Name 'viewport'
    if ((Get-JsonString -Element $viewport -Name 'scope') -cne 'app-window') {
        throw "Candidate screenshot scope must be 'app-window'."
    }
    $reference = Get-PngSize -Path (Get-ArtifactPath $Artifacts (Get-JsonString $viewport 'referenceArtifactId'))
    $candidate = Get-PngSize -Path (Get-ArtifactPath $Artifacts (Get-JsonString $viewport 'candidateArtifactId'))
    if ($reference[0] -ne 1920 -or $reference[1] -ne 1215) {
        throw 'Canonical reference must be the 1920x1215 app-only crop including the app titlebar.'
    }

    Assert-CandidateViewportSize $candidate $reference $viewport
}

function Assert-PositiveMcpCalls {
    param([System.Text.Json.JsonElement] $Root, [hashtable] $Artifacts)

    $required = [System.Collections.Generic.HashSet[string]]::new(
        [string[]] @('connect', 'get_active_process', 'get_ui_summary', 'get_element_snapshot',
            'get_state_diff', 'restore_state_snapshot'))
    foreach ($call in (Get-JsonArray $Root 'positiveMcpCalls').EnumerateArray()) {
        $tool = Get-JsonString $call 'tool'
        $required.Remove($tool) | Out-Null
        $evidence = Read-JsonArtifact $Artifacts (Get-JsonString $call 'artifactId') "positive MCP call '$tool'"
        Assert-SuccessfulToolResult $evidence "positive MCP call '$tool'" | Out-Null
        $postcondition = Get-JsonProperty $evidence 'semanticPostcondition'
        if (-not (Get-JsonBoolean $postcondition 'passed')) {
            throw "positive MCP call '$tool' did not prove a successful semantic postcondition."
        }
    }
    if ($required.Count -ne 0) {
        throw "Positive MCP evidence is missing: $([string]::Join(', ', $required))."
    }
}

function Assert-PreviewReadiness {
    param([System.Text.Json.JsonElement] $Root)

    $readiness = Get-JsonProperty $Root 'previewReadiness'
    foreach ($name in @('valid', 'buildSucceeded', 'hostStarted', 'screenshotInspectable', 'visualContractPassed')) {
        Assert-TrueField $readiness $name 'Preview readiness'
    }
    Assert-FalseField $readiness 'inspectionTruncated' 'Preview readiness'
    if ((Get-JsonInteger $readiness 'attentionRequiredCount') -ne 0) {
        throw 'Preview readiness requires attentionRequiredCount=0.'
    }
}

function Assert-Attempts {
    param(
        [System.Text.Json.JsonElement] $Root,
        [hashtable] $Artifacts,
        [switch] $RequireJudgeArtifacts
    )

    $contractHash = Get-JsonString $Root 'visualContractHash'
    Assert-ArtifactReference $Root 'visualContractArtifactId' $Artifacts
    $contractArtifact = Get-ArtifactPath $Artifacts (Get-JsonString $Root 'visualContractArtifactId')
    if (-not (Get-Sha256 $contractArtifact).Equals($contractHash, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Visual-contract hash does not match its artifact.'
    }

    $attempts = @((Get-JsonArray $Root 'attempts').EnumerateArray())
    $viewport = Get-JsonProperty $Root 'viewport'
    $sourceReferencePath = Get-ArtifactPath $Artifacts (Get-JsonString $viewport 'referenceArtifactId')
    $sourceCandidatePath = Get-ArtifactPath $Artifacts (Get-JsonString $viewport 'candidateArtifactId')
    $sourceReferenceHash = Get-Sha256 $sourceReferencePath
    $sourceCandidateHash = Get-Sha256 $sourceCandidatePath
    if ($attempts.Count -lt 1 -or $attempts.Count -gt 2) {
        throw 'Evidence must contain one or two judge attempts.'
    }
    for ($index = 0; $index -lt $attempts.Count; $index++) {
        $attempt = $attempts[$index]
        $number = Get-JsonInteger $attempt 'number'
        $kind = Get-JsonString $attempt 'repairKind'
        if ($number -ne ($index + 1) -or
            ($number -eq 1 -and $kind -cne 'none') -or
            ($number -eq 2 -and $kind -cne 'aesthetic')) {
            throw 'Attempt numbering or repair kind violates the one-aesthetic-repair budget.'
        }
        if ((Get-JsonString $attempt 'visualContractHash') -cne $contractHash) {
            throw 'Visual-contract hash changed across judge attempts.'
        }
        $artifactFields = @('referenceArtifactId', 'candidateArtifactId')
        if ($RequireJudgeArtifacts) {
            $artifactFields += @('judgeResultArtifactId', 'imageMappingArtifactId')
        }
        foreach ($name in $artifactFields) {
            Assert-ArtifactReference $attempt $name $Artifacts
        }
        $referencePath = Get-ArtifactPath $Artifacts (Get-JsonString $attempt 'referenceArtifactId')
        $candidatePath = Get-ArtifactPath $Artifacts (Get-JsonString $attempt 'candidateArtifactId')
        $referenceSize = Get-PngSize $referencePath
        $candidateSize = Get-PngSize $candidatePath
        if ($referenceSize[0] -ne 1920 -or $referenceSize[1] -ne 1215 -or
            -not (Get-Sha256 $referencePath).Equals($sourceReferenceHash, [StringComparison]::OrdinalIgnoreCase)) {
            throw "Attempt $number reference image must be a decodable frozen copy of the canonical source."
        }
        Assert-CandidateViewportSize $candidateSize $referenceSize $viewport
        if ($number -eq 1 -and
            -not (Get-Sha256 $candidatePath).Equals($sourceCandidateHash, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Attempt 1 candidate image must be a frozen copy of the prepared candidate source.'
        }
    }
}

function Assert-CoreJourney {
    param([System.Text.Json.JsonElement] $Root, [hashtable] $Artifacts)

    $journey = Get-JsonProperty $Root 'coreJourney'
    foreach ($name in @(
            'sceneLocated', 'meaningfulSelection', 'detailSelectionVmVerified',
            'primaryBoundCommandExecuted', 'visibleFeedbackVerified', 'viewModelFeedbackVerified',
            'secondaryInteractionVerified', 'stateDiffCaptured', 'restoreSucceeded',
            'selectionRestored', 'stateRestored', 'focusRestored', 'remainingControlsSmoked')) {
        Assert-TrueField $journey $name 'Core user journey'
    }
    foreach ($id in (Get-JsonArray $journey 'artifactIds').EnumerateArray()) {
        Get-ArtifactPath $Artifacts $id.GetString() | Out-Null
    }

    $state = Get-JsonProperty $Root 'stateSafety'
    Assert-TrueField $state 'diffSucceeded' 'State safety'
    Assert-TrueField $state 'restoreSucceeded' 'State safety'
    Assert-ArtifactReference $state 'diffArtifactId' $Artifacts
    Assert-ArtifactReference $state 'restoreArtifactId' $Artifacts
    $diff = Read-JsonArtifact $Artifacts (Get-JsonString $state 'diffArtifactId') 'state diff'
    $diffResult = Assert-SuccessfulToolResult $diff 'state diff'
    if ((Get-JsonInteger (Get-JsonProperty $diffResult 'structuredContent') 'changeCount') -le 0) {
        throw 'State diff must prove at least one runtime change.'
    }
    $restore = Read-JsonArtifact $Artifacts (Get-JsonString $state 'restoreArtifactId') 'state restore'
    $restoreContent = Get-JsonProperty (Assert-SuccessfulToolResult $restore 'state restore') 'structuredContent'
    foreach ($name in @('restoredSelection', 'restoredState', 'restoredFocus')) {
        Assert-TrueField $restoreContent $name 'State restore proof'
    }
    Assert-TrueField (Get-JsonProperty $restore 'readback') 'matchesBaseline' 'State restore readback'
}

function Assert-PreJudgeEvidence {
    param(
        [System.Text.Json.JsonElement] $Root,
        [string] $EvidenceRoot,
        [switch] $RequireJudgeArtifacts
    )

    if ((Get-JsonString $Root 'schemaVersion') -cne 'wpfdevtools.e2e-run-evidence.v1') {
        throw "Unsupported E2E evidence schemaVersion."
    }
    $artifacts = Assert-EvidenceArtifacts -Root $Root -EvidenceRoot $EvidenceRoot
    Assert-ReleaseIdentity $Root
    Assert-Viewport $Root $artifacts
    Assert-PositiveMcpCalls $Root $artifacts
    Assert-PreviewReadiness $Root
    Assert-InteractiveEvidence $Root $artifacts
    Assert-CoreJourney $Root $artifacts
    Assert-Attempts $Root $artifacts -RequireJudgeArtifacts:$RequireJudgeArtifacts
    return $artifacts
}
