using FluentAssertions;
using WpfDevTools.Injector.Discovery;
using WpfDevTools.Shared.Enums;

namespace WpfDevTools.Tests.Unit.Injector;

public class WpfProcessDetectorArchitectureMappingTests
{
    private const ushort ImageFileMachineUnknown = 0x0000;
    private const ushort ImageFileMachineI386 = 0x014c;
    private const ushort ImageFileMachineAmd64 = 0x8664;
    private const ushort ImageFileMachineArm64 = 0xAA64;

    [Fact]
    public void DetectArchitectureFromMachineTypes_NativeArm64Process_ShouldReturnArm64()
    {
        var result = WpfProcessDetector.DetectArchitectureFromMachineTypes(
            processMachine: ImageFileMachineUnknown,
            nativeMachine: ImageFileMachineArm64,
            is64BitOperatingSystem: true);

        result.Should().Be(ProcessArchitecture.ARM64);
    }

    [Fact]
    public void DetectArchitectureFromMachineTypes_X64EmulatedOnArm64_ShouldReturnX64()
    {
        var result = WpfProcessDetector.DetectArchitectureFromMachineTypes(
            processMachine: ImageFileMachineAmd64,
            nativeMachine: ImageFileMachineArm64,
            is64BitOperatingSystem: true);

        result.Should().Be(ProcessArchitecture.X64);
    }

    [Fact]
    public void DetectArchitectureFromMachineTypes_X86Wow64Process_ShouldReturnX86()
    {
        var result = WpfProcessDetector.DetectArchitectureFromMachineTypes(
            processMachine: ImageFileMachineI386,
            nativeMachine: ImageFileMachineAmd64,
            is64BitOperatingSystem: true);

        result.Should().Be(ProcessArchitecture.X86);
    }

    [Fact]
    public void DetectArchitectureFromMachineTypes_UnknownOn32BitOs_ShouldReturnX86()
    {
        var result = WpfProcessDetector.DetectArchitectureFromMachineTypes(
            processMachine: ImageFileMachineUnknown,
            nativeMachine: ImageFileMachineUnknown,
            is64BitOperatingSystem: false);

        result.Should().Be(ProcessArchitecture.X86);
    }
}
