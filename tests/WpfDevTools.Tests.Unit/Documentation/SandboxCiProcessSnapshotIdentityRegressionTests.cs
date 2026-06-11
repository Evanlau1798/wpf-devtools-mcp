using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed partial class SandboxCiScriptContractTests
{
    [Fact]
    public void SandboxArtifactPreflight_ProcessSnapshotExists_ShouldTolerateTickLevelCreationDateDrift()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var scriptPath = Path.Combine(RepoRoot, "scripts", "ci", "SandboxCi.ArtifactPreflight.ps1");
            var probeOutputPath = Path.Combine(tempRoot, "probe-output.txt");
            var command = $$"""
            $ErrorActionPreference = 'Stop'
            try {
                {{GetSmokeTargetFunctionBootstrap(scriptPath)}}
                $process = Start-Process powershell.exe -ArgumentList @('-NoProfile', '-Command', 'Start-Sleep -Seconds 30') -WindowStyle Hidden -PassThru
                try {
                    $snapshot = New-ProcessSnapshotFromProcess -Process $process
                    if ($null -eq $snapshot) {
                        throw 'Process snapshot was not captured.'
                    }

                    if (-not (Test-ProcessSnapshotExists -Snapshot $snapshot)) {
                        throw 'Process.StartTime snapshot did not match CIM creation time for a live process.'
                    }

                    $snapshot.CreationDateUtcTicks = [long]$snapshot.CreationDateUtcTicks + 1
                    if (-not (Test-ProcessSnapshotExists -Snapshot $snapshot)) {
                        throw 'Tick-level creation time drift made a live process snapshot look absent.'
                    }
                }
                finally {
                    if ($null -ne $process -and -not $process.HasExited) {
                        $process.Kill()
                        $process.WaitForExit(5000) | Out-Null
                    }

                    if ($null -ne $process) {
                        $process.Dispose()
                    }
                }
            }
            catch {
                [System.IO.File]::WriteAllText('{{EscapePowerShellPath(probeOutputPath)}}', "FAILED: $($_ | Out-String)")
                exit 1
            }

            [System.IO.File]::WriteAllText('{{EscapePowerShellPath(probeOutputPath)}}', 'PASS')
            """;
            var probePath = Path.Combine(tempRoot, "probe-process-snapshot-identity.ps1");
            File.WriteAllText(probePath, command);

            var result = RunPowerShellFileWithoutRedirect(probePath);

            var probeOutput = File.Exists(probeOutputPath) ? File.ReadAllText(probeOutputPath) : "";
            result.ExitCode.Should().Be(0, "PowerShell output: {0}", probeOutput);
        }
        finally
        {
            DeleteTempRootWithRetry(tempRoot);
        }
    }
}
