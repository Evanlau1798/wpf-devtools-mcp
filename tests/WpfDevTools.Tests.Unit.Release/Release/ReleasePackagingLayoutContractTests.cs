using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed partial class ReleasePackagingContractTests
{
    [Fact]
    public void PublishReleaseScript_ShouldLeaveOutputRootWithArchivesAndReleaseSidecarsOnly()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var testRepo = CreateMinimalSkipBuildReleaseRepo(tempRoot, "x64");
            var toolRoot = Path.Combine(tempRoot, "tools");
            Directory.CreateDirectory(toolRoot);

            var fakeDotnet = Path.Combine(toolRoot, "dotnet.cmd");
            File.WriteAllText(fakeDotnet, "@echo off\r\nexit /b 0\r\n");
            var fakeMsbuild = Path.Combine(toolRoot, "msbuild.cmd");
            File.WriteAllText(fakeMsbuild, "@echo off\r\nexit /b 0\r\n");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                testRepo.PackagingScriptPath,
                ["-Configuration", "Release", "-Architectures", "x64", "-OutputRoot", testRepo.OutputRoot, "-SkipBuild"],
                new Dictionary<string, string?>
                {
                    ["PATH"] = toolRoot + ";" + Environment.GetEnvironmentVariable("PATH"),
                    ["WPFDEVTOOLS_PUBLISH_RELEASE_MSBUILD_PATH"] = fakeMsbuild,
                    ["WPFDEVTOOLS_TEST_SIGNATURE_STATUS"] = "Valid"
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            Directory.GetDirectories(testRepo.OutputRoot, "release_*_win-*")
                .Should().BeEmpty("release output should contain GitHub-release-ready assets, not expanded package build directories");
            Directory.GetFiles(testRepo.OutputRoot)
                .Select(Path.GetFileName)
                .Should().BeEquivalentTo(
                [
                    "release_1.2.3_win-x64.zip",
                    "SHA256SUMS.txt",
                    "release-assets.json"
                ]);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }
}
