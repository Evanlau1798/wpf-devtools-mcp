using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Documentation;

public class BootstrapperClrHostingContractTests
{
    [Fact]
    public void ClrHostingSource_ShouldResolveClrCreateInstanceFromMscoreeDllAtRuntime()
    {
        var content = File.ReadAllText(GetRepoFilePath(
            "src/WpfDevTools.Bootstrapper/clr_hosting.cpp"));

        content.Should().Contain("LoadLibraryW(L\"mscoree.dll\")",
            "ARM64 packaging should not depend on an architecture-specific mscoree import library just to locate CLRCreateInstance");
        content.Should().Contain("GetProcAddress",
            "the bootstrapper should bind CLRCreateInstance dynamically from mscoree.dll at runtime");
    }

    [Fact]
    public void ClrHostingSource_ShouldNotStaticallyLinkMscoreeImportLibrary()
    {
        var content = File.ReadAllText(GetRepoFilePath(
            "src/WpfDevTools.Bootstrapper/clr_hosting.cpp"));

        content.Should().NotContain("#pragma comment(lib, \"mscoree.lib\")",
            "static mscoree import-library linkage is brittle across ARM64 release toolchains");
    }

    private static string GetRepoFilePath(string relativePath)
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}
