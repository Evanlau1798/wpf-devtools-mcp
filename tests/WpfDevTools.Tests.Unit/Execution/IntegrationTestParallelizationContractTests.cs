using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Execution;

public sealed class IntegrationTestParallelizationContractTests
{
    [Fact]
    public void XunitRunnerConfig_ShouldEnableCollectionParallelizationForIntegrationSuite()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(TestRepositoryPaths.GetRepoFilePath("tests/WpfDevTools.Tests.Integration/xunit.runner.json")));

        document.RootElement.GetProperty("parallelizeAssembly").GetBoolean()
            .Should().BeTrue("the integration suite should run independent collections in parallel while shared-state lanes opt out explicitly");
        document.RootElement.GetProperty("parallelizeTestCollections").GetBoolean()
            .Should().BeTrue("safe integration collections should be allowed to run in parallel instead of serializing the whole assembly");
        document.RootElement.GetProperty("maxParallelThreads").GetInt32()
            .Should().Be(0, "integration collection fan-out should scale with the host CPU count rather than pinning the assembly to one worker");
    }

    [Theory]
    [InlineData("tests/WpfDevTools.Tests.Integration/PackagingIntegrationCollection.cs", "[CollectionDefinition(\"PackagingIntegration\", DisableParallelization = true)]")]
    [InlineData("tests/WpfDevTools.Tests.Integration/E2E/McpE2eCollection.cs", "[CollectionDefinition(\"McpE2E\", DisableParallelization = true)]")]
    public void SharedStateCollections_ShouldStayNonParallel(string relativePath, string expectedAttribute)
    {
        File.ReadAllText(TestRepositoryPaths.GetRepoFilePath(relativePath))
            .Should().Contain(expectedAttribute);
    }

    [Theory]
    [InlineData("tests/WpfDevTools.Tests.Integration/WpfAndBootstrapIntegrationCollection.cs", "[CollectionDefinition(\"WpfAndBootstrapIntegration\")]")]
    [InlineData("tests/WpfDevTools.Tests.Integration/E2E/VisibilityDiagnosisE2eCollection.cs", "[CollectionDefinition(\"VisibilityMcpE2E\")]")]
    public void IsolatedCollections_ShouldRemainEligibleForParallelScheduling(string relativePath, string expectedAttribute)
    {
        var content = File.ReadAllText(TestRepositoryPaths.GetRepoFilePath(relativePath));

        content.Should().Contain(expectedAttribute);
        content.Should().NotContain("DisableParallelization = true",
            "this collection is intended to participate in collection-level parallelism rather than forcing assembly-wide serialization");
    }
}
