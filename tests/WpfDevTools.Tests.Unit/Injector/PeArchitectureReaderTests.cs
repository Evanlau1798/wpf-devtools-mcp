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
    public void Detect_WithInspectorDllFromTestOutput_ShouldReturnUnknownForNeutralManagedAssembly()
    {
        var inspectorDllPath = RequireAssemblyFile(typeof(global::WpfDevTools.Inspector.Bootstrap));

        var result = PeArchitectureReader.Detect(inspectorDllPath);

        result.Should().Be(ProcessArchitecture.Unknown,
            "the test output inspector assembly is a neutral IL-only managed DLL and should not be misreported as native x86");
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
    public void Detect_WithSharedProjectDllFromTestOutput_ShouldReturnUnknownForNeutralManagedAssembly()
    {
        var sharedDllPath = RequireAssemblyFile(typeof(ProcessArchitecture));

        var result = PeArchitectureReader.Detect(sharedDllPath);

        result.Should().Be(ProcessArchitecture.Unknown,
            "the shared assembly in unit test output is also a neutral IL-only managed DLL");
    }

    [Fact]
    public void Detect_WithNullPath_ShouldReturnUnknown()
    {
        var result = PeArchitectureReader.Detect(null!);

        result.Should().Be(ProcessArchitecture.Unknown);
    }

    private static string RequireAssemblyFile(Type assemblyMarkerType)
    {
        var assemblyPath = assemblyMarkerType.Assembly.Location;

        assemblyPath.Should().NotBeNullOrWhiteSpace(
            $"{assemblyMarkerType.Assembly.GetName().Name} must be resolved from the unit test output");
        File.Exists(assemblyPath).Should().BeTrue(
            $"{assemblyMarkerType.Assembly.GetName().Name} must exist as a deterministic PE reader test asset");

        return assemblyPath;
    }
}
