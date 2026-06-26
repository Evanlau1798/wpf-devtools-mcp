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
            DeleteDirectoryWithRetry(root);
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
            DeleteDirectoryWithRetry(root);
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
            DeleteDirectoryWithRetry(root);
        }
    }

    [Fact]
    public void EnumerateCandidates_WithReleasePackageManifest_ShouldNotIncludeWorkspaceArtifacts()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var solutionRoot = Path.Combine(root, "repo");
        var serverDir = Path.Combine(solutionRoot, "tmp", "install", "x64", "current", "bin");
        Directory.CreateDirectory(serverDir);
        File.WriteAllText(Path.Combine(solutionRoot, "WpfDevTools.sln"), string.Empty);
        File.WriteAllText(Path.Combine(serverDir, "manifest.json"), ReleaseManifestJson);

        try
        {
            var inspectorCandidates = DllCandidateResolver.EnumerateInspectorCandidates(serverDir).ToArray();
            var bootstrapperCandidates = DllCandidateResolver.EnumerateBootstrapperCandidates(serverDir).ToArray();

            inspectorCandidates.Should().NotContain(Path.GetFullPath(Path.Combine(
                solutionRoot,
                "src",
                "WpfDevTools.Inspector",
                "bin",
                "Release",
                "net48",
                "WpfDevTools.Inspector.dll")));
            bootstrapperCandidates.Should().NotContain(Path.GetFullPath(Path.Combine(
                solutionRoot,
                "artifacts",
                "bootstrapper",
                "Release",
                "Win32",
                "WpfDevTools.Bootstrapper.x86.dll")));
        }
        finally
        {
            DeleteDirectoryWithRetry(root);
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
            DeleteDirectoryWithRetry(root);
        }
    }

    [Fact]
    public void EnumerateInspectorCandidates_ShouldPreferCurrentBuildConfigurationBeforeDebug()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var solutionRoot = Path.Combine(root, "repo");
        var serverDir = Path.Combine(solutionRoot, "src", "WpfDevTools.Mcp.Server", "bin", "Task2", "net8.0");
        Directory.CreateDirectory(serverDir);
        File.WriteAllText(Path.Combine(solutionRoot, "WpfDevTools.sln"), string.Empty);

        try
        {
            var candidates = DllCandidateResolver.EnumerateInspectorCandidates(serverDir).ToArray();
            var task2Candidate = Path.GetFullPath(Path.Combine(
                solutionRoot,
                "src",
                "WpfDevTools.Inspector",
                "bin",
                "Task2",
                "net8.0-windows",
                "WpfDevTools.Inspector.dll"));
            var debugCandidate = Path.GetFullPath(Path.Combine(
                solutionRoot,
                "src",
                "WpfDevTools.Inspector",
                "bin",
                "Debug",
                "net8.0-windows",
                "WpfDevTools.Inspector.dll"));

            Array.IndexOf(candidates, task2Candidate).Should().BeGreaterThanOrEqualTo(0);
            Array.IndexOf(candidates, debugCandidate).Should().BeGreaterThanOrEqualTo(0);
            Array.IndexOf(candidates, task2Candidate).Should().BeLessThan(Array.IndexOf(candidates, debugCandidate));
        }
        finally
        {
            DeleteDirectoryWithRetry(root);
        }
    }

    private static void DeleteDirectoryWithRetry(string path)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }

                return;
            }
            catch (IOException) when (attempt < 9)
            {
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException) when (attempt < 9)
            {
                Thread.Sleep(100);
            }
        }
    }

    private const string ReleaseManifestJson = """
        {
          "name": "wpf-devtools",
          "version": "1.0.0-beta.14",
          "architecture": "x64",
          "runtimeId": "win-x64",
          "channel": "release",
          "buildConfiguration": "Release",
          "signaturePolicy": "ReleaseChecksumOnly",
          "entryExecutable": "bin/wpf-devtools-x64.exe",
          "inspector": {
            "net8": "bin/inspectors/net8.0-windows/WpfDevTools.Inspector.dll",
            "net48": "bin/inspectors/net48/WpfDevTools.Inspector.dll"
          },
          "bootstrapper": "bin/bootstrapper/x64/WpfDevTools.Bootstrapper.x64.dll"
        }
        """;
}
