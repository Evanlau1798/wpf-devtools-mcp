Set-StrictMode -Version Latest

function Assert-ExcludedControl {
    param([System.Text.Json.JsonElement] $Control, [hashtable] $Artifacts)

    $origin = Get-JsonString $Control 'origin'
    $reason = Get-JsonString $Control 'exclusionReason'
    $allowedReason = if ($origin -ceq 'template-generated') {
        'template-generated'
    }
    elseif ($origin -ceq 'os-chrome') {
        'os-chrome'
    }
    elseif (-not (Get-JsonBoolean $Control 'visible')) {
        'hidden'
    }
    elseif (-not (Get-JsonBoolean $Control 'enabled')) {
        'disabled'
    }
    elseif (-not (Get-JsonBoolean $Control 'hitTestable')) {
        'non-hit-testable'
    }
    else {
        $null
    }

    if ($null -eq $allowedReason -or $reason -cne $allowedReason) {
        throw "Control exclusion '$reason' is not allowed by its runtime evidence."
    }
    Assert-ArtifactReference $Control 'exclusionEvidenceArtifactId' $Artifacts
}

function Assert-InteractionProof {
    param([System.Text.Json.JsonElement] $Item, [hashtable] $Artifacts)

    $id = Get-JsonString $Item 'id'
    $kind = Get-JsonString $Item 'controlKind'
    $interaction = Get-JsonProperty $Item 'interaction'
    foreach ($name in @('beforeArtifactId', 'actionArtifactId', 'afterArtifactId')) {
        Assert-ArtifactReference $interaction $name $Artifacts
    }

    $before = Find-UniqueEvidenceItem (
        Read-JsonArtifact $Artifacts (Get-JsonString $interaction 'beforeArtifactId') 'interaction before') 'controls' $id
    $after = Find-UniqueEvidenceItem (
        Read-JsonArtifact $Artifacts (Get-JsonString $interaction 'afterArtifactId') 'interaction after') 'controls' $id
    $action = Find-UniqueEvidenceItem (
        Read-JsonArtifact $Artifacts (Get-JsonString $interaction 'actionArtifactId') 'interaction action') 'actions' $id
    foreach ($state in @($before, $after)) {
        if ((Get-JsonString $state 'controlKind') -cne $kind) {
            throw "Interactive control '$id' controlKind does not match its runtime state evidence."
        }
    }
    if ((Get-JsonString $action 'transport') -cne 'mcp-native') {
        throw "Interactive control '$id' must use MCP-native interaction."
    }
    Assert-SuccessfulToolResult $action "interactive control '$id'" | Out-Null
    $beforeState = Get-JsonProperty $before 'state'
    $afterState = Get-JsonProperty $after 'state'
    if ($beforeState.GetRawText() -ceq $afterState.GetRawText()) {
        throw "Interactive control '$id' did not prove a runtime state change."
    }

    if ($kind -in @('List', 'ListBox', 'ListView', 'DataGrid', 'ComboBox', 'Tab', 'TabControl')) {
        Get-JsonString $afterState 'viewModelValue' | Out-Null
    }
    elseif ($kind -in @('Button', 'Menu', 'MenuItem', 'NavigationAction', 'Hyperlink')) {
        Get-JsonString $afterState 'visibleFeedback' | Out-Null
        Get-JsonString $afterState 'viewModelValue' | Out-Null
    }
    elseif ($kind -ceq 'ScrollViewer') {
        Get-JsonProperty $beforeState 'nativeValue' | Out-Null
        Get-JsonProperty $afterState 'nativeValue' | Out-Null
    }
}

function Assert-ControlBinding {
    param([System.Text.Json.JsonElement] $Item, [hashtable] $Artifacts)

    $id = Get-JsonString $Item 'id'
    $kind = Get-JsonString $Item 'controlKind'
    $binding = Get-JsonProperty $Item 'binding'
    $bindingKind = Get-JsonString $binding 'kind'
    $actionKinds = @('Button', 'Menu', 'MenuItem', 'NavigationAction', 'Hyperlink')
    $selectorKinds = @('List', 'ListBox', 'ListView', 'DataGrid', 'ComboBox', 'Tab', 'TabControl')
    $valueProperties = @{
        TextBox = 'Text'
        CheckBox = 'IsChecked'
        RadioButton = 'IsChecked'
        ToggleButton = 'IsChecked'
        Slider = 'Value'
        DatePicker = 'SelectedDate'
    }
    $requiredProperties = $null

    if ($kind -in $actionKinds) {
        $requiredProperties = @('Command', 'CommandParameter')
        if ($bindingKind -cne 'command') { throw "Control '$id' command binding proof is incomplete." }
    }
    elseif ($kind -in $selectorKinds) {
        $requiredProperties = @('ItemsSource', 'SelectedItem')
        if ($bindingKind -cne 'selector') { throw "Control '$id' selector binding proof is incomplete." }
    }
    elseif ($valueProperties.ContainsKey($kind)) {
        $requiredProperties = @($valueProperties[$kind])
        if ($bindingKind -cne 'property') { throw "Control '$id' property binding proof is incomplete." }
    }
    elseif ($kind -ceq 'ScrollViewer') {
        if ($bindingKind -cne 'native-state-only') {
            throw "ScrollViewer '$id' must use native-state-only binding evidence."
        }
    }
    else {
        throw "Interactive control '$id' has unsupported controlKind '$kind'."
    }

    if ($null -ne $requiredProperties) {
        $evidence = Read-JsonArtifact $Artifacts (Get-JsonString $binding 'artifactId') "binding for '$id'"
        if ((Get-JsonString $evidence 'controlId') -cne $id -or
            (Get-JsonString $evidence 'controlKind') -cne $kind) {
            throw "Control '$id' binding evidence identity does not match the inventory."
        }
        $active = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($entry in (Get-JsonArray $evidence 'bindings').EnumerateArray()) {
            $property = Get-JsonString $entry 'property'
            if (-not $active.Add($property) -or (Get-JsonString $entry 'status') -cne 'Active') {
                throw "Control '$id' binding evidence contains a duplicate or inactive binding."
            }
        }
        foreach ($property in $requiredProperties) {
            if (-not $active.Contains($property)) {
                throw "Control '$id' binding proof is missing active '$property'."
            }
        }
    }
}

function Find-UniqueEvidenceItem {
    param([System.Text.Json.JsonElement] $Evidence, [string] $ArrayName, [string] $Id)

    $matches = @((Get-JsonArray $Evidence $ArrayName).EnumerateArray() |
        Where-Object { (Get-JsonString $_ 'id') -ceq $Id })
    if ($matches.Count -ne 1) {
        throw "Runtime interaction evidence must contain exactly one entry for '$Id'."
    }
    return $matches[0]
}

function Add-CheckpointControls {
    param(
        [System.Text.Json.JsonElement] $Checkpoints,
        [hashtable] $Controls,
        [hashtable] $Artifacts,
        [string] $Source
    )

    foreach ($checkpoint in $Checkpoints.EnumerateArray()) {
        Get-JsonString $checkpoint 'name' | Out-Null
        $checkpointIds = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($control in (Get-JsonArray $checkpoint 'controls').EnumerateArray()) {
            $id = Get-JsonString $control 'id'
            if (-not $checkpointIds.Add($id)) {
                throw "$Source contains duplicate stable identity '$id' in one checkpoint."
            }
            $origin = Get-JsonString $control 'origin'
            $eligible = $origin -ceq 'app-authored' -and
                (Get-JsonBoolean $control 'visible') -and (Get-JsonBoolean $control 'enabled') -and
                (Get-JsonBoolean $control 'hitTestable') -and (Get-JsonBoolean $control 'loaded')
            if (-not $eligible) {
                Assert-ExcludedControl $control $Artifacts
                continue
            }
            if ((Get-JsonString $control 'identityKind') -notin @('x:Name', 'composer-correlation')) {
                throw "Eligible control '$id' lacks a stable app-authored identity."
            }
            $kind = Get-JsonString $control 'controlKind'
            if ($Controls.ContainsKey($id) -and $Controls[$id] -cne $kind) {
                throw "Eligible control '$id' has inconsistent controlKind evidence."
            }
            $Controls[$id] = $kind
        }
    }
}

function Assert-InteractiveEvidence {
    param([System.Text.Json.JsonElement] $Root, [hashtable] $Artifacts)

    $interactive = Get-JsonProperty $Root 'interactive'
    $runtime = Read-JsonArtifact $Artifacts (Get-JsonString $interactive 'runtimeInventoryArtifactId') 'runtime inventory'
    $runtimeEligible = @{}
    Add-CheckpointControls (Get-JsonArray $runtime 'checkpoints') $runtimeEligible $Artifacts 'Runtime inventory'
    $eligible = @{}
    Add-CheckpointControls (Get-JsonArray $interactive 'checkpoints') $eligible $Artifacts 'Manifest checkpoints'
    if ($eligible.Count -ne $runtimeEligible.Count) {
        throw 'Manifest checkpoints do not match the hashed runtime inventory.'
    }
    foreach ($id in $runtimeEligible.Keys) {
        if (-not $eligible.ContainsKey($id) -or $eligible[$id] -cne $runtimeEligible[$id]) {
            throw "Control '$id' is missing from or inconsistent with the hashed runtime inventory."
        }
    }
    $sawSelector = $false
    $sawAction = $false

    $inventoryIds = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($item in (Get-JsonArray $interactive 'inventory').EnumerateArray()) {
        $id = Get-JsonString $item 'id'
        if (-not $inventoryIds.Add($id)) {
            throw "Interactive inventory contains duplicate control '$id'."
        }
        if (-not $eligible.ContainsKey($id)) {
            throw "Inventory control '$id' is not eligible in any checkpoint."
        }
        $kind = Get-JsonString $item 'controlKind'
        if ($eligible[$id] -cne $kind) {
            throw "Inventory control '$id' controlKind does not match checkpoint evidence."
        }

        Assert-ControlBinding $item $Artifacts
        Assert-InteractionProof $item $Artifacts
        $sawSelector = $sawSelector -or $kind -in @('List', 'ListBox', 'ListView', 'DataGrid', 'ComboBox', 'Tab', 'TabControl')
        $sawAction = $sawAction -or $kind -in @('Button', 'Menu', 'MenuItem', 'NavigationAction', 'Hyperlink')
    }

    foreach ($id in $eligible.Keys) {
        if (-not $inventoryIds.Contains($id)) {
            throw "eligible control '$id' is missing from the interactive inventory."
        }
    }
    if (-not $sawSelector -or -not $sawAction) {
        throw 'Core user journey requires a selector and a bound primary action.'
    }
}
