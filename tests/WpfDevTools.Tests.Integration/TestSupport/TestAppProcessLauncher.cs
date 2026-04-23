using System.Diagnostics;

namespace WpfDevTools.Tests.Integration.TestSupport;

internal static class TestAppProcessLauncher
{
    private static readonly TimeSpan DefaultStartupTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan DefaultPollInterval = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan DefaultInputIdleGracePeriod = TimeSpan.FromMilliseconds(250);

    internal readonly record struct ProcessWindowState(bool HasExited, bool HasMainWindow);

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
        => WaitForMainWindowCore(
            timeout,
            remainingTimeout => WaitForInputIdle(process, remainingTimeout),
            () =>
            {
                process.Refresh();
                return new ProcessWindowState(
                    process.HasExited,
                    process.MainWindowHandle != IntPtr.Zero);
            },
            static delay => Thread.Sleep(delay));

    internal static bool WaitForMainWindowCore(
        TimeSpan timeout,
        Func<TimeSpan, bool> waitForInputIdle,
        Func<ProcessWindowState> readState,
        Action<TimeSpan> sleep)
    {
        if (timeout <= TimeSpan.Zero)
        {
            return false;
        }

        var stopwatch = Stopwatch.StartNew();
        var remainingTimeout = GetRemainingTimeout(timeout, stopwatch);
        if (remainingTimeout > TimeSpan.Zero)
        {
            var inputIdleTimeout = remainingTimeout < DefaultInputIdleGracePeriod
                ? remainingTimeout
                : DefaultInputIdleGracePeriod;
            waitForInputIdle(inputIdleTimeout);
        }

        while ((remainingTimeout = GetRemainingTimeout(timeout, stopwatch)) > TimeSpan.Zero)
        {
            var state = readState();

            if (state.HasExited)
            {
                return false;
            }

            if (state.HasMainWindow)
            {
                return true;
            }

            sleep(remainingTimeout < DefaultPollInterval ? remainingTimeout : DefaultPollInterval);
        }

        return false;
    }

    private static bool WaitForInputIdle(Process process, TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            return false;
        }

        try
        {
            var milliseconds = (int)Math.Min(timeout.TotalMilliseconds, int.MaxValue);
            return process.WaitForInputIdle(milliseconds);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static TimeSpan GetRemainingTimeout(TimeSpan timeout, Stopwatch stopwatch)
    {
        var remainingTimeout = timeout - stopwatch.Elapsed;
        return remainingTimeout > TimeSpan.Zero
            ? remainingTimeout
            : TimeSpan.Zero;
    }
}