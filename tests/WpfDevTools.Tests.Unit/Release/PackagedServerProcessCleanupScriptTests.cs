using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class PackagedServerProcessCleanupScriptTests
{
    private static readonly string HelperPath = ReleaseScriptTestHarness.GetRepoFilePath(
        "scripts/tools/packaging/Test-PackagedServerProcessCleanup.ps1");

    [Fact]
    public void GetProcessDiagnostics_WhenProcessStillRuns_ShouldReturnWithoutDrainingStderr()
    {
        var result = RunHelperScript("""
            $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
            $startInfo.FileName = 'powershell.exe'
            $startInfo.Arguments = '-NoProfile -Command "[Console]::Error.WriteLine(''ready''); Start-Sleep -Seconds 10"'
            $startInfo.RedirectStandardError = $true
            $startInfo.UseShellExecute = $false
            $process = [System.Diagnostics.Process]::Start($startInfo)
            try {
                Start-Sleep -Milliseconds 500
                $watch = [System.Diagnostics.Stopwatch]::StartNew()
                $diagnostics = Get-ProcessDiagnostics -Process $process
                $watch.Stop()
                if ($watch.ElapsedMilliseconds -gt 1000) {
                    throw "Get-ProcessDiagnostics blocked for $($watch.ElapsedMilliseconds)ms"
                }
                if (-not $diagnostics.Contains('still running')) {
                    throw "Expected running-process diagnostic, got: $diagnostics"
                }
            }
            finally {
                if (-not $process.HasExited) {
                    $process.Kill()
                    $process.WaitForExit(5000) | Out-Null
                }
                $process.Dispose()
            }
            """);

        result.ExitCode.Should().Be(0, result.Output);
    }

    [Fact]
    public void StopPackagedServerProcess_WhenTaskKillCannotExitProcess_ShouldReturnFalse()
    {
        var result = RunHelperScript("""
            $fakeTaskKill = Join-Path $env:TEMP ('fake-taskkill-' + [guid]::NewGuid().ToString('N') + '.cmd')
            Set-Content -LiteralPath $fakeTaskKill -Value "@echo off`r`nexit /b 1" -Encoding ASCII
            $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
            $startInfo.FileName = 'powershell.exe'
            $startInfo.Arguments = '-NoProfile -Command "Start-Sleep -Seconds 10"'
            $startInfo.UseShellExecute = $false
            $process = [System.Diagnostics.Process]::Start($startInfo)
            try {
                $stopped = Stop-PackagedServerProcess -Process $process -TaskKillCommand $fakeTaskKill -WaitMilliseconds 50
                if ($stopped -ne $false) {
                    throw "Expected Stop-PackagedServerProcess to return false when taskkill fails"
                }
            }
            finally {
                Remove-Item -LiteralPath $fakeTaskKill -Force -ErrorAction SilentlyContinue
                if (-not $process.HasExited) {
                    $process.Kill()
                    $process.WaitForExit(5000) | Out-Null
                }
                $process.Dispose()
            }
            """);

        result.ExitCode.Should().Be(0, result.Output);
    }

    private static ScriptRunResult RunHelperScript(string body)
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var scriptPath = Path.Combine(tempRoot, "process-cleanup-probe.ps1");
            File.WriteAllText(scriptPath, CreateProbeScript(body));
            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                scriptPath,
                [],
                timeout: TimeSpan.FromSeconds(30));

            return new ScriptRunResult(result.ExitCode, result.Stdout + result.Stderr);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static string CreateProbeScript(string body) =>
        $$"""
        $ErrorActionPreference = 'Stop'
        . {{QuotePowerShellString(HelperPath)}}

        {{body}}
        """;

    private static string QuotePowerShellString(string value) =>
        "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private sealed record ScriptRunResult(int ExitCode, string Output);
}
