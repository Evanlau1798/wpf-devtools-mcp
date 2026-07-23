using FluentAssertions;
using System.Diagnostics;
using WpfDevTools.Tests.Unit.TestSupport;

namespace WpfDevTools.Tests.Unit.Documentation;

public sealed class BuiltinComposerPackSecurityScriptTests
{
    [Fact]
    public void Script_ShouldValidateEveryRepositoryBuiltinPack()
    {
        var repoRoot = Path.GetDirectoryName(TestRepositoryPaths.GetRepoFilePath("WpfDevTools.sln"))!;
        var result = RunScript(repoRoot);
        var expectedPacks = Directory
            .EnumerateFiles(Path.Combine(repoRoot, "packs", "builtin"), "pack.json", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(Path.Combine(repoRoot, "packs", "builtin"), Path.GetDirectoryName(path)!))
            .Select(path => path.Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();

        result.ExitCode.Should().Be(0, result.Output);
        expectedPacks.Should().NotBeEmpty("the repository should ship at least one built-in Composer pack");
        foreach (var pack in expectedPacks)
        {
            result.Output.Should().Contain(pack);
        }
    }

    [Fact]
    public void Script_ShouldRejectUnapprovedLicenseInAnyBuiltinPack()
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), $"wpfdevtools-pack-security-{Guid.NewGuid():N}");
        try
        {
            var packRoot = Path.Combine(tempRoot, "packs", "builtin", "sample", "1.0.0");
            var baselineRoot = Path.Combine(tempRoot, "packs", "baselines");
            Directory.CreateDirectory(packRoot);
            Directory.CreateDirectory(baselineRoot);
            File.WriteAllText(Path.Combine(packRoot, "pack.json"), """
                {
                  "schemaVersion": "wpfdevtools.ui-pack.v1",
                  "id": "sample",
                  "version": "1.0.0",
                  "kind": "layout-pack",
                  "source": { "lockFile": "source.lock.json" }
                }
                """);
            File.WriteAllText(Path.Combine(packRoot, "install.manifest.json"), """
                {
                  "schemaVersion": "wpfdevtools.pack-install-manifest.v1",
                  "id": "sample",
                  "version": "1.0.0",
                  "scope": "composer-builtin",
                  "path": ".",
                  "enabled": true
                }
                """);
            File.WriteAllText(Path.Combine(packRoot, "source.lock.json"), """
                {
                  "schemaVersion": "wpfdevtools.source-lock.v1",
                  "sources": [
                    {
                      "name": "sample",
                      "url": "https://example.com/sample",
                      "version": "1.0.0",
                      "license": "GPL-3.0"
                    }
                  ]
                }
                """);
            File.WriteAllText(Path.Combine(baselineRoot, "builtin-pack-policy.json"), """
                {
                  "schemaVersion": "wpfdevtools.builtin-pack-baseline-policy.v1",
                  "baselineExemptions": [
                    {
                      "id": "sample",
                      "version": "1.0.0",
                      "reason": "test fixture"
                    }
                  ]
                }
                """);

            var result = RunScript(tempRoot);

            result.ExitCode.Should().NotBe(0);
            result.Output.Should().Contain("unapproved license");
            result.Output.Should().Contain("sample/1.0.0");
        }
        finally
        {
            if (Directory.Exists(tempRoot))
            {
                Directory.Delete(tempRoot, recursive: true);
            }
        }
    }

    private static (int ExitCode, string Output) RunScript(string repoRoot)
    {
        var scriptPath = TestRepositoryPaths.GetRepoFilePath(
            "scripts/tools/security/Test-BuiltinComposerPackSecurity.ps1");
        using var process = new Process();
        process.StartInfo.FileName = "powershell.exe";
        process.StartInfo.ArgumentList.Add("-NoProfile");
        process.StartInfo.ArgumentList.Add("-ExecutionPolicy");
        process.StartInfo.ArgumentList.Add("Bypass");
        process.StartInfo.ArgumentList.Add("-File");
        process.StartInfo.ArgumentList.Add(scriptPath);
        process.StartInfo.ArgumentList.Add("-RepoRoot");
        process.StartInfo.ArgumentList.Add(repoRoot);
        process.StartInfo.RedirectStandardOutput = true;
        process.StartInfo.RedirectStandardError = true;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.CreateNoWindow = true;
        process.Start().Should().BeTrue();

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        process.WaitForExit(30000).Should().BeTrue("built-in pack security scan should complete promptly");
        Task.WaitAll(stdout, stderr);
        return (process.ExitCode, stdout.Result + stderr.Result);
    }
}
