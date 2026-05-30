using System.IO;

namespace WpfDevTools.Tests.Integration;

internal static class IntegrationExecutableLocator
{
    internal static string? FindExecutable(
        string appBaseDirectory,
        string projectDir,
        string projectName,
        string framework,
        string exeName)
    {
        var configuration = TryGetBuildConfiguration(appBaseDirectory);
        if (string.IsNullOrWhiteSpace(configuration))
        {
            return null;
        }

        var solutionRoot = FindSolutionRoot(appBaseDirectory);
        var platform = TryGetBuildPlatform(appBaseDirectory);
        var candidate = string.IsNullOrWhiteSpace(platform)
            ? Path.Combine(solutionRoot, projectDir, projectName, "bin", configuration, framework, exeName)
            : Path.Combine(solutionRoot, projectDir, projectName, "bin", platform, configuration, framework, exeName);
        return File.Exists(candidate) ? candidate : null;
    }

    internal static string? TryGetBuildConfiguration(string appBaseDirectory)
    {
        if (string.IsNullOrWhiteSpace(appBaseDirectory))
        {
            return null;
        }

        var baseDir = appBaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var targetFrameworkDirectory = new DirectoryInfo(baseDir);
        return targetFrameworkDirectory.Parent?.Name;
    }

    internal static string? TryGetBuildPlatform(string appBaseDirectory)
    {
        if (string.IsNullOrWhiteSpace(appBaseDirectory))
        {
            return null;
        }

        var baseDir = appBaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var targetFrameworkDirectory = new DirectoryInfo(baseDir);
        var configurationDirectory = targetFrameworkDirectory.Parent;
        var possiblePlatformDirectory = configurationDirectory?.Parent;
        if (possiblePlatformDirectory?.Parent == null ||
            !string.Equals(possiblePlatformDirectory.Parent.Name, "bin", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return possiblePlatformDirectory.Name;
    }

    private static string FindSolutionRoot(string appBaseDirectory)
    {
        var directory = new DirectoryInfo(appBaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "WpfDevTools.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Solution root not found");
    }
}
