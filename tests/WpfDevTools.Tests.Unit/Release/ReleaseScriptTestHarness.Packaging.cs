using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;

namespace WpfDevTools.Tests.Unit.Release;

internal static partial class ReleaseScriptTestHarness
{
    [DllImport("Kernel32.dll", EntryPoint = "CreateHardLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateHardLink(string fileName, string existingFileName, nint securityAttributes);

    public static string CreatePackageDirectory(string tempRoot, string architecture = "x64", bool useSignedPayload = false)
    {
        var packageDir = Path.Combine(tempRoot, "package");
        if (Directory.Exists(packageDir))
        {
            DeleteDirectory(packageDir);
        }

        var cachedArtifacts = GetCachedPackageArtifacts(architecture, useSignedPayload);
        CopyDirectory(cachedArtifacts.PackageDirectoryPath, packageDir);
        return packageDir;
    }

    public static string CreatePackageArchive(
        string tempRoot,
        string architecture = "x64",
        bool useSignedPayload = false,
        bool isolateArchiveContents = false)
    {
        var archivePath = Path.Combine(tempRoot, $"release_1.2.3_win-{architecture}.zip");
        var cachedArtifacts = GetCachedPackageArtifacts(architecture, useSignedPayload);
        ReplicateFile(cachedArtifacts.ArchivePath, archivePath, preferHardLink: !isolateArchiveContents);
        ReplicateFile(
            Path.Combine(cachedArtifacts.MetadataDirectoryPath, "SHA256SUMS.txt"),
            Path.Combine(tempRoot, "SHA256SUMS.txt"),
            preferHardLink: !isolateArchiveContents);
        ReplicateFile(
            Path.Combine(cachedArtifacts.MetadataDirectoryPath, "release-assets.json"),
            Path.Combine(tempRoot, "release-assets.json"),
            preferHardLink: !isolateArchiveContents);
        return archivePath;
    }

    public static void WriteAdjacentReleaseMetadata(string archivePath, string? publishedAssetName = null)
    {
        var archiveFile = new FileInfo(archivePath);
        if (!archiveFile.Exists)
        {
            throw new FileNotFoundException("Archive for release metadata was not found.", archivePath);
        }

        var assetName = string.IsNullOrWhiteSpace(publishedAssetName)
            ? archiveFile.Name
            : publishedAssetName;
        var versionMatch = System.Text.RegularExpressions.Regex.Match(
            assetName,
            @"^release_(?<version>.+)_win-(?<architecture>x64|x86|arm64)\.zip$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var tag = versionMatch.Success
            ? "v" + versionMatch.Groups["version"].Value
            : "vtest";
        var sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(archivePath)))
            .ToLowerInvariant();
        var metadataRoot = archiveFile.DirectoryName!;

        File.WriteAllText(
            Path.Combine(metadataRoot, "SHA256SUMS.txt"),
            $"{sha256}  {assetName}{Environment.NewLine}");

        var manifest = JsonSerializer.Serialize(
            new
            {
                tag,
                assetCount = 1,
                assets = new[]
                {
                    new
                    {
                        name = assetName,
                        sizeBytes = archiveFile.Length,
                        sha256
                    }
                }
            });
        File.WriteAllText(Path.Combine(metadataRoot, "release-assets.json"), manifest);
    }

    public static void WriteAdjacentReleaseMetadata(
        string archivePath,
        string? signerThumbprint,
        string? signerSubject,
        string? publishedAssetName = null)
    {
        var archiveFile = new FileInfo(archivePath);
        if (!archiveFile.Exists)
        {
            throw new FileNotFoundException("Archive for release metadata was not found.", archivePath);
        }

        var assetName = string.IsNullOrWhiteSpace(publishedAssetName)
            ? archiveFile.Name
            : publishedAssetName;
        var versionMatch = System.Text.RegularExpressions.Regex.Match(
            assetName,
            @"^release_(?<version>.+)_win-(?<architecture>x64|x86|arm64)\.zip$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        var tag = versionMatch.Success
            ? "v" + versionMatch.Groups["version"].Value
            : "vtest";
        var sha256 = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(archivePath)))
            .ToLowerInvariant();
        var metadataRoot = archiveFile.DirectoryName!;

        File.WriteAllText(
            Path.Combine(metadataRoot, "SHA256SUMS.txt"),
            $"{sha256}  {assetName}{Environment.NewLine}");

        var manifest = JsonSerializer.Serialize(
            new
            {
                tag,
                assetCount = 1,
                assets = new[]
                {
                    new
                    {
                        name = assetName,
                        sizeBytes = archiveFile.Length,
                        sha256,
                        signerThumbprint,
                        signerSubject
                    }
                }
            });
        File.WriteAllText(Path.Combine(metadataRoot, "release-assets.json"), manifest);
    }

    private static CachedPackageArtifacts GetCachedPackageArtifacts(string architecture, bool useSignedPayload)
    {
        var cacheKey = ComputePackageArtifactCacheKey(architecture, useSignedPayload);
        var lazyArtifacts = PackageArtifactCache.GetOrAdd(
            cacheKey,
            _ => new Lazy<CachedPackageArtifacts>(
                () => BuildCachedPackageArtifacts(cacheKey, architecture, useSignedPayload),
                LazyThreadSafetyMode.ExecutionAndPublication));

        return lazyArtifacts.Value;
    }

    private static string ComputePackageArtifactCacheKey(string architecture, bool useSignedPayload)
    {
        var inputs = new List<string>
        {
            "release-cache-v4",
            architecture,
            useSignedPayload ? "signed" : "unsigned",
            GetFileContentHash(GetRepoFilePath("scripts/online-installer.ps1")),
            GetFileContentHash(GetRepoFilePath(Path.Combine("scripts", "tools", "packaging", "run-template.bat"))),
            GetFileContentHash(GetRepoFilePath(Path.Combine("scripts", "installer", "installer-helpers.manifest.json")))
        };

        foreach (var helperFile in GetInstallerHelperFiles())
        {
            inputs.Add(GetFileContentHash(GetRepoFilePath(Path.Combine("scripts", "installer", helperFile))));
        }

        if (useSignedPayload)
        {
            var signerMetadata = SignedPayload.Value;
            inputs.Add(signerMetadata.Thumbprint);
            inputs.Add(signerMetadata.Subject);
            inputs.Add(signerMetadata.Path);
        }
        else
        {
            inputs.Add("unsigned-payload");
        }

        var content = string.Join("\n", inputs);
        var hashBytes = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static CachedPackageArtifacts BuildCachedPackageArtifacts(string cacheKey, string architecture, bool useSignedPayload)
    {
        var cacheRoot = Path.Combine(GetRepoFilePath("tmp"), "release-script-harness-cache", cacheKey);
        var packageDir = Path.Combine(cacheRoot, "package");
        var archiveLayoutDir = Path.Combine(cacheRoot, "archive-layout");
        var archivePath = Path.Combine(cacheRoot, $"release_1.2.3_win-{architecture}.zip");
        var metadataDirectoryPath = cacheRoot;

        if (Directory.Exists(packageDir) &&
            File.Exists(archivePath) &&
            File.Exists(Path.Combine(metadataDirectoryPath, "SHA256SUMS.txt")) &&
            File.Exists(Path.Combine(metadataDirectoryPath, "release-assets.json")) &&
            IsValidArchive(archivePath))
        {
            return new CachedPackageArtifacts(packageDir, archivePath, metadataDirectoryPath);
        }

        if (Directory.Exists(cacheRoot))
        {
            DeleteDirectory(cacheRoot);
        }

        Directory.CreateDirectory(cacheRoot);
        BuildPackageDirectory(packageDir, architecture, useSignedPayload);
        CopyDirectory(packageDir, archiveLayoutDir);

        var archiveBinDir = Path.Combine(archiveLayoutDir, "bin");
        File.Copy(GetRepoFilePath("scripts/online-installer.ps1"), Path.Combine(archiveBinDir, "install.ps1"), overwrite: true);
        File.Copy(GetRepoFilePath("scripts/tools/packaging/run-template.bat"), Path.Combine(archiveLayoutDir, "run.bat"), overwrite: true);

        ZipFile.CreateFromDirectory(archiveLayoutDir, archivePath);
        WriteAdjacentReleaseMetadata(archivePath);
        DeleteDirectory(archiveLayoutDir);

        return new CachedPackageArtifacts(packageDir, archivePath, metadataDirectoryPath);
    }

    private static void BuildPackageDirectory(string packageDir, string architecture, bool useSignedPayload)
    {
        var binDir = Path.Combine(packageDir, "bin");
        var helperDir = Path.Combine(binDir, "installer");
        Directory.CreateDirectory(binDir);
        Directory.CreateDirectory(helperDir);
        var inspectorNet8Dir = Path.Combine(binDir, "inspectors", "net8.0-windows");
        var inspectorNet48Dir = Path.Combine(binDir, "inspectors", "net48");
        var bootstrapperDir = Path.Combine(binDir, "bootstrapper", architecture);
        var signerMetadata = useSignedPayload ? SignedPayload.Value : UnsignedPayload;
        Directory.CreateDirectory(inspectorNet8Dir);
        Directory.CreateDirectory(inspectorNet48Dir);
        Directory.CreateDirectory(bootstrapperDir);
        WritePackagePayloadFile(Path.Combine(binDir, $"wpf-devtools-{architecture}.exe"), signerMetadata.Path, "stub", useSignedPayload);
        WritePackagePayloadFile(Path.Combine(inspectorNet8Dir, "WpfDevTools.Inspector.dll"), signerMetadata.Path, "net8-inspector", useSignedPayload);
        WritePackagePayloadFile(Path.Combine(inspectorNet48Dir, "WpfDevTools.Inspector.dll"), signerMetadata.Path, "net48-inspector", useSignedPayload);
        WritePackagePayloadFile(Path.Combine(bootstrapperDir, $"WpfDevTools.Bootstrapper.{architecture}.dll"), signerMetadata.Path, "bootstrapper", useSignedPayload);
        File.WriteAllText(
            Path.Combine(binDir, "manifest.json"),
            JsonSerializer.Serialize(new
            {
                name = "wpf-devtools",
                version = "1.2.3",
                architecture,
                runtimeId = architecture == "x86" ? "win-x86" : architecture == "arm64" ? "win-arm64" : "win-x64",
                channel = useSignedPayload ? "release" : "dev",
                buildConfiguration = useSignedPayload ? "Release" : "Debug",
                signaturePolicy = useSignedPayload ? "RequireAuthenticodeSignature" : "DebugTrustedRootSkip",
                entryExecutable = $"bin/wpf-devtools-{architecture}.exe",
                runBatch = "run.bat",
                installScript = "bin/install.ps1",
                signerThumbprint = signerMetadata.Thumbprint,
                signerSubject = signerMetadata.Subject,
                inspector = new
                {
                    net8 = "bin/inspectors/net8.0-windows/WpfDevTools.Inspector.dll",
                    net48 = "bin/inspectors/net48/WpfDevTools.Inspector.dll"
                },
                bootstrapper = $"bin/bootstrapper/{architecture}/WpfDevTools.Bootstrapper.{architecture}.dll"
            }));

        CopyInstallerHelperFiles(helperDir);
    }

    private static void CopyDirectory(string sourceDirectory, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);

        foreach (var directory in Directory.EnumerateDirectories(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, directory);
            Directory.CreateDirectory(Path.Combine(destinationDirectory, relativePath));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories))
        {
            var relativePath = Path.GetRelativePath(sourceDirectory, file);
            var destinationPath = Path.Combine(destinationDirectory, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            File.Copy(file, destinationPath, overwrite: true);
        }
    }

    private static string GetFileContentHash(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        var hashBytes = System.Security.Cryptography.SHA256.HashData(stream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static bool IsValidArchive(string archivePath)
    {
        try
        {
            using var archive = ZipFile.OpenRead(archivePath);
            return archive.Entries.Count >= 0;
        }
        catch (InvalidDataException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static void CopyInstallerHelperFiles(string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        var installerDirectory = GetRepoFilePath(Path.Combine("scripts", "installer"));
        var manifestFileName = "installer-helpers.manifest.json";
        File.Copy(
            Path.Combine(installerDirectory, manifestFileName),
            Path.Combine(destinationDirectory, manifestFileName),
            overwrite: true);

        foreach (var helperFile in GetInstallerHelperFiles())
        {
            File.Copy(
                Path.Combine(installerDirectory, helperFile),
                Path.Combine(destinationDirectory, helperFile),
                overwrite: true);
        }
    }

    private static string[] GetInstallerHelperFiles()
    {
        var manifestPath = GetRepoFilePath(Path.Combine("scripts", "installer", "installer-helpers.manifest.json"));
        using var manifest = JsonDocument.Parse(File.ReadAllText(manifestPath));
        return manifest.RootElement.GetProperty("helperFiles")
            .EnumerateArray()
            .Select(static entry => entry.ValueKind == JsonValueKind.Object
                ? entry.GetProperty("path").GetString()
                : entry.GetString())
            .Where(static entry => !string.IsNullOrWhiteSpace(entry))
            .Cast<string>()
            .ToArray();
    }

    private static void ReplicateFile(string sourcePath, string destinationPath, bool preferHardLink)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        if (File.Exists(destinationPath))
        {
            File.Delete(destinationPath);
        }

        if (preferHardLink && TryCreateHardLink(destinationPath, sourcePath))
        {
            return;
        }

        File.Copy(sourcePath, destinationPath, overwrite: true);
    }

    private static bool TryCreateHardLink(string destinationPath, string sourcePath)
    {
        if (!OperatingSystem.IsWindows())
        {
            return false;
        }

        try
        {
            return CreateHardLink(destinationPath, sourcePath, nint.Zero);
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    private static void WritePackagePayloadFile(string destinationPath, string signedSourcePath, string unsignedContent, bool useSignedPayload)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        if (useSignedPayload)
        {
            File.Copy(signedSourcePath, destinationPath, overwrite: true);
            return;
        }

        File.WriteAllText(destinationPath, unsignedContent);
    }
}
