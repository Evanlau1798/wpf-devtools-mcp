using FluentAssertions;
using WpfDevTools.Shared.Enums;
using Xunit;

namespace WpfDevTools.Tests.Unit.Injector;

public class RuntimeDetectionTests
{
    [Theory]
    [InlineData(TargetRuntime.NetFramework, "net48")]
    [InlineData(TargetRuntime.NetCore, "net8.0-windows")]
    public void RuntimeToTfm_Mapping_ShouldBeConsistent(
        TargetRuntime runtime, string expectedTfmFragment)
    {
        var candidates = new[]
        {
            @"C:\app\net48\Inspector.dll",
            @"C:\app\net8.0-windows\Inspector.dll"
        };

        var result = WpfDevTools.Injector.RuntimeSelector.SelectInspectorDll(
            runtime, candidates);

        result.Should().Contain(expectedTfmFragment);
    }

    [Fact]
    public void TargetRuntime_Values_ShouldNotOverlap()
    {
        var values = Enum.GetValues<TargetRuntime>();
        var distinctValues = values.Select(v => (int)v).Distinct().ToList();

        distinctValues.Count.Should().Be(values.Length,
            "all TargetRuntime enum values must be unique");
    }
}
