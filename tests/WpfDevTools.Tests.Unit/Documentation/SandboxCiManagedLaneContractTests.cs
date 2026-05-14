using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed partial class SandboxCiScriptContractTests
{
    [Fact]
    public void InvokeUnitDebugTests_ShouldRunFourShardsUnderStrictMode()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var managedScript = Path.Combine(RepoRoot, "scripts", "ci", "SandboxCi.Managed.ps1");
            var command = CreateManagedLaneHarness(
                managedScript,
                tempRoot,
                "strict-unit-debug",
                """
                Invoke-UnitDebugTests `
                    -DotNetPath 'dotnet.exe' `
                    -ResultsRoot '{{TEMP_ROOT}}' `
                    -Configuration Debug `
                    -MaxParallelLanes 4 `
                    -UnitDebugShardCount 4
                """);

            var result = RunPowerShell(command);

            result.ExitCode.Should().Be(0, result.Output);
            result.Output.Should().Contain("lanes=4;commands=4");
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void InvokeManagedTestLanes_ShouldNotCapReleaseOnlyShardsWhenUnitDebugShardCountIsConfigured()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var managedScript = Path.Combine(RepoRoot, "scripts", "ci", "SandboxCi.Managed.ps1");
            var command = CreateManagedLaneHarness(
                managedScript,
                tempRoot,
                "release-only-lanes",
                """
                Invoke-ManagedTestLanes `
                    -DotNetPath 'dotnet.exe' `
                    -ResultsRoot '{{TEMP_ROOT}}' `
                    -Configuration Release `
                    -MaxParallelLanes 4 `
                    -UnitDebugShardCount 4 `
                    -ReleaseUnitShardCount 8 `
                    -IncludeReleaseUnit
                """);

            var result = RunPowerShell(command);

            result.ExitCode.Should().Be(0, result.Output);
            result.Output.Should().Contain("lanes=4;commands=8");
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void InvokeManagedTestLanes_ShouldUseRequestedParallelismForMixedDebugAndReleaseShards()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var managedScript = Path.Combine(RepoRoot, "scripts", "ci", "SandboxCi.Managed.ps1");
            var command = CreateManagedLaneHarness(
                managedScript,
                tempRoot,
                "mixed-lanes",
                """
                Invoke-ManagedTestLanes `
                    -DotNetPath 'dotnet.exe' `
                    -ResultsRoot '{{TEMP_ROOT}}' `
                    -Configuration Release `
                    -MaxParallelLanes 4 `
                    -UnitDebugShardCount 4 `
                    -ReleaseUnitShardCount 8 `
                    -IncludeUnitDebug `
                    -IncludeReleaseUnit
                """);

            var result = RunPowerShell(command);

            result.ExitCode.Should().Be(0, result.Output);
            result.Output.Should().Contain("lanes=4;commands=12");
            result.Output.Should().NotContain("cap parallelism");
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    private static string CreateManagedLaneHarness(
        string managedScript,
        string tempRoot,
        string timestamp,
        string invocation)
    {
        var escapedTempRoot = EscapePowerShellPath(tempRoot);
        var escapedInvocation = invocation.Replace("{{TEMP_ROOT}}", escapedTempRoot, StringComparison.Ordinal);
        return $$"""
        $ErrorActionPreference = 'Stop'
        . '{{EscapePowerShellPath(managedScript)}}'
        $script:MappedOutputRoot = '{{escapedTempRoot}}'
        $script:timestamp = '{{timestamp}}'
        function Invoke-ExternalBatchWithTimeout {
            param(
                [Parameter(Mandatory = $true)] [string]$Name,
                [Parameter(Mandatory = $true)] [object[]]$Commands,
                [Parameter(Mandatory = $true)] [int]$MaxParallelLanes,
                [Parameter(Mandatory = $true)] [string]$OutputRoot,
                [Parameter(Mandatory = $true)] [string]$Timestamp
            )

            "lanes=$MaxParallelLanes;commands=$($Commands.Count)"
        }

        Set-StrictMode -Version Latest
        {{escapedInvocation}}
        """;
    }
}
