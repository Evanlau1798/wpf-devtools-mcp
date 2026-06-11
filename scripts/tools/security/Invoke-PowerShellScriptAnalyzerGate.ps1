param(
    [string]$Path = 'scripts',

    [ValidateSet('Error', 'Warning', 'Information')]
    [string]$Severity = 'Error'
)

$ErrorActionPreference = 'Stop'

Import-Module PSScriptAnalyzer -MinimumVersion 1.25.0 -ErrorAction Stop

$results = @(Invoke-ScriptAnalyzer -Path $Path -Recurse -Severity $Severity)
if ($results.Count -eq 0) {
    return
}

foreach ($result in $results) {
    $line = if ($result.Line -gt 0) { $result.Line } else { 1 }
    [Console]::Error.WriteLine("$($result.ScriptName):$line $($result.RuleName) [$($result.Severity)] $($result.Message)")
}

exit 1
