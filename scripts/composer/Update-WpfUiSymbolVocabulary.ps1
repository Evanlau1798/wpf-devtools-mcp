param(
    [string]$Repository = "lepoco/wpfui",
    [string]$Ref = "153baadeaf475875a81a4605881ca5c2dbe53972",
    [string]$OutputPath = "packs/builtin/wpfui/0.1.0/vocabularies/symbolRegular.json"
)

$ErrorActionPreference = "Stop"

$sourcePath = "src/Wpf.Ui/Controls/SymbolRegular.cs"
$source = gh api `
    -H "Accept: application/vnd.github.raw+json" `
    "repos/$Repository/contents/$sourcePath`?ref=$Ref" | Out-String
if ($LASTEXITCODE -ne 0) {
    throw "Unable to read $sourcePath from $Repository at $Ref."
}

$matches = [regex]::Matches(
    $source,
    '(?m)^\s*(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*=')
$names = @($matches | ForEach-Object { $_.Groups['name'].Value })
if ($names.Count -lt 1) {
    throw "No SymbolRegular enum values were found."
}

$unique = [System.Collections.Generic.HashSet[string]]::new(
    [System.StringComparer]::Ordinal)
foreach ($name in $names) {
    if (-not $unique.Add($name)) {
        throw "Duplicate SymbolRegular enum value: $name"
    }
}

$fullOutputPath = [System.IO.Path]::GetFullPath((Join-Path (Get-Location) $OutputPath))
$outputDirectory = [System.IO.Path]::GetDirectoryName($fullOutputPath)
[System.IO.Directory]::CreateDirectory($outputDirectory) | Out-Null
$items = $names | ForEach-Object { '"' + $_ + '"' }
$json = "[" + ($items -join ",") + "]`n"
[System.IO.File]::WriteAllText(
    $fullOutputPath,
    $json,
    [System.Text.UTF8Encoding]::new($false))

Write-Output "Generated $($names.Count) SymbolRegular values at $fullOutputPath"
