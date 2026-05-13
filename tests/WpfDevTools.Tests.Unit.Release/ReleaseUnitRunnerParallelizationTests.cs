using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class ReleaseUnitRunnerParallelizationTests
{
    [Fact]
    public void ReleaseUnitRunner_ShouldUseFourThreadsForPowerShellHeavySuites()
    {
        var runnerConfig = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("tests/WpfDevTools.Tests.Unit.Release/xunit.runner.json"));

        runnerConfig.Should().Contain("\"maxParallelThreads\": 4");
    }
}
