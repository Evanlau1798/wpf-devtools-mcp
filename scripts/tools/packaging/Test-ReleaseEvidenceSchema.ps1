param(
    [Parameter(Mandatory)] [string]$EvidencePath,
    [string]$SchemaPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Get-JsonProperty {
    param(
        [Parameter(Mandatory)] [object]$Object,
        [Parameter(Mandatory)] [string]$Name
    )

    return $Object.PSObject.Properties[$Name]
}

function Get-SchemaTypeNames {
    param([Parameter(Mandatory)] [object]$Schema)

    $typeProperty = Get-JsonProperty -Object $Schema -Name 'type'
    if ($null -eq $typeProperty) {
        return @()
    }

    if ($typeProperty.Value -is [object[]]) {
        return @($typeProperty.Value | ForEach-Object { [string]$_ })
    }

    return @([string]$typeProperty.Value)
}

function Test-JsonType {
    param(
        [object]$Value,
        [Parameter(Mandatory)] [string[]]$ExpectedTypes
    )

    $expectedTypeValues = @($ExpectedTypes)
    if ($expectedTypeValues.Count -eq 0 -or ($expectedTypeValues -contains 'null' -and $null -eq $Value)) {
        return $true
    }

    foreach ($type in $expectedTypeValues) {
        switch ($type) {
            'object' { if ($null -ne $Value -and $Value -isnot [string] -and @($Value.PSObject.Properties).Count -gt 0) { return $true } }
            'array' { if ($Value -is [object[]]) { return $true } }
            'string' { if ($Value -is [string]) { return $true } }
            'integer' { if ($Value -is [int] -or $Value -is [long]) { return $true } }
            'boolean' { if ($Value -is [bool]) { return $true } }
            'number' { if ($Value -is [int] -or $Value -is [long] -or $Value -is [double] -or $Value -is [decimal]) { return $true } }
        }
    }

    return $false
}

function Test-JsonAgainstSchema {
    param(
        [object]$Value,
        [Parameter(Mandatory)] [object]$Schema,
        [Parameter(Mandatory)] [string]$Path,
        [System.Collections.Generic.List[string]]$Errors
    )

    $expectedTypes = Get-SchemaTypeNames -Schema $Schema
    if (-not (Test-JsonType -Value $Value -ExpectedTypes $expectedTypes)) {
        $Errors.Add("$Path has an unexpected JSON type.")
        return
    }

    if ($expectedTypes -contains 'object') {
        $requiredProperty = Get-JsonProperty -Object $Schema -Name 'required'
        if ($null -ne $requiredProperty) {
            foreach ($name in @($requiredProperty.Value)) {
                if ($null -eq (Get-JsonProperty -Object $Value -Name ([string]$name))) {
                    $Errors.Add("$Path.$name is required.")
                }
            }
        }

        $propertiesProperty = Get-JsonProperty -Object $Schema -Name 'properties'
        if ($null -ne $propertiesProperty) {
            foreach ($schemaProperty in $propertiesProperty.Value.PSObject.Properties) {
                $actualProperty = Get-JsonProperty -Object $Value -Name $schemaProperty.Name
                if ($null -ne $actualProperty) {
                    Test-JsonAgainstSchema -Value $actualProperty.Value -Schema $schemaProperty.Value -Path "$Path.$($schemaProperty.Name)" -Errors $Errors
                }
            }
        }
    }
    elseif ($expectedTypes -contains 'array') {
        $itemsProperty = Get-JsonProperty -Object $Schema -Name 'items'
        if ($null -ne $itemsProperty) {
            $index = 0
            foreach ($item in @($Value)) {
                Test-JsonAgainstSchema -Value $item -Schema $itemsProperty.Value -Path "$Path[$index]" -Errors $Errors
                $index++
            }
        }
    }
}

if ([string]::IsNullOrWhiteSpace($SchemaPath)) {
    $SchemaPath = Join-Path $PSScriptRoot 'release-evidence.schema.json'
}

if (-not (Test-Path -LiteralPath $EvidencePath -PathType Leaf)) {
    throw "Release evidence JSON is missing: $EvidencePath"
}

if (-not (Test-Path -LiteralPath $SchemaPath -PathType Leaf)) {
    throw "Release evidence schema JSON is missing: $SchemaPath"
}

$evidence = Get-Content -LiteralPath $EvidencePath -Raw | ConvertFrom-Json
$schema = Get-Content -LiteralPath $SchemaPath -Raw | ConvertFrom-Json
$errors = [System.Collections.Generic.List[string]]::new()
Test-JsonAgainstSchema -Value $evidence -Schema $schema -Path '$' -Errors $errors
if ($errors.Count -gt 0) {
    throw "Release evidence schema validation failed: $($errors -join '; ')"
}

Write-Host "Release evidence schema validation passed: $EvidencePath"
