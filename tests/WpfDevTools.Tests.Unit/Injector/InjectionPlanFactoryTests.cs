using FluentAssertions;
using WpfDevTools.Injector;
using WpfDevTools.Injector.Discovery;
using WpfDevTools.Injector.Injection;
using WpfDevTools.Shared.Enums;
using Xunit;

namespace WpfDevTools.Tests.Unit.Injector;

public class InjectionPlanFactoryTests
{
    private static WpfProcessInfo CreateProcessInfo(
        int pid = 1234,
        TargetRuntime runtime = TargetRuntime.NetCore,
        ProcessArchitecture arch = ProcessArchitecture.X64)
    {
        return new WpfProcessInfo
        {
            ProcessId = pid,
            ProcessName = "TestApp",
            Architecture = arch,
            Runtime = runtime,
            IsWpfApplication = true
        };
    }

    [Fact]
    public void CreateRequest_NetCore_X64_ShouldSelectCorrectDlls()
    {
        var inspectorCandidates = new[]
        {
            @"C:\app\net8.0-windows\Inspector.dll",
            @"C:\app\net48\Inspector.dll"
        };
        var bootstrapperCandidates = new[]
        {
            @"C:\app\Bootstrapper.x86.dll",
            @"C:\app\Bootstrapper.x64.dll"
        };
        var info = CreateProcessInfo(pid: 5678, runtime: TargetRuntime.NetCore, arch: ProcessArchitecture.X64);

        var result = InjectionPlanFactory.CreateRequest(
            info, inspectorCandidates, bootstrapperCandidates);

        result.Should().NotBeNull();
        result!.ProcessId.Should().Be(5678);
        result.InspectorDllPath.Should().Contain("net8.0-windows");
        result.BootstrapperDllPath.Should().Contain("x64");
        result.ExpectedPipeName.Should().MatchRegex("^WpfDevTools_5678_[0-9a-f]{32}$");
    }

    [Fact]
    public void CreateRequest_NetFramework_X86_ShouldSelectCorrectDlls()
    {
        var inspectorCandidates = new[]
        {
            @"C:\app\net8.0-windows\Inspector.dll",
            @"C:\app\net48\Inspector.dll"
        };
        var bootstrapperCandidates = new[]
        {
            @"C:\app\Bootstrapper.x86.dll",
            @"C:\app\Bootstrapper.x64.dll"
        };
        var info = CreateProcessInfo(runtime: TargetRuntime.NetFramework, arch: ProcessArchitecture.X86);

        var result = InjectionPlanFactory.CreateRequest(
            info, inspectorCandidates, bootstrapperCandidates);

        result.Should().NotBeNull();
        result!.InspectorDllPath.Should().Contain("net48");
        result.BootstrapperDllPath.Should().Contain("x86");
    }

    [Fact]
    public void CreateRequest_NoMatchingBootstrapper_ShouldReturnNull()
    {
        var inspectorCandidates = new[] { @"C:\app\net8.0-windows\Inspector.dll" };
        var bootstrapperCandidates = new[] { @"C:\app\Bootstrapper.arm64.dll" };
        var info = CreateProcessInfo(arch: ProcessArchitecture.X64);

        var result = InjectionPlanFactory.CreateRequest(
            info, inspectorCandidates, bootstrapperCandidates);

        result.Should().BeNull("no x64 bootstrapper available");
    }

    [Fact]
    public void CreateRequest_NoMatchingInspector_ShouldReturnNull()
    {
        var inspectorCandidates = Array.Empty<string>();
        var bootstrapperCandidates = new[] { @"C:\app\Bootstrapper.x64.dll" };
        var info = CreateProcessInfo();

        var result = InjectionPlanFactory.CreateRequest(
            info, inspectorCandidates, bootstrapperCandidates);

        result.Should().BeNull("no Inspector DLL available");
    }

    [Fact]
    public void CreateRequest_WhenRuntimeSpecificInspectorIsMissing_ShouldReturnNull()
    {
        var inspectorCandidates = new[] { @"C:\app\net48\Inspector.dll" };
        var bootstrapperCandidates = new[] { @"C:\app\Bootstrapper.x64.dll" };
        var info = CreateProcessInfo(runtime: TargetRuntime.NetCore, arch: ProcessArchitecture.X64);

        var result = InjectionPlanFactory.CreateRequest(
            info, inspectorCandidates, bootstrapperCandidates);

        result.Should().BeNull(
            "the injection plan must fail early when the inspector payload does not match the detected target runtime");
    }
}
