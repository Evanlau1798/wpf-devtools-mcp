using Xunit;
using FluentAssertions;
using WpfDevTools.Injector;
using WpfDevTools.Shared.Enums;

namespace WpfDevTools.Tests.Unit.Injector;

public class ArchitectureCompatibilityTests
{
    // === Compatibility check tests ===

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

    // === Error message tests ===

    [Fact]
    public void ErrorMessage_InjectorBitnessMismatch_AnyCpuDll_ShouldBlameServer()
    {
        // x64 server targeting x86 process with AnyCPU DLL:
        // message must blame server/injector bitness, NOT the DLL
        var message = ProcessInjector.GetArchitectureErrorMessage(
            processArch: ProcessArchitecture.X86,
            dllArch: ProcessArchitecture.Unknown,
            isInjector64Bit: true);

        message.Should().Contain("server", "message must mention the MCP server as the root cause");
        message.Should().Contain("AnyCPU", "message must clarify that the AnyCPU DLL is not the problem");
        message.Should().NotContain("Inspector DLL is Unknown",
            "must NOT display raw 'Unknown' enum value for AnyCPU DLLs");
    }

    [Fact]
    public void ErrorMessage_NativeDllMismatch_ShouldBlameDll()
    {
        // x86 DLL targeting x64 process:
        // message must blame the DLL architecture
        var message = ProcessInjector.GetArchitectureErrorMessage(
            processArch: ProcessArchitecture.X64,
            dllArch: ProcessArchitecture.X86,
            isInjector64Bit: true);

        message.Should().Contain("Inspector DLL is X86",
            "message must identify the DLL architecture");
        message.Should().Contain("X64",
            "message must identify the target process architecture");
    }

    [Fact]
    public void ErrorMessage_X86ServerToX64Target_AnyCpuDll_ShouldBlameServer()
    {
        // x86 server targeting x64 process with AnyCPU DLL
        var message = ProcessInjector.GetArchitectureErrorMessage(
            processArch: ProcessArchitecture.X64,
            dllArch: ProcessArchitecture.Unknown,
            isInjector64Bit: false);

        message.Should().Contain("server", "must blame the server");
        message.Should().Contain("X86", "must show the server is X86");
        message.Should().Contain("X64", "must show the target is X64");
    }
}
