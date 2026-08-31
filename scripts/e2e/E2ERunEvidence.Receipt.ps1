Set-StrictMode -Version Latest

function Get-PreJudgeDigest {
    param([System.Text.Json.JsonElement] $Root, [hashtable] $Artifacts)

    $artifactIds = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($id in @(
            (Get-JsonString $Root 'visualContractArtifactId'),
            (Get-JsonString (Get-JsonProperty $Root 'runner') 'eventsArtifactId'),
            (Get-JsonString (Get-JsonProperty $Root 'viewport') 'referenceArtifactId'),
            (Get-JsonString (Get-JsonProperty $Root 'viewport') 'candidateArtifactId'))) {
        $artifactIds.Add($id) | Out-Null
    }
    foreach ($call in (Get-JsonArray $Root 'positiveMcpCalls').EnumerateArray()) {
        $artifactIds.Add((Get-JsonString $call 'artifactId')) | Out-Null
    }
    $interactive = Get-JsonProperty $Root 'interactive'
    $artifactIds.Add((Get-JsonString $interactive 'runtimeInventoryArtifactId')) | Out-Null
    foreach ($item in (Get-JsonArray $interactive 'inventory').EnumerateArray()) {
        $binding = Get-JsonProperty $item 'binding'
        if ((Get-JsonString $binding 'kind') -cne 'native-state-only') {
            $artifactIds.Add((Get-JsonString $binding 'artifactId')) | Out-Null
        }
        $interaction = Get-JsonProperty $item 'interaction'
        foreach ($name in @('beforeArtifactId', 'actionArtifactId', 'afterArtifactId')) {
            $artifactIds.Add((Get-JsonString $interaction $name)) | Out-Null
        }
    }
    $state = Get-JsonProperty $Root 'stateSafety'
    $artifactIds.Add((Get-JsonString $state 'diffArtifactId')) | Out-Null
    $artifactIds.Add((Get-JsonString $state 'restoreArtifactId')) | Out-Null
    foreach ($id in (Get-JsonArray (Get-JsonProperty $Root 'coreJourney') 'artifactIds').EnumerateArray()) {
        $artifactIds.Add($id.GetString()) | Out-Null
    }
    $firstAttempt = @((Get-JsonArray $Root 'attempts').EnumerateArray())[0]
    $artifactIds.Add((Get-JsonString $firstAttempt 'referenceArtifactId')) | Out-Null
    $artifactIds.Add((Get-JsonString $firstAttempt 'candidateArtifactId')) | Out-Null

    $parts = [System.Collections.Generic.List[string]]::new()
    foreach ($name in @('schemaVersion', 'visualContractHash')) {
        $parts.Add("$name=$(Get-JsonString $Root $name)")
    }
    foreach ($name in @('release', 'runner', 'viewport', 'positiveMcpCalls', 'previewReadiness',
            'interactive', 'coreJourney', 'stateSafety')) {
        $parts.Add("$name=$((Get-JsonProperty $Root $name).GetRawText())")
    }
    $parts.Add("attempt1=$((Get-JsonInteger $firstAttempt 'number'))|$((Get-JsonString $firstAttempt 'repairKind'))|" +
        "$((Get-JsonString $firstAttempt 'visualContractHash'))|$((Get-JsonString $firstAttempt 'referenceArtifactId'))|" +
        "$((Get-JsonString $firstAttempt 'candidateArtifactId'))")
    foreach ($id in @($artifactIds | Sort-Object)) {
        $parts.Add("artifact:$id=$(Get-Sha256 (Get-ArtifactPath $Artifacts $id))")
    }

    $bytes = [System.Text.Encoding]::UTF8.GetBytes([string]::Join("`n", $parts))
    return [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData($bytes)).ToLowerInvariant()
}

function New-PreJudgeReceipt {
    param([System.Text.Json.JsonElement] $Root, [hashtable] $Artifacts, [string] $EvidenceRoot)

    $path = Join-Path $EvidenceRoot 'prejudge-receipt.json'
    if (Test-Path -LiteralPath $path) {
        throw 'PreJudge receipt already exists and cannot be overwritten.'
    }
    $receipt = [ordered]@{
        schemaVersion = 'wpfdevtools.e2e-prejudge-receipt.v1'
        visualContractHash = Get-JsonString $Root 'visualContractHash'
        operationalEvidenceHash = Get-PreJudgeDigest $Root $Artifacts
        createdUtc = [DateTimeOffset]::UtcNow.ToString('O')
    }
    $json = [System.Text.Json.JsonSerializer]::Serialize($receipt, $receipt.GetType())
    $bytes = [System.Text.UTF8Encoding]::new($false).GetBytes($json)
    $stream = [System.IO.File]::Open($path, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write)
    try { $stream.Write($bytes, 0, $bytes.Length) } finally { $stream.Dispose() }
}

function Assert-PreJudgeReceipt {
    param([System.Text.Json.JsonElement] $Root, [hashtable] $Artifacts, [string] $EvidenceRoot)

    $path = Join-Path $EvidenceRoot 'prejudge-receipt.json'
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw 'PreJudge receipt is missing; Final requires a completed PreJudge phase.'
    }
    Assert-NoReparsePoint $EvidenceRoot $path
    $receiptArtifacts = @{ receipt = $path }
    $receipt = Read-JsonArtifact $receiptArtifacts 'receipt' 'PreJudge receipt'
    if ((Get-JsonString $receipt 'schemaVersion') -cne 'wpfdevtools.e2e-prejudge-receipt.v1' -or
        (Get-JsonString $receipt 'visualContractHash') -cne (Get-JsonString $Root 'visualContractHash') -or
        (Get-JsonString $receipt 'operationalEvidenceHash') -cne (Get-PreJudgeDigest $Root $Artifacts)) {
        throw 'PreJudge receipt does not match the current operational evidence.'
    }
    [DateTimeOffset]::Parse((Get-JsonString $receipt 'createdUtc')) | Out-Null
}
