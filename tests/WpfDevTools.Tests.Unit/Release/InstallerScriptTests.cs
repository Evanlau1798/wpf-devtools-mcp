using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class InstallerScriptTests
{
    [Fact]
    public void InstallScript_ShouldCreateClientRegistrationArtifacts()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var packageDir = Path.Combine(tempRoot, "package");
            var installRoot = Path.Combine(tempRoot, "install-root");
            Directory.CreateDirectory(packageDir);
            File.WriteAllText(Path.Combine(packageDir, "WpfDevTools.Mcp.Server.exe"), "stub");
            File.WriteAllText(
                Path.Combine(packageDir, "manifest.json"),
                JsonSerializer.Serialize(new
                {
                    name = "wpf-devtools",
                    version = "1.2.3",
                    architecture = "x64",
                    runtimeId = "win-x64"
                }));

            var result = RunPowerShellScript(
                GetRepoFilePath("scripts/release/Install-WpfDevTools.ps1"),
                new[] { "-PackagePath", packageDir, "-InstallRoot", installRoot, "-Force" });

            result.ExitCode.Should().Be(0, result.Stderr);
            var installBase = Path.Combine(installRoot, "x64");
            var registrationDir = Path.Combine(installBase, "client-registration");
            File.Exists(Path.Combine(installBase, "install-manifest.json")).Should().BeTrue();
            Directory.Exists(registrationDir).Should().BeTrue();
            File.ReadAllText(Path.Combine(registrationDir, "claude-code.txt")).Should().Contain("claude mcp add");
            File.ReadAllText(Path.Combine(registrationDir, "codex-cli.txt")).Should().Contain("codex mcp add");
            File.ReadAllText(Path.Combine(registrationDir, "claude-desktop.json")).Should().Contain("WpfDevTools.Mcp.Server.exe");
            File.ReadAllText(Path.Combine(registrationDir, "cursor-vscode.json")).Should().Contain("WpfDevTools.Mcp.Server.exe");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void UninstallScript_ShouldRemoveInstalledArchitectureDirectory()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var installBase = Path.Combine(tempRoot, "install-root", "x64");
            Directory.CreateDirectory(Path.Combine(installBase, "current"));
            File.WriteAllText(Path.Combine(installBase, "install-manifest.json"), "{}");

            var result = RunPowerShellScript(
                GetRepoFilePath("scripts/release/Uninstall-WpfDevTools.ps1"),
                new[] { "-InstallRoot", Path.Combine(tempRoot, "install-root"), "-Architecture", "x64" });

            result.ExitCode.Should().Be(0, result.Stderr);
            Directory.Exists(installBase).Should().BeFalse();
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void InstallScript_ShouldFailWhenServerExecutableIsMissing()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var packageDir = Path.Combine(tempRoot, "package");
            var installRoot = Path.Combine(tempRoot, "install-root");
            Directory.CreateDirectory(packageDir);
            File.WriteAllText(
                Path.Combine(packageDir, "manifest.json"),
                JsonSerializer.Serialize(new
                {
                    name = "wpf-devtools",
                    version = "1.2.3",
                    architecture = "x64",
                    runtimeId = "win-x64"
                }));

            var result = RunPowerShellScript(
                GetRepoFilePath("scripts/release/Install-WpfDevTools.ps1"),
                new[] { "-PackagePath", packageDir, "-InstallRoot", installRoot, "-Force" });

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("WpfDevTools.Mcp.Server.exe");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(GetRepoFilePath("tmp"), "wpf-devtools-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

    private static (int ExitCode, string Stdout, string Stderr) RunPowerShellScript(string scriptPath, string[] arguments)
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

        process.Start();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, stdout, stderr);
    }

    private static string GetRepoFilePath(string relativePath)
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", relativePath));
}
