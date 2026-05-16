using System;
using System.Diagnostics;

namespace WpfDevTools.Tests.Integration.TestSupport;

internal static partial class ReleasePackagingTestHarness
{
    private static readonly TimeSpan DefaultProcessTimeout = TimeSpan.FromSeconds(60);

    private static (int ExitCode, string Stdout, string Stderr) RunProcess(
        ProcessStartInfo startInfo,
        TimeSpan? timeout)
    {
        using var process = new Process { StartInfo = startInfo };
        process.Start();

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        var effectiveTimeout = timeout ?? DefaultProcessTimeout;
        var timeoutMilliseconds = effectiveTimeout.TotalMilliseconds > int.MaxValue
            ? int.MaxValue
            : (int)Math.Ceiling(effectiveTimeout.TotalMilliseconds);
        var timeoutMessage = $"PowerShell command timed out after {effectiveTimeout.TotalSeconds:0.###} second(s).";

        if (!process.WaitForExit(timeoutMilliseconds))
        {
            string? cleanupFailure = null;
            try
            {
                TryKillProcessTree(process);
            }
            catch (Exception ex)
            {
                cleanupFailure = ex.Message;
            }

            try
            {
                process.WaitForExit(2000);
            }
            catch (InvalidOperationException)
            {
            }

            throw new TimeoutException(
                string.IsNullOrWhiteSpace(cleanupFailure)
                    ? timeoutMessage
                    : timeoutMessage + " Cleanup failed: " + cleanupFailure);
        }

        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();
        if (process.ExitCode != 0 && string.IsNullOrWhiteSpace(stderr) && !string.IsNullOrWhiteSpace(stdout))
        {
            stderr = stdout;
        }

        return (process.ExitCode, stdout, stderr);
    }

    private static void TryKillProcessTree(Process process)
    {
        if (process.HasExited)
        {
            return;
        }

        Exception? managedKillFailure = null;
        try
        {
            if (ForceTaskKillFallbackForTesting)
            {
                throw new InvalidOperationException("Forced taskkill fallback for testing.");
            }

            process.Kill(entireProcessTree: true);
            if (WaitForExit(process, 5000))
            {
                return;
            }

            managedKillFailure = new InvalidOperationException(
                $"Managed process-tree kill did not terminate PID {process.Id} within 5000 ms.");
        }
        catch (InvalidOperationException) when (process.HasExited)
        {
            return;
        }
        catch (Exception ex)
        {
            managedKillFailure = ex;
        }

        if (TryTaskKillProcessTree(process, out var fallbackFailure) && WaitForExit(process, 5000))
        {
            return;
        }

        throw new InvalidOperationException(
            string.IsNullOrWhiteSpace(fallbackFailure)
                ? $"Failed to terminate timed-out PowerShell process tree for PID {process.Id}."
                : $"Failed to terminate timed-out PowerShell process tree for PID {process.Id}. {fallbackFailure}",
            managedKillFailure);
    }

    private static bool TryTaskKillProcessTree(Process process, out string failureMessage)
    {
        failureMessage = string.Empty;
        if (process.HasExited)
        {
            return true;
        }

        try
        {
            using var taskKill = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "taskkill.exe",
                    Arguments = $"/PID {process.Id} /T /F",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            taskKill.Start();
            if (!taskKill.WaitForExit(5000))
            {
                failureMessage = "taskkill.exe did not exit within 5000 ms.";
                return false;
            }

            var stdout = taskKill.StandardOutput.ReadToEnd().Trim();
            var stderr = taskKill.StandardError.ReadToEnd().Trim();
            if (taskKill.ExitCode != 0 && !process.HasExited)
            {
                failureMessage = $"taskkill.exe exited with code {taskKill.ExitCode}. stdout: {stdout}; stderr: {stderr}";
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            failureMessage = ex.Message;
            return false;
        }
    }

    private static bool WaitForExit(Process process, int milliseconds)
    {
        try
        {
            return process.HasExited || process.WaitForExit(milliseconds);
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }
}
