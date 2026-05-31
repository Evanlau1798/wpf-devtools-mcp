using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed partial class SandboxCiScriptContractTests
{
    [Fact]
    public void SandboxManagedScript_UnitDebugSingleShard_ShouldRunUnderStrictMode()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var scriptPath = Path.Combine(RepoRoot, "scripts", "ci", "SandboxCi.Managed.ps1");
            var logPath = Path.Combine(tempRoot, "single-shard.log");
            var command = $$"""
            $ErrorActionPreference = 'Stop'
            Set-StrictMode -Version Latest
            . '{{EscapePowerShellPath(scriptPath)}}'
            $script:MappedOutputRoot = '{{EscapePowerShellPath(tempRoot)}}'
            $script:timestamp = 'single-shard-contract'
            function Invoke-ExternalWithTimeout {
                param(
                    [string]$Name,
                    [string]$FilePath,
                    [string[]]$Arguments,
                    [int]$TimeoutSeconds,
                    [string]$OutputRoot,
                    [string]$Timestamp
                )

                [System.IO.File]::AppendAllText(
                    '{{EscapePowerShellPath(logPath)}}',
                    ($Name + '|' + $FilePath + '|' + $TimeoutSeconds + '|' + $Timestamp + "`n"))
            }
            Invoke-UnitDebugTests `
                -DotNetPath 'DOTNET-STUB' `
                -ResultsRoot '{{EscapePowerShellPath(tempRoot)}}' `
                -MaxParallelLanes 2 `
                -UnitDebugShardCount 1
            """;

            var result = RunPowerShell(command);

            result.ExitCode.Should().Be(0, result.Output);
            File.ReadAllText(logPath).Should().Contain("Run unit tests Debug|DOTNET-STUB|3600|single-shard-contract");
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void SandboxManagedScript_ManagedReleaseUnitSingleShard_ShouldRunUnderStrictMode()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var scriptPath = Path.Combine(RepoRoot, "scripts", "ci", "SandboxCi.Managed.ps1");
            var logPath = Path.Combine(tempRoot, "single-release-shard.log");
            var command = $$"""
            $ErrorActionPreference = 'Stop'
            Set-StrictMode -Version Latest
            . '{{EscapePowerShellPath(scriptPath)}}'
            $script:MappedOutputRoot = '{{EscapePowerShellPath(tempRoot)}}'
            $script:timestamp = 'single-release-shard-contract'
            function Invoke-ExternalWithTimeout {
                param(
                    [string]$Name,
                    [string]$FilePath,
                    [string[]]$Arguments,
                    [int]$TimeoutSeconds,
                    [string]$OutputRoot,
                    [string]$Timestamp
                )

                [System.IO.File]::AppendAllText(
                    '{{EscapePowerShellPath(logPath)}}',
                    ($Name + '|' + $FilePath + '|' + $TimeoutSeconds + '|' + $Timestamp + "`n"))
            }
            Invoke-ManagedTestLanes `
                -DotNetPath 'DOTNET-STUB' `
                -ResultsRoot '{{EscapePowerShellPath(tempRoot)}}' `
                -MaxParallelLanes 2 `
                -ReleaseUnitShardCount 1 `
                -IncludeReleaseUnit
            """;

            var result = RunPowerShell(command);

            result.ExitCode.Should().Be(0, result.Output);
            File.ReadAllText(logPath).Should().Contain("Run release unit tests Debug|DOTNET-STUB|3600|single-release-shard-contract");
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }
}
