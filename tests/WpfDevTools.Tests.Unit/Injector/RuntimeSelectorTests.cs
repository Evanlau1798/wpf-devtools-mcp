using FluentAssertions;
using WpfDevTools.Injector;
using WpfDevTools.Shared.Enums;
using Xunit;

namespace WpfDevTools.Tests.Unit.Injector;

public class RuntimeSelectorTests
{
    // === Inspector DLL selection (TFM) ===

    [Fact]
    public void SelectInspectorDll_NetFramework_ShouldPreferNet48()
    {
        var candidates = new[]
        {
            @"C:\app\bin\net8.0-windows\Inspector.dll",
            @"C:\app\bin\net48\Inspector.dll"
        };

        var result = RuntimeSelector.SelectInspectorDll(
            TargetRuntime.NetFramework, candidates);

        result.Should().Contain("net48");
    }

    [Fact]
    public void SelectInspectorDll_NetCore_ShouldPreferNet8()
    {
        var candidates = new[]
        {
            @"C:\app\bin\net8.0-windows\Inspector.dll",
            @"C:\app\bin\net48\Inspector.dll"
        };

        var result = RuntimeSelector.SelectInspectorDll(
            TargetRuntime.NetCore, candidates);

        result.Should().Contain("net8.0-windows");
    }

    [Fact]
    public void SelectInspectorDll_Unknown_ShouldFallbackToFirstAvailable()
    {
        var candidates = new[]
        {
            @"C:\app\bin\net8.0-windows\Inspector.dll",
            @"C:\app\bin\net48\Inspector.dll"
        };

        var result = RuntimeSelector.SelectInspectorDll(
            TargetRuntime.Unknown, candidates);

        result.Should().NotBeNull();
    }

    [Fact]
    public void SelectInspectorDll_EmptyCandidates_ShouldReturnNull()
    {
        var result = RuntimeSelector.SelectInspectorDll(
            TargetRuntime.NetCore, Array.Empty<string>());

        result.Should().BeNull();
    }

    [Fact]
    public void SelectInspectorDll_NetFramework_OnlyNet8Available_ShouldFallback()
    {
        var candidates = new[] { @"C:\app\bin\net8.0-windows\Inspector.dll" };

        var result = RuntimeSelector.SelectInspectorDll(
            TargetRuntime.NetFramework, candidates);

        result.Should().Contain("net8.0-windows",
            "should fallback to available TFM when preferred is missing");
    }

    // === Bootstrapper DLL selection (arch) ===

    [Fact]
    public void SelectBootstrapperDll_X86_ShouldSelectX86()
    {
        var candidates = new[]
        {
            @"C:\app\Bootstrapper.x86.dll",
            @"C:\app\Bootstrapper.x64.dll",
            @"C:\app\Bootstrapper.arm64.dll"
        };

        var result = RuntimeSelector.SelectBootstrapperDll(
            ProcessArchitecture.X86, candidates);

        result.Should().Contain("x86");
    }

    [Fact]
    public void SelectBootstrapperDll_X64_ShouldSelectX64()
    {
        var candidates = new[]
        {
            @"C:\app\Bootstrapper.x86.dll",
            @"C:\app\Bootstrapper.x64.dll"
        };

        var result = RuntimeSelector.SelectBootstrapperDll(
            ProcessArchitecture.X64, candidates);

        result.Should().Contain("x64");
    }

    [Fact]
    public void SelectBootstrapperDll_ARM64_ShouldSelectArm64()
    {
        var candidates = new[]
        {
            @"C:\app\Bootstrapper.x86.dll",
            @"C:\app\Bootstrapper.arm64.dll"
        };

        var result = RuntimeSelector.SelectBootstrapperDll(
            ProcessArchitecture.ARM64, candidates);

        result.Should().Contain("arm64");
    }

    [Fact]
    public void SelectBootstrapperDll_Unknown_ShouldReturnNull()
    {
        var candidates = new[] { @"C:\app\Bootstrapper.x64.dll" };

        var result = RuntimeSelector.SelectBootstrapperDll(
            ProcessArchitecture.Unknown, candidates);

        result.Should().BeNull();
    }

    [Fact]
    public void SelectBootstrapperDll_EmptyCandidates_ShouldReturnNull()
    {
        var result = RuntimeSelector.SelectBootstrapperDll(
            ProcessArchitecture.X64, Array.Empty<string>());

        result.Should().BeNull();
    }
}
