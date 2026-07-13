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
        var actions = packages.Select(package => CreateAction(package, mode)).ToArray();
        var inspection = mode switch
        {
            "central" => (Confidence: "best-effort", Reason: "Detected ManagePackageVersionsCentrally=true in an inspected project or central package file."),
            "project" => (Confidence: "best-effort", Reason: "Inspected the target project and did not detect ManagePackageVersionsCentrally=true."),
            _ => (Confidence: "none", Reason: "No target project file was available for package-management inspection.")
        };
        var inspectedFiles = new[] { projectFile, centralFile }
            .Where(path => path is not null)
            .Select(path => ToProjectRelativePath(projectRoot, path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new PackageIntegrationPlan(
            mode,
            projectFile is not null,
            inspection.Confidence,
            inspection.Reason,
            inspectedFiles,
            "Static XML inspection does not evaluate MSBuild imports, conditions, inherited properties, or additional project files.",
            ToProjectRelativePath(projectRoot, projectFile),
            central ? ToProjectRelativePath(projectRoot, centralFile ?? Path.Combine(projectRoot!, "Directory.Packages.props")) : string.Empty,
            actions,
            CreateGuidance(mode));
    }

    private static PackageIntegrationAction CreateAction(RequiredNuGetPackage package, string mode)
    {
        var id = SecurityElement.Escape(package.Id) ?? string.Empty;
        var version = SecurityElement.Escape(package.VersionRange) ?? string.Empty;
        return new PackageIntegrationAction(
            package.Id,
            package.VersionRange,
            mode switch
            {
                "central" => $"<PackageReference Include=\"{id}\" />",
                "project" => $"<PackageReference Include=\"{id}\" Version=\"{version}\" />",
                _ => null
            },
            mode == "central" ? $"<PackageVersion Include=\"{id}\" Version=\"{version}\" />" : null);
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
    string InspectionConfidence,
    string InspectionReason,
    IReadOnlyList<string> InspectedFiles,
    string InspectionLimitations,
    string ProjectFile,
    string CentralPackageFile,
    IReadOnlyList<PackageIntegrationAction> Packages,
    string Guidance);

internal sealed record PackageIntegrationAction(
    string Id,
    string VersionRange,
    string? ProjectPackageReference,
    string? CentralPackageVersion);
