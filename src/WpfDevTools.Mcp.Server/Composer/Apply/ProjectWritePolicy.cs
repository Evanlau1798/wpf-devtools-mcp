namespace WpfDevTools.Mcp.Server.Composer.Apply;

internal static class ProjectWritePolicy
{
    private static readonly string[] BlockedProjectFileNames =
    [
        "App.xaml"
    ];

    private static readonly string[] BlockedProjectFileExtensions =
    [
        ".cs",
        ".csproj",
        ".fsproj",
        ".vbproj"
    ];

    private static readonly string[] BlockedProjectPathSegments =
    [
        ".git",
        ".wpfdevtools",
        "ViewModels",
        "Resources",
        "ResourceDictionaries",
        "Themes"
    ];

    public static ProjectWriteAuthorization Authorize(string projectRoot)
        => Authorize(
            projectRoot,
            Environment.GetEnvironmentVariable(McpServerConfiguration.AllowProjectWritesEnvVar),
            Environment.GetEnvironmentVariable(McpServerConfiguration.AllowedProjectRootsEnvVar));

    private static ProjectWriteAuthorization Authorize(
        string projectRoot,
        string? configuredWrites,
        string? configuredRoots)
    {
        if (!string.Equals(
                configuredWrites,
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            return ProjectWriteAuthorization.Denied(
                "ProjectWritesDisabled",
                $"Project writes are disabled by default. Set {McpServerConfiguration.AllowProjectWritesEnvVar}=true.",
                "Enable project writes only after reviewing the generated file plan.");
        }

        var roots = ParseAllowedRoots(configuredRoots);
        if (!roots.Valid)
        {
            return ProjectWriteAuthorization.Denied(
                "InvalidProjectRootAllowlist",
                $"{McpServerConfiguration.AllowedProjectRootsEnvVar} contains a non-local or non-absolute root.",
                "Use semicolon-separated local absolute project roots.");
        }

        if (roots.Count == 0)
        {
            return ProjectWriteAuthorization.Denied(
                "ProjectRootNotAllowlisted",
                $"No project root is allowlisted in {McpServerConfiguration.AllowedProjectRootsEnvVar}.",
                "Set the allowed project roots environment variable to the reviewed local project root.");
        }

        var normalizedProjectRoot = NormalizeRoot(projectRoot);
        return roots.Roots.Any(root => string.Equals(root, normalizedProjectRoot, StringComparison.OrdinalIgnoreCase))
            ? ProjectWriteAuthorization.CreateAllowed()
            : ProjectWriteAuthorization.Denied(
                "ProjectRootNotAllowlisted",
                "projectRoot is not allowlisted for UI Composer writes.",
                $"Add the exact projectRoot to {McpServerConfiguration.AllowedProjectRootsEnvVar}.");
    }

    internal static ProjectWriteAuthorization AuthorizeSession(
        string projectRoot,
        Func<string, bool> tryConsumeGrant)
        => AuthorizeSession(
            projectRoot,
            tryConsumeGrant,
            Environment.GetEnvironmentVariable(McpServerConfiguration.AllowProjectWritesEnvVar),
            Environment.GetEnvironmentVariable(McpServerConfiguration.AllowedProjectRootsEnvVar));

    internal static ProjectWriteAuthorization AuthorizeSession(
        string projectRoot,
        Func<string, bool> tryConsumeGrant,
        string? configuredWrites,
        string? configuredRoots)
    {
        ArgumentNullException.ThrowIfNull(tryConsumeGrant);
        if (!string.IsNullOrWhiteSpace(configuredWrites))
        {
            return Authorize(projectRoot, configuredWrites, configuredRoots);
        }

        var roots = ParseAllowedRoots(configuredRoots);
        if (!roots.Valid)
        {
            return ProjectWriteAuthorization.Denied(
                "InvalidProjectRootAllowlist",
                $"{McpServerConfiguration.AllowedProjectRootsEnvVar} contains a non-local or non-absolute root.",
                "Use semicolon-separated local absolute project roots.");
        }

        var normalizedProjectRoot = NormalizeRoot(projectRoot);
        if (roots.Count > 0
            && !roots.Roots.Any(root => string.Equals(
                root,
                normalizedProjectRoot,
                StringComparison.OrdinalIgnoreCase)))
        {
            return ProjectWriteAuthorization.Denied(
                "ProjectRootNotAllowlisted",
                "projectRoot is outside the configured project root ceiling.",
                $"Choose an exact projectRoot already listed in {McpServerConfiguration.AllowedProjectRootsEnvVar}.");
        }

        return tryConsumeGrant(normalizedProjectRoot)
            ? ProjectWriteAuthorization.CreateAllowed()
            : ProjectWriteAuthorization.Denied(
                "InteractiveConsentRequired",
                "Writing this project requires temporary user-approved access.",
                $"Call request_session_access for project-write with projectRoot '{normalizedProjectRoot}', then retry in this session.");
    }

    public static bool IsLocalAbsolutePath(string path)
        => Path.IsPathFullyQualified(path) && !path.StartsWith(@"\\", StringComparison.Ordinal) && !path.StartsWith("//", StringComparison.Ordinal);

    public static bool IsPathUnderRoot(string root, string candidate)
    {
        var normalizedRoot = NormalizeRoot(root);
        var rootWithSeparator = normalizedRoot + Path.DirectorySeparatorChar;
        var normalizedCandidate = Path.GetFullPath(candidate);
        return normalizedCandidate.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSystemDirectoryPath(string path)
    {
        var normalizedPath = NormalizeRoot(path);
        foreach (var systemPath in EnumerateSystemPaths())
        {
            if (string.IsNullOrWhiteSpace(systemPath))
            {
                continue;
            }

            var normalizedSystemPath = NormalizeRoot(systemPath);
            if (string.Equals(normalizedPath, normalizedSystemPath, StringComparison.OrdinalIgnoreCase)
                || normalizedPath.StartsWith(normalizedSystemPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static bool IsProtectedMetadataPath(string root, string candidate)
        => GetRelativePathParts(root, candidate)
            .Any(part => string.Equals(part, ".git", StringComparison.OrdinalIgnoreCase));

    public static bool IsBlockedProjectFileTarget(string root, string candidate)
    {
        var parts = GetRelativePathParts(root, candidate);
        if (parts.Length == 0)
        {
            return false;
        }

        var fileName = parts[^1];
        var extension = Path.GetExtension(fileName);
        return !string.Equals(extension, ".xaml", StringComparison.OrdinalIgnoreCase)
               || BlockedProjectFileNames.Contains(fileName, StringComparer.OrdinalIgnoreCase)
               || BlockedProjectFileExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase)
               || parts.Take(parts.Length - 1).Any(part => BlockedProjectPathSegments.Contains(part, StringComparer.OrdinalIgnoreCase));
    }

    public static string? FindReparsePoint(string root, string candidate)
    {
        var normalizedRoot = NormalizeRoot(root);
        var current = normalizedRoot;
        if (HasReparsePoint(current))
        {
            return current;
        }

        var targetParent = Path.GetDirectoryName(Path.GetFullPath(candidate));
        if (string.IsNullOrWhiteSpace(targetParent))
        {
            return null;
        }

        var relativeParent = Path.GetRelativePath(normalizedRoot, targetParent);
        foreach (var part in relativeParent.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == ".")
            {
                continue;
            }

            current = Path.Combine(current, part);
            if (!Directory.Exists(current))
            {
                break;
            }

            if (HasReparsePoint(current))
            {
                return current;
            }
        }

        return File.Exists(candidate) && HasReparsePoint(candidate)
            ? candidate
            : null;
    }

    private static IEnumerable<string> EnumerateSystemPaths()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.System);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        yield return Environment.GetEnvironmentVariable("WINDIR") ?? string.Empty;
    }

    private static string[] GetRelativePathParts(string root, string candidate)
    {
        var relative = Path.GetRelativePath(NormalizeRoot(root), Path.GetFullPath(candidate));
        return relative.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
    }

    private static bool HasReparsePoint(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
        catch (DirectoryNotFoundException)
        {
            return false;
        }
    }

    private static AllowedProjectRoots ParseAllowedRoots(string? configuredRoots)
    {
        if (string.IsNullOrWhiteSpace(configuredRoots))
        {
            return new AllowedProjectRoots(true, []);
        }

        var roots = new List<string>();
        foreach (var entry in configuredRoots.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!IsLocalAbsolutePath(entry))
            {
                return new AllowedProjectRoots(false, []);
            }

            roots.Add(NormalizeRoot(entry));
        }

        return new AllowedProjectRoots(true, roots.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static string NormalizeRoot(string path)
        => Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}

internal sealed record AllowedProjectRoots(bool Valid, IReadOnlyList<string> Roots)
{
    public int Count => Roots.Count;
}

internal sealed record ProjectWriteAuthorization(
    bool Allowed,
    string Code,
    string Message,
    string RepairSuggestion)
{
    public static ProjectWriteAuthorization CreateAllowed()
        => new(true, string.Empty, string.Empty, string.Empty);

    public static ProjectWriteAuthorization Denied(string code, string message, string repairSuggestion)
        => new(false, code, message, repairSuggestion);
}
