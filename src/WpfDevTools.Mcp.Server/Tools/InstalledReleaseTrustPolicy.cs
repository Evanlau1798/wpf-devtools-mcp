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
        var installBase = currentDirectory is null
            ? null
            : Directory.GetParent(currentDirectory)?.FullName;
        if (currentDirectory is null || installBase is null)
        {
            return false;
        }

        var packageManifestPath = Path.Combine(binDirectory, "manifest.json");
        var installManifestPath = Path.Combine(installBase, "install-manifest.json");
        if (!File.Exists(packageManifestPath) || !File.Exists(installManifestPath))
        {
            return false;
        }

        using var packageManifest = JsonDocument.Parse(File.ReadAllText(packageManifestPath));
        using var installManifest = JsonDocument.Parse(File.ReadAllText(installManifestPath));
        var packageRoot = packageManifest.RootElement;
        var installRoot = installManifest.RootElement;

        if (!IsChecksumOnlyReleaseManifest(packageRoot)
            || !IsChecksumOnlyReleaseManifest(installRoot)
            || !ManifestValuesMatch(packageRoot, installRoot, "version")
            || !ManifestValuesMatch(packageRoot, installRoot, "architecture"))
        {
            return false;
        }

        var installDir = GetString(installRoot, "installDir");
        var installedExecutable = GetString(installRoot, "executable");
        var normalizedCurrentDirectory = NormalizeDirectory(currentDirectory);
        var normalizedProcessPath = NormalizeFile(processPath);
        if (!PathEquals(installDir, normalizedCurrentDirectory)
            || !PathEquals(installedExecutable, normalizedProcessPath))
        {
            return false;
        }

        var packageExecutable = ResolvePackageRelativePath(
            normalizedCurrentDirectory,
            GetString(packageRoot, "entryExecutable"));
        if (!PathEquals(packageExecutable, normalizedProcessPath))
        {
            return false;
        }

        var normalizedDllPath = NormalizeFile(dllPath);
        return EnumerateManifestPayloads(packageRoot, normalizedCurrentDirectory)
            .Any(payloadPath => PathEquals(payloadPath, normalizedDllPath));
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

    private static IEnumerable<string> EnumerateManifestPayloads(JsonElement packageManifest, string installDirectory)
    {
        foreach (var relativePath in new[]
        {
            GetNestedString(packageManifest, "inspector", "net8"),
            GetNestedString(packageManifest, "inspector", "net48"),
            GetString(packageManifest, "bootstrapper")
        })
        {
            var resolvedPath = ResolvePackageRelativePath(installDirectory, relativePath);
            if (resolvedPath is not null)
            {
                yield return resolvedPath;
            }
        }
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
