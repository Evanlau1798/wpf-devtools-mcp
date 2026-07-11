using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class GitHubCiHangDiagnosticsContractTests
{
    private static readonly string RepoRoot = FindRepoRoot();

    [Theory]
    [InlineData("Run unit tests", "unit-${{ matrix.configuration }}-${{ matrix.platform }}.trx", "./TestResults/${{ matrix.configuration }}/unit", "20")]
    [InlineData("Run integration tests", "integration-${{ matrix.configuration }}-${{ matrix.platform }}.trx", "./TestResults/${{ matrix.configuration }}/integration", "20")]
    public void BuildAndTestWorkflow_TestSteps_ShouldFailFastAndPersistHangDiagnostics(
        string stepName,
        string trxFileName,
        string resultsDirectory,
        string timeoutMinutes)
    {
        var lines = File.ReadAllLines(Path.Combine(RepoRoot, ".github", "workflows", "ci-cd.yml"));

        var step = string.Join(Environment.NewLine, GetNamedStepBlock(lines, stepName));

        step.Should().Contain($"timeout-minutes: {timeoutMinutes}");
        step.Should().Contain("--blame-hang-timeout 10m",
            "hosted Windows test hangs must terminate quickly enough to avoid burning GitHub runner minutes");
        step.Should().Contain("--logger \"trx;LogFileName=" + trxFileName + "\"",
            "failed or cancelled runs should leave a structured VSTest result file");
        step.Should().Contain("--results-directory " + resultsDirectory,
            "test diagnostics should be grouped by configuration and lane");
    }

    [Fact]
    public void BuildAndTestWorkflow_ReleaseUnitStep_ShouldUseManagedShardsAndPersistHangDiagnostics()
    {
        var lines = File.ReadAllLines(Path.Combine(RepoRoot, ".github", "workflows", "ci-cd.yml"));
        var managedScript = File.ReadAllText(Path.Combine(RepoRoot, "scripts", "ci", "SandboxCi.Managed.ps1"));

        var step = string.Join(Environment.NewLine, GetNamedStepBlock(lines, "Run release unit tests"));

        step.Should().Contain("timeout-minutes: 25");
        step.Should().Contain("SandboxCi.Process.ps1");
        step.Should().Contain("SandboxCi.Managed.ps1");
        step.Should().Contain("$env:WPFDEVTOOLS_TEST_TIMEOUT_SCALE = '4'");
        step.Should().Contain("Invoke-ManagedTestLanes");
        step.Should().Contain("$resultsRoot = Join-Path (Get-Location).ProviderPath 'TestResults'");
        step.Should().Contain("$script:MappedOutputRoot = $resultsRoot");
        step.Should().Contain("-ResultsRoot $resultsRoot");
        step.Should().Contain("-MaxParallelLanes 2",
            "four shard processes with four xUnit threads each exhausted hosted-runner process and pipe resources");
        step.Should().Contain("-ReleaseUnitShardCount 8");
        step.Should().Contain("-IncludeReleaseUnit");
        managedScript.Should().Contain("'--blame-hang-timeout'");
        managedScript.Should().Contain("'10m'");
        managedScript.Should().Contain("release-unit-$configurationSlug-shard-$shardNumber.trx",
            "each shard should keep an independent TRX result for failed or cancelled hosted runs");
    }

    [Fact]
    public void BuildAndTestWorkflow_ShouldUploadTestDiagnosticsWhenTestsFailOrAreCancelled()
    {
        var lines = File.ReadAllLines(Path.Combine(RepoRoot, ".github", "workflows", "ci-cd.yml"));

        var step = string.Join(Environment.NewLine, GetNamedStepBlock(lines, "Upload test diagnostics"));

        step.Should().Contain("if: failure() || cancelled()");
        step.Should().Contain("actions/upload-artifact@");
        step.Should().Contain("name: test-diagnostics-${{ matrix.configuration }}-${{ matrix.platform }}");
        step.Should().Contain("path: TestResults/");
        step.Should().Contain("if-no-files-found: ignore");
    }

    [Fact]
    public void CodeCoverageWorkflow_TestStep_ShouldFailFastAndAvoidReleaseHeavyRetest()
    {
        var lines = File.ReadAllLines(Path.Combine(RepoRoot, ".github", "workflows", "ci-cd.yml"));

        var step = string.Join(Environment.NewLine, GetNamedStepBlock(lines, "Run tests with coverage"));

        step.Should().Contain("timeout-minutes: 20");
        step.Should().Contain("--blame-hang-timeout 10m",
            "coverage test hangs need VSTest diagnostics instead of waiting for the job timeout");
        step.Should().Contain("--logger \"trx;LogFileName=coverage-debug.trx\"",
            "coverage failures should leave a structured TRX file for hosted CI and GitHub artifacts");
        step.Should().Contain("--results-directory ./TestResults/coverage");
        step.Should().Contain("--filter \"FullyQualifiedName!~WpfDevTools.Tests.Unit.Release&FullyQualifiedName!~WpfDevTools.Tests.Unit.Documentation\"",
            "release and documentation contracts already run in unit lanes and do not contribute managed coverage");
    }

    [Fact]
    public void CodeCoverageWorkflow_ShouldUploadCoverageDiagnosticsWhenTestsFailOrAreCancelled()
    {
        var lines = File.ReadAllLines(Path.Combine(RepoRoot, ".github", "workflows", "ci-cd.yml"));

        var step = string.Join(Environment.NewLine, GetNamedStepBlock(lines, "Upload coverage test diagnostics"));

        step.Should().Contain("if: failure() || cancelled()");
        step.Should().Contain("actions/upload-artifact@");
        step.Should().Contain("name: coverage-test-diagnostics");
        step.Should().Contain("path: TestResults/");
        step.Should().Contain("if-no-files-found: ignore");
    }

    private static List<string> GetNamedStepBlock(string[] lines, string stepName)
    {
        var start = Array.FindIndex(lines, line => line.Trim() == $"- name: {stepName}");
        start.Should().BeGreaterThanOrEqualTo(0, $"workflow should contain a '{stepName}' step");

        var end = lines.Length;
        for (var index = start + 1; index < lines.Length; index++)
        {
            if (lines[index].StartsWith("    - name:", StringComparison.Ordinal))
            {
                end = index;
                break;
            }
        }

        return lines[start..end].ToList();
    }

    private static string FindRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "WpfDevTools.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root.");
    }
}
