using WpfDevTools.Tests.Unit;

namespace WpfDevTools.Tests.Unit.Release;

internal static class OnlineInstallerScriptTestHarness
{
    public static string BuildDefinitionOnlyPrelude(string dotSourceArguments, string? scriptPath = null)
    {
        ArgumentNullException.ThrowIfNull(dotSourceArguments);
        scriptPath ??= ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1");

        return $$"""
$scriptPath='{{EscapePowerShellSingleQuotedString(scriptPath)}}'
$scriptContent = Get-Content -LiteralPath $scriptPath -Raw
$marker = '{{EscapePowerShellSingleQuotedString(TestHelpers.OnlineInstallerDefinitionBoundaryMarker)}}'
$markerIndex = $scriptContent.IndexOf($marker, [System.StringComparison]::Ordinal)
if ($markerIndex -lt 0) { throw 'Main script boundary marker not found.' }
$sourceScriptRoot = Split-Path -Parent $scriptPath
$definitionsRoot = Join-Path (Split-Path -Parent $sourceScriptRoot) 'tmp\online-installer-definitions'
New-Item -ItemType Directory -Force -Path $definitionsRoot | Out-Null
$definitionsPath = Join-Path $definitionsRoot ('online-installer-definitions-' + [guid]::NewGuid().ToString('N') + '.ps1')
$definitions = $scriptContent.Substring(0, $markerIndex)
try {
    Set-Content -LiteralPath $definitionsPath -Value $definitions -Encoding UTF8
    . $definitionsPath {{dotSourceArguments}}
    $script:DefinitionOnlyInstallerScriptRoot = $sourceScriptRoot
    Set-Item -Path Function:\Resolve-InstallerScriptRoot -Value { $script:DefinitionOnlyInstallerScriptRoot }
}
finally {
    Remove-Item -LiteralPath $definitionsPath -Force -ErrorAction SilentlyContinue
}
""";
    }

    public static string EscapePowerShellSingleQuotedString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Replace("'", "''", StringComparison.Ordinal);
    }
}
