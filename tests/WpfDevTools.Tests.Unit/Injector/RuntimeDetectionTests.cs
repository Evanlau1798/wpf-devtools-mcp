using FluentAssertions;
using WpfDevTools.Shared.Enums;
using Xunit;

namespace WpfDevTools.Tests.Unit.Injector;

public class RuntimeDetectionTests
{
    [Fact]
    public void DetectRuntimeFromModuleNames_WhenCoreClrIsPresent_ShouldReturnNetCore()
    {
        var runtime = WpfDevTools.Injector.Discovery.WpfProcessDetector.DetectRuntimeFromModuleNames(
            new[]
            {
                @"C:\Program Files\dotnet\shared\Microsoft.NETCore.App\8.0.24\coreclr.dll",
                @"C:\Windows\System32\kernel32.dll"
            });

        runtime.Should().Be(TargetRuntime.NetCore,
            "coreclr.dll must not be misdiagnosed as clr.dll");
    }

    [Fact]
    public void DetectRuntimeFromModuleNames_WhenClrAndCoreClrBothAppear_ShouldPreferNetCore()
    {
        var runtime = WpfDevTools.Injector.Discovery.WpfProcessDetector.DetectRuntimeFromModuleNames(
            new[]
            {
                @"C:\Windows\System32\clr.dll",
                @"C:\Program Files\dotnet\shared\Microsoft.NETCore.App\8.0.24\coreclr.dll"
            });

        runtime.Should().Be(TargetRuntime.NetCore,
            "modern .NET WPF targets may still load additional CLR-related modules, but coreclr.dll should win");
    }

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
