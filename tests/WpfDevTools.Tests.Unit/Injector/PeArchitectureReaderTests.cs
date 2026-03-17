using FluentAssertions;
using System.Runtime.InteropServices;
using WpfDevTools.Injector.Discovery;
using WpfDevTools.Shared.Enums;

namespace WpfDevTools.Tests.Unit.Injector;

/// <summary>
/// Tests for PeArchitectureReader which detects DLL architecture
/// from PE headers, including managed AnyCPU detection via CLR metadata.
/// </summary>
public class PeArchitectureReaderTests
{
    [Fact]
    public void Detect_WithInspectorDllFromCurrentPlatformBuild_ShouldMatchCurrentProcessArchitecture()
    {
        var inspectorDllPath = Path.Combine(AppContext.BaseDirectory, "WpfDevTools.Inspector.dll");

        if (!File.Exists(inspectorDllPath))
        {
            // Skip if DLL not available in test output
            return;
        }

        var result = PeArchitectureReader.Detect(inspectorDllPath);

        result.Should().Be(GetExpectedCurrentArchitecture(),
            "platform-specific build outputs should report the actual PE architecture that will be injected");
    }

    [Fact]
    public void Detect_WithNonExistentFile_ShouldReturnUnknown()
    {
        var result = PeArchitectureReader.Detect("C:\\nonexistent\\fake.dll");

        result.Should().Be(ProcessArchitecture.Unknown);
    }

    [Fact]
    public void Detect_WithEmptyFile_ShouldReturnUnknown()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var result = PeArchitectureReader.Detect(tempFile);

            result.Should().Be(ProcessArchitecture.Unknown);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Detect_WithNonPeFile_ShouldReturnUnknown()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tempFile, new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 });

            var result = PeArchitectureReader.Detect(tempFile);

            result.Should().Be(ProcessArchitecture.Unknown);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void Detect_WithSharedProjectDllFromCurrentPlatformBuild_ShouldMatchCurrentProcessArchitecture()
    {
        var sharedDllPath = Path.Combine(AppContext.BaseDirectory, "WpfDevTools.Shared.dll");

        if (!File.Exists(sharedDllPath))
        {
            return;
        }

        var result = PeArchitectureReader.Detect(sharedDllPath);

        result.Should().Be(GetExpectedCurrentArchitecture(),
            "platform-specific shared outputs should report the actual PE architecture");
    }

    [Fact]
    public void Detect_WithNullPath_ShouldReturnUnknown()
    {
        var result = PeArchitectureReader.Detect(null!);

        result.Should().Be(ProcessArchitecture.Unknown);
    }

    private static ProcessArchitecture GetExpectedCurrentArchitecture() => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.X86 => ProcessArchitecture.X86,
        Architecture.X64 => ProcessArchitecture.X64,
        Architecture.Arm64 => ProcessArchitecture.ARM64,
        _ => ProcessArchitecture.Unknown
    };
}
