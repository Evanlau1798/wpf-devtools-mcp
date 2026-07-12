using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class ReleaseChecksumOnlyInstallerTests
{
    [Fact]
    public void Install_WithReleaseChecksumOnlyPackageAndTrustedShaMetadata_ShouldInstallUnsignedPayload()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = CreateReleaseChecksumOnlyArchive(tempRoot);
            var installRoot = Path.Combine(tempRoot, "install");
            var scriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                scriptPath,
                [
                    "-PackageArchivePath", archivePath,
                    "-TrustedReleaseMetadataDirectory", tempRoot,
                    "-Architecture", "x64",
                    "-Client", "other",
                    "-InstallRoot", installRoot,
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ],
                CreateInstallerEnvironment(tempRoot),
                timeout: TimeSpan.FromMinutes(2));

            result.ExitCode.Should().Be(0, result.Stdout + result.Stderr);
            using var document = JsonDocument.Parse(result.Stdout);
            var installedExecutable = document.RootElement.GetProperty("installedExecutable").GetString();
            File.Exists(installedExecutable).Should().BeTrue();

            var installManifestPath = Path.Combine(installRoot, "x64", "install-manifest.json");
            using var manifest = JsonDocument.Parse(File.ReadAllText(installManifestPath));
            manifest.RootElement.GetProperty("signaturePolicy").GetString().Should().Be("ReleaseChecksumOnly");
            manifest.RootElement.GetProperty("buildConfiguration").GetString().Should().Be("Release");
            manifest.RootElement.GetProperty("channel").GetString().Should().Be("release");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void Install_WithReleaseChecksumOnlyPackageWithoutTrustedShaMetadata_ShouldRejectPackage()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = CreateReleaseChecksumOnlyArchive(tempRoot);
            File.Delete(Path.Combine(tempRoot, "release-assets.json"));
            File.Delete(Path.Combine(tempRoot, "SHA256SUMS.txt"));
            var installRoot = Path.Combine(tempRoot, "install");
            var scriptPath = ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1");
            var environment = CreateInstallerEnvironment(tempRoot);
            environment["WPFDEVTOOLS_TEST_TRUST_LOCAL_ARCHIVE_RELEASE_METADATA"] = "0";

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                scriptPath,
                [
                    "-PackageArchivePath", archivePath,
                    "-Architecture", "x64",
                    "-Client", "other",
                    "-InstallRoot", installRoot,
                    "-NonInteractive",
                    "-Force",
                    "-OutputJson"
                ],
                environment,
                timeout: TimeSpan.FromMinutes(2));

            result.ExitCode.Should().NotBe(0);
            (result.Stdout + result.Stderr).Should().Contain("no trusted release metadata");
            Directory.Exists(Path.Combine(installRoot, "x64", "current")).Should().BeFalse();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static string CreateReleaseChecksumOnlyArchive(string tempRoot)
    {
        var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(
            tempRoot,
            architecture: "x64",
            isolateArchiveContents: true);
        RewritePackageManifestForChecksumOnlyRelease(archivePath);
        ReleaseScriptTestHarness.WriteAdjacentReleaseMetadata(archivePath);
        return archivePath;
    }

    private static void RewritePackageManifestForChecksumOnlyRelease(string archivePath)
    {
        using var archive = ZipFile.Open(archivePath, ZipArchiveMode.Update);
        var entry = archive.GetEntry("bin/manifest.json");
        entry.Should().NotBeNull("test package archives should include bin/manifest.json");

        JsonObject manifest;
        using (var reader = new StreamReader(entry!.Open(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
        {
            manifest = JsonNode.Parse(reader.ReadToEnd())!.AsObject();
        }

        entry.Delete();
        manifest["channel"] = "release";
        manifest["buildConfiguration"] = "Release";
        manifest["signaturePolicy"] = "ReleaseChecksumOnly";
        manifest.Remove("signerThumbprint");
        manifest.Remove("signerSubject");

        var newEntry = archive.CreateEntry("bin/manifest.json", CompressionLevel.Optimal);
        using var writer = new StreamWriter(newEntry.Open(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(manifest.ToJsonString(new JsonSerializerOptions { WriteIndented = false }));
    }

    private static Dictionary<string, string?> CreateInstallerEnvironment(string tempRoot)
        => new()
        {
            ["APPDATA"] = Path.Combine(tempRoot, "AppData", "Roaming"),
            ["LOCALAPPDATA"] = Path.Combine(tempRoot, "AppData", "Local"),
            ["USERPROFILE"] = Path.Combine(tempRoot, "UserProfile"),
            ["TEMP"] = Path.Combine(tempRoot, "Temp"),
            ["TMP"] = Path.Combine(tempRoot, "Temp"),
            ["WPFDEVTOOLS_INSTALLER_ASSUME_ELEVATED"] = "1",
            ["WPFDEVTOOLS_SKIP_ELEVATION"] = "1"
        };
}
