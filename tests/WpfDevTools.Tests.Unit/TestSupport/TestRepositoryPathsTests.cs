using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.TestSupport;

public class TestRepositoryPathsTests
{
    [Fact]
    public void ResolveFromBaseDirectory_ShouldFindRepositoryRoot_ForPlatformedBuildOutput()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var repoRoot = Path.Combine(tempRoot, "wpf-devtools-mcp");
        var baseDirectory = Path.Combine(
            repoRoot,
            "tests",
            "WpfDevTools.Tests.Unit",
            "bin",
            "x64",
            "Release",
            "net8.0-windows");

        Directory.CreateDirectory(baseDirectory);
        File.WriteAllText(Path.Combine(repoRoot, "WpfDevTools.sln"), string.Empty);

        try
        {
            var path = TestRepositoryPaths.ResolveFromBaseDirectory(
                baseDirectory,
                "README.md");

            path.Should().Be(Path.Combine(repoRoot, "README.md"));
        }
        finally
        {
            Directory.Delete(tempRoot, recursive: true);
        }
    }
}
