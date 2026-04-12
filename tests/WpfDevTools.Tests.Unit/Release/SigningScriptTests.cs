using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class SigningScriptTests
{
    [Fact]
    public void SignBinariesScript_ShouldAvoidPassingPfxPasswordsToSigntoolArguments()
    {
        var content = File.ReadAllText(
            ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/Sign-Binaries.ps1"));

        content.Should().NotContain("/p $Password");
        content.Should().Contain("Import-PfxCertificate",
            "the script should import the PFX with a secure password handling path and sign by thumbprint instead of exposing the password in process arguments");
        content.Should().Contain("CertificateThumbprint");
    }

    [Fact]
    public void SignBinariesScript_ShouldSupportSigntoolAndRootOverridesForDeterministicTests()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var logPath = Path.Combine(tempRoot, "signtool.log");
            var fakeSigntool = ReleaseScriptTestHarness.CreateFakeCommand(tempRoot, "fake-signtool", logPath);
            var fakeRoot = Path.Combine(tempRoot, "fake-build-root");
            var binaryDir = Path.Combine(fakeRoot, "src", "Sample", "bin", "Release");
            Directory.CreateDirectory(binaryDir);
            File.WriteAllText(Path.Combine(binaryDir, "WpfDevTools.Sample.exe"), "stub");

            var certificatePath = Path.Combine(tempRoot, "dummy.pfx");
            File.WriteAllText(certificatePath, "not-a-real-pfx");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/Sign-Binaries.ps1"),
                new[]
                {
                    "-CertificateThumbprint", "ABCD1234",
                    "-BuildConfiguration", "Release"
                },
                new Dictionary<string, string?>
                {
                    ["WPFDEVTOOLS_SIGNTOOL_PATH"] = fakeSigntool,
                    ["WPFDEVTOOLS_SIGN_BINARIES_ROOTS"] = fakeRoot
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            var invocation = File.ReadAllText(logPath);
            invocation.Should().Contain("WpfDevTools.Sample.exe");
            invocation.Should().Contain("/sha1");
            invocation.Should().Contain("ABCD1234");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }
}
