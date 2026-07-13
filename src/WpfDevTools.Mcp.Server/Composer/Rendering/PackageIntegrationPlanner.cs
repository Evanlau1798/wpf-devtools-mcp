using System.Security;
using System.Xml;
using System.Xml.Linq;

namespace WpfDevTools.Mcp.Server.Composer.Rendering;

internal static class PackageIntegrationPlanner
{
    public static PackageIntegrationPlan Create(
        string? projectRoot,
        IReadOnlyList<RequiredNuGetPackage> packages)
    {
        var projectFile = FindProjectFile(projectRoot);
        var centralFile = FindCentralPackageFile(projectRoot);
        var central = HasEnabledProperty(projectFile) || HasEnabledProperty(centralFile);
        var mode = central ? "central" : projectFile is null ? "unknown" : "project";
        var actions = packages.Select(package => CreateAction(package, central)).ToArray();

        return new PackageIntegrationPlan(
            mode,
            projectFile is not null,
            ToProjectRelativePath(projectRoot, projectFile),
            central ? ToProjectRelativePath(projectRoot, centralFile ?? Path.Combine(projectRoot!, "Directory.Packages.props")) : string.Empty,
            actions,
            CreateGuidance(mode));
    }

    private static PackageIntegrationAction CreateAction(RequiredNuGetPackage package, bool central)
    {
        var id = SecurityElement.Escape(package.Id) ?? string.Empty;
        var version = SecurityElement.Escape(package.VersionRange) ?? string.Empty;
        return new PackageIntegrationAction(
            package.Id,
            package.VersionRange,
            central
                ? $"<PackageReference Include=\"{id}\" />"
                : $"<PackageReference Include=\"{id}\" Version=\"{version}\" />",
            central ? $"<PackageVersion Include=\"{id}\" Version=\"{version}\" />" : null);
    }

    private static string CreateGuidance(string mode)
        => mode switch
        {
            "central" => "Add each versionless PackageReference to the target project and each PackageVersion to Directory.Packages.props. Composer does not write either file.",
            "project" => "Add each versioned PackageReference to the target project. Composer does not edit the project file.",
            _ => "Inspect the target project's package-management mode before adding the returned packages. Composer does not edit project or central package files."
        };

    private static string? FindProjectFile(string? projectRoot)
        => !string.IsNullOrWhiteSpace(projectRoot) && Directory.Exists(projectRoot)
            ? Directory.EnumerateFiles(projectRoot, "*.csproj", SearchOption.TopDirectoryOnly)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault()
            : null;

    private static string? FindCentralPackageFile(string? projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot))
        {
            return null;
        }

        var directory = new DirectoryInfo(Path.GetFullPath(projectRoot));
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "Directory.Packages.props");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static bool HasEnabledProperty(string? path)
    {
        if (path is null)
        {
            return false;
        }

        try
        {
            using var reader = XmlReader.Create(path, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            });
            return XDocument.Load(reader)
                .Descendants()
                .Any(element => element.Name.LocalName == "ManagePackageVersionsCentrally"
                    && bool.TryParse(element.Value, out var enabled)
                    && enabled);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return false;
        }
    }

    private static string ToProjectRelativePath(string? projectRoot, string? path)
        => string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : string.IsNullOrWhiteSpace(projectRoot)
                ? Path.GetFileName(path)
                : Path.GetRelativePath(Path.GetFullPath(projectRoot), Path.GetFullPath(path));
}

internal sealed record PackageIntegrationPlan(
    string Mode,
    bool ProjectInspected,
    string ProjectFile,
    string CentralPackageFile,
    IReadOnlyList<PackageIntegrationAction> Packages,
    string Guidance);

internal sealed record PackageIntegrationAction(
    string Id,
    string VersionRange,
    string ProjectPackageReference,
    string? CentralPackageVersion);
