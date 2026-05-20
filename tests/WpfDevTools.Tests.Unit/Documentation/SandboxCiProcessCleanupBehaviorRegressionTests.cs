using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed partial class SandboxCiScriptContractTests
{
    [Fact]
    public void SandboxProcessCleanup_GetMatchingProcessFromSnapshot_ShouldRejectSamePidWithWrongCreationTicks()
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
                $process = Start-Process powershell.exe -ArgumentList @('-NoProfile', '-Command', 'Start-Sleep -Seconds 30') -PassThru
                try {
                    $snapshot = New-ProcessSnapshotFromProcess -Process $process
                    if ($null -eq $snapshot) { throw 'Process snapshot was not captured.' }

                    $wrongSnapshot = [pscustomobject]@{
                        ProcessId = $process.Id
                        CreationDateUtcTicks = [long]$snapshot.CreationDateUtcTicks - [TimeSpan]::FromSeconds(2).Ticks
                        DescendantCutoffUtcTicks = [DateTime]::UtcNow.Ticks
                    }
                    $match = Get-MatchingProcessFromSnapshot -Snapshot $wrongSnapshot
                    if ($null -ne $match) {
                        $match.Dispose()
                        throw 'Mismatched creation ticks returned a live process match.'
                    }
                    if ($process.HasExited) { throw 'Identity rejection should not terminate the live process.' }
                }
                finally {
                    if ($null -ne $process -and -not $process.HasExited) {
                        $process.Kill()
                        $process.WaitForExit(5000) | Out-Null
                    }
                    if ($null -ne $process) { $process.Dispose() }
                }
            }
            catch {
                [System.IO.File]::WriteAllText('{{EscapePowerShellPath(probeOutputPath)}}', "FAILED: $($_ | Out-String)")
                exit 1
            }

            [System.IO.File]::WriteAllText('{{EscapePowerShellPath(probeOutputPath)}}', 'PASS')
            """;
            var probePath = Path.Combine(tempRoot, "probe-matching-process-identity.ps1");
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

    [Fact]
    public void SandboxProcessCleanup_ShouldDiscoverLateDescendantFromStoppedNonRootSnapshot()
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
                $script:alive = @{ 111 = $true; 222 = $false }
                $script:exitTimes = @{}
                $script:lateCreationTicks = 0L
                $script:lateWasScanned = $false
                $script:stopped = @()

                function Test-ProcessSnapshotExists {
                    param([object]$Snapshot)
                    return [bool]$script:alive[[int]$Snapshot.ProcessId]
                }
                function Get-DescendantProcessSnapshots {
                    param([int]$ParentProcessId, [long]$CreationCutoffUtcTicks = [long]::MaxValue, [int[]]$VisitedProcessIds = @())
                    if ($ParentProcessId -ne 111 -or -not $script:alive[222] -or $script:lateCreationTicks -le 0) { return @() }
                    if ($script:lateCreationTicks -gt $CreationCutoffUtcTicks) { return @() }
                    $script:lateWasScanned = $true
                    return [pscustomobject]@{ ProcessId = 222; CreationDateUtcTicks = $script:lateCreationTicks; DescendantCutoffUtcTicks = 1 }
                }
                function Get-MatchingProcessFromSnapshot {
                    param([object]$Snapshot)
                    if (-not (Test-ProcessSnapshotExists -Snapshot $Snapshot)) { return $null }
                    $process = [pscustomobject]@{}
                    $process | Add-Member NoteProperty ProcessId ([int]$Snapshot.ProcessId)
                    $process | Add-Member ScriptProperty HasExited { -not [bool]$script:alive[[int]$this.ProcessId] }
                    $process | Add-Member ScriptProperty ExitTime {
                        if ($script:exitTimes.ContainsKey([int]$this.ProcessId)) { return $script:exitTimes[[int]$this.ProcessId] }
                        return [DateTime]::UtcNow
                    }
                    $process | Add-Member ScriptMethod Kill {
                        if ($this.ProcessId -eq 111) {
                            $script:lateCreationTicks = [DateTime]::UtcNow.Ticks
                            $script:alive[222] = $true
                        }
                        $script:stopped += [int]$this.ProcessId
                        $script:alive[[int]$this.ProcessId] = $false
                        $script:exitTimes[[int]$this.ProcessId] = [DateTime]::UtcNow.AddMilliseconds(20)
                    }
                    $process | Add-Member ScriptMethod WaitForExit { return $true }
                    $process | Add-Member ScriptMethod Dispose { }
                    return $process
                }

                $root = [pscustomobject]@{ ProcessId = 999; CreationDateUtcTicks = 1; DescendantCutoffUtcTicks = [DateTime]::UtcNow.Ticks }
                $child = [pscustomobject]@{ ProcessId = 111; CreationDateUtcTicks = 1; DescendantCutoffUtcTicks = 1 }
                Stop-ProcessSnapshots -Snapshots @($child) -ScanRoots @($root)
                if (-not $script:lateWasScanned) { throw 'Late descendant was not discovered from the refreshed child cutoff.' }
                if ($script:stopped -notcontains 222) { throw "Late descendant was not stopped. Stopped: $($script:stopped -join ', ')" }
            }
            catch {
                [System.IO.File]::WriteAllText('{{EscapePowerShellPath(probeOutputPath)}}', "FAILED: $($_ | Out-String)")
                exit 1
            }

            [System.IO.File]::WriteAllText('{{EscapePowerShellPath(probeOutputPath)}}', 'PASS')
            """;
            var probePath = Path.Combine(tempRoot, "probe-non-root-late-descendant.ps1");
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
