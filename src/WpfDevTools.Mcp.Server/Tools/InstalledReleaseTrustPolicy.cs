using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace WpfDevTools.Mcp.Server.Tools;

internal static class InstalledReleaseTrustPolicy
{
    private const string ReleaseChecksumOnly = "ReleaseChecksumOnly";
    private const string ReleaseChannel = "release";
    private const string ReleaseConfiguration = "Release";

    public static bool CanSkipSignatureForChecksumOnlyPayload(
        string dllPath,
        string baseDirectory)
        => CanSkipSignatureForChecksumOnlyPayload(
            dllPath,
            baseDirectory,
            Environment.ProcessPath);

    internal static bool CanSkipSignatureForChecksumOnlyPayload(
        string dllPath,
        string baseDirectory,
        string? processPath)
    {
        try
        {
            return CanSkipSignatureForChecksumOnlyPayloadCore(
                dllPath,
                baseDirectory,
                processPath);
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (IOException)
        {
            return false;
        }
        catch (InvalidDataException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool CanSkipSignatureForChecksumOnlyPayloadCore(
        string dllPath,
        string baseDirectory,
        string? processPath)
    {
        if (string.IsNullOrWhiteSpace(dllPath)
            || string.IsNullOrWhiteSpace(baseDirectory)
            || string.IsNullOrWhiteSpace(processPath))
        {
            return false;
        }

        var binDirectory = NormalizeDirectory(baseDirectory);
        if (!string.Equals(Path.GetFileName(binDirectory), "bin", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var currentDirectory = Directory.GetParent(binDirectory)?.FullName;
        if (currentDirectory is null)
        {
            return false;
        }

        var packageManifestPath = Path.Combine(binDirectory, "manifest.json");
        if (!File.Exists(packageManifestPath))
        {
            return false;
        }

        using var packageManifest = JsonDocument.Parse(File.ReadAllText(packageManifestPath));
        var packageRoot = packageManifest.RootElement;

        if (!IsChecksumOnlyReleaseManifest(packageRoot))
        {
            return false;
        }

        var normalizedCurrentDirectory = NormalizeDirectory(currentDirectory);
        var normalizedProcessPath = NormalizeFile(processPath);
        var packageExecutable = ResolvePackageRelativePath(
            normalizedCurrentDirectory,
            GetString(packageRoot, "entryExecutable"));
        if (!PathEquals(packageExecutable, normalizedProcessPath))
        {
            return false;
        }

        var normalizedDllPath = NormalizeFile(dllPath);
        var payloadRelativePath = EnumerateManifestPayloadRelativePaths(packageRoot)
            .FirstOrDefault(relativePath => PathEquals(
                ResolvePackageRelativePath(normalizedCurrentDirectory, relativePath),
                normalizedDllPath));
        if (payloadRelativePath is null)
        {
            return false;
        }

        return CanSkipInstalledChecksumOnlyPayload(
                   packageRoot,
                   normalizedCurrentDirectory,
                   normalizedProcessPath)
               || CanSkipPortableChecksumOnlyPayload(
                   packageRoot,
                   packageManifestPath,
                   normalizedCurrentDirectory,
                   normalizedProcessPath,
                   normalizedDllPath,
                   payloadRelativePath);
    }

    private static bool CanSkipInstalledChecksumOnlyPayload(
        JsonElement packageRoot,
        string currentDirectory,
        string processPath)
    {
        var installBase = Directory.GetParent(currentDirectory)?.FullName;
        if (installBase is null)
        {
            return false;
        }

        var installManifestPath = Path.Combine(installBase, "install-manifest.json");
        if (!File.Exists(installManifestPath))
        {
            return false;
        }

        using var installManifest = JsonDocument.Parse(File.ReadAllText(installManifestPath));
        var installRoot = installManifest.RootElement;

        if (!IsChecksumOnlyReleaseManifest(installRoot)
            || !ManifestValuesMatch(packageRoot, installRoot, "version")
            || !ManifestValuesMatch(packageRoot, installRoot, "architecture"))
        {
            return false;
        }

        var installDir = GetString(installRoot, "installDir");
        var installedExecutable = GetString(installRoot, "executable");
        return PathEquals(installDir, currentDirectory)
               && PathEquals(installedExecutable, processPath);
    }

    private static bool CanSkipPortableChecksumOnlyPayload(
        JsonElement packageRoot,
        string packageManifestPath,
        string packageDirectory,
        string processPath,
        string dllPath,
        string payloadRelativePath)
    {
        var archiveName = GetPortableArchiveName(packageRoot);
        if (archiveName is null)
        {
            return false;
        }

        foreach (var releaseDirectory in EnumeratePortableReleaseDirectories(packageDirectory))
        {
            var archivePath = Path.Combine(releaseDirectory, archiveName);
            var shaSidecarPath = Path.Combine(releaseDirectory, "SHA256SUMS.txt");
            if (!File.Exists(archivePath) || !File.Exists(shaSidecarPath))
            {
                continue;
            }

            if (!VerifyArchiveHashSidecar(archivePath, shaSidecarPath, archiveName))
            {
                continue;
            }

            var executableRelativePath = GetString(packageRoot, "entryExecutable");
            if (VerifyArchiveEntryMatchesFile(archivePath, "bin/manifest.json", packageManifestPath)
                && VerifyArchiveEntryMatchesFile(archivePath, executableRelativePath, processPath)
                && VerifyArchiveEntryMatchesFile(archivePath, payloadRelativePath, dllPath))
            {
                return true;
            }
        }

        return false;
    }

    private static string? GetPortableArchiveName(JsonElement packageRoot)
    {
        var version = GetString(packageRoot, "version");
        var architecture = GetString(packageRoot, "architecture");
        if (string.IsNullOrWhiteSpace(version) || string.IsNullOrWhiteSpace(architecture))
        {
            return null;
        }

        return $"release_{version}_win-{architecture}.zip";
    }

    private static IEnumerable<string> EnumeratePortableReleaseDirectories(string packageDirectory)
    {
        yield return packageDirectory;

        var parentDirectory = Directory.GetParent(packageDirectory)?.FullName;
        if (parentDirectory is not null)
        {
            yield return parentDirectory;
        }
    }

    private static bool VerifyArchiveHashSidecar(
        string archivePath,
        string shaSidecarPath,
        string archiveName)
    {
        var expectedHash = ReadExpectedSha256(shaSidecarPath, archiveName);
        if (expectedHash is null)
        {
            return false;
        }

        using var archiveStream = File.OpenRead(archivePath);
        var actualHash = Convert.ToHexString(SHA256.HashData(archiveStream));
        return string.Equals(expectedHash, actualHash, StringComparison.OrdinalIgnoreCase);
    }

    private static string? ReadExpectedSha256(string shaSidecarPath, string archiveName)
    {
        foreach (var line in File.ReadLines(shaSidecarPath))
        {
            var parts = line.Split(
                [' ', '\t'],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2)
            {
                continue;
            }

            var candidateName = parts[^1].TrimStart('*');
            if (string.Equals(candidateName, archiveName, StringComparison.OrdinalIgnoreCase)
                && parts[0].Length == 64
                && parts[0].All(Uri.IsHexDigit))
            {
                return parts[0];
            }
        }

        return null;
    }

    private static bool VerifyArchiveEntryMatchesFile(
        string archivePath,
        string? relativePath,
        string filePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return false;
        }

        var entryName = NormalizeZipEntryName(relativePath);
        if (entryName is null)
        {
            return false;
        }

        using var archive = ZipFile.OpenRead(archivePath);
        var entry = archive.GetEntry(entryName);
        if (entry is null)
        {
            return false;
        }

        using var entryStream = entry.Open();
        using var fileStream = File.OpenRead(filePath);
        var entryHash = Convert.ToHexString(SHA256.HashData(entryStream));
        var fileHash = Convert.ToHexString(SHA256.HashData(fileStream));
        return string.Equals(entryHash, fileHash, StringComparison.OrdinalIgnoreCase);
    }

    private static string? NormalizeZipEntryName(string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
        {
            return null;
        }

        var entryName = relativePath.Replace('\\', '/').TrimStart('/');
        return entryName.Contains("../", StringComparison.Ordinal)
               || entryName.Equals("..", StringComparison.Ordinal)
            ? null
            : entryName;
    }

    private static bool IsChecksumOnlyReleaseManifest(JsonElement manifest)
        => string.Equals(GetString(manifest, "signaturePolicy"), ReleaseChecksumOnly, StringComparison.Ordinal)
           && string.Equals(GetString(manifest, "channel"), ReleaseChannel, StringComparison.OrdinalIgnoreCase)
           && string.Equals(GetString(manifest, "buildConfiguration"), ReleaseConfiguration, StringComparison.OrdinalIgnoreCase);

    private static bool ManifestValuesMatch(JsonElement left, JsonElement right, string propertyName)
    {
        var leftValue = GetString(left, propertyName);
        var rightValue = GetString(right, propertyName);
        return !string.IsNullOrWhiteSpace(leftValue)
               && string.Equals(leftValue, rightValue, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string?> EnumerateManifestPayloadRelativePaths(JsonElement packageManifest)
    {
        yield return GetNestedString(packageManifest, "inspector", "net8");
        yield return GetNestedString(packageManifest, "inspector", "net48");
        yield return GetString(packageManifest, "bootstrapper");
    }

    private static string? ResolvePackageRelativePath(string installDirectory, string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
        {
            return null;
        }

        var normalizedRelativePath = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var candidate = Path.GetFullPath(Path.Combine(installDirectory, normalizedRelativePath));
        return IsPathWithinRoot(candidate, installDirectory)
            ? candidate
            : null;
    }

    private static bool IsPathWithinRoot(string fullPath, string rootPath)
    {
        var normalizedFullPath = NormalizeFile(fullPath);
        var normalizedRootPath = NormalizeDirectory(rootPath);
        return normalizedFullPath.StartsWith(
            normalizedRootPath + Path.DirectorySeparatorChar,
            PathComparison);
    }

    private static string? GetNestedString(JsonElement element, string parentName, string childName)
    {
        if (element.TryGetProperty(parentName, out var parent)
            && parent.ValueKind == JsonValueKind.Object)
        {
            return GetString(parent, childName);
        }

        return null;
    }

    private static string? GetString(JsonElement element, string propertyName)
        => element.TryGetProperty(propertyName, out var value)
           && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool PathEquals(string? left, string? right)
        => !string.IsNullOrWhiteSpace(left)
           && !string.IsNullOrWhiteSpace(right)
           && string.Equals(NormalizeFile(left), NormalizeFile(right), PathComparison);

    private static string NormalizeDirectory(string path)
        => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

    private static string NormalizeFile(string path)
        => Path.GetFullPath(path);

    private static StringComparison PathComparison
        => OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
}
