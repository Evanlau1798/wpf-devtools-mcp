using WpfDevTools.Tests.Unit;

namespace WpfDevTools.Tests.Unit.Release;

internal static class OnlineInstallerScriptTestHarness
{
    public static string BuildDefinitionOnlyPrelude(
        string dotSourceArguments,
        string? scriptPath = null,
        bool enableInternalTestMode = true)
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
$definitionsBaseRoot = Join-Path (Split-Path -Parent $sourceScriptRoot) 'tmp\online-installer-definitions'
$definitionsRoot = Join-Path $definitionsBaseRoot ([guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $definitionsRoot | Out-Null
$definitionsInstallerRoot = Join-Path $definitionsRoot 'installer'
New-Item -ItemType Directory -Force -Path $definitionsInstallerRoot | Out-Null
$releaseAssetModuleSource = Join-Path $sourceScriptRoot 'installer\online-installer.release-assets.ps1'
if (-not (Test-Path -LiteralPath $releaseAssetModuleSource -PathType Leaf)) {
    $releaseAssetModuleSource = Join-Path (Get-Location).Path 'scripts\installer\online-installer.release-assets.ps1'
}
if (Test-Path -LiteralPath $releaseAssetModuleSource -PathType Leaf) {
    Copy-Item -LiteralPath $releaseAssetModuleSource -Destination (Join-Path $definitionsInstallerRoot 'online-installer.release-assets.ps1') -Force
}
$definitionsPath = Join-Path $definitionsRoot 'online-installer-definitions.ps1'
$definitions = $scriptContent.Substring(0, $markerIndex)
{{BuildInternalTestModeReplacementLine(enableInternalTestMode)}}
$script:WpfDevToolsInstallerTestModeHarnessEnabled = ${{(enableInternalTestMode ? "true" : "false")}}
$script:WpfDevToolsInstallerTestModeEnabled = ${{(enableInternalTestMode ? "true" : "false")}}
try {
    Set-Content -LiteralPath $definitionsPath -Value $definitions -Encoding UTF8
    . $definitionsPath {{dotSourceArguments}}
    $script:WpfDevToolsInstallerTestModeEnabled = ${{(enableInternalTestMode ? "true" : "false")}}
    Set-Item -Path Function:\Test-InstallerTestModeEnabled -Value { return ${{(enableInternalTestMode ? "true" : "false")}} }
    $script:DefinitionOnlyInstallerScriptRoot = $sourceScriptRoot
    Set-Item -Path Function:\Resolve-InstallerScriptRoot -Value { $script:DefinitionOnlyInstallerScriptRoot }
}
finally {
    Remove-Item -LiteralPath $definitionsPath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $definitionsRoot -Recurse -Force -ErrorAction SilentlyContinue
}
""";
    }

    public static string EscapePowerShellSingleQuotedString(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.Replace("'", "''", StringComparison.Ordinal);
    }

    private static string BuildInternalTestModeReplacementLine(bool enableInternalTestMode)
    {
        return enableInternalTestMode
            ? "$definitions = $definitions.Replace('$script:WpfDevToolsInstallerTestModeEnabled = [bool]$script:WpfDevToolsInstallerTestModeEnabled -and [bool]$script:WpfDevToolsInstallerTestModeHarnessEnabled', '$script:WpfDevToolsInstallerTestModeEnabled = [bool]$script:WpfDevToolsInstallerTestModeEnabled -and [bool]$script:WpfDevToolsInstallerTestModeHarnessEnabled')"
            : "$definitions = $definitions.Replace('$script:WpfDevToolsInstallerTestModeEnabled = [bool]$script:WpfDevToolsInstallerTestModeEnabled -and [bool]$script:WpfDevToolsInstallerTestModeHarnessEnabled', '$script:WpfDevToolsInstallerTestModeEnabled = $false')";
    }
}
