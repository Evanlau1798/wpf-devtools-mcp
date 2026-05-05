using System.Diagnostics;
using System.IO;

namespace WpfDevTools.Tests.Integration.TestSupport;

internal static class ReleasePackagingNuGetEnvironment
{
    public static void EnsureStablePackageCache(ProcessStartInfo startInfo, string tmpRoot)
    {
        if (startInfo.Environment.TryGetValue("NUGET_PACKAGES", out var packagesPath) &&
            !string.IsNullOrWhiteSpace(packagesPath))
        {
            return;
        }

        var stablePackagesPath = Path.Combine(tmpRoot, "release-integration-nuget", "packages");
        Directory.CreateDirectory(stablePackagesPath);
        startInfo.Environment["NUGET_PACKAGES"] = stablePackagesPath;
    }
}
