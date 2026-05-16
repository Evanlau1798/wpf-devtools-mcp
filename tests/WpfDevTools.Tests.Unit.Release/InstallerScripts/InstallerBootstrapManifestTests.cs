using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using FluentAssertions;
using WpfDevTools.Tests.Unit;
using Xunit;
using Xunit.Sdk;

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
        if (!CanValidateTrackedFilesWithGit(repoRoot))
        {
            throw SkipException.ForSkip("Git metadata or the git executable is unavailable, so tracked-file validation cannot run in this environment.");
        }

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

    private static string ComputeManifestCacheKey(string installerDirectory, IReadOnlyCollection<string> helperFiles)
    {
        var records = helperFiles
            .OrderBy(static file => file, StringComparer.OrdinalIgnoreCase)
            .Select(file => file + ":" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(Path.Combine(installerDirectory, file)))).ToLowerInvariant())
            .ToArray();
        return "sha256:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("|", records)))).ToLowerInvariant();
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
