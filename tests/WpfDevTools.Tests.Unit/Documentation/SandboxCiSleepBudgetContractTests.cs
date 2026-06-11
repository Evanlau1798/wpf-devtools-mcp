using FluentAssertions;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class SandboxCiSleepBudgetContractTests
{
    [Fact]
    public void SandboxParallelContractTests_ShouldAvoidLongSleeperFixtures()
    {
        var source = File.ReadAllText(TestRepositoryPaths.GetRepoFilePath(
            "tests/WpfDevTools.Tests.Unit/Documentation/SandboxCiParallelContractTests.cs"));

        source.Should().NotContain("Start-Sleep -Seconds 30",
            "timeout fixture processes only need to outlive their one-second deadline");
        source.Should().NotContain("Start-Sleep -Seconds 3",
            "concurrency fixtures should prove overlap without adding seconds of idle test time");
    }
}
