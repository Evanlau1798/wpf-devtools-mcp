using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.TestSupport;

public class TestRepositoryPathsTests
{
    [Fact]
    public void ResolveFromBaseDirectory_ShouldFindRepositoryRoot_ForPlatformedBuildOutput()
    {
        var baseDirectory = Path.Combine(
            "G:\\",
            "wpf-devtools-mcp",
            "tests",
            "WpfDevTools.Tests.Unit",
            "bin",
            "x64",
            "Release",
            "net8.0-windows");

        var path = TestRepositoryPaths.ResolveFromBaseDirectory(
            baseDirectory,
            "README.md");

        path.Should().Be(Path.Combine("G:\\", "wpf-devtools-mcp", "README.md"));
    }
}
