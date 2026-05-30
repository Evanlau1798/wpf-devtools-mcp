using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class SecurityScanScriptTests
{
    [Fact]
    public void RepositorySecretScan_ShouldFailOnSyntheticSecretFixture()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var fixturePath = Path.Combine(tempRoot, "synthetic-secret.txt");
            File.WriteAllText(
                fixturePath,
                "Synthetic fixture only: sk-proj_" + new string('A', 40));

            var scriptPath = ReleaseScriptTestHarness.GetRepoFilePath(
                "scripts/tools/security/Invoke-RepositorySecretScan.ps1");
            File.Exists(scriptPath).Should().BeTrue("the secret scanner should be reusable outside GitHub Actions YAML");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                scriptPath,
                ["-Path", fixturePath]);

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("openai-api-key").And.Contain("synthetic-secret.txt");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void PowerShellScriptAnalyzerGate_ShouldFailOnSyntheticAnalyzerViolation()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var fixturePath = Path.Combine(tempRoot, "InvokeExpressionFixture.ps1");
            File.WriteAllText(fixturePath, "Invoke-Expression $input");

            var scriptPath = ReleaseScriptTestHarness.GetRepoFilePath(
                "scripts/tools/security/Invoke-PowerShellScriptAnalyzerGate.ps1");
            File.Exists(scriptPath).Should().BeTrue("the ScriptAnalyzer gate should be reusable outside GitHub Actions YAML");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                scriptPath,
                ["-Path", tempRoot, "-Severity", "Warning"]);

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("PSAvoidUsingInvokeExpression");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }
}
