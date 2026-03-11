using FluentAssertions;
using WpfDevTools.Shared.IO;
using Xunit;

namespace WpfDevTools.Tests.Unit.Shared;

public class RepositoryLayoutLocatorTests
{
    [Fact]
    public void EnumerateSolutionRoots_ShouldIncludePrimaryRepositoryRoot_ForWorktreeSolutions()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var mainRoot = Path.Combine(root, "repo");
        var worktreeRoot = Path.Combine(mainRoot, ".worktrees", "feature-branch");
        var serverDir = Path.Combine(worktreeRoot, "src", "WpfDevTools.Mcp.Server", "bin", "Debug");

        Directory.CreateDirectory(serverDir);
        File.WriteAllText(Path.Combine(mainRoot, "WpfDevTools.sln"), string.Empty);
        File.WriteAllText(Path.Combine(worktreeRoot, "WpfDevTools.sln"), string.Empty);

        try
        {
            var roots = RepositoryLayoutLocator.EnumerateSolutionRoots(serverDir).ToArray();

            roots.Should().ContainInOrder(
                Path.GetFullPath(worktreeRoot),
                Path.GetFullPath(mainRoot));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EnumerateSolutionRoots_ShouldReturnOnlyNearestRoot_WhenNotInWorktree()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var solutionRoot = Path.Combine(root, "repo");
        var serverDir = Path.Combine(solutionRoot, "src", "WpfDevTools.Mcp.Server", "bin", "Debug");

        Directory.CreateDirectory(serverDir);
        File.WriteAllText(Path.Combine(solutionRoot, "WpfDevTools.sln"), string.Empty);

        try
        {
            var roots = RepositoryLayoutLocator.EnumerateSolutionRoots(serverDir).ToArray();

            roots.Should().Equal(Path.GetFullPath(solutionRoot));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
