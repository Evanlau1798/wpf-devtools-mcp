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

    $interaction = Get-JsonProperty $Item 'interaction'
    if ((Get-JsonString $interaction 'transport') -cne 'mcp-native') {
        throw "Interactive control '$((Get-JsonString $Item 'id'))' must use MCP-native interaction."
    }
    foreach ($name in @('beforeArtifactId', 'actionArtifactId', 'afterArtifactId')) {
        Assert-ArtifactReference $interaction $name $Artifacts
    }
    Assert-TrueField $interaction 'semanticPostcondition' 'Interactive control proof'
}

function Assert-ControlBinding {
    param([System.Text.Json.JsonElement] $Item)

    $id = Get-JsonString $Item 'id'
    $kind = Get-JsonString $Item 'controlKind'
    $binding = Get-JsonProperty $Item 'binding'
    $bindingKind = Get-JsonString $binding 'kind'
    $actionKinds = @('Button', 'Menu', 'MenuItem', 'NavigationAction', 'Hyperlink')
    $selectorKinds = @('List', 'ListBox', 'ListView', 'DataGrid', 'ComboBox', 'Tab', 'TabControl')
    $valueKinds = @('TextBox', 'CheckBox', 'RadioButton', 'ToggleButton', 'Slider', 'DatePicker')

    if ($kind -in $actionKinds) {
        if ($bindingKind -cne 'command' -or
            -not (Get-JsonBoolean $binding 'commandBound') -or
            -not (Get-JsonBoolean $binding 'commandParameterBound')) {
            throw "Control '$id' command binding proof is incomplete."
        }
    }
    elseif ($kind -in $selectorKinds) {
        if ($bindingKind -cne 'selector' -or
            -not (Get-JsonBoolean $binding 'itemsSourceBound') -or
            -not (Get-JsonBoolean $binding 'selectionBound')) {
            throw "Control '$id' selector binding proof is incomplete."
        }
    }
    elseif ($kind -in $valueKinds) {
        if ($bindingKind -cne 'property' -or -not (Get-JsonBoolean $binding 'propertyBound')) {
            throw "Control '$id' property binding proof is incomplete."
        }
    }
    elseif ($kind -ceq 'ScrollViewer') {
        if ($bindingKind -cne 'native-state-only') {
            throw "ScrollViewer '$id' must use native-state-only binding evidence."
        }
        $interaction = Get-JsonProperty $Item 'interaction'
        if ((Get-JsonString $interaction 'beforeValue') -ceq (Get-JsonString $interaction 'afterValue')) {
            throw "ScrollViewer '$id' must prove an offset change."
        }
    }
    else {
        throw "Interactive control '$id' has unsupported controlKind '$kind'."
    }
}

function Assert-InteractiveEvidence {
    param([System.Text.Json.JsonElement] $Root, [hashtable] $Artifacts)

    $interactive = Get-JsonProperty $Root 'interactive'
    $eligible = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $sawSelector = $false
    $sawAction = $false
    foreach ($checkpoint in (Get-JsonArray $interactive 'checkpoints').EnumerateArray()) {
        Get-JsonString $checkpoint 'name' | Out-Null
        foreach ($control in (Get-JsonArray $checkpoint 'controls').EnumerateArray()) {
            $id = Get-JsonString $control 'id'
            $origin = Get-JsonString $control 'origin'
            $visible = Get-JsonBoolean $control 'visible'
            $enabled = Get-JsonBoolean $control 'enabled'
            $hitTestable = Get-JsonBoolean $control 'hitTestable'
            $loaded = Get-JsonBoolean $control 'loaded'
            $isEligible = $origin -ceq 'app-authored' -and $visible -and $enabled -and $hitTestable -and $loaded
            if ($isEligible) {
                $identity = Get-JsonString $control 'identityKind'
                if ($identity -notin @('x:Name', 'composer-correlation')) {
                    throw "Eligible control '$id' lacks a stable app-authored identity."
                }
                $eligible.Add($id) | Out-Null
            }
            else {
                Assert-ExcludedControl $control $Artifacts
            }
        }
    }

    $inventoryIds = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    foreach ($item in (Get-JsonArray $interactive 'inventory').EnumerateArray()) {
        $id = Get-JsonString $item 'id'
        if (-not $inventoryIds.Add($id)) {
            throw "Interactive inventory contains duplicate control '$id'."
        }
        if (-not $eligible.Contains($id)) {
            throw "Inventory control '$id' is not eligible in any checkpoint."
        }

        Assert-ControlBinding $item
        Assert-InteractionProof $item $Artifacts
        $kind = Get-JsonString $item 'controlKind'
        $sawSelector = $sawSelector -or $kind -in @('List', 'ListBox', 'ListView', 'DataGrid', 'ComboBox', 'Tab', 'TabControl')
        $sawAction = $sawAction -or $kind -in @('Button', 'Menu', 'MenuItem', 'NavigationAction', 'Hyperlink')
    }

    foreach ($id in $eligible) {
        if (-not $inventoryIds.Contains($id)) {
            throw "eligible control '$id' is missing from the interactive inventory."
        }
    }
    if (-not $sawSelector -or -not $sawAction) {
        throw 'Core user journey requires a selector and a bound primary action.'
    }
}
