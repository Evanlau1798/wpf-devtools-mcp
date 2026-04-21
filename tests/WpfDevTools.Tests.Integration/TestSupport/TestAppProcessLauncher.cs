using System.Diagnostics;

namespace WpfDevTools.Tests.Integration.TestSupport;

internal static class TestAppProcessLauncher
{
    private static readonly TimeSpan DefaultStartupTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(100);

    public static Process StartAndWaitForMainWindow(string executablePath, TimeSpan? startupTimeout = null)
    {
        var process = Process.Start(new ProcessStartInfo
        {
            FileName = executablePath,
            UseShellExecute = true
        }) ?? throw new InvalidOperationException("Failed to start TestApp process");

        if (WaitForMainWindow(process, startupTimeout ?? DefaultStartupTimeout))
        {
            return process;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill();
                process.WaitForExit(5000);
            }
        }
        catch
        {
        }

        process.Dispose();
        throw new TimeoutException($"Timed out waiting for TestApp main window to become ready within {(startupTimeout ?? DefaultStartupTimeout).TotalSeconds:0.#} seconds.");
    }

    public static string FindTestAppExe()
    {
        return IntegrationExecutableLocator.FindExecutable(
                AppContext.BaseDirectory,
                "tests",
                "WpfDevTools.Tests.TestApp",
                "net8.0-windows",
                "WpfDevTools.Tests.TestApp.exe")
            ?? throw new InvalidOperationException(
                "TestApp executable not found for the current test configuration. Build tests/WpfDevTools.Tests.TestApp first.");
    }

    public static bool WaitForMainWindow(Process process, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < timeout)
        {
            process.Refresh();

            if (process.HasExited)
            {
                return false;
            }

            if (process.MainWindowHandle != IntPtr.Zero)
            {
                return true;
            }

            Thread.Sleep(DefaultPollInterval);
        }

        return false;
    }
}