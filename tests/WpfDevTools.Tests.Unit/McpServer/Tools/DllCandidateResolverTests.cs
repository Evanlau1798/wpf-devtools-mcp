using FluentAssertions;
using WpfDevTools.Mcp.Server.Tools;
using Xunit;

namespace WpfDevTools.Tests.Unit.McpServer.Tools;

public class DllCandidateResolverTests
{
    [Fact]
    public void EnumerateInspectorCandidates_ShouldIncludeReleaseLayoutSubfolders()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var serverDir = Path.Combine(root, "server");
        Directory.CreateDirectory(serverDir);

        try
        {
            var candidates = DllCandidateResolver.EnumerateInspectorCandidates(serverDir).ToArray();

            candidates.Should().Contain(Path.GetFullPath(Path.Combine(serverDir, "WpfDevTools.Inspector.dll")));
            candidates.Should().Contain(Path.GetFullPath(Path.Combine(serverDir, "inspectors", "net8.0-windows", "WpfDevTools.Inspector.dll")));
            candidates.Should().Contain(Path.GetFullPath(Path.Combine(serverDir, "inspectors", "net48", "WpfDevTools.Inspector.dll")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EnumerateBootstrapperCandidates_ShouldIncludeReleaseLayoutSubfolders()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var serverDir = Path.Combine(root, "server");
        Directory.CreateDirectory(serverDir);

        try
        {
            var candidates = DllCandidateResolver.EnumerateBootstrapperCandidates(serverDir).ToArray();

            candidates.Should().Contain(Path.GetFullPath(Path.Combine(serverDir, "WpfDevTools.Bootstrapper.x64.dll")));
            candidates.Should().Contain(Path.GetFullPath(Path.Combine(serverDir, "bootstrapper", "x64", "WpfDevTools.Bootstrapper.x64.dll")));
            candidates.Should().Contain(Path.GetFullPath(Path.Combine(serverDir, "bootstrapper", "x86", "WpfDevTools.Bootstrapper.x86.dll")));
            candidates.Should().Contain(Path.GetFullPath(Path.Combine(serverDir, "bootstrapper", "arm64", "WpfDevTools.Bootstrapper.arm64.dll")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EnumerateBootstrapperCandidates_ShouldIncludePrimaryRepositoryArtifacts_WhenRunningFromWorktree()
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
            var candidates = DllCandidateResolver.EnumerateBootstrapperCandidates(serverDir).ToArray();

            candidates.Should().Contain(Path.GetFullPath(Path.Combine(
                mainRoot, "artifacts", "bootstrapper", "Debug", "x64", "WpfDevTools.Bootstrapper.x64.dll")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void EnumerateInspectorCandidates_ShouldIncludeWorkspaceDebugInspectorArtifact()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var solutionRoot = Path.Combine(root, "repo");
        var serverDir = Path.Combine(solutionRoot, "src", "WpfDevTools.Mcp.Server", "bin", "Debug", "net8.0");
        Directory.CreateDirectory(serverDir);
        File.WriteAllText(Path.Combine(solutionRoot, "WpfDevTools.sln"), string.Empty);

        try
        {
            var candidates = DllCandidateResolver.EnumerateInspectorCandidates(serverDir).ToArray();

            candidates.Should().Contain(Path.GetFullPath(Path.Combine(
                solutionRoot,
                "src",
                "WpfDevTools.Inspector",
                "bin",
                "Debug",
                "net8.0-windows",
                "WpfDevTools.Inspector.dll")));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
