Set-StrictMode -Version Latest

function Initialize-RpcArtifact([hashtable] $Artifacts, [hashtable] $Cache, [string] $ArtifactId) {
    if (-not $Cache.ContainsKey($ArtifactId)) {
        $Cache[$ArtifactId] = @(foreach ($line in [IO.File]::ReadLines((Get-ArtifactPath $Artifacts $ArtifactId))) {
            $document = [System.Text.Json.JsonDocument]::Parse($line)
            try { $document.RootElement.Clone() } finally { $document.Dispose() }
        })
    }
}

function Get-RpcMessage {
    param([hashtable] $Artifacts, [hashtable] $Cache, [string] $ArtifactId,
        [System.Text.Json.JsonElement] $RequestId)

    Initialize-RpcArtifact $Artifacts $Cache $ArtifactId
    $matches = @($Cache[$ArtifactId] | Where-Object {
        $id = [System.Text.Json.JsonElement]::new()
        $_.ValueKind -eq [System.Text.Json.JsonValueKind]::Object -and
        $_.TryGetProperty('id', [ref] $id) -and (Test-JsonValueEqual $id $RequestId)
    })
    if ($matches.Count -ne 1) { throw 'MCP provenance requires exactly one matching raw RPC message.' }
    return $matches[0]
}

function Assert-McpCallProvenance {
    param([System.Text.Json.JsonElement] $Call, [hashtable] $Artifacts, [hashtable] $Cache,
        [string] $ExpectedTool)

    if ((Get-JsonString $Call 'schemaVersion') -cne 'wpfdevtools.e2e-mcp-call-evidence.v1') {
        throw 'MCP call provenance schema is invalid.'
    }
    $tool = Get-JsonString $Call 'tool'
    if ($ExpectedTool -and $tool -cne $ExpectedTool) { throw 'MCP call provenance tool identity differs.' }
    $id = Get-JsonProperty $Call 'requestId'
    if ($id.ValueKind -notin @([System.Text.Json.JsonValueKind]::String, [System.Text.Json.JsonValueKind]::Number)) {
        throw 'MCP call provenance requestId must be a string or number.'
    }
    $raw = Get-JsonProperty $Call 'rawRpc'
    $request = Get-RpcMessage $Artifacts $Cache (Get-JsonString $raw 'requestsArtifactId') $id
    $response = Get-RpcMessage $Artifacts $Cache (Get-JsonString $raw 'responsesArtifactId') $id
    if ((Get-JsonString $request 'jsonrpc') -cne '2.0' -or
        (Get-JsonString $response 'jsonrpc') -cne '2.0' -or
        (Get-JsonString $request 'method') -cne 'tools/call' -or
        (Get-JsonString (Get-JsonProperty $request 'params') 'name') -cne $tool -or
        -not (Test-JsonValueEqual (Get-JsonProperty $Call 'result') (Get-JsonProperty $response 'result'))) {
        throw "MCP '$tool' provenance must match the unaltered raw tools/call request and result."
    }
    Assert-SuccessfulToolResult $Call "MCP '$tool' provenance" | Out-Null
    $transportId = Get-JsonString $raw 'transportArtifactId'
    $causal = Assert-RpcTransportOrder $Artifacts $Cache $transportId $id $request $response
    return [pscustomobject]@{
        RequestsArtifactId = Get-JsonString $raw 'requestsArtifactId'
        ResponsesArtifactId = Get-JsonString $raw 'responsesArtifactId'
        RequestIndex = [array]::IndexOf([array]$Cache[(Get-JsonString $raw 'requestsArtifactId')], $request)
        ResponseIndex = [array]::IndexOf([array]$Cache[(Get-JsonString $raw 'responsesArtifactId')], $response)
        Request = $request
        TransportArtifactId = $transportId
        SentSequence = $causal.SentSequence
        ReceivedSequence = $causal.ReceivedSequence
    }
}

function Assert-RpcTransportOrder {
    param([hashtable] $Artifacts, [hashtable] $Cache, [string] $ArtifactId,
        [System.Text.Json.JsonElement] $RequestId, [System.Text.Json.JsonElement] $Request,
        [System.Text.Json.JsonElement] $Response)

    Initialize-RpcArtifact $Artifacts $Cache $ArtifactId
    $previous = 0
    $matching = @{ sent = @(); received = @() }
    foreach ($event in $Cache[$ArtifactId]) {
        $sequence = Get-JsonInteger $event 'sequence'
        $direction = Get-JsonString $event 'direction'
        if ($sequence -le $previous -or $direction -cnotin @('sent', 'received')) {
            throw 'RPC transport sequence must be positive, strictly increasing, with sent/received directions.'
        }
        $previous = $sequence
        $message = Get-JsonProperty $event 'message'
        if ((Get-JsonString $message 'jsonrpc') -cne '2.0') { throw 'RPC transport message must use JSON-RPC 2.0.' }
        $id = [System.Text.Json.JsonElement]::new()
        if ($message.TryGetProperty('id', [ref] $id) -and (Test-JsonValueEqual $id $RequestId)) {
            $matching[$direction] += $event
        }
    }
    if ($matching.sent.Count -ne 1 -or $matching.received.Count -ne 1 -or
        -not (Test-JsonValueEqual (Get-JsonProperty $matching.sent[0] 'message') $Request) -or
        -not (Test-JsonValueEqual (Get-JsonProperty $matching.received[0] 'message') $Response)) {
        throw 'RPC transport must contain unique, unchanged sent and received messages matching the raw streams.'
    }
    $sent = Get-JsonInteger $matching.sent[0] 'sequence'
    $received = Get-JsonInteger $matching.received[0] 'sequence'
    if ($sent -ge $received) { throw 'RPC response must be received after its request is sent.' }
    return [pscustomobject]@{ SentSequence = $sent; ReceivedSequence = $received }
}

function Get-JsonPointerValue {
    param([System.Text.Json.JsonElement] $Value, [string] $Pointer)

    if (-not $Pointer.StartsWith('/') -or $Pointer -match '~(?![01])') {
        throw 'Readback provenance requires a valid nonempty JSON pointer into the tool result.'
    }
    foreach ($part in $Pointer.Substring(1).Split('/')) {
        $key = $part.Replace('~1', '/').Replace('~0', '~')
        if ($Value.ValueKind -eq [System.Text.Json.JsonValueKind]::Array) {
            $index = 0
            if ($key -notmatch '^(0|[1-9][0-9]*)$' -or -not [int]::TryParse($key, [ref] $index) -or
                $index -ge $Value.GetArrayLength()) { throw 'Readback provenance array pointer is invalid.' }
            $Value = $Value[$index].Clone()
        }
        else { $Value = Get-JsonProperty $Value $key }
    }
    return $Value
}

function Assert-ReadbackProvenance {
    param([System.Text.Json.JsonElement] $Readback, [hashtable] $Artifacts, [hashtable] $Cache,
        $DiffProof, $RestoreProof)

    $provenance = Get-JsonProperty $Readback 'provenance'
    foreach ($phase in @('baseline', 'actual')) {
        foreach ($category in @('selection', 'state', 'focus')) {
            $values = Get-JsonProperty (Get-JsonProperty $Readback $phase) $category
            $sources = Get-JsonProperty (Get-JsonProperty $provenance $phase) $category
            if (@($sources.EnumerateObject()).Count -ne @($values.EnumerateObject()).Count) {
                throw 'Readback provenance must cover every measured value exactly.'
            }
            foreach ($property in $values.EnumerateObject()) {
                $source = Get-JsonProperty $sources $property.Name
                $call = Read-JsonArtifact $Artifacts (Get-JsonString $source 'callArtifactId') 'readback provenance'
                $proof = Assert-McpCallProvenance $call $Artifacts $Cache
                $anchor = if ($phase -eq 'baseline') { $DiffProof } else { $RestoreProof }
                $ordered = if ($phase -eq 'baseline') {
                    $proof.RequestIndex -lt $anchor.RequestIndex -and $proof.ResponseIndex -lt $anchor.ResponseIndex
                } else {
                    $proof.RequestIndex -gt $anchor.RequestIndex -and $proof.ResponseIndex -gt $anchor.ResponseIndex
                }
                if ($proof.RequestsArtifactId -cne $anchor.RequestsArtifactId -or
                    $proof.ResponsesArtifactId -cne $anchor.ResponsesArtifactId -or
                    $proof.TransportArtifactId -cne $anchor.TransportArtifactId -or -not $ordered -or
                    ($phase -eq 'baseline' -and $proof.ReceivedSequence -ge $anchor.SentSequence) -or
                    ($phase -eq 'actual' -and $proof.SentSequence -le $anchor.ReceivedSequence)) {
                    throw 'Readback provenance requires distinct baseline-before-diff and actual-after-restore calls in the same RPC streams.'
                }
                $observed = Get-JsonPointerValue (Get-JsonProperty $call 'result') (Get-JsonString $source 'valuePointer')
                if (-not (Test-JsonValueEqual $property.Value $observed)) {
                    throw 'Readback provenance value differs from the actual MCP result.'
                }
            }
        }
    }
}

function Get-StateProvenanceArtifactIds {
    param([System.Text.Json.JsonElement] $Root, [hashtable] $Artifacts)

    $state = Get-JsonProperty $Root 'stateSafety'
    $ids = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $ids.Add((Get-JsonString $state 'diffArtifactId')) | Out-Null
    $ids.Add((Get-JsonString $state 'restoreArtifactId')) | Out-Null
    $restore = Read-JsonArtifact $Artifacts (Get-JsonString $state 'restoreArtifactId') 'state restore'
    $provenance = Get-JsonProperty (Get-JsonProperty $restore 'readback') 'provenance'
    foreach ($phase in @('baseline', 'actual')) {
        foreach ($category in @('selection', 'state', 'focus')) {
            foreach ($source in (Get-JsonProperty (Get-JsonProperty $provenance $phase) $category).EnumerateObject()) {
                $ids.Add((Get-JsonString $source.Value 'callArtifactId')) | Out-Null
            }
        }
    }
    foreach ($id in @($ids)) {
        $raw = Get-JsonProperty (Read-JsonArtifact $Artifacts $id 'state provenance') 'rawRpc'
        $ids.Add((Get-JsonString $raw 'requestsArtifactId')) | Out-Null
        $ids.Add((Get-JsonString $raw 'responsesArtifactId')) | Out-Null
        $ids.Add((Get-JsonString $raw 'transportArtifactId')) | Out-Null
    }
    return $ids
}
