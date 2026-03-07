using FluentAssertions;
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
    public void Detect_WithAnyCpuManagedDll_ShouldReturnUnknown()
    {
        // The Inspector DLL is built as AnyCPU - it should be detected as
        // Unknown (compatible with any architecture), NOT as X86
        var inspectorDllPath = Path.Combine(AppContext.BaseDirectory, "WpfDevTools.Inspector.dll");

        if (!File.Exists(inspectorDllPath))
        {
            // Skip if DLL not available in test output
            return;
        }

        var result = PeArchitectureReader.Detect(inspectorDllPath);

        // AnyCPU managed assemblies should return Unknown (compatible with any arch)
        result.Should().Be(ProcessArchitecture.Unknown,
            "AnyCPU managed assemblies should be detected as compatible with any architecture");
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
    public void Detect_WithSharedProjectDll_ShouldReturnUnknown()
    {
        // WpfDevTools.Shared.dll is also AnyCPU
        var sharedDllPath = Path.Combine(AppContext.BaseDirectory, "WpfDevTools.Shared.dll");

        if (!File.Exists(sharedDllPath))
        {
            return;
        }

        var result = PeArchitectureReader.Detect(sharedDllPath);

        result.Should().Be(ProcessArchitecture.Unknown,
            "Shared library (AnyCPU) should be compatible with any architecture");
    }

    [Fact]
    public void Detect_WithNullPath_ShouldReturnUnknown()
    {
        var result = PeArchitectureReader.Detect(null!);

        result.Should().Be(ProcessArchitecture.Unknown);
    }
}
