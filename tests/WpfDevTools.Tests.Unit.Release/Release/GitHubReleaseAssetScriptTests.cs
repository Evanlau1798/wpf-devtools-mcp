using System.Text.Json;
using System.IO.Compression;
using FluentAssertions;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed class GitHubReleaseAssetScriptTests
{
    [Fact]
    public void ExportGitHubReleaseAssets_ShouldStageZipAssetsAndChecksums()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var inputRoot = Path.Combine(tempRoot, "release-input");
            var outputRoot = Path.Combine(tempRoot, "release-output");
            Directory.CreateDirectory(inputRoot);
            File.WriteAllText(Path.Combine(inputRoot, "release_1.2.3_win-x64.zip"), "x64-asset");
            File.WriteAllText(Path.Combine(inputRoot, "release_1.2.3_win-x86.zip"), "x86-asset");
            File.WriteAllText(Path.Combine(inputRoot, "ignore.txt"), "not-an-asset");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Export-GitHubReleaseAssets.ps1"),
                new[]
                {
                    "-InputRoot", inputRoot,
                    "-OutputRoot", outputRoot,
                    "-Tag", "v1.2.3",
                    "-OutputJson"
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            var stagedRoot = Path.Combine(outputRoot, "v1.2.3");
            File.Exists(Path.Combine(stagedRoot, "release_1.2.3_win-x64.zip")).Should().BeTrue();
            File.Exists(Path.Combine(stagedRoot, "release_1.2.3_win-x86.zip")).Should().BeTrue();
            File.Exists(Path.Combine(stagedRoot, "SHA256SUMS.txt")).Should().BeTrue();
            File.Exists(Path.Combine(stagedRoot, "release-assets.json")).Should().BeTrue();
            File.Exists(Path.Combine(stagedRoot, "release-sbom.spdx.json")).Should().BeTrue();
            File.Exists(Path.Combine(stagedRoot, "upload-gh-release.ps1")).Should().BeTrue();
            File.ReadAllText(Path.Combine(stagedRoot, "SHA256SUMS.txt"))
                .Should().Contain("release_1.2.3_win-x64.zip")
                .And.Contain("release_1.2.3_win-x86.zip");
            File.ReadAllText(Path.Combine(stagedRoot, "upload-gh-release.ps1"))
                .Should().Contain("$PSScriptRoot")
                .And.Contain("$ReleaseTag")
                .And.Contain("release_1.2.3_win-x64.zip")
                .And.Contain("release-sbom.spdx.json");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void ExportGitHubReleaseAssets_ShouldIncludeDevAssetsInManifest()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var inputRoot = Path.Combine(tempRoot, "release-input");
            var outputRoot = Path.Combine(tempRoot, "release-output");
            Directory.CreateDirectory(inputRoot);
            File.WriteAllText(Path.Combine(inputRoot, "release_1.2.3-dev.1_win-x64.zip"), "dev-asset");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Export-GitHubReleaseAssets.ps1"),
                new[]
                {
                    "-InputRoot", inputRoot,
                    "-OutputRoot", outputRoot,
                    "-Tag", "v1.2.3-dev.1",
                    "-OutputJson"
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputRoot, "v1.2.3-dev.1", "release-assets.json")));
            manifest.RootElement.GetProperty("tag").GetString().Should().Be("v1.2.3-dev.1");
            manifest.RootElement.GetProperty("assets")[0].GetProperty("name").GetString().Should().Be("release_1.2.3-dev.1_win-x64.zip");
            manifest.RootElement.GetProperty("assets")[0].GetProperty("sha256").GetString().Should().NotBeNullOrWhiteSpace();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void ExportGitHubReleaseAssets_ShouldWritePortableUploadScriptAndStableManifest()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var inputRoot = Path.Combine(tempRoot, "release-input");
            var outputRoot = Path.Combine(tempRoot, "release-output");
            Directory.CreateDirectory(inputRoot);
            File.WriteAllText(Path.Combine(inputRoot, "release_1.2.3_win-x64.zip"), "x64-asset");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Export-GitHubReleaseAssets.ps1"),
                new[] { "-InputRoot", inputRoot, "-OutputRoot", outputRoot, "-Tag", "v1.2.3", "-OutputJson" });

            result.ExitCode.Should().Be(0, result.Stderr);
            var stagedRoot = Path.Combine(outputRoot, "v1.2.3");
            var uploadScript = File.ReadAllText(Path.Combine(stagedRoot, "upload-gh-release.ps1"));
            uploadScript.Should().Contain("$PSScriptRoot");
            uploadScript.Should().NotContain("gh release upload $ReleaseTag @assets --clobber");

            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(stagedRoot, "release-assets.json")));
            var asset = manifest.RootElement.GetProperty("assets")[0];
            asset.TryGetProperty("path", out _).Should().BeFalse();
            manifest.RootElement.TryGetProperty("generatedUtc", out _).Should().BeFalse();
            manifest.RootElement.TryGetProperty("outputRoot", out _).Should().BeFalse();
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void ExportGitHubReleaseAssets_ShouldRejectUnsafeReleaseTags()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var inputRoot = Path.Combine(tempRoot, "release-input");
            var outputRoot = Path.Combine(tempRoot, "release-output");
            Directory.CreateDirectory(inputRoot);
            File.WriteAllText(Path.Combine(inputRoot, "release_1.2.3_win-x64.zip"), "x64-asset");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Export-GitHubReleaseAssets.ps1"),
                new[]
                {
                    "-InputRoot", inputRoot,
                    "-OutputRoot", outputRoot,
                    "-Tag", "v1.2.3'; Write-Error injected; #",
                    "-OutputJson"
                });

            result.ExitCode.Should().NotBe(0);
            result.Stderr.Should().Contain("Invalid release tag");
            Directory.Exists(outputRoot).Should().BeFalse(
                "unsafe tags must be rejected before creating staging paths or upload scripts");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void ExportGitHubReleaseAssets_ShouldIncludeSignerMetadataForEachPackageAsset()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var inputRoot = Path.Combine(tempRoot, "release-input");
            var outputRoot = Path.Combine(tempRoot, "release-output");
            Directory.CreateDirectory(inputRoot);

            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot, "x64", useSignedPayload: true, isolateArchiveContents: true);
            var archiveFileName = Path.GetFileName(archivePath);
            var stagedArchivePath = Path.Combine(inputRoot, archiveFileName);
            File.Copy(archivePath, stagedArchivePath, overwrite: true);

            string? expectedThumbprint;
            string? expectedSubject;
            using (var archive = ZipFile.OpenRead(stagedArchivePath))
            using (var stream = archive.GetEntry("bin/manifest.json")!.Open())
            using (var reader = new StreamReader(stream))
            using (var document = JsonDocument.Parse(reader.ReadToEnd()))
            {
                expectedThumbprint = document.RootElement.GetProperty("signerThumbprint").GetString();
                expectedSubject = document.RootElement.GetProperty("signerSubject").GetString();
            }

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Write-ReleaseSidecars.ps1"),
                new[] { "-ArchiveRoot", inputRoot, "-Tag", "v1.2.3", "-OutputJson" });

            result.ExitCode.Should().Be(0, result.Stderr);
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(inputRoot, "release-assets.json")));
            var asset = manifest.RootElement.GetProperty("assets")[0];
            asset.GetProperty("signerThumbprint").GetString().Should().Be(expectedThumbprint);
            asset.GetProperty("signerSubject").GetString().Should().Be(expectedSubject);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void ExportGitHubReleaseAssets_ShouldWriteSpdxSbomForEveryPackageAsset()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var inputRoot = Path.Combine(tempRoot, "release-input");
            var outputRoot = Path.Combine(tempRoot, "release-output");
            Directory.CreateDirectory(inputRoot);
            File.WriteAllText(Path.Combine(inputRoot, "release_1.2.3_win-x64.zip"), "x64-asset");
            File.WriteAllText(Path.Combine(inputRoot, "release_1.2.3_win-x86.zip"), "x86-asset");

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Export-GitHubReleaseAssets.ps1"),
                new[] { "-InputRoot", inputRoot, "-OutputRoot", outputRoot, "-Tag", "v1.2.3", "-OutputJson" });

            result.ExitCode.Should().Be(0, result.Stderr);
            var stagedRoot = Path.Combine(outputRoot, "v1.2.3");
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(stagedRoot, "release-assets.json")));
            using var sbom = JsonDocument.Parse(File.ReadAllText(Path.Combine(stagedRoot, "release-sbom.spdx.json")));

            sbom.RootElement.GetProperty("spdxVersion").GetString().Should().Be("SPDX-2.3");
            sbom.RootElement.GetProperty("name").GetString().Should().Be("wpf-devtools-mcp-v1.2.3");
            var packages = sbom.RootElement.GetProperty("packages").EnumerateArray().ToArray();
            packages.Select(package => package.GetProperty("name").GetString())
                .Should().BeEquivalentTo("release_1.2.3_win-x64.zip", "release_1.2.3_win-x86.zip");

            var manifestAssets = manifest.RootElement.GetProperty("assets").EnumerateArray()
                .ToDictionary(asset => asset.GetProperty("name").GetString()!);
            foreach (var package in packages)
            {
                var packageName = package.GetProperty("name").GetString()!;
                var checksum = package.GetProperty("checksums")[0];
                checksum.GetProperty("algorithm").GetString().Should().Be("SHA256");
                checksum.GetProperty("checksumValue").GetString()
                    .Should().Be(manifestAssets[packageName].GetProperty("sha256").GetString());
            }
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }
}
