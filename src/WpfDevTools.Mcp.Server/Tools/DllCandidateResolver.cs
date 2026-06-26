using System.Text.Json;
using WpfDevTools.Shared.IO;

namespace WpfDevTools.Mcp.Server.Tools;

/// <summary>
/// Resolves candidate DLL paths for Inspector and Bootstrapper DLLs.
/// Searches the server directory and solution workspace for matching artifacts.
/// </summary>
internal static class DllCandidateResolver
{
    public static IEnumerable<string> EnumerateInspectorCandidates(string serverDir)
    {
        yield return Path.GetFullPath(Path.Combine(serverDir, "WpfDevTools.Inspector.dll"));
        yield return Path.GetFullPath(Path.Combine(serverDir, "inspectors", "net8.0-windows", "WpfDevTools.Inspector.dll"));
        yield return Path.GetFullPath(Path.Combine(serverDir, "inspectors", "net48", "WpfDevTools.Inspector.dll"));

        if (IsReleasePackageDirectory(serverDir))
        {
            yield break;
        }

        var configurations = GetPreferredBuildConfigurations(serverDir);
        foreach (var solutionRoot in RepositoryLayoutLocator.EnumerateSolutionRoots(serverDir))
        {
            var inspectorBinRoot = Path.Combine(solutionRoot, "src", "WpfDevTools.Inspector", "bin");
            var frameworks = new[] { "net8.0-windows", "net48" };

            foreach (var configuration in configurations)
            {
                foreach (var framework in frameworks)
                {
                    yield return Path.GetFullPath(Path.Combine(
                        inspectorBinRoot,
                        configuration,
                        framework,
                        "WpfDevTools.Inspector.dll"));
                }
            }
        }
    }

    public static IEnumerable<string> EnumerateBootstrapperCandidates(string serverDir)
    {
        yield return Path.GetFullPath(Path.Combine(serverDir, "WpfDevTools.Bootstrapper.x64.dll"));
        yield return Path.GetFullPath(Path.Combine(serverDir, "WpfDevTools.Bootstrapper.x86.dll"));
        yield return Path.GetFullPath(Path.Combine(serverDir, "WpfDevTools.Bootstrapper.arm64.dll"));
        yield return Path.GetFullPath(Path.Combine(serverDir, "bootstrapper", "x64", "WpfDevTools.Bootstrapper.x64.dll"));
        yield return Path.GetFullPath(Path.Combine(serverDir, "bootstrapper", "x86", "WpfDevTools.Bootstrapper.x86.dll"));
        yield return Path.GetFullPath(Path.Combine(serverDir, "bootstrapper", "arm64", "WpfDevTools.Bootstrapper.arm64.dll"));

        if (IsReleasePackageDirectory(serverDir))
        {
            yield break;
        }

        var configurations = GetPreferredBuildConfigurations(serverDir);
        foreach (var solutionRoot in RepositoryLayoutLocator.EnumerateSolutionRoots(serverDir))
        {
            var artifactsRoot = Path.Combine(solutionRoot, "artifacts", "bootstrapper");
            var platforms = new[] { ("x64", "x64"), ("Win32", "x86"), ("ARM64", "arm64") };

            foreach (var configuration in configurations)
            {
                foreach (var (platform, suffix) in platforms)
                {
                    yield return Path.GetFullPath(Path.Combine(
                        artifactsRoot, configuration, platform,
                        $"WpfDevTools.Bootstrapper.{suffix}.dll"));
                }
            }
        }
    }

    public static string? GetSolutionRoot(string startDirectory)
    {
        return RepositoryLayoutLocator.EnumerateSolutionRoots(startDirectory).FirstOrDefault();
    }

    internal static IReadOnlyList<string> GetPreferredBuildConfigurations(string serverDir)
    {
        var currentConfiguration = TryGetBuildConfiguration(serverDir);
        return new[] { currentConfiguration, "Debug", "Release" }
            .Where(configuration => !string.IsNullOrWhiteSpace(configuration))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray()!;
    }

    private static bool IsReleasePackageDirectory(string serverDir)
    {
        var manifestPath = Path.Combine(serverDir, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(manifestPath));
            var root = document.RootElement;
            return StringPropertyEquals(root, "name", "wpf-devtools")
                   && StringPropertyEquals(root, "channel", "release")
                   && StringPropertyEquals(root, "buildConfiguration", "Release");
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

    private static bool StringPropertyEquals(JsonElement root, string propertyName, string expected)
    {
        return root.TryGetProperty(propertyName, out var value)
               && value.ValueKind == JsonValueKind.String
               && string.Equals(value.GetString(), expected, StringComparison.OrdinalIgnoreCase);
    }

    private static string? TryGetBuildConfiguration(string serverDir)
    {
        if (string.IsNullOrWhiteSpace(serverDir))
        {
            return null;
        }

        var normalizedServerDirectory = serverDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var targetFrameworkDirectory = new DirectoryInfo(normalizedServerDirectory);
        var configurationDirectory = targetFrameworkDirectory.Parent;
        var binDirectory = configurationDirectory?.Parent;

        if (configurationDirectory == null
            || binDirectory == null
            || !string.Equals(binDirectory.Name, "bin", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return configurationDirectory.Name;
    }
}
