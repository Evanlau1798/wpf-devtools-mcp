using System.Diagnostics;
using System.Text.Json;
using System.Threading;
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
            Directory.CreateDirectory(Path.Combine(packageDir, "bin"));
            File.WriteAllText(Path.Combine(packageDir, "bin", "wpf-devtools-x64.exe"), "stub");
            File.WriteAllText(
                Path.Combine(packageDir, "bin", "manifest.json"),
                JsonSerializer.Serialize(new
                {
                    name = "wpf-devtools",
                    version = "1.2.3",
                    architecture = "x64",
                    runtimeId = "win-x64"
                }));

            var result = RunPowerShellScript(
                GetRepoFilePath("scripts/tools/packaging/Install-WpfDevTools.ps1"),
                new[] { "-PackagePath", packageDir, "-InstallRoot", installRoot, "-Force" });

            result.ExitCode.Should().Be(0, result.Stderr);
            var installBase = Path.Combine(installRoot, "x64");
            var registrationDir = Path.Combine(installBase, "client-registration");
            File.Exists(Path.Combine(installBase, "install-manifest.json")).Should().BeTrue();
            Directory.Exists(registrationDir).Should().BeTrue();
            File.ReadAllText(Path.Combine(registrationDir, "claude-code.txt")).Should().Contain("claude mcp add");
            File.ReadAllText(Path.Combine(registrationDir, "codex-cli.txt")).Should().Contain("codex mcp add");
            File.ReadAllText(Path.Combine(registrationDir, "claude-desktop.json")).Should().Contain("wpf-devtools-x64.exe");
            File.ReadAllText(Path.Combine(registrationDir, "cursor-vscode.json")).Should().Contain("wpf-devtools-x64.exe");
            File.ReadAllText(Path.Combine(registrationDir, "claude-code.project.mcp.json")).Should().Contain("\"mcpServers\"");
            File.ReadAllText(Path.Combine(registrationDir, "claude-code.project.mcp.json")).Should().Contain("wpf-devtools-x64.exe");
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
                GetRepoFilePath("scripts/tools/packaging/Uninstall-WpfDevTools.ps1"),
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
            Directory.CreateDirectory(Path.Combine(packageDir, "bin"));
            File.WriteAllText(
                Path.Combine(packageDir, "bin", "manifest.json"),
                JsonSerializer.Serialize(new
                {
                    name = "wpf-devtools",
                    version = "1.2.3",
                    architecture = "x64",
                    runtimeId = "win-x64"
                }));

            var result = RunPowerShellScript(
                GetRepoFilePath("scripts/tools/packaging/Install-WpfDevTools.ps1"),
                new[] { "-PackagePath", packageDir, "-InstallRoot", installRoot, "-Force" });

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("wpf-devtools");
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void InstallScript_ShouldUsePackageDirectoryWhenPackagePathIsOmitted()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var packageDir = Path.Combine(tempRoot, "package");
            var installRoot = Path.Combine(tempRoot, "install-root");
            Directory.CreateDirectory(Path.Combine(packageDir, "bin"));
            File.WriteAllText(Path.Combine(packageDir, "bin", "wpf-devtools-x64.exe"), "stub");
            File.WriteAllText(
                Path.Combine(packageDir, "bin", "manifest.json"),
                JsonSerializer.Serialize(new
                {
                    name = "wpf-devtools",
                    version = "1.2.3",
                    architecture = "x64",
                    runtimeId = "win-x64"
                }));

            var packageLocalScript = Path.Combine(packageDir, "install.ps1");
            File.Copy(GetRepoFilePath("scripts/tools/packaging/Install-WpfDevTools.ps1"), packageLocalScript, overwrite: true);

            var result = RunPowerShellScript(
                packageLocalScript,
                new[] { "-InstallRoot", installRoot, "-Force" });

            result.ExitCode.Should().Be(0, result.Stderr);
            File.Exists(Path.Combine(installRoot, "x64", "current", "bin", "wpf-devtools-x64.exe")).Should().BeTrue();
        }
        finally
        {
            DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void InstallScript_ShouldWriteAbsolutePathsWhenInstallRootIsRelative()
    {
        var tempRoot = CreateTempDirectory();
        var relativeInstallRoot = Path.Combine("tmp", "relative-install", Guid.NewGuid().ToString("N"));
        var absoluteInstallRoot = GetRepoFilePath(relativeInstallRoot);

        try
        {
            var packageDir = Path.Combine(tempRoot, "package");
            Directory.CreateDirectory(Path.Combine(packageDir, "bin"));
            File.WriteAllText(Path.Combine(packageDir, "bin", "wpf-devtools-x64.exe"), "stub");
            File.WriteAllText(
                Path.Combine(packageDir, "bin", "manifest.json"),
                JsonSerializer.Serialize(new
                {
                    name = "wpf-devtools",
                    version = "1.2.3",
                    architecture = "x64",
                    runtimeId = "win-x64"
                }));

            var result = RunPowerShellScript(
                GetRepoFilePath("scripts/tools/packaging/Install-WpfDevTools.ps1"),
                new[] { "-PackagePath", packageDir, "-InstallRoot", relativeInstallRoot, "-Force" });

            result.ExitCode.Should().Be(0, result.Stderr);

            var registrationDir = Path.Combine(absoluteInstallRoot, "x64", "client-registration");
            File.ReadAllText(Path.Combine(registrationDir, "claude-code.txt"))
                .Should().Contain(Path.Combine(absoluteInstallRoot, "x64", "current", "bin", "wpf-devtools-x64.exe"));
            File.ReadAllText(Path.Combine(registrationDir, "claude-desktop.json"))
                .Should().Contain(Path.Combine(absoluteInstallRoot, "x64", "current", "bin", "wpf-devtools-x64.exe").Replace("\\", "\\\\"));
        }
        finally
        {
            DeleteDirectory(tempRoot);
            DeleteDirectory(absoluteInstallRoot);
        }
    }

    [Fact]
    public void InstallScript_ShouldPersistPackageMetadataForDevelopmentPackages()
    {
        var tempRoot = CreateTempDirectory();
        try
        {
            var packageDir = Path.Combine(tempRoot, "package");
            var installRoot = Path.Combine(tempRoot, "install-root");
            Directory.CreateDirectory(Path.Combine(packageDir, "bin"));
            File.WriteAllText(Path.Combine(packageDir, "bin", "wpf-devtools-x64.exe"), "stub");
            File.WriteAllText(
                Path.Combine(packageDir, "bin", "manifest.json"),
                JsonSerializer.Serialize(new
                {
                    name = "wpf-devtools",
                    version = "1.2.3",
                    architecture = "x64",
                    runtimeId = "win-x64",
                    channel = "dev",
                    buildConfiguration = "Debug",
                    signaturePolicy = "DebugTrustedRootSkip"
                }));

            var result = RunPowerShellScript(
                GetRepoFilePath("scripts/tools/packaging/Install-WpfDevTools.ps1"),
                new[] { "-PackagePath", packageDir, "-InstallRoot", installRoot, "-Force" });

            result.ExitCode.Should().Be(0, result.Stderr);

            using var installManifest = JsonDocument.Parse(
                File.ReadAllText(Path.Combine(installRoot, "x64", "install-manifest.json")));
            installManifest.RootElement.GetProperty("channel").GetString().Should().Be("dev");
            installManifest.RootElement.GetProperty("buildConfiguration").GetString().Should().Be("Debug");
            installManifest.RootElement.GetProperty("signaturePolicy").GetString().Should().Be("DebugTrustedRootSkip");
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
        => WpfDevTools.Tests.Unit.TestSupport.TestRepositoryPaths.GetRepoFilePath(relativePath);
}

