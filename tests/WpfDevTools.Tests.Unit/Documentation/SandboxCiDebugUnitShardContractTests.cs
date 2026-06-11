using FluentAssertions;
using System.Xml.Linq;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed partial class SandboxCiScriptContractTests
{
    [Fact]
    public void InvokeWindowsSandboxCi_GenerateOnly_ShouldForwardUnitDebugShardCountToSandboxRunner()
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
                "4",
                "-UnitDebugShardCount",
                "4",
                "-GenerateOnly");

            result.ExitCode.Should().Be(0, result.Output);
            var configPath = Directory.GetFiles(workRoot, "WpfDevTools-LocalCi-*.wsb").Should().ContainSingle().Subject;
            var command = XDocument.Load(configPath).Descendants("Command").Single().Value;

            command.Should().Contain("-UnitDebugShardCount 4");
        }
        finally
        {
            DeleteTempRoot(tempRoot);
        }
    }

    [Fact]
    public void SandboxManagedScript_ShouldSupportDebugUnitShards()
    {
        var managed = ReadScript(Path.Combine(RepoRoot, "scripts", "ci"), "SandboxCi.Managed.ps1");

        managed.Should().Contain("New-UnitDebugShardCommands");
        managed.Should().Contain("Get-UnitDebugShardFilters");
        managed.Should().Contain("UnitDebugShardCount");
        managed.Should().Contain("unit-$configurationSlug-shard-$shardNumber.trx");
        managed.Should().Contain("WpfDevTools.Tests.Unit.Documentation");
        managed.Should().Contain("WpfDevTools.Tests.Unit.McpServer.Tools");
        managed.Should().Contain("WpfDevTools.Tests.Unit.InspectorSdk");
        managed.Should().Contain("WpfDevTools.Tests.Unit.Security");
        managed.Should().Contain("WpfDevTools.Tests.Unit.McpServer.NamedPipe");
        managed.Should().Contain("WpfDevTools.Tests.Unit.McpServer.PipeConnected");
        managed.Should().Contain("WpfDevTools.Tests.Unit.McpServer.SessionManagerConnectedPipeCleanupTests");
        managed.Should().Contain("UnitDebugShardCount currently supports 1 or 4");
        managed.Should().NotContain("[Math]::Min($laneCount, 2)",
            "MaxParallelLanes should control debug-unit shard concurrency without hidden caps");
    }

    [Fact]
    public void StartSandboxCi_ShouldForwardUnitDebugShardCountToManagedModes()
    {
        var runner = ReadScript(Path.Combine(RepoRoot, "scripts", "ci"), "Start-SandboxCi.ps1");
        var unitDebugBlock = GetModeBlock(runner, "'UnitDebug' {", "'UnitRelease' {");
        var unitReleaseBlock = GetModeBlock(runner, "'UnitRelease' {", "'FullManaged' {");
        var fullManagedBlock = GetModeBlock(runner, "'FullManaged' {", "'NativeSmoke' {");
        var nativeSmokeBlock = GetModeBlock(runner, "'NativeSmoke' {", "'NativeFull' {");
        var nativeFullBlock = runner.Substring(runner.IndexOf("'NativeFull' {", StringComparison.Ordinal));

        unitDebugBlock.Should().Contain("-UnitDebugShardCount $UnitDebugShardCount");
        fullManagedBlock.Should().Contain("-UnitDebugShardCount $UnitDebugShardCount");
        nativeSmokeBlock.Should().Contain("-UnitDebugShardCount $UnitDebugShardCount");
        nativeFullBlock.Should().Contain("-UnitDebugShardCount $UnitDebugShardCount");
        unitReleaseBlock.Should().NotContain("-UnitDebugShardCount");
    }

    [Fact]
    public void SandboxManagedScript_DebugUnitShardFilters_ShouldCoverEveryUnitTestClassOnce()
    {
        var managed = ReadScript(Path.Combine(RepoRoot, "scripts", "ci"), "SandboxCi.Managed.ps1");
        var filters = ExtractShardFilters(managed, "Get-UnitDebugShardFilters", "function New-UnitDebugShardCommands");
        var unitRoot = Path.Combine(RepoRoot, "tests", "WpfDevTools.Tests.Unit");
        var testClasses = Directory.GetFiles(unitRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Release{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Select(ReadTestClassSource)
            .Where(text => text.Contains("[Fact", StringComparison.Ordinal) || text.Contains("[Theory", StringComparison.Ordinal))
            .SelectMany(text =>
            {
                var namespaceMatch = System.Text.RegularExpressions.Regex.Match(text, @"namespace (?<namespace>[A-Za-z0-9_.]+);");
                namespaceMatch.Success.Should().BeTrue("unit test files should use file-scoped namespaces");
                var namespaceName = namespaceMatch.Groups["namespace"].Value;
                return System.Text.RegularExpressions.Regex.Matches(text, @"(?:public|internal) (?:sealed |partial |abstract )*class (?<name>[A-Za-z0-9_]+)")
                    .Select(match => $"{namespaceName}.{match.Groups["name"].Value}");
            })
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        filters.Should().HaveCount(4);
        testClasses.Should().NotBeEmpty();
        foreach (var fullyQualifiedClassName in testClasses)
        {
            var fullyQualifiedName = $"{fullyQualifiedClassName}.SyntheticTest";
            filters.Count(filter => FilterMatchesFullyQualifiedName(filter, fullyQualifiedName))
                .Should().Be(1, $"{fullyQualifiedClassName} should belong to exactly one debug-unit shard");
        }
    }

    private static string ReadTestClassSource(string path)
    {
        return File.ReadAllText(path);
    }

    private static string[] ExtractShardFilters(string script, string functionName, string endMarker)
    {
        var start = script.IndexOf($"function {functionName}", StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0);
        var end = script.IndexOf(endMarker, start, StringComparison.Ordinal);
        end.Should().BeGreaterThan(start);
        var block = script[start..end];

        return System.Text.RegularExpressions.Regex.Matches(block, "'(?<filter>FullyQualifiedName[^']+)'")
            .Select(match => match.Groups["filter"].Value)
            .ToArray();
    }
}
