using System.IO;
using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Threading;
using System.Linq;

namespace WpfDevTools.Tests.Integration.TestSupport;

internal static class ReleasePackagingTestHarness
{
    private static readonly string RepoRoot = ResolveRepoRoot();
    private static readonly TimeSpan DefaultProcessTimeout = TimeSpan.FromSeconds(60);

    internal static bool ForceTaskKillFallbackForTesting { get; set; }

    public static string GetRepoFilePath(string relativePath)
        => Path.GetFullPath(Path.Combine(RepoRoot, relativePath));

    public static string CreateTempDirectory()
    {
        var path = Path.Combine(GetRepoFilePath("tmp"), "release-integration", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

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

    public static (int ExitCode, string Stdout, string Stderr) RunPowerShellScript(
        string scriptPath,
        IEnumerable<string> arguments,
        IReadOnlyDictionary<string, string?>? environmentOverrides = null,
        TimeSpan? timeout = null)
    {
        var argumentList = arguments.ToList();
        var canReuseSharedEnvironmentRoot = string.Equals(Path.GetFileName(scriptPath), "online-installer.ps1", StringComparison.OrdinalIgnoreCase);
        var (environmentRoot, ownsEnvironmentRoot) = canReuseSharedEnvironmentRoot
            ? ResolveProcessEnvironmentRoot(environmentOverrides)
            : (Path.Combine(GetRepoFilePath("tmp"), "release-integration-env", Guid.NewGuid().ToString("N")), true);
        string? injectedWorkingRoot = null;
        if (string.Equals(Path.GetFileName(scriptPath), "online-installer.ps1", StringComparison.OrdinalIgnoreCase) &&
            !argumentList.Contains("-WorkingRoot", StringComparer.OrdinalIgnoreCase))
        {
            injectedWorkingRoot = ownsEnvironmentRoot
                ? Path.Combine(environmentRoot, "wpf-devtools-working-root")
                : Path.Combine(environmentRoot, "wpf-devtools-working-root", Guid.NewGuid().ToString("N"));
            argumentList.Add("-WorkingRoot");
            argumentList.Add(injectedWorkingRoot);
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = GetRepoFilePath(".")
        };

        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(scriptPath);

        foreach (var argument in argumentList)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (environmentOverrides is not null)
        {
            foreach (var pair in environmentOverrides)
            {
                startInfo.Environment[pair.Key] = pair.Value ?? string.Empty;
            }
        }

        EnsureIsolatedProcessEnvironment(startInfo, environmentRoot, environmentOverrides);

        if (!startInfo.Environment.ContainsKey("WPFDEVTOOLS_INSTALLER_TEST_MODE"))
        {
            startInfo.Environment["WPFDEVTOOLS_INSTALLER_TEST_MODE"] = "1";
        }

        try
        {
            return RunProcess(startInfo, timeout);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(injectedWorkingRoot) && !ownsEnvironmentRoot)
            {
                DeleteDirectory(injectedWorkingRoot);
            }

            if (ownsEnvironmentRoot)
            {
                DeleteDirectory(environmentRoot);
            }
        }
    }

    public static (int ExitCode, string Stdout, string Stderr) RunPowerShellCommand(
        string command,
        IReadOnlyDictionary<string, string?>? environmentOverrides = null,
        TimeSpan? timeout = null)
    {
        var isolatedEnvironmentRoot = Path.Combine(GetRepoFilePath("tmp"), "release-integration-env", Guid.NewGuid().ToString("N"));
        var startInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = GetRepoFilePath(".")
        };

        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-Command");
        startInfo.ArgumentList.Add(command);

        if (environmentOverrides is not null)
        {
            foreach (var pair in environmentOverrides)
            {
                startInfo.Environment[pair.Key] = pair.Value ?? string.Empty;
            }
        }

        EnsureIsolatedProcessEnvironment(startInfo, isolatedEnvironmentRoot, environmentOverrides);

        if (!startInfo.Environment.ContainsKey("WPFDEVTOOLS_INSTALLER_TEST_MODE"))
        {
            startInfo.Environment["WPFDEVTOOLS_INSTALLER_TEST_MODE"] = "1";
        }

        try
        {
            return RunProcess(startInfo, timeout);
        }
        finally
        {
            DeleteDirectory(isolatedEnvironmentRoot);
        }
    }

    public static string CreateFakeCommand(
        string directory,
        string commandName,
        string logPath,
        string trailingCommand = "exit /b 0")
    {
        Directory.CreateDirectory(directory);
        var scriptPath = Path.Combine(directory, commandName + ".cmd");
        File.WriteAllText(
            scriptPath,
            "@echo off" + Environment.NewLine +
            $"echo %*>>\"{logPath}\"" + Environment.NewLine +
            trailingCommand + Environment.NewLine);

        return scriptPath;
    }

    public static string ExtractArchive(string archivePath, string tempRoot)
    {
        var extractRoot = Path.Combine(tempRoot, "extracted");
        ZipFile.ExtractToDirectory(archivePath, extractRoot);
        return extractRoot;
    }

    public static JsonDocument ParseJson(string json)
        => JsonDocument.Parse(json);

    private static (string EnvironmentRoot, bool OwnsEnvironmentRoot) ResolveProcessEnvironmentRoot(
        IReadOnlyDictionary<string, string?>? environmentOverrides)
    {
        if (TryResolveSharedEnvironmentRoot(environmentOverrides, out var sharedEnvironmentRoot))
        {
            return (sharedEnvironmentRoot, false);
        }

        return (Path.Combine(GetRepoFilePath("tmp"), "release-integration-env", Guid.NewGuid().ToString("N")), true);
    }

    private static bool TryResolveSharedEnvironmentRoot(
        IReadOnlyDictionary<string, string?>? environmentOverrides,
        out string sharedEnvironmentRoot)
    {
        sharedEnvironmentRoot = string.Empty;
        if (environmentOverrides is null ||
            !TryGetPathOverride(environmentOverrides, "APPDATA", out var appDataPath) ||
            !TryGetPathOverride(environmentOverrides, "LOCALAPPDATA", out var localAppDataPath) ||
            !TryGetPathOverride(environmentOverrides, "USERPROFILE", out var userProfilePath))
        {
            return false;
        }

        var appDataRoot = TryGetEnvironmentRootFromAppDataPath(appDataPath, "Roaming");
        var localAppDataRoot = TryGetEnvironmentRootFromAppDataPath(localAppDataPath, "Local");
        if (appDataRoot is null ||
            localAppDataRoot is null ||
            !string.Equals(appDataRoot, localAppDataRoot, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var userProfileRoot = Directory.GetParent(userProfilePath)?.FullName;
        if (string.IsNullOrWhiteSpace(userProfileRoot) ||
            !string.Equals(appDataRoot, userProfileRoot, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        sharedEnvironmentRoot = appDataRoot;
        return true;
    }

    private static bool TryGetPathOverride(
        IReadOnlyDictionary<string, string?> environmentOverrides,
        string key,
        out string path)
    {
        path = string.Empty;
        if (!environmentOverrides.TryGetValue(key, out var value) || string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        path = Path.GetFullPath(value);
        return true;
    }

    private static string? TryGetEnvironmentRootFromAppDataPath(string appDataPath, string leafDirectoryName)
    {
        var leafDirectory = new DirectoryInfo(appDataPath);
        if (!leafDirectory.Name.Equals(leafDirectoryName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var appDataDirectory = leafDirectory.Parent;
        if (appDataDirectory is null || !appDataDirectory.Name.Equals("AppData", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return appDataDirectory.Parent?.FullName;
    }

    private static void EnsureIsolatedProcessEnvironment(
        ProcessStartInfo startInfo,
        string environmentRoot,
        IReadOnlyDictionary<string, string?>? environmentOverrides)
    {
        var roamingAppData = Path.Combine(environmentRoot, "AppData", "Roaming");
        var localAppData = Path.Combine(environmentRoot, "AppData", "Local");
        var tempPath = Path.Combine(environmentRoot, "Temp");
        Directory.CreateDirectory(roamingAppData);
        Directory.CreateDirectory(localAppData);
        Directory.CreateDirectory(tempPath);

        startInfo.Environment["USERPROFILE"] = environmentOverrides is not null && TryGetPathOverride(environmentOverrides, "USERPROFILE", out var userProfileOverride)
            ? userProfileOverride
            : environmentRoot;
        startInfo.Environment["APPDATA"] = environmentOverrides is not null && TryGetPathOverride(environmentOverrides, "APPDATA", out var appDataOverride)
            ? appDataOverride
            : roamingAppData;
        startInfo.Environment["LOCALAPPDATA"] = environmentOverrides is not null && TryGetPathOverride(environmentOverrides, "LOCALAPPDATA", out var localAppDataOverride)
            ? localAppDataOverride
            : localAppData;
        startInfo.Environment["TEMP"] = environmentOverrides is not null && TryGetPathOverride(environmentOverrides, "TEMP", out var tempOverride)
            ? tempOverride
            : tempPath;
        startInfo.Environment["TMP"] = environmentOverrides is not null && TryGetPathOverride(environmentOverrides, "TMP", out var tmpOverride)
            ? tmpOverride
            : startInfo.Environment["TEMP"];
    }

    private static string ResolveRepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, ".git")) ||
                Directory.Exists(Path.Combine(current.FullName, ".git")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root from test base directory.");
    }

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

        return (
            process.ExitCode,
            stdoutTask.GetAwaiter().GetResult(),
            stderrTask.GetAwaiter().GetResult());
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
