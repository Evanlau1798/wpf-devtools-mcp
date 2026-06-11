using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace WpfDevTools.Tests.Integration.TestSupport;

internal static partial class ReleasePackagingTestHarness
{
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
            Debug.WriteLine($"ReleasePackagingTestHarness: best-effort cleanup skipped for '{path}': {lastException.Message}");
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

            var quarantineRoot = Path.Combine(GetRepoFilePath("tmp"), "release-integration-pending-delete");
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
}
