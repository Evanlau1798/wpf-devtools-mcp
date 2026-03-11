using WpfDevTools.Shared.IO;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// Resolves candidate DLL paths for Inspector and Bootstrapper DLLs.
/// Searches the server directory and solution workspace for matching artifacts.
/// </summary>
internal static class DllCandidateResolver
{
    public static IEnumerable<string> EnumerateInspectorCandidates(string serverDir)
    {
        yield return Path.GetFullPath(Path.Combine(serverDir, "WpfDevTools.Inspector.dll"));
        yield return Path.GetFullPath(Path.Combine(serverDir, "inspectors", "net8.0-windows", "WpfDevTools.Inspector.dll"));
        yield return Path.GetFullPath(Path.Combine(serverDir, "inspectors", "net48", "WpfDevTools.Inspector.dll"));

        foreach (var solutionRoot in RepositoryLayoutLocator.EnumerateSolutionRoots(serverDir))
        {
            var inspectorBinRoot = Path.Combine(solutionRoot, "src", "WpfDevTools.Inspector", "bin");
            var configurations = new[] { "Debug", "Release" };
            var frameworks = new[] { "net8.0-windows", "net48" };

            foreach (var configuration in configurations)
            {
                foreach (var framework in frameworks)
                {
                    yield return Path.GetFullPath(Path.Combine(
                        inspectorBinRoot,
                        configuration,
                        framework,
                        "WpfDevTools.Inspector.dll"));
                }
            }
        }
    }

    public static IEnumerable<string> EnumerateBootstrapperCandidates(string serverDir)
    {
        yield return Path.GetFullPath(Path.Combine(serverDir, "WpfDevTools.Bootstrapper.x64.dll"));
        yield return Path.GetFullPath(Path.Combine(serverDir, "WpfDevTools.Bootstrapper.x86.dll"));
        yield return Path.GetFullPath(Path.Combine(serverDir, "WpfDevTools.Bootstrapper.arm64.dll"));
        yield return Path.GetFullPath(Path.Combine(serverDir, "bootstrapper", "x64", "WpfDevTools.Bootstrapper.x64.dll"));
        yield return Path.GetFullPath(Path.Combine(serverDir, "bootstrapper", "x86", "WpfDevTools.Bootstrapper.x86.dll"));
        yield return Path.GetFullPath(Path.Combine(serverDir, "bootstrapper", "arm64", "WpfDevTools.Bootstrapper.arm64.dll"));

        foreach (var solutionRoot in RepositoryLayoutLocator.EnumerateSolutionRoots(serverDir))
        {
            var artifactsRoot = Path.Combine(solutionRoot, "artifacts", "bootstrapper");
            var configurations = new[] { "Debug", "Release" };
            var platforms = new[] { ("x64", "x64"), ("Win32", "x86"), ("ARM64", "arm64") };

            foreach (var configuration in configurations)
            {
                foreach (var (platform, suffix) in platforms)
                {
                    yield return Path.GetFullPath(Path.Combine(
                        artifactsRoot, configuration, platform,
                        $"WpfDevTools.Bootstrapper.{suffix}.dll"));
                }
            }
        }
    }

    public static string? GetSolutionRoot(string startDirectory)
    {
        return RepositoryLayoutLocator.EnumerateSolutionRoots(startDirectory).FirstOrDefault();
    }
}
