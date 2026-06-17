using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
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
            ReleaseScriptTestHarness.WriteDummyReleaseArchiveSet(inputRoot);
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
                .And.Contain("release_1.2.3_win-x86.zip")
                .And.Contain("release_1.2.3_win-arm64.zip");
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
            ReleaseScriptTestHarness.WriteDummyReleaseArchiveSet(inputRoot, "1.2.3-dev.1");

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
            manifest.RootElement.GetProperty("assets").EnumerateArray()
                .Select(asset => asset.GetProperty("name").GetString())
                .Should().Contain("release_1.2.3-dev.1_win-x64.zip");
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
            ReleaseScriptTestHarness.WriteDummyReleaseArchiveSet(inputRoot);

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

    [Theory]
    [InlineData("release_1.2.3_win-x64.zip")]
    [InlineData("SHA256SUMS.txt")]
    [InlineData("release-assets.json")]
    [InlineData("release-sbom.spdx.json")]
    [InlineData("package-sbom.spdx.json")]
    public void ExportGitHubReleaseAssets_UploadScriptShouldFailBeforeGhWhenStagedAssetIsMissing(
        string missingAssetName)
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var inputRoot = Path.Combine(tempRoot, "release-input");
            var outputRoot = Path.Combine(tempRoot, "release-output");
            ReleaseScriptTestHarness.WriteDummyReleaseArchiveSet(inputRoot);

            var exportResult = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Export-GitHubReleaseAssets.ps1"),
                new[] { "-InputRoot", inputRoot, "-OutputRoot", outputRoot, "-Tag", "v1.2.3", "-OutputJson" });
            exportResult.ExitCode.Should().Be(0, exportResult.Stderr);

            var stagedRoot = Path.Combine(outputRoot, "v1.2.3");
            File.Delete(Path.Combine(stagedRoot, missingAssetName));
            var uploadResult = ReleaseScriptTestHarness.RunPowerShellScript(
                Path.Combine(stagedRoot, "upload-gh-release.ps1"),
                new[] { "-ReleaseTag", "v1.2.3" });

            uploadResult.ExitCode.Should().NotBe(0);
            (uploadResult.Stdout + Environment.NewLine + uploadResult.Stderr)
                .Should().Contain("missing staged release asset");
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
    public void ExportGitHubReleaseAssets_WithSignedArchiveMetadataAndNoTrustedSigner_ShouldFailClosedInProductionMode()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var inputRoot = Path.Combine(tempRoot, "release-input");
            var outputRoot = Path.Combine(tempRoot, "release-output");
            ReleaseScriptTestHarness.CreatePackageArchiveSet(
                inputRoot,
                useSignedPayload: true,
                isolateArchiveContents: true);

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Export-GitHubReleaseAssets.ps1"),
                new[] { "-InputRoot", inputRoot, "-OutputRoot", outputRoot, "-Tag", "v1.2.3", "-OutputJson" },
                new Dictionary<string, string?>
                {
                    ["WPFDEVTOOLS_INSTALLER_TEST_MODE"] = "0",
                    ["WPFDEVTOOLS_TEST_TRUST_LOCAL_ARCHIVE_RELEASE_METADATA"] = null,
                    ["WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT"] = null
                });

            result.ExitCode.Should().NotBe(0);
            (result.Stdout + Environment.NewLine + result.Stderr)
                .Should().Contain("provide a trusted signer thumbprint");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void ExportGitHubReleaseAssets_WithSignedArchiveMetadataAndTrustedSignerParameter_ShouldSucceedInProductionMode()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var inputRoot = Path.Combine(tempRoot, "release-input");
            var outputRoot = Path.Combine(tempRoot, "release-output");
            var archives = ReleaseScriptTestHarness.CreatePackageArchiveSet(
                inputRoot,
                useSignedPayload: true,
                isolateArchiveContents: true);
            var stagedArchivePath = archives["x64"];
            var signer = ReadArchiveSignerMetadata(stagedArchivePath);

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Export-GitHubReleaseAssets.ps1"),
                new[]
                {
                    "-InputRoot", inputRoot,
                    "-OutputRoot", outputRoot,
                    "-Tag", "v1.2.3",
                    "-TrustedSignerThumbprint", signer.Thumbprint,
                    "-OutputJson"
                },
                new Dictionary<string, string?>
                {
                    ["WPFDEVTOOLS_INSTALLER_TEST_MODE"] = "0",
                    ["WPFDEVTOOLS_TEST_TRUST_LOCAL_ARCHIVE_RELEASE_METADATA"] = null,
                    ["WPFDEVTOOLS_RELEASE_SIGNER_THUMBPRINT"] = null
                });

            result.ExitCode.Should().Be(0, result.Stderr);
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(outputRoot, "v1.2.3", "release-assets.json")));
            var policy = manifest.RootElement.GetProperty("assets")[0].GetProperty("signerTrustPolicy");
            policy.GetProperty("source").GetString().Should().Be("parameter");
            policy.GetProperty("trustedSignerThumbprint").GetString().Should().Be(signer.Thumbprint);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void ExportGitHubReleaseAssets_ShouldWriteAssetLevelSpdxSbomForEveryPackageAsset()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var inputRoot = Path.Combine(tempRoot, "release-input");
            var outputRoot = Path.Combine(tempRoot, "release-output");
            ReleaseScriptTestHarness.WriteDummyReleaseArchiveSet(inputRoot);

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Export-GitHubReleaseAssets.ps1"),
                new[] { "-InputRoot", inputRoot, "-OutputRoot", outputRoot, "-Tag", "v1.2.3", "-OutputJson" });

            result.ExitCode.Should().Be(0, result.Stderr);
            var stagedRoot = Path.Combine(outputRoot, "v1.2.3");
            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(stagedRoot, "release-assets.json")));
            var sbomPath = Path.Combine(stagedRoot, "release-sbom.spdx.json");
            using var sbom = JsonDocument.Parse(File.ReadAllText(sbomPath));

            sbom.RootElement.GetProperty("spdxVersion").GetString().Should().Be("SPDX-2.3");
            sbom.RootElement.GetProperty("name").GetString().Should().Be("wpf-devtools-mcp-v1.2.3-release-assets");
            sbom.RootElement.GetProperty("documentComment").GetString()
                .Should().Contain("release asset SPDX inventory")
                .And.Contain("not a full package/dependency SBOM");
            var sbomSidecar = manifest.RootElement.GetProperty("sidecars")
                .EnumerateArray()
                .Single(sidecar => sidecar.GetProperty("name").GetString() == "release-sbom.spdx.json");
            sbomSidecar.GetProperty("sha256").GetString().Should().Be(ComputeSha256(sbomPath));
            sbomSidecar.GetProperty("role").GetString().Should().Be("release-asset-spdx-sbom");
            var packages = sbom.RootElement.GetProperty("packages").EnumerateArray().ToArray();
            packages.Select(package => package.GetProperty("name").GetString())
                .Should().BeEquivalentTo(
                    "release_1.2.3_win-x64.zip",
                    "release_1.2.3_win-x86.zip",
                    "release_1.2.3_win-arm64.zip");

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

    [Fact]
    public void ExportGitHubReleaseAssets_ShouldWriteFullPackageDependencySbom()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var inputRoot = Path.Combine(tempRoot, "release-input");
            var outputRoot = Path.Combine(tempRoot, "release-output");
            ReleaseScriptTestHarness.CreatePackageArchiveSet(
                inputRoot,
                useSignedPayload: false,
                isolateArchiveContents: true);

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Export-GitHubReleaseAssets.ps1"),
                new[] { "-InputRoot", inputRoot, "-OutputRoot", outputRoot, "-Tag", "v1.2.3", "-OutputJson" });

            result.ExitCode.Should().Be(0, result.Stderr);
            var stagedRoot = Path.Combine(outputRoot, "v1.2.3");
            var packageSbomPath = Path.Combine(stagedRoot, "package-sbom.spdx.json");
            File.Exists(packageSbomPath).Should().BeTrue();

            using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(stagedRoot, "release-assets.json")));
            var packageSidecar = manifest.RootElement.GetProperty("sidecars")
                .EnumerateArray()
                .Single(sidecar => sidecar.GetProperty("name").GetString() == "package-sbom.spdx.json");
            packageSidecar.GetProperty("role").GetString().Should().Be("package-dependency-spdx-sbom");
            packageSidecar.GetProperty("sha256").GetString().Should().Be(ComputeSha256(packageSbomPath));

            var uploadScript = File.ReadAllText(Path.Combine(stagedRoot, "upload-gh-release.ps1"));
            uploadScript.Should().Contain("package-sbom.spdx.json");

            using var sbom = JsonDocument.Parse(File.ReadAllText(packageSbomPath));
            sbom.RootElement.GetProperty("documentComment").GetString()
                .Should().Contain("full package/dependency SBOM");
            sbom.RootElement.GetProperty("packages").EnumerateArray()
                .Select(package => package.GetProperty("name").GetString())
                .Should().Contain("ModelContextProtocol");

            var files = sbom.RootElement.GetProperty("files").EnumerateArray().ToArray();
            files.Select(file => file.GetProperty("fileName").GetString()).Should().Contain(
                "release_1.2.3_win-x64.zip!/bin/install.ps1",
                "release_1.2.3_win-x64.zip!/run.bat",
                "release_1.2.3_win-x64.zip!/bin/wpf-devtools-x64.exe",
                "release_1.2.3_win-x64.zip!/bin/inspectors/net8.0-windows/WpfDevTools.Inspector.dll",
                "release_1.2.3_win-x64.zip!/bin/bootstrapper/x64/WpfDevTools.Bootstrapper.x64.dll",
                "scripts/online-installer.ps1");
            files.Should().OnlyContain(file =>
                file.GetProperty("checksums")[0].GetProperty("checksumValue").GetString()!.Length == 64);
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void WriteReleaseSidecars_ShouldReadLockFilesWhenRepositoryRootPathContainsTmpSegment()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var repoRoot = Path.Combine(tempRoot, "outer", "tmp", "repo");
            var scriptDirectory = Path.Combine(repoRoot, "scripts", "tools", "packaging");
            var archiveRoot = Path.Combine(tempRoot, "release");
            var packageDirectory = Path.Combine(tempRoot, "package");
            Directory.CreateDirectory(scriptDirectory);
            Directory.CreateDirectory(archiveRoot);
            Directory.CreateDirectory(Path.Combine(packageDirectory, "bin"));
            File.Copy(ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Write-ReleaseSidecars.ps1"),
                Path.Combine(scriptDirectory, "Write-ReleaseSidecars.ps1"));
            File.Copy(ReleaseScriptTestHarness.GetRepoFilePath("scripts/tools/packaging/Write-ReleaseSbomDocuments.ps1"),
                Path.Combine(scriptDirectory, "Write-ReleaseSbomDocuments.ps1"));
            File.WriteAllText(Path.Combine(packageDirectory, "run.bat"), "@echo off");
            File.WriteAllText(Path.Combine(packageDirectory, "bin", "install.ps1"), "Write-Host install");
            File.WriteAllText(Path.Combine(repoRoot, "packages.lock.json"), """
                {
                  "version": 1,
                  "dependencies": {
                    "net8.0": {
                      "ModelContextProtocol": {
                        "type": "Direct",
                        "resolved": "0.3.0"
                      }
                    }
                  }
                }
                """);
            ZipFile.CreateFromDirectory(packageDirectory, Path.Combine(archiveRoot, "release_1.2.3_win-x64.zip"));

            var result = ReleaseScriptTestHarness.RunPowerShellScript(
                Path.Combine(scriptDirectory, "Write-ReleaseSidecars.ps1"),
                new[] { "-ArchiveRoot", archiveRoot, "-Tag", "v1.2.3", "-OutputJson" });

            result.ExitCode.Should().Be(0, result.Stderr);
            using var sbom = JsonDocument.Parse(File.ReadAllText(Path.Combine(archiveRoot, "package-sbom.spdx.json")));
            sbom.RootElement.GetProperty("packages").EnumerateArray()
                .Select(package => package.GetProperty("name").GetString())
                .Should().Contain("ModelContextProtocol");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static (string Thumbprint, string? Subject) ReadArchiveSignerMetadata(string archivePath)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        using var stream = archive.GetEntry("bin/manifest.json")!.Open();
        using var document = JsonDocument.Parse(stream);
        return (
            document.RootElement.GetProperty("signerThumbprint").GetString()!,
            document.RootElement.GetProperty("signerSubject").GetString());
    }

    private static string ComputeSha256(string path)
        => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
}
