using System.Diagnostics;

namespace WpfDevTools.Tests.Integration.TestSupport;

internal static class LiveTestProcessCleanup
{
    public static void StopAndDispose(Process? process, int timeoutMilliseconds = 5000)
    {
        if (process == null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    if (!process.WaitForExit(timeoutMilliseconds))
                    {
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(timeoutMilliseconds);
                    }
                }
                catch (InvalidOperationException)
                {
                    // Process exited between HasExited and Kill/WaitForExit.
                }
                catch (System.ComponentModel.Win32Exception)
                {
                    // Process handle became unavailable during best-effort cleanup.
                }
            }
        }
        finally
        {
            process.Dispose();
        }
    }
}
