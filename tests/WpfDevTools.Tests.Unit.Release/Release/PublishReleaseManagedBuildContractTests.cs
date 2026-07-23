using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class PublishReleaseManagedBuildContractTests
{
    [Fact]
    public void PublishReleaseScript_ShouldBuildArchitectureIndependentAssembliesOnce()
    {
        var script = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Publish-Release.ps1"));
        var architectureLoop = script.IndexOf(
            "foreach ($architecture in $resolvedArchitectures)",
            StringComparison.Ordinal);

        architectureLoop.Should().BeGreaterThan(0);
        script.IndexOf("'build', $inspectorProject", StringComparison.Ordinal)
            .Should().BeLessThan(architectureLoop);
        script.LastIndexOf("'build', $inspectorProject", StringComparison.Ordinal)
            .Should().BeLessThan(architectureLoop);
        script.IndexOf("'build', $inspectorSdkProject", StringComparison.Ordinal)
            .Should().BeLessThan(architectureLoop);
    }
}
