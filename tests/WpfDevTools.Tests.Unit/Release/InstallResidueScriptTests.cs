using System.Diagnostics;
using FluentAssertions;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class InstallResidueScriptTests
{
    private static readonly string ScriptPath = TestRepositoryPaths.GetRepoFilePath(
        "scripts/tools/packaging/Test-InstallResidue.ps1");

    [Fact]
    public void Script_ShouldPassWhenInstallRootIsAbsentOrEmpty()
    {
        var root = CreateFixtureRoot();
        try
        {
            Directory.CreateDirectory(root);

            var result = RunScript(root, "x64");

            result.ExitCode.Should().Be(0, result.CombinedOutput);
            result.CombinedOutput.Should().Contain("Install residue validation passed");
        }
        finally
        {
            DeleteFixtureRoot(root);
        }
    }

    [Fact]
    public void Script_ShouldFailWhenInstallerOwnedPayloadOrGeneratedArtifactsRemain()
    {
        var root = CreateFixtureRoot();
        try
        {
            WriteFile(root, "x64/install-manifest.json", "{}");
            WriteFile(root, "x64/current/bin/wpf-devtools-x64.exe", "binary");
            WriteFile(root, "x64/current/bin/install.ps1", "script");
            WriteFile(root, "x64/client-registration/other.mcpServers.json", "{}");
            WriteFile(root, "x64/current.rollback-123/leftover.txt", "rollback");
            WriteFile(root, "x64/current/bin/bootstrap.tmp", "temp");

            var result = RunScript(root, "x64");

            result.ExitCode.Should().NotBe(0);
            result.CombinedOutput.Should().Contain("Residual installer-owned path");
            result.CombinedOutput.Should().Contain("Residual generated registration artifact");
            result.CombinedOutput.Should().Contain("Residual rollback or temporary artifact");
        }
        finally
        {
            DeleteFixtureRoot(root);
        }
    }

    private static string CreateFixtureRoot()
        => Path.Combine(Path.GetTempPath(), $"wpf-devtools-install-residue-{Guid.NewGuid():N}");

    private static void WriteFile(string root, string relativePath, string content)
    {
        var path = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static ScriptResult RunScript(string installRoot, string architecture)
    {
        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("-NoProfile");
        startInfo.ArgumentList.Add("-ExecutionPolicy");
        startInfo.ArgumentList.Add("Bypass");
        startInfo.ArgumentList.Add("-File");
        startInfo.ArgumentList.Add(ScriptPath);
        startInfo.ArgumentList.Add("-InstallRoot");
        startInfo.ArgumentList.Add(installRoot);
        startInfo.ArgumentList.Add("-Architecture");
        startInfo.ArgumentList.Add(architecture);

        using var process = Process.Start(startInfo)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        return new ScriptResult(process.ExitCode, output + error);
    }

    private static void DeleteFixtureRoot(string root)
    {
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed record ScriptResult(int ExitCode, string CombinedOutput);
}
