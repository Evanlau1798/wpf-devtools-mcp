using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Threading;

namespace WpfDevTools.Tests.Unit.Release;

internal static class ReleaseScriptTestHarness
{
    private static readonly string RepoRoot = ResolveRepoRoot();
    private static readonly TimeSpan DefaultProcessTimeout = TimeSpan.FromSeconds(60);

    public static string CreateTempDirectory()
    {
        var path = Path.Combine(GetRepoFilePath("tmp"), "wpf-devtools-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    public static void DeleteDirectory(string path)
    {
        if (!Directory.Exists(path))
        {
            return;
        }

        for (var attempt = 0; attempt < 10; attempt++)
        {
            try
            {
                Directory.Delete(path, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 9)
            {
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException) when (attempt < 9)
            {
                Thread.Sleep(100);
            }
        }
    }

    public static string CreatePackageDirectory(string tempRoot, string architecture = "x64", bool useSignedPayload = true)
    {
        var packageDir = Path.Combine(tempRoot, "package");
        var binDir = Path.Combine(packageDir, "bin");
        var helperDir = Path.Combine(binDir, "installer");
        Directory.CreateDirectory(binDir);
        Directory.CreateDirectory(helperDir);
        var inspectorNet8Dir = Path.Combine(binDir, "inspectors", "net8.0-windows");
        var inspectorNet48Dir = Path.Combine(binDir, "inspectors", "net48");
        var bootstrapperDir = Path.Combine(binDir, "bootstrapper", architecture);
        Directory.CreateDirectory(inspectorNet8Dir);
        Directory.CreateDirectory(inspectorNet48Dir);
        Directory.CreateDirectory(bootstrapperDir);
        WritePackagePayloadFile(Path.Combine(binDir, $"wpf-devtools-{architecture}.exe"), "notepad.exe", "stub", useSignedPayload);
        WritePackagePayloadFile(Path.Combine(inspectorNet8Dir, "WpfDevTools.Inspector.dll"), "kernel32.dll", "net8-inspector", useSignedPayload);
        WritePackagePayloadFile(Path.Combine(inspectorNet48Dir, "WpfDevTools.Inspector.dll"), "user32.dll", "net48-inspector", useSignedPayload);
        WritePackagePayloadFile(Path.Combine(bootstrapperDir, $"WpfDevTools.Bootstrapper.{architecture}.dll"), "advapi32.dll", "bootstrapper", useSignedPayload);
        File.WriteAllText(
            Path.Combine(binDir, "manifest.json"),
            JsonSerializer.Serialize(new
            {
                name = "wpf-devtools",
                version = "1.2.3",
                architecture,
                runtimeId = architecture == "x86" ? "win-x86" : architecture == "arm64" ? "win-arm64" : "win-x64",
                inspector = new
                {
                    net8 = "bin/inspectors/net8.0-windows/WpfDevTools.Inspector.dll",
                    net48 = "bin/inspectors/net48/WpfDevTools.Inspector.dll"
                },
                bootstrapper = $"bin/bootstrapper/{architecture}/WpfDevTools.Bootstrapper.{architecture}.dll"
            }));

        CopyInstallerHelperFiles(helperDir);

        return packageDir;
    }

    public static string CreatePackageArchive(string tempRoot, string architecture = "x64", bool useSignedPayload = true)
    {
        var packageDir = CreatePackageDirectory(tempRoot, architecture, useSignedPayload);
        var binDir = Path.Combine(packageDir, "bin");
        var helperDir = Path.Combine(binDir, "installer");
        Directory.CreateDirectory(helperDir);
        File.Copy(GetRepoFilePath("scripts/online-installer.ps1"), Path.Combine(binDir, "install.ps1"), overwrite: true);
        File.Copy(GetRepoFilePath("scripts/tools/packaging/run-template.bat"), Path.Combine(packageDir, "run.bat"), overwrite: true);
        CopyInstallerHelperFiles(helperDir);

        var archivePath = Path.Combine(tempRoot, $"release_1.2.3_win-{architecture}.zip");
        if (File.Exists(archivePath))
        {
            File.Delete(archivePath);
        }

        ZipFile.CreateFromDirectory(packageDir, archivePath);
        WriteAdjacentReleaseMetadata(archivePath);
        return archivePath;
    }

    public static void WriteAdjacentReleaseMetadata(string archivePath, string? publishedAssetName = null)
    {
        var archiveFile = new FileInfo(archivePath);
        if (!archiveFile.Exists)
        {
            throw new FileNotFoundException("Archive for release metadata was not found.", archivePath);
        }

        var assetName = string.IsNullOrWhiteSpace(publishedAssetName)
            ? archiveFile.Name
            : publishedAssetName;
        var versionMatch = System.Text.RegularExpressions.Regex.Match(
            assetName,
            @"^release_(?<version>.+)_win-(?<architecture>x64|x86|arm64)\.zip$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var tag = versionMatch.Success
            ? "v" + versionMatch.Groups["version"].Value
            : "vtest";
        var sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(archivePath)))
            .ToLowerInvariant();
        var metadataRoot = archiveFile.DirectoryName!;

        File.WriteAllText(
            Path.Combine(metadataRoot, "SHA256SUMS.txt"),
            $"{sha256}  {assetName}{Environment.NewLine}");

        var manifest = JsonSerializer.Serialize(
            new
            {
                tag,
                assetCount = 1,
                assets = new[]
                {
                    new
                    {
                        name = assetName,
                        sizeBytes = archiveFile.Length,
                        sha256
                    }
                }
            });
        File.WriteAllText(Path.Combine(metadataRoot, "release-assets.json"), manifest);
    }

    public static string CreateFakeCommand(string directory, string commandName, string logPath)
    {
        Directory.CreateDirectory(directory);
        var scriptPath = Path.Combine(directory, commandName + ".cmd");
        File.WriteAllText(
            scriptPath,
            "@echo off" + Environment.NewLine +
            "setlocal EnableDelayedExpansion" + Environment.NewLine +
            "set \"STATE_PATH=" + logPath + ".state\"" + Environment.NewLine +
            $"echo %*>>\"{logPath}\"" + Environment.NewLine +
            "for %%A in (%*) do set \"LAST_ARG=%%~A\"" + Environment.NewLine +
            "if /I \"%1 %2\"==\"mcp add\" >\"%STATE_PATH%\" echo wpf-devtools !LAST_ARG!" + Environment.NewLine +
            "if /I \"%1 %2\"==\"mcp list\" if exist \"%STATE_PATH%\" type \"%STATE_PATH%\"" + Environment.NewLine +
            "exit /b 0" + Environment.NewLine);

        return scriptPath;
    }

    public static (int ExitCode, string Stdout, string Stderr) RunPowerShellScript(
        string scriptPath,
        IEnumerable<string> arguments,
        IReadOnlyDictionary<string, string?>? environmentOverrides = null,
        TimeSpan? timeout = null)
    {
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

        foreach (var argument in arguments)
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

        return RunProcess(startInfo, timeout);
    }

    public static (int ExitCode, string Stdout, string Stderr) RunPowerShellCommand(
        string command,
        IReadOnlyDictionary<string, string?>? environmentOverrides = null,
        TimeSpan? timeout = null)
    {
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

        return RunProcess(startInfo, timeout);
    }

    public static string GetRepoFilePath(string relativePath)
        => Path.GetFullPath(Path.Combine(RepoRoot, relativePath));

    private static void CopyInstallerHelperFiles(string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        var installerDirectory = GetRepoFilePath(Path.Combine("scripts", "installer"));
        var manifestFileName = "installer-helpers.manifest.json";
        File.Copy(
            Path.Combine(installerDirectory, manifestFileName),
            Path.Combine(destinationDirectory, manifestFileName),
            overwrite: true);

        foreach (var helperFile in GetInstallerHelperFiles())
        {
            File.Copy(
                Path.Combine(installerDirectory, helperFile),
                Path.Combine(destinationDirectory, helperFile),
                overwrite: true);
        }
    }

    private static string[] GetInstallerHelperFiles()
    {
        var manifestPath = GetRepoFilePath(Path.Combine("scripts", "installer", "installer-helpers.manifest.json"));
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        return manifest.RootElement.GetProperty("helperFiles")
            .EnumerateArray()
            .Select(static entry => entry.ValueKind == JsonValueKind.Object
                ? entry.GetProperty("path").GetString()
                : entry.GetString())
            .Where(static entry => !string.IsNullOrWhiteSpace(entry))
            .Cast<string>()
            .ToArray();
    }

    private static void WritePackagePayloadFile(string destinationPath, string signedSystemFileName, string unsignedContent, bool useSignedPayload)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        if (useSignedPayload)
        {
            var signedSourcePath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                "System32",
                signedSystemFileName);
            File.Copy(signedSourcePath, destinationPath, overwrite: true);
            return;
        }

        File.WriteAllText(destinationPath, unsignedContent);
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

        if (!process.WaitForExit(timeoutMilliseconds))
        {
            TryKillProcessTree(process);
            try
            {
                process.WaitForExit(2000);
            }
            catch (InvalidOperationException)
            {
            }

            throw new TimeoutException(
                $"PowerShell command timed out after {effectiveTimeout.TotalSeconds:0.###} second(s).");
        }

        return (
            process.ExitCode,
            stdoutTask.GetAwaiter().GetResult(),
            stderrTask.GetAwaiter().GetResult());
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (process.HasExited)
            {
                return;
            }

            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }
        catch
        {
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
                taskKill.WaitForExit(5000);
            }
            catch
            {
            }
        }
    }
}
