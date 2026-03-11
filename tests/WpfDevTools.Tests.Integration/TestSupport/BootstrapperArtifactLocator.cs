using System.IO;
using WpfDevTools.Shared.IO;

namespace WpfDevTools.Tests.Integration.TestSupport;

internal static class BootstrapperArtifactLocator
{
    public static bool HasNativeBootstrapper(string startDirectory)
    {
        foreach (var solutionRoot in RepositoryLayoutLocator.EnumerateSolutionRoots(startDirectory))
        {
            var artifactsDir = Path.Combine(solutionRoot, "artifacts", "bootstrapper");
            if (Directory.Exists(artifactsDir))
            {
                var dlls = Directory.GetFiles(
                    artifactsDir,
                    "WpfDevTools.Bootstrapper.*.dll",
                    SearchOption.AllDirectories);
                if (Array.Exists(dlls, static path => new FileInfo(path).Length > 0))
                {
                    return true;
                }
            }
        }

        var localPath = Path.Combine(AppContext.BaseDirectory, "WpfDevTools.Bootstrapper.x64.dll");
        return File.Exists(localPath) && new FileInfo(localPath).Length > 0;
    }
}
