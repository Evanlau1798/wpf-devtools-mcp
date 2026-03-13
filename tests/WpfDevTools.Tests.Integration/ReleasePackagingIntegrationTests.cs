using System.IO;
using System.Text.Json;
using FluentAssertions;
using WpfDevTools.Tests.Integration.TestSupport;
using Xunit;

namespace WpfDevTools.Tests.Integration;

[Collection("PackagingIntegration")]
public sealed class ReleasePackagingIntegrationTests
{
    [Fact]
    public void BuildReleaseScript_ShouldProduceVersionedPackageWithBinLayout()
    {
        var tempRoot = ReleasePackagingTestHarness.CreateTempDirectory();
        try
        {
            var outputRoot = Path.Combine(tempRoot, "release-output");
            var result = ReleasePackagingTestHarness.RunPowerShellScript(
                ReleasePackagingTestHarness.GetRepoFilePath("scripts/build-release.ps1"),
                new[] { "-Configuration", "Debug", "-Architectures", "x64", "-OutputRoot", outputRoot, "-SkipBuild" });

            result.ExitCode.Should().Be(0, result.Stderr);
            var archivePath = Directory.GetFiles(outputRoot, "release_*_win-x64.zip").Single();
            var extractRoot = ReleasePackagingTestHarness.ExtractArchive(archivePath, tempRoot);

            File.Exists(Path.Combine(extractRoot, "install.bat")).Should().BeTrue();
            File.Exists(Path.Combine(extractRoot, "install.ps1")).Should().BeTrue();
            File.Exists(Path.Combine(extractRoot, "manifest.json")).Should().BeTrue();
            File.Exists(Path.Combine(extractRoot, "bin", "WpfDevTools.Mcp.Server.exe")).Should().BeTrue();

            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(extractRoot, "manifest.json")));
            manifest.RootElement.GetProperty("entryExecutable").GetString()
                .Should().Be("bin/WpfDevTools.Mcp.Server.exe");
        }
        finally
        {
            ReleasePackagingTestHarness.DeleteDirectory(tempRoot);
        }
    }
}
