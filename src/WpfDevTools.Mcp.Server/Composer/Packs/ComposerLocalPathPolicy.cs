namespace WpfDevTools.Mcp.Server.Composer.Packs;

internal static class ComposerLocalPathPolicy
{
    public static string RequireLocalRoot(
        string path,
        string parameterName,
        Func<string, DriveType>? driveTypeResolver = null)
    {
        if (string.IsNullOrWhiteSpace(path) || !Path.IsPathFullyQualified(path))
        {
            throw new ArgumentException("Composer pack roots must be fully qualified local paths.", parameterName);
        }

        var fullPath = Path.GetFullPath(path);
        if (fullPath.StartsWith(@"\\", StringComparison.Ordinal)
            || fullPath.StartsWith("//", StringComparison.Ordinal))
        {
            throw new ArgumentException("Composer pack roots must be local paths.", parameterName);
        }

        if (!OperatingSystem.IsWindows())
        {
            return fullPath;
        }

        var driveRoot = Path.GetPathRoot(fullPath);
        if (string.IsNullOrWhiteSpace(driveRoot))
        {
            throw new ArgumentException("Composer pack root storage could not be validated as local.", parameterName);
        }

        DriveType driveType;
        try
        {
            driveTypeResolver ??= static root => new DriveInfo(root).DriveType;
            driveType = driveTypeResolver(driveRoot);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            throw new ArgumentException(
                "Composer pack root storage could not be validated as local.",
                parameterName,
                ex);
        }

        if (driveType is DriveType.Network or DriveType.NoRootDirectory or DriveType.Unknown)
        {
            throw new ArgumentException("Composer pack roots must be on local storage.", parameterName);
        }

        return fullPath;
    }
}
