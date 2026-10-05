Set-StrictMode -Version Latest

function Test-JsonValueEqual([System.Text.Json.JsonElement] $Left, [System.Text.Json.JsonElement] $Right) {
    return [System.Text.Json.Nodes.JsonNode]::DeepEquals(
        [System.Text.Json.Nodes.JsonNode]::Parse($Left.GetRawText()),
        [System.Text.Json.Nodes.JsonNode]::Parse($Right.GetRawText()))
}

function Assert-StateSafetyResults {
    param([System.Text.Json.JsonElement] $Diff, [System.Text.Json.JsonElement] $Restore, [hashtable] $Artifacts)

    $diffContent = Get-JsonProperty (Assert-SuccessfulToolResult $Diff 'state diff') 'structuredContent'
    $restoreContent = Get-JsonProperty (Assert-SuccessfulToolResult $Restore 'state restore') 'structuredContent'
    $cache = @{}
    $diffProof = Assert-McpCallProvenance $Diff $Artifacts $cache 'get_state_diff'
    $restoreProof = Assert-McpCallProvenance $Restore $Artifacts $cache 'restore_state_snapshot'
    $snapshotId = Get-JsonString (Get-JsonProperty (Get-JsonProperty $diffProof.Request 'params') 'arguments') 'snapshotId'
    $restoreSnapshotId = Get-JsonString (Get-JsonProperty (Get-JsonProperty $restoreProof.Request 'params') 'arguments') 'snapshotId'
    if ($snapshotId -cne $restoreSnapshotId -or (Get-JsonString $diffContent 'snapshotId') -cne $snapshotId -or
        $diffProof.RequestsArtifactId -cne $restoreProof.RequestsArtifactId -or
        $diffProof.ResponsesArtifactId -cne $restoreProof.ResponsesArtifactId -or
        $diffProof.TransportArtifactId -cne $restoreProof.TransportArtifactId -or
        $diffProof.ReceivedSequence -ge $restoreProof.SentSequence -or
        $diffProof.RequestIndex -ge $restoreProof.RequestIndex -or $diffProof.ResponseIndex -ge $restoreProof.ResponseIndex) {
        throw 'State restore must follow the diff for the same snapshot and RPC streams.'
    }
    $changes = 0
    foreach ($group in @(
            @{ diff = 'propertyChanges'; singular = 'DependencyProperty'; plural = 'DependencyProperties' },
            @{ diff = 'viewModelChanges'; singular = 'ViewModelProperty'; plural = 'ViewModelProperties' })) {
        $entries = @()
        foreach ($disposition in @('restored', 'skipped')) {
            $array = Get-JsonArray $restoreContent ("$disposition$($group.plural)")
            if ((Get-JsonInteger $restoreContent ("$disposition$($group.singular)Count")) -ne $array.GetArrayLength()) {
                throw 'State restore counts must match verified property entries.'
            }
            $entries += @($array.EnumerateArray())
        }
        foreach ($entry in $entries) {
            Assert-TrueField $entry 'verified' 'State restore'
            Get-JsonString $entry 'propertyName' | Out-Null
            if (-not (Test-JsonValueEqual (Get-JsonProperty $entry 'expectedValue') (Get-JsonProperty $entry 'currentValue'))) {
                throw 'State restore expected and current values differ.'
            }
        }
        foreach ($change in (Get-JsonArray $diffContent $group.diff).EnumerateArray()) {
            $property = Get-JsonString $change 'propertyName'
            $before = Get-JsonProperty $change 'beforeValue'
            if (Test-JsonValueEqual $before (Get-JsonProperty $change 'afterValue')) {
                throw 'State diff entry does not prove a runtime change.'
            }
            $matching = @($entries | Where-Object {
                (Get-JsonString $_ 'propertyName') -ceq $property -and
                (Test-JsonValueEqual $before (Get-JsonProperty $_ 'expectedValue'))
            })
            if ($matching.Count -ne 1) {
                throw "State restore must verify the changed property '$property' against its baseline."
            }
            $changes++
        }
    }
    if ($changes -eq 0) { throw 'State diff must prove at least one runtime change.' }
    Assert-TrueField $restoreContent 'restoredFocus' 'State restore proof'
    if ((Get-JsonArray $restoreContent 'warnings').GetArrayLength() -ne 0) {
        throw 'State restore must have no unresolved warnings.'
    }

    $readback = Get-JsonProperty $Restore 'readback'
    Assert-TrueField $readback 'matchesBaseline' 'State restore readback'
    $baseline = Get-JsonProperty $readback 'baseline'
    $actual = Get-JsonProperty $readback 'actual'
    foreach ($category in @('selection', 'state', 'focus')) {
        $expected = Get-JsonProperty $baseline $category
        $current = Get-JsonProperty $actual $category
        if ($expected.ValueKind -ne [System.Text.Json.JsonValueKind]::Object -or
            @($expected.EnumerateObject()).Count -eq 0 -or
            -not (Test-JsonValueEqual $expected $current)) {
            throw "State restore readback '$category' must contain matching nonempty measured values."
        }
    }
    Assert-ReadbackProvenance $readback $Artifacts $cache $diffProof $restoreProof
}
