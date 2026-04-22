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
$definitionsPath = Join-Path (Split-Path -Parent $scriptPath) ('.online-installer-definitions-' + [guid]::NewGuid().ToString('N') + '.ps1')
$definitions = $scriptContent.Substring(0, $markerIndex)
try {
    Set-Content -LiteralPath $definitionsPath -Value $definitions -Encoding UTF8
    . $definitionsPath {{dotSourceArguments}}
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