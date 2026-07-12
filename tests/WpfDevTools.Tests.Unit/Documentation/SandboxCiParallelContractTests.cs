using FluentAssertions;
using System.Xml.Linq;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed partial class SandboxCiScriptContractTests
{
    [Fact]
    public void InvokeWindowsSandboxCi_GenerateOnly_ShouldForwardMaxParallelLanesToSandboxRunner()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var workRoot = Path.Combine(tempRoot, "work root");
            var scriptPath = Path.Combine(RepoRoot, "scripts", "ci", "Invoke-WindowsSandboxCi.ps1");
            var result = RunPowerShellFile(
                scriptPath,
                "-Mode",
                "FullManaged",
                "-Repeat",
                "1",
                "-WorkRoot",
                workRoot,
                "-MaxParallelLanes",
                "3",
                "-GenerateOnly");

            result.ExitCode.Should().Be(0, result.Output);
            var configPath = Directory.GetFiles(workRoot, "WpfDevTools-LocalCi-*.wsb").Should().ContainSingle().Subject;
            var command = XDocument.Load(configPath).Descendants("Command").Single().Value;

            command.Should().Contain("-MaxParallelLanes 3");
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void InvokeWindowsSandboxCi_GenerateOnly_ShouldForwardReleaseUnitShardCountToSandboxRunner()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var workRoot = Path.Combine(tempRoot, "work root");
            var scriptPath = Path.Combine(RepoRoot, "scripts", "ci", "Invoke-WindowsSandboxCi.ps1");
            var result = RunPowerShellFile(
                scriptPath,
                "-Mode",
                "NativeSmoke",
                "-Repeat",
                "1",
                "-WorkRoot",
                workRoot,
                "-MaxParallelLanes",
                "4",
                "-ReleaseUnitShardCount",
                "8",
                "-GenerateOnly");

            result.ExitCode.Should().Be(0, result.Output);
            var configPath = Directory.GetFiles(workRoot, "WpfDevTools-LocalCi-*.wsb").Should().ContainSingle().Subject;
            var command = XDocument.Load(configPath).Descendants("Command").Single().Value;

            command.Should().Contain("-ReleaseUnitShardCount 8");
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void SandboxProcessRunner_ShouldRunExternalBatchConcurrently()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var scriptPath = Path.Combine(RepoRoot, "scripts", "ci", "SandboxCi.Process.ps1");
            var laneOneTimesPath = Path.Combine(tempRoot, "lane-one-times.txt");
            var laneTwoTimesPath = Path.Combine(tempRoot, "lane-two-times.txt");
            var command = $$"""
            $ErrorActionPreference = 'Stop'
            . '{{EscapePowerShellPath(scriptPath)}}'
            $env:LANE_ONE_TIMES = '{{EscapePowerShellPath(laneOneTimesPath)}}'
            $env:LANE_TWO_TIMES = '{{EscapePowerShellPath(laneTwoTimesPath)}}'
            $batch = @(
                [pscustomobject]@{
                    Name = 'parallel lane one'
                    FilePath = 'powershell.exe'
                    Arguments = @('-NoProfile', '-Command', '[System.IO.File]::WriteAllText($env:LANE_ONE_TIMES, "start=$([DateTime]::UtcNow.Ticks)`n"); $deadline = [DateTime]::UtcNow.AddSeconds(5); while (-not [System.IO.File]::Exists($env:LANE_TWO_TIMES)) { if ([DateTime]::UtcNow -ge $deadline) { throw "lane two did not start before lane one finished" }; Start-Sleep -Milliseconds 50 }; Start-Sleep -Milliseconds 200; [System.IO.File]::AppendAllText($env:LANE_ONE_TIMES, "end=$([DateTime]::UtcNow.Ticks)`n"); Write-Output lane-one')
                    TimeoutSeconds = 30
                },
                [pscustomobject]@{
                    Name = 'parallel lane two'
                    FilePath = 'powershell.exe'
                    Arguments = @('-NoProfile', '-Command', '[System.IO.File]::WriteAllText($env:LANE_TWO_TIMES, "start=$([DateTime]::UtcNow.Ticks)`n"); $deadline = [DateTime]::UtcNow.AddSeconds(5); while (-not [System.IO.File]::Exists($env:LANE_ONE_TIMES)) { if ([DateTime]::UtcNow -ge $deadline) { throw "lane one did not start before lane two finished" }; Start-Sleep -Milliseconds 50 }; Start-Sleep -Milliseconds 200; [System.IO.File]::AppendAllText($env:LANE_TWO_TIMES, "end=$([DateTime]::UtcNow.Ticks)`n"); Write-Output lane-two')
                    TimeoutSeconds = 30
                }
            )
            Invoke-ExternalBatchWithTimeout `
                -Name 'parallel test lanes' `
                -Commands $batch `
                -MaxParallelLanes 2 `
                -OutputRoot '{{EscapePowerShellPath(tempRoot)}}' `
                -Timestamp 'parallel-contract'
            """;

            var result = RunPowerShell(command);

            result.ExitCode.Should().Be(0, result.Output);
            result.Output.Should().Contain("lane-one");
            result.Output.Should().Contain("lane-two");
            var laneOne = ReadLaneTimes(laneOneTimesPath);
            var laneTwo = ReadLaneTimes(laneTwoTimesPath);
            laneOne.Start.Should().BeLessOrEqualTo(laneTwo.End);
            laneTwo.Start.Should().BeLessOrEqualTo(laneOne.End);
            File.ReadAllText(Path.Combine(tempRoot, "logs", "process", "parallel-contract", "parallel_lane_one.stdout.log"))
                .Should().Contain("lane-one");
            File.ReadAllText(Path.Combine(tempRoot, "logs", "process", "parallel-contract", "parallel_lane_two.stdout.log"))
                .Should().Contain("lane-two");
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void SandboxProcessRunner_ShouldReportPeerCleanupFailure()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var scriptPath = Path.Combine(RepoRoot, "scripts", "ci", "SandboxCi.Process.ps1");
            var command = $$"""
            $ErrorActionPreference = 'Stop'
            . '{{EscapePowerShellPath(scriptPath)}}'
            function Write-CompletedProcessLogs {
                param(
                    [Parameter(Mandatory = $true)] [string]$Name,
                    [Parameter(Mandatory = $true)] [System.Threading.Tasks.Task[string]]$StdoutTask,
                    [Parameter(Mandatory = $true)] [System.Threading.Tasks.Task[string]]$StderrTask,
                    [Parameter(Mandatory = $true)] [string]$StdoutPath,
                    [Parameter(Mandatory = $true)] [string]$StderrPath
                )

                if ($Name -eq 'long peer') {
                    throw 'simulated peer cleanup failure'
                }

                [System.IO.File]::WriteAllText($StdoutPath, [string]$StdoutTask.Result, [System.Text.Encoding]::UTF8)
                [System.IO.File]::WriteAllText($StderrPath, [string]$StderrTask.Result, [System.Text.Encoding]::UTF8)
                Write-ProcessLogs -StdoutPath $StdoutPath -StderrPath $StderrPath
            }

            $batch = @(
                [pscustomobject]@{
                    Name = 'fast failure'
                    FilePath = 'powershell.exe'
                    Arguments = @('-NoProfile', '-Command', 'exit 123')
                    TimeoutSeconds = 30
                },
                [pscustomobject]@{
                    Name = 'long peer'
                    FilePath = 'powershell.exe'
                    Arguments = @('-NoProfile', '-Command', 'Start-Sleep -Seconds 5')
                    TimeoutSeconds = 30
                }
            )
            Invoke-ExternalBatchWithTimeout `
                -Name 'parallel failing lanes' `
                -Commands $batch `
                -MaxParallelLanes 2 `
                -OutputRoot '{{EscapePowerShellPath(tempRoot)}}' `
                -Timestamp 'parallel-failure-contract'
            """;

            var result = RunPowerShell(command);

            result.ExitCode.Should().NotBe(0, result.Output);
            result.Output.Should().Contain("fast failure failed with exit code 123");
            result.Output.Should().Contain("Peer cleanup failed");
            result.Output.Should().Contain("simulated peer cleanup failure");
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void SandboxProcessRunner_ShouldStopTimedOutBatchLane()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var token = $"sandbox-timeout-{Guid.NewGuid():N}";
            var scriptPath = Path.Combine(RepoRoot, "scripts", "ci", "SandboxCi.Process.ps1");
            var command = $$"""
            $ErrorActionPreference = 'Stop'
            . '{{EscapePowerShellPath(scriptPath)}}'
            $batch = @(
                [pscustomobject]@{
                    Name = 'timeout lane'
                    FilePath = 'powershell.exe'
                    Arguments = @('-NoProfile', '-Command', '$token = "{{token}}"; Start-Sleep -Seconds 5')
                    TimeoutSeconds = 1
                }
            )
            Invoke-ExternalBatchWithTimeout `
                -Name 'parallel timeout lanes' `
                -Commands $batch `
                -MaxParallelLanes 1 `
                -OutputRoot '{{EscapePowerShellPath(tempRoot)}}' `
                -Timestamp 'parallel-timeout-contract'
            """;

            var result = RunPowerShell(command);

            result.ExitCode.Should().NotBe(0, result.Output);
            result.Output.Should().Contain("timeout lane timed out after 1 seconds");
            AssertNoProcessCommandLineContains(token);
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void SandboxProcessRunner_ShouldStopPeersWhenOneLaneTimesOut()
    {
        var tempRoot = CreateTempRoot();
        try
        {
            var timeoutToken = $"sandbox-timeout-primary-{Guid.NewGuid():N}";
            var peerToken = $"sandbox-timeout-peer-{Guid.NewGuid():N}";
            var scriptPath = Path.Combine(RepoRoot, "scripts", "ci", "SandboxCi.Process.ps1");
            var command = $$"""
            $ErrorActionPreference = 'Stop'
            . '{{EscapePowerShellPath(scriptPath)}}'
            $batch = @(
                [pscustomobject]@{
                    Name = 'timeout primary'
                    FilePath = 'powershell.exe'
                    Arguments = @('-NoProfile', '-Command', '$token = "{{timeoutToken}}"; Start-Sleep -Seconds 5')
                    TimeoutSeconds = 1
                },
                [pscustomobject]@{
                    Name = 'timeout peer'
                    FilePath = 'powershell.exe'
                    Arguments = @('-NoProfile', '-Command', '$token = "{{peerToken}}"; Start-Sleep -Seconds 5')
                    TimeoutSeconds = 30
                }
            )
            Invoke-ExternalBatchWithTimeout `
                -Name 'parallel timeout peer lanes' `
                -Commands $batch `
                -MaxParallelLanes 2 `
                -OutputRoot '{{EscapePowerShellPath(tempRoot)}}' `
                -Timestamp 'parallel-timeout-peer-contract'
            """;

            var result = RunPowerShell(command);

            result.ExitCode.Should().NotBe(0, result.Output);
            result.Output.Should().Contain("timeout primary timed out after 1 seconds");
            AssertNoProcessCommandLineContains(timeoutToken);
            AssertNoProcessCommandLineContains(peerToken);
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void StartSandboxCi_ShouldUseParallelManagedTestLanesForFullModes()
    {
        var runner = ReadScript(Path.Combine(RepoRoot, "scripts", "ci"), "Start-SandboxCi.ps1");
        var managed = ReadScript(Path.Combine(RepoRoot, "scripts", "ci"), "SandboxCi.Managed.ps1");
        var fullManagedBlock = GetModeBlock(runner, "'FullManaged' {", "'NativeSmoke' {");
        var nativeSmokeBlock = GetModeBlock(runner, "'NativeSmoke' {", "'NativeFull' {");
        var nativeFullBlock = runner.Substring(runner.IndexOf("'NativeFull' {", StringComparison.Ordinal));
        var unitDebugBlock = GetModeBlock(runner, "'UnitDebug' {", "'UnitRelease' {");
        var unitReleaseBlock = GetModeBlock(runner, "'UnitRelease' {", "'FullManaged' {");

        runner.Should().Contain("[ValidateRange(1, 8)]");
        runner.Should().Contain("-MaxParallelLanes $MaxParallelLanes");
        runner.Should().Contain("-ReleaseUnitShardCount $ReleaseUnitShardCount");
        managed.Should().Contain("Invoke-ManagedTestLanes");
        managed.Should().Contain("Invoke-ExternalBatchWithTimeout");
        managed.Should().Contain("MaxParallelLanes");
        fullManagedBlock.Should().Contain("Invoke-ManagedTestLanes");
        nativeSmokeBlock.Should().Contain("Invoke-ManagedTestLanes");
        nativeFullBlock.Should().Contain("Invoke-ManagedTestLanes");
        unitDebugBlock.Should().Contain("Invoke-UnitDebugTests");
        unitDebugBlock.Should().NotContain("Invoke-ManagedTestLanes");
        unitReleaseBlock.Should().Contain("Invoke-ManagedTestLanes");
        unitReleaseBlock.Should().Contain("-IncludeReleaseUnit");
        unitReleaseBlock.Should().NotContain("-IncludeUnitDebug");
    }

    [Fact]
    public void SandboxManagedScript_ShouldSupportReleaseUnitShards()
    {
        var managed = ReadScript(Path.Combine(RepoRoot, "scripts", "ci"), "SandboxCi.Managed.ps1");

        managed.Should().Contain("New-ReleaseUnitShardCommands");
        managed.Should().Contain("Get-ReleaseUnitShardFilters");
        managed.Should().Contain("Get-ReleaseUnitEightShardFilters");
        managed.Should().Contain("ReleaseUnitShardCount");
        managed.Should().Contain("[ValidateScript({");
        managed.Should().Contain("release-unit-$configurationSlug-shard-$shardNumber.trx");
        managed.Should().Contain("FullyQualifiedName~InstallerTui");
        managed.Should().Contain("FullyQualifiedName!~InstallerTui");
        managed.Should().Contain("ReleaseUnitShardCount currently supports 1, 4, or 8");
    }

    [Fact]
    public void SandboxManagedScript_ReleaseUnitShardFilters_ShouldCoverEveryReleaseTestClassOnce()
    {
        var managed = ReadScript(Path.Combine(RepoRoot, "scripts", "ci"), "SandboxCi.Managed.ps1");
        var filters = ExtractReleaseShardFilters(managed);
        var testClasses = GetReleaseTestClasses();

        filters.Should().HaveCount(4);
        testClasses.Should().NotBeEmpty();
        foreach (var fullyQualifiedClassName in testClasses)
        {
            var fullyQualifiedName = $"{fullyQualifiedClassName}.SyntheticTest";
            filters.Count(filter => FilterMatchesFullyQualifiedName(filter, fullyQualifiedName))
                .Should().Be(1, $"{fullyQualifiedClassName} should belong to exactly one release-unit shard");
        }
    }

    [Fact]
    public void SandboxManagedScript_ReleaseUnitEightShardFilters_ShouldCoverEveryClassOnceAndPrioritizeLongestGroups()
    {
        var managed = ReadScript(Path.Combine(RepoRoot, "scripts", "ci"), "SandboxCi.Managed.ps1");
        var filters = ExtractReleaseEightShardFilters(managed);
        var testClasses = GetReleaseTestClasses();

        filters.Should().HaveCount(8);
        testClasses.Should().NotBeEmpty();
        string[] priorityMarkers =
        [
            "FullyQualifiedName!~",
            "InstallerScriptTests",
            "InstallerFullUninstallTests",
            "InstallerCursorClientUninstallTests",
            "InstallerTuiRuntimeTests",
            "InstallerIdeRegistrationShimBackedTests",
            "InstallerInteractiveUiScriptTests",
            "PackagedServerRuntimeSmokeScriptTests"
        ];
        for (var index = 0; index < priorityMarkers.Length; index++)
        {
            filters[index].Should().Contain(priorityMarkers[index],
                $"release-unit shard {index + 1} should preserve the measured longest-first priority order");
        }

        foreach (var fullyQualifiedClassName in testClasses)
        {
            var fullyQualifiedName = $"{fullyQualifiedClassName}.SyntheticTest";
            filters.Count(filter => FilterMatchesFullyQualifiedName(filter, fullyQualifiedName))
                .Should().Be(1, $"{fullyQualifiedClassName} should belong to exactly one release-unit shard");
        }
    }

    private static (long Start, long End) ReadLaneTimes(string path)
    {
        var values = File.ReadAllLines(path)
            .Select(line => line.Split('=', 2))
            .ToDictionary(parts => parts[0], parts => long.Parse(parts[1]));

        return (values["start"], values["end"]);
    }

    private static string GetModeBlock(string script, string startMarker, string endMarker)
    {
        var start = script.IndexOf(startMarker, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, $"{startMarker} should exist");
        var end = script.IndexOf(endMarker, start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start, $"{endMarker} should follow {startMarker}");
        return script[start..end];
    }

    private static void AssertNoProcessCommandLineContains(string token)
    {
        var processCheck = RunPowerShell($$"""
        $matches = @(Get-CimInstance Win32_Process | Where-Object { $_.ProcessId -ne $PID -and $_.CommandLine -like '*{{token}}*' })
        $matches.Count
        """);
        processCheck.ExitCode.Should().Be(0, processCheck.Output);
        processCheck.Output.Trim().Should().Be("0");
    }

    private static string[] ExtractReleaseShardFilters(string managedScript)
    {
        var start = managedScript.IndexOf("function Get-ReleaseUnitShardFilters", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        var end = managedScript.IndexOf("function New-ReleaseUnitShardCommands", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);
        var block = managedScript[start..end];

        return System.Text.RegularExpressions.Regex.Matches(block, "'(?<filter>FullyQualifiedName[^']+)'")
            .Select(match => match.Groups["filter"].Value)
            .ToArray();
    }

    private static string[] ExtractReleaseEightShardFilters(string managedScript)
    {
        var start = managedScript.IndexOf("function Get-ReleaseUnitEightShardFilters", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        var end = managedScript.IndexOf("function Get-UnitDebugShardFilters", start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);
        var block = managedScript[start..end];

        return System.Text.RegularExpressions.Regex.Matches(block, "'(?<filter>FullyQualifiedName[^']+)'")
            .Select(match => match.Groups["filter"].Value)
            .ToArray();
    }

    private static string[] GetReleaseTestClasses()
    {
        var releaseRoot = Path.Combine(RepoRoot, "tests", "WpfDevTools.Tests.Unit.Release");
        return Directory.GetFiles(releaseRoot, "*.cs", SearchOption.AllDirectories)
            .Select(File.ReadAllText)
            .Where(text => text.Contains("[Fact", StringComparison.Ordinal) || text.Contains("[Theory", StringComparison.Ordinal))
            .SelectMany(text =>
            {
                var namespaceMatch = System.Text.RegularExpressions.Regex.Match(text, @"namespace\s+(?<name>[A-Za-z0-9_.]+)\s*;");
                namespaceMatch.Success.Should().BeTrue("release test files should use a file-scoped namespace");
                return System.Text.RegularExpressions.Regex.Matches(text, @"public sealed (?:partial )?class (?<name>[A-Za-z0-9_]+)")
                    .Select(match => $"{namespaceMatch.Groups["name"].Value}.{match.Groups["name"].Value}");
            })
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool FilterMatchesFullyQualifiedName(string filter, string fullyQualifiedName)
    {
        return filter.Split('|').Any(orGroup => orGroup.Split('&').All(condition =>
        {
            const string contains = "FullyQualifiedName~";
            const string notContains = "FullyQualifiedName!~";
            if (condition.StartsWith(notContains, StringComparison.Ordinal))
            {
                return !fullyQualifiedName.Contains(condition[notContains.Length..], StringComparison.Ordinal);
            }

            condition.StartsWith(contains, StringComparison.Ordinal).Should().BeTrue();
            return fullyQualifiedName.Contains(condition[contains.Length..], StringComparison.Ordinal);
        }));
    }
}
