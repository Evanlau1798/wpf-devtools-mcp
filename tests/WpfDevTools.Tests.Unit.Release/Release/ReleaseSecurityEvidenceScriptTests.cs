using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class ReleaseSecurityEvidenceScriptTests
{
    [Fact]
    public void WriteReleaseSecurityEvidence_ShouldResolveDefaultRepoRootWhenInvokedDirectly()
    {
            var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var fakeBin = Path.Combine(tempRoot, "bin");
            var dotnetLogPath = Path.Combine(tempRoot, "dotnet.log");
            Directory.CreateDirectory(fakeBin);
            File.WriteAllText(
                Path.Combine(fakeBin, "dotnet.ps1"),
                "param([Parameter(ValueFromRemainingArguments=$true)][string[]]$Arguments)" + Environment.NewLine +
                $"$Arguments[0] | Out-File -LiteralPath '{dotnetLogPath.Replace("'", "''")}' -Append -Encoding utf8" + Environment.NewLine,
                System.Text.Encoding.UTF8);
            var outputPath = Path.Combine(tempRoot, "security-evidence.json");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Write-ReleaseSecurityEvidence.ps1"),
                [
                    "-OutputPath", outputPath,
                    "-Configuration", "Debug",
                    "-ResultsDirectory", Path.Combine(tempRoot, "results")
                ],
                new Dictionary<string, string?>
                {
                    ["PATH"] = fakeBin + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH")
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            File.Exists(outputPath).Should().BeTrue();
            File.ReadAllText(dotnetLogPath).Should().Contain("build").And.Contain("test");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void WriteReleaseSecurityEvidence_ShouldRunReleaseBlockingSecurityTestsBeforeWritingEvidence()
    {
        var content = File.ReadAllText(ReleaseScriptTestHarness.GetRepoFilePath(
            "scripts/tools/packaging/Write-ReleaseSecurityEvidence.ps1"));

        content.Should().Contain("NamedPipeMitmAdversarialMatrixTests");
        content.Should().Contain("ScreenshotResourceIntegrityTests");
        content.Should().Contain("dotnet build");
        content.Should().Contain("dotnet test");
        content.Should().Contain("mitmMatrixPassed = $true");
        content.Should().Contain("screenshotIntegrityPassed = $true");
    }
}
