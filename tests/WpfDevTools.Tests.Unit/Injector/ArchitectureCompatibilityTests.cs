using Xunit;
using FluentAssertions;
using WpfDevTools.Injector;
using WpfDevTools.Shared.Enums;

namespace WpfDevTools.Tests.Unit.Injector;

public class ArchitectureCompatibilityTests
{
    [Fact]
    public void CheckCompatibility_X64Injector_X64Target_AnyCpuDll_ShouldPass()
    {
        var result = ProcessInjector.CheckArchitectureCompatibility(
            processArch: ProcessArchitecture.X64,
            dllArch: ProcessArchitecture.Unknown,
            isInjector64Bit: true);

        result.Should().Be(InjectionError.None);
    }

    [Fact]
    public void CheckCompatibility_X64Injector_X86Target_AnyCpuDll_ShouldFail()
    {
        // Even though DLL is AnyCPU, CreateRemoteThread requires same bitness
        var result = ProcessInjector.CheckArchitectureCompatibility(
            processArch: ProcessArchitecture.X86,
            dllArch: ProcessArchitecture.Unknown,
            isInjector64Bit: true);

        result.Should().Be(InjectionError.ArchitectureMismatch);
    }

    [Fact]
    public void CheckCompatibility_X86Injector_X64Target_AnyCpuDll_ShouldFail()
    {
        var result = ProcessInjector.CheckArchitectureCompatibility(
            processArch: ProcessArchitecture.X64,
            dllArch: ProcessArchitecture.Unknown,
            isInjector64Bit: false);

        result.Should().Be(InjectionError.ArchitectureMismatch);
    }

    [Fact]
    public void CheckCompatibility_X64Injector_X64Target_X64Dll_ShouldPass()
    {
        var result = ProcessInjector.CheckArchitectureCompatibility(
            processArch: ProcessArchitecture.X64,
            dllArch: ProcessArchitecture.X64,
            isInjector64Bit: true);

        result.Should().Be(InjectionError.None);
    }

    [Fact]
    public void CheckCompatibility_X64Injector_X64Target_X86Dll_ShouldFail()
    {
        var result = ProcessInjector.CheckArchitectureCompatibility(
            processArch: ProcessArchitecture.X64,
            dllArch: ProcessArchitecture.X86,
            isInjector64Bit: true);

        result.Should().Be(InjectionError.ArchitectureMismatch);
    }

    [Fact]
    public void CheckCompatibility_UnknownTarget_AnyDll_ShouldPass()
    {
        // Can't determine target architecture, so proceed and let injection fail naturally
        var result = ProcessInjector.CheckArchitectureCompatibility(
            processArch: ProcessArchitecture.Unknown,
            dllArch: ProcessArchitecture.X86,
            isInjector64Bit: true);

        result.Should().Be(InjectionError.None);
    }

    [Fact]
    public void CheckCompatibility_X86Injector_X86Target_AnyCpuDll_ShouldPass()
    {
        var result = ProcessInjector.CheckArchitectureCompatibility(
            processArch: ProcessArchitecture.X86,
            dllArch: ProcessArchitecture.Unknown,
            isInjector64Bit: false);

        result.Should().Be(InjectionError.None);
    }
}
