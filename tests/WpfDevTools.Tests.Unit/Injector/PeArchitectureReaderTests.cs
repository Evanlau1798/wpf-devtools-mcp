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
    public void Detect_WithManagedAnyCpuPe_ShouldReturnUnknown()
    {
        var dllPath = CreateManagedI386PeFixture(corFlags: 0x00000001);
        try
        {
            var result = PeArchitectureReader.Detect(dllPath);

            result.Should().Be(ProcessArchitecture.Unknown,
                "IL-only managed I386 assemblies without 32BITREQUIRED are neutral AnyCPU assets");
        }
        finally
        {
            File.Delete(dllPath);
        }
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
    public void Detect_WithManaged32BitRequiredPe_ShouldReturnX86()
    {
        var dllPath = CreateManagedI386PeFixture(corFlags: 0x00000003);
        try
        {
            var result = PeArchitectureReader.Detect(dllPath);

            result.Should().Be(ProcessArchitecture.X86,
                "managed I386 assemblies with 32BITREQUIRED are architecture-specific");
        }
        finally
        {
            File.Delete(dllPath);
        }
    }

    [Fact]
    public void Detect_WithNullPath_ShouldReturnUnknown()
    {
        var result = PeArchitectureReader.Detect(null!);

        result.Should().Be(ProcessArchitecture.Unknown);
    }

    private static string CreateManagedI386PeFixture(uint corFlags)
    {
        const int PeOffset = 0x80;
        const int OptionalHeaderOffset = PeOffset + 24;
        const int SizeOfOptionalHeader = 224;
        const int DataDirectoriesOffset = OptionalHeaderOffset + 96;
        const int ClrDirectoryIndex = 14;
        const int SectionTableOffset = OptionalHeaderOffset + SizeOfOptionalHeader;
        const uint ClrRva = 0x2000;
        const uint ClrFileOffset = 0x300;

        var path = Path.Combine(Path.GetTempPath(), $"wpf-devtools-pe-{Guid.NewGuid():N}.dll");
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new BinaryWriter(stream);
        writer.Write(new byte[0x400]);

        WriteAt(0, () => writer.Write((ushort)0x5A4D));
        WriteAt(0x3C, () => writer.Write(PeOffset));
        WriteAt(PeOffset, () => writer.Write(0x00004550u));
        WriteAt(PeOffset + 4, () => writer.Write((ushort)0x014C));
        WriteAt(PeOffset + 6, () => writer.Write((ushort)1));
        WriteAt(PeOffset + 20, () => writer.Write((ushort)SizeOfOptionalHeader));
        WriteAt(OptionalHeaderOffset, () => writer.Write((ushort)0x010B));
        WriteAt(DataDirectoriesOffset + (ClrDirectoryIndex * 8), () =>
        {
            writer.Write(ClrRva);
            writer.Write(0x48u);
        });
        WriteAt(SectionTableOffset, () => writer.Write(new byte[8]));
        WriteAt(SectionTableOffset + 8, () => writer.Write(0x100u));
        WriteAt(SectionTableOffset + 12, () => writer.Write(ClrRva));
        WriteAt(SectionTableOffset + 16, () => writer.Write(0x200u));
        WriteAt(SectionTableOffset + 20, () => writer.Write(ClrFileOffset));
        WriteAt(ClrFileOffset + 16, () => writer.Write(corFlags));

        return path;

        void WriteAt(long offset, Action write)
        {
            stream.Seek(offset, SeekOrigin.Begin);
            write();
        }
    }
}
