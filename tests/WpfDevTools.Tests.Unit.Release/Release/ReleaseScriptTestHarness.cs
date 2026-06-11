using System.Collections.Concurrent;
using System.Threading;

namespace WpfDevTools.Tests.Unit.Release;

internal static partial class ReleaseScriptTestHarness
{
    private static readonly string RepoRoot = ResolveRepoRoot();
    private const string HarnessTempDirectoryName = "wdt";
    private const string WorkingRootDirectoryName = "wr";
    private static readonly TimeSpan DefaultProcessTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan SelfSignedPayloadTimeout = TimeSpan.FromMinutes(3);
    private static readonly ConcurrentDictionary<string, Lazy<CachedPackageArtifacts>> PackageArtifactCache = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, byte> GeneratedCertificateThumbprints = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Lazy<SignedPayloadInfo> SignedPayload =
        new(ResolveSignedPayloadInfo, LazyThreadSafetyMode.ExecutionAndPublication);
    private static int generatedCertificateCleanupRegistered;

    internal static bool ForceTaskKillFallbackForTesting { get; set; }

    private sealed record CachedPackageArtifacts(string PackageDirectoryPath, string ArchivePath, string MetadataDirectoryPath);

    private sealed record SignedPayloadInfo(string Path, string Thumbprint, string Subject);

    private static readonly SignedPayloadInfo UnsignedPayload = new(
        string.Empty,
        "0000000000000000000000000000000000000000",
        "CN=WPFDEVTOOLS UNSIGNED TEST PAYLOAD");

    public static string CreateTempDirectory()
    {
        var path = CreateShortTempDirectory("t");
        Directory.CreateDirectory(path);
        return path;
    }

    private static string CreateShortTempDirectory(string prefix)
        => Path.Combine(GetHarnessTempRoot(), prefix + Guid.NewGuid().ToString("N")[..16]);

    private static string GetHarnessTempRoot()
        => Path.Combine(Path.GetTempPath(), HarnessTempDirectoryName);

    public static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        Exception? lastException = null;

        static void NormalizeAttributes(string root)
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(entry, FileAttributes.Normal);
            }

            File.SetAttributes(root, FileAttributes.Normal);
        }

        for (var attempt = 0; attempt < 60; attempt++)
        {
            try
            {
                if (attempt > 0)
                {
                    NormalizeAttributes(path);
                }

                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException ex)
            {
                lastException = ex;
                if (!Directory.Exists(path))
                {
                    return;
                }

                if (attempt == 0 && TryQuarantineDirectory(path))
                {
                    return;
                }

                if (attempt < 59)
                {
                    Thread.Sleep(250);
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                lastException = ex;
                if (!Directory.Exists(path))
                {
                    return;
                }

                if (attempt == 0 && TryQuarantineDirectory(path))
                {
                    return;
                }

                if (attempt < 59)
                {
                    Thread.Sleep(250);
                }
            }
        }

        TryQuarantineDirectory(path);

        if (Directory.Exists(path) && lastException is not null)
        {
            System.Diagnostics.Debug.WriteLine($"ReleaseScriptTestHarness: best-effort cleanup skipped for '{path}': {lastException.Message}");
        }
    }

    private static bool TryQuarantineDirectory(string path)
    {
        try
        {
            if (!Directory.Exists(path))
            {
                return true;
            }

            var quarantineRoot = Path.Combine(GetHarnessTempRoot(), "pd");
            Directory.CreateDirectory(quarantineRoot);
            var quarantinePath = Path.Combine(quarantineRoot, Path.GetFileName(path) + "-" + Guid.NewGuid().ToString("N"));
            Directory.Move(path, quarantinePath);
            return !Directory.Exists(path);
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static string GetRepoFilePath(string relativePath)
        => Path.GetFullPath(Path.Combine(RepoRoot, relativePath));

    private static string ResolveRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, ".git")) ||
                Directory.Exists(Path.Combine(current.FullName, ".git")) ||
                File.Exists(Path.Combine(current.FullName, "WpfDevTools.sln")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test base directory.");
    }
}
