using FluentAssertions;
using WpfDevTools.Injector.Injection;
using Xunit;

namespace WpfDevTools.Tests.Unit.Injector;

public class PeExportReaderTests
{
    [Fact]
    public void GetExportRva_NullPath_ShouldReturnNull()
    {
        var result = PeExportReader.GetExportRva(null!, "BootstrapInspector");

        result.Should().BeNull();
    }

    [Fact]
    public void GetExportRva_NonExistentFile_ShouldReturnNull()
    {
        var result = PeExportReader.GetExportRva(
            @"C:\nonexistent\fake.dll", "BootstrapInspector");

        result.Should().BeNull();
    }

    [Fact]
    public void GetExportRva_EmptyExportName_ShouldReturnNull()
    {
        var result = PeExportReader.GetExportRva("some.dll", "");

        result.Should().BeNull();
    }

    [Fact]
    public void GetExportRva_Kernel32_LoadLibraryW_ShouldReturnNonZeroRva()
    {
        var systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var kernel32Path = Path.Combine(systemDir, "kernel32.dll");

        var result = PeExportReader.GetExportRva(kernel32Path, "LoadLibraryW");

        result.Should().NotBeNull("kernel32.dll exports LoadLibraryW");
        result!.Value.Should().BeGreaterThan(0u);
    }

    [Fact]
    public void GetExportRva_Kernel32_NonExistentExport_ShouldReturnNull()
    {
        var systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
        var kernel32Path = Path.Combine(systemDir, "kernel32.dll");

        var result = PeExportReader.GetExportRva(kernel32Path, "NonExistentFunction12345");

        result.Should().BeNull();
    }
}
