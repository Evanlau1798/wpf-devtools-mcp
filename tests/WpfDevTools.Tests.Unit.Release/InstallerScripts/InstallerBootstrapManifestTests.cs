using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using WpfDevTools.Tests.Unit;
using Xunit;

namespace WpfDevTools.Tests.Unit.Release;

public sealed partial class InstallerBootstrapTests
{
    [Fact]
    public void InstallerHelperManifest_ShouldMatchCurrentHelperFiles()
    {
        var installerDirectory = ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer");
        var manifestPath = Path.Combine(installerDirectory, "installer-helpers.manifest.json");
        File.Exists(manifestPath).Should().BeTrue();

        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var helperFiles = manifest.RootElement.GetProperty("helperFiles")
            .EnumerateArray()
            .Select(static entry => entry.ValueKind == JsonValueKind.Object
                ? entry.GetProperty("path").GetString()
                : entry.GetString())
            .Where(static entry => !string.IsNullOrWhiteSpace(entry))
            .Cast<string>()
            .ToArray();

        helperFiles.Should().NotBeEmpty();
        foreach (var helperFile in helperFiles)
        {
            File.Exists(Path.Combine(installerDirectory, helperFile)).Should().BeTrue();
        }

        var expectedCacheKey = ComputeManifestCacheKey(installerDirectory, helperFiles);
        manifest.RootElement.GetProperty("cacheKey").GetString().Should().Be(expectedCacheKey);
    }

    [Fact]
    public void InstallerHelperManifest_ShouldOnlyReferenceGitTrackedHelperFiles()
    {
        var repoRoot = ReleaseScriptTestHarness.GetRepoFilePath(".");
        CanValidateTrackedFilesWithGit(repoRoot).Should().BeTrue(
            "release manifest validation requires git metadata and the git executable");

        var installerDirectory = ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer");
        var manifestPath = Path.Combine(installerDirectory, "installer-helpers.manifest.json");
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        var helperFiles = manifest.RootElement.GetProperty("helperFiles")
            .EnumerateArray()
            .Select(static entry => entry.ValueKind == JsonValueKind.Object
                ? entry.GetProperty("path").GetString()
                : entry.GetString())
            .Where(static entry => !string.IsNullOrWhiteSpace(entry))
            .Cast<string>()
            .ToArray();

        foreach (var helperFile in helperFiles)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = repoRoot,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("ls-files");
            startInfo.ArgumentList.Add("--error-unmatch");
            startInfo.ArgumentList.Add(Path.Combine("scripts", "installer", helperFile).Replace('\\', '/'));

            using var process = Process.Start(startInfo)!;
            WaitForGitValidationExit(process, helperFile);
            process.ExitCode.Should().Be(0, $"{helperFile} must be tracked because the installer manifest ships it");
        }
    }

    [Fact]
    public void InstallerHelperManifestIntegrity_ShouldFail_WhenAHelperRecordIsMissing()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var helperRoot = Path.Combine(tempRoot, "installer");
            Directory.CreateDirectory(helperRoot);

            var sourceHelperRoot = ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer");
            foreach (var sourcePath in Directory.GetFiles(sourceHelperRoot, "*.ps1"))
            {
                File.Copy(sourcePath, Path.Combine(helperRoot, Path.GetFileName(sourcePath)), overwrite: true);
            }

            var manifestPath = Path.Combine(helperRoot, "installer-helpers.manifest.json");
            var manifestNode = JsonNode.Parse(File.ReadAllText(Path.Combine(sourceHelperRoot, "installer-helpers.manifest.json")))!.AsObject();
            var helperFiles = manifestNode["helperFiles"]!.AsArray();
            var filteredHelperFiles = new JsonArray();
            foreach (var entry in helperFiles)
            {
                if (entry is null)
                {
                    continue;
                }

                var path = entry["path"]?.GetValue<string>() ?? entry.GetValue<string>();
                if (string.Equals(path, "Installer.Actions.ps1", StringComparison.Ordinal))
                {
                    continue;
                }

                filteredHelperFiles.Add(entry.DeepClone());
            }

            manifestNode["helperFiles"] = filteredHelperFiles;
            File.WriteAllText(manifestPath, manifestNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

            var command = $$"""
$env:APPDATA='{{Path.Combine(tempRoot, "AppData", "Roaming").Replace("'", "''")}}'
$env:LOCALAPPDATA='{{Path.Combine(tempRoot, "AppData", "Local").Replace("'", "''")}}'
$env:USERPROFILE='{{Path.Combine(tempRoot, "UserProfile").Replace("'", "''")}}'
{{OnlineInstallerScriptTestHarness.BuildDefinitionOnlyPrelude("-Action install -Version latest -Architecture x64 -Client other -NonInteractive")}}
$manifest = Read-TuiHelperManifest -ManifestPath '{{manifestPath.Replace("'", "''")}}' -HelperDirectory '{{helperRoot.Replace("'", "''")}}'
Assert-InstallerHelperManifestIntegrity -HelperDirectory '{{helperRoot.Replace("'", "''")}}' -Manifest $manifest
""";

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().NotBe(0);
            (result.Stdout + Environment.NewLine + result.Stderr)
                .Should().MatchRegex("must exactly match the expected helper file set|missing integrity metadata",
                    "bootstrap should fail before loading helper code when the manifest no longer provides a complete integrity record set");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstallerDefinitions_ShouldNotTrustInstalledHelperManifestAsBootstrapAuthority()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var scriptRoot = Path.Combine(tempRoot, "scripts");
            Directory.CreateDirectory(scriptRoot);
            var scriptPath = Path.Combine(scriptRoot, "online-installer.ps1");
            File.Copy(ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"), scriptPath);

            var installRoot = Path.Combine(tempRoot, "install-root");
            var installedHelperRoot = Path.Combine(installRoot, "x64", "current", "bin", "installer");
            Directory.CreateDirectory(installedHelperRoot);
            var bootstrapPath = Path.Combine(installedHelperRoot, "Installer.BootstrapUi.ps1");
            File.WriteAllText(bootstrapPath, "$global:MaliciousInstalledBootstrapLoaded = 'yes'");
            var bootstrapBytes = File.ReadAllBytes(bootstrapPath);
            var bootstrapHash = Convert.ToHexString(SHA256.HashData(bootstrapBytes)).ToLowerInvariant();
            var manifest = new
            {
                schemaVersion = 1,
                cacheKey = "sha256:test",
                helperFiles = new[]
                {
                    new
                    {
                        path = "Installer.BootstrapUi.ps1",
                        sha256 = bootstrapHash,
                        sizeBytes = bootstrapBytes.Length
                    }
                }
            };
            File.WriteAllText(
                Path.Combine(installedHelperRoot, "installer-helpers.manifest.json"),
                JsonSerializer.Serialize(manifest));

            var command = $$"""
$global:MaliciousInstalledBootstrapLoaded = 'no'
{{OnlineInstallerScriptTestHarness.BuildDefinitionOnlyPrelude("-Action uninstall -Architecture x64 -Client other -InstallRoot '" + installRoot.Replace("'", "''") + "' -NonInteractive", scriptPath, enableInternalTestMode: false)}}
$global:MaliciousInstalledBootstrapLoaded
""";

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(command);

            result.ExitCode.Should().Be(0, result.Stderr);
            result.Stdout.Trim().Should().Be("no");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstallerDefinitions_ShouldRejectSelfAttestedLocalHelperManifestBeforeBootstrapImport()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var scriptRoot = Path.Combine(tempRoot, "scripts");
            var helperRoot = Path.Combine(scriptRoot, "installer");
            Directory.CreateDirectory(helperRoot);

            var scriptPath = Path.Combine(scriptRoot, "online-installer.ps1");
            File.Copy(ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"), scriptPath);
            CopyInstallerHelpers(helperRoot);

            var markerPath = Path.Combine(tempRoot, "self-attested-bootstrap.marker");
            var bootstrapPath = Path.Combine(helperRoot, "Installer.BootstrapUi.ps1");
            File.WriteAllText(
                bootstrapPath,
                "Set-Content -LiteralPath $env:WPFDEVTOOLS_SELF_ATTESTED_HELPER_MARKER -Value executed -Encoding UTF8" +
                Environment.NewLine);
            RewriteHelperManifestForDirectory(helperRoot);

            var command = $$"""
{{OnlineInstallerScriptTestHarness.BuildDefinitionOnlyPrelude("-Action install -Architecture x64 -Client other -NonInteractive", scriptPath, enableInternalTestMode: false)}}
$null = Ensure-TuiHelpersAvailable
'loaded'
""";

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(
                command,
                new Dictionary<string, string?>
                {
                    ["APPDATA"] = Path.Combine(tempRoot, "AppData", "Roaming"),
                    ["LOCALAPPDATA"] = Path.Combine(tempRoot, "AppData", "Local"),
                    ["USERPROFILE"] = Path.Combine(tempRoot, "UserProfile"),
                    ["WPFDEVTOOLS_SELF_ATTESTED_HELPER_MARKER"] = markerPath,
                    ["WPFDEVTOOLS_INSTALLER_TEST_MODE"] = "0"
                });

            result.ExitCode.Should().NotBe(0);
            (result.Stdout + Environment.NewLine + result.Stderr)
                .Should().Contain("pinned installer helper manifest cache key");
            File.Exists(markerPath).Should().BeFalse(
                "a helper plus a matching co-located manifest must not be trusted before an independent helper trust anchor is checked");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    [Fact]
    public void OnlineInstallerDefinitions_ShouldRejectArchiveTrustedHelperManifestWithDifferentCacheKey()
    {
        var tempRoot = ReleaseScriptTestHarness.CreateTempDirectory();
        try
        {
            var archivePath = ReleaseScriptTestHarness.CreatePackageArchive(tempRoot, "x64", isolateArchiveContents: true);
            var extractRoot = Path.Combine(tempRoot, "modified-package");
            System.IO.Compression.ZipFile.ExtractToDirectory(archivePath, extractRoot);

            var helperRoot = Path.Combine(extractRoot, "bin", "installer");
            var markerPath = Path.Combine(tempRoot, "archive-helper.marker");
            File.AppendAllText(
                Path.Combine(helperRoot, "Installer.Actions.ps1"),
                Environment.NewLine +
                "Set-Content -LiteralPath $env:WPFDEVTOOLS_ARCHIVE_HELPER_MARKER -Value executed -Encoding UTF8" +
                Environment.NewLine);
            RewriteHelperManifestForDirectory(helperRoot);

            File.Delete(archivePath);
            System.IO.Compression.ZipFile.CreateFromDirectory(extractRoot, archivePath);
            ReleaseScriptTestHarness.WriteAdjacentReleaseMetadata(archivePath);

            var scriptPath = Path.Combine(tempRoot, "online-installer.ps1");
            var workingRoot = Path.Combine(tempRoot, "working");
            File.Copy(ReleaseScriptTestHarness.GetRepoFilePath("scripts/online-installer.ps1"), scriptPath);

            var command = $$"""
{{OnlineInstallerScriptTestHarness.BuildDefinitionOnlyPrelude("-Action install -Version 1.2.3 -Architecture x64 -Client other -PackageArchivePath '" + archivePath.Replace("'", "''") + "' -TrustedReleaseMetadataDirectory '" + tempRoot.Replace("'", "''") + "' -WorkingRoot '" + workingRoot.Replace("'", "''") + "' -NonInteractive", scriptPath, enableInternalTestMode: false)}}
$helperRoot = Ensure-TuiHelpersAvailable
foreach ($helperPath in @(Import-TuiHelpers)) {
    . $helperPath
}
'loaded'
""";

            var result = ReleaseScriptTestHarness.RunPowerShellCommand(
                command,
                new Dictionary<string, string?>
                {
                    ["APPDATA"] = Path.Combine(tempRoot, "AppData", "Roaming"),
                    ["LOCALAPPDATA"] = Path.Combine(tempRoot, "AppData", "Local"),
                    ["USERPROFILE"] = Path.Combine(tempRoot, "UserProfile"),
                    ["WPFDEVTOOLS_ARCHIVE_HELPER_MARKER"] = markerPath,
                    ["WPFDEVTOOLS_INSTALLER_TEST_MODE"] = "0"
                });

            result.ExitCode.Should().NotBe(0);
            (result.Stdout + Environment.NewLine + result.Stderr)
                .Should().Contain("pinned installer helper manifest cache key");
            File.Exists(markerPath).Should().BeFalse(
                "release archive metadata must not be allowed to self-attest executable helper scripts before an independent helper trust anchor is checked");
        }
        finally
        {
            ReleaseScriptTestHarness.DeleteDirectory(tempRoot);
        }
    }

    private static string ComputeManifestCacheKey(string installerDirectory, IReadOnlyCollection<string> helperFiles)
    {
        var records = helperFiles
            .OrderBy(static file => file, StringComparer.OrdinalIgnoreCase)
            .Select(file => file + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(installerDirectory, file)))).ToLowerInvariant())
            .ToArray();
        return "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", records)))).ToLowerInvariant();
    }

    private static void CopyInstallerHelpers(string helperRoot)
    {
        var sourceHelperRoot = ReleaseScriptTestHarness.GetRepoFilePath("scripts/installer");
        foreach (var sourcePath in Directory.EnumerateFiles(sourceHelperRoot))
        {
            File.Copy(sourcePath, Path.Combine(helperRoot, Path.GetFileName(sourcePath)), overwrite: true);
        }
    }

    private static void RewriteHelperManifestForDirectory(string helperRoot)
    {
        var manifestPath = Path.Combine(helperRoot, "installer-helpers.manifest.json");
        var manifestNode = JsonNode.Parse(File.ReadAllText(manifestPath))!.AsObject();
        var helperFiles = manifestNode["helperFiles"]!.AsArray();
        var helperNames = new List<string>();

        foreach (var entry in helperFiles)
        {
            var entryObject = entry!.AsObject();
            var helperName = entryObject["path"]!.GetValue<string>();
            var helperPath = Path.Combine(helperRoot, helperName);
            var helperBytes = File.ReadAllBytes(helperPath);

            entryObject["sha256"] = Convert.ToHexString(SHA256.HashData(helperBytes)).ToLowerInvariant();
            entryObject["sizeBytes"] = helperBytes.Length;
            helperNames.Add(helperName);
        }

        manifestNode["cacheKey"] = ComputeManifestCacheKey(helperRoot, helperNames);
        File.WriteAllText(manifestPath, manifestNode.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static bool CanValidateTrackedFilesWithGit(string repoRoot)
    {
        var gitMetadataPath = Path.Combine(repoRoot, ".git");
        if (!Directory.Exists(gitMetadataPath) && !File.Exists(gitMetadataPath))
        {
            return false;
        }

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                WorkingDirectory = repoRoot,
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("--version");

            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return false;
            }

            if (!process.WaitForExit(5000))
            {
                process.Kill(entireProcessTree: true);
                return false;
            }

            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }

    private static void WaitForGitValidationExit(Process process, string helperFile)
    {
        if (process.WaitForExit(5000))
        {
            return;
        }

        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
        }

        throw new TimeoutException($"Timed out validating tracked installer helper '{helperFile}'.");
    }
}
