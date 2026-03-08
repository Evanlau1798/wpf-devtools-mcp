using System.Diagnostics;
using System.IO.Compression;
using System.Text.Json;
using System.Threading;

namespace WpfDevTools.Tests.Unit.Release;

internal static class ReleaseScriptTestHarness
{
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

    public static string CreatePackageDirectory(string tempRoot, string architecture = "x64")
    {
        var packageDir = Path.Combine(tempRoot, "package");
        Directory.CreateDirectory(packageDir);
        File.WriteAllText(Path.Combine(packageDir, "WpfDevTools.Mcp.Server.exe"), "stub");
        File.WriteAllText(
            Path.Combine(packageDir, "manifest.json"),
            JsonSerializer.Serialize(new
            {
                name = "wpf-devtools",
                version = "1.2.3",
                architecture,
                runtimeId = architecture == "x86" ? "win-x86" : architecture == "arm64" ? "win-arm64" : "win-x64"
            }));

        return packageDir;
    }

    public static string CreatePackageArchive(string tempRoot, string architecture = "x64")
    {
        var packageDir = CreatePackageDirectory(tempRoot, architecture);
        File.Copy(GetRepoFilePath("scripts/release/Install-WpfDevTools.ps1"), Path.Combine(packageDir, "install.ps1"), overwrite: true);
        File.Copy(GetRepoFilePath("scripts/release/Setup-WpfDevTools.ps1"), Path.Combine(packageDir, "setup.ps1"), overwrite: true);
        File.Copy(GetRepoFilePath("scripts/release/Uninstall-WpfDevTools.ps1"), Path.Combine(packageDir, "uninstall.ps1"), overwrite: true);

        var archivePath = Path.Combine(tempRoot, $"WpfDevTools-win-{architecture}.zip");
        if (File.Exists(archivePath))
        {
            File.Delete(archivePath);
        }

        ZipFile.CreateFromDirectory(packageDir, archivePath);
        return archivePath;
    }

    public static string CreateFakeCommand(string directory, string commandName, string logPath)
    {
        Directory.CreateDirectory(directory);
        var scriptPath = Path.Combine(directory, commandName + ".cmd");
        File.WriteAllText(
            scriptPath,
            "@echo off" + Environment.NewLine +
            $"echo %*>>\"{logPath}\"" + Environment.NewLine +
            "exit /b 0" + Environment.NewLine);

        return scriptPath;
    }

    public static (int ExitCode, string Stdout, string Stderr) RunPowerShellScript(
        string scriptPath,
        IEnumerable<string> arguments,
        IReadOnlyDictionary<string, string?>? environmentOverrides = null)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = GetRepoFilePath(".")
        };

        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-ExecutionPolicy");
        process.StartInfo.ArgumentList.Add("Bypass");
        process.StartInfo.ArgumentList.Add("-File");
        process.StartInfo.ArgumentList.Add(scriptPath);

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        if (environmentOverrides is not null)
        {
            foreach (var pair in environmentOverrides)
            {
                process.StartInfo.Environment[pair.Key] = pair.Value ?? string.Empty;
            }
        }

        process.Start();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout, stderr);
    }

    public static string GetRepoFilePath(string relativePath)
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));
}
