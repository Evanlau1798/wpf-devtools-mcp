using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using WpfDevTools.Mcp.Server.Composer.Contracts;
using WpfDevTools.Mcp.Server.Composer.Packs;
using WpfDevTools.Mcp.Server.Composer.Rendering;

namespace WpfDevTools.Mcp.Server.Composer.Apply;

internal static class ProjectIntegrationPlanBuilder
{
    public static ProjectIntegrationPlan Build(
        PackRegistry registry,
        string blueprintJson,
        string projectRoot,
        string targetPath,
        string appliedXaml,
        IReadOnlyList<RequiredNuGetPackage> packages,
        IReadOnlyList<string> resources,
        CodeBehindIntegrationPlan? codeBehind)
    {
        var errors = new List<ApplyBlueprintIssue>();
        var operations = new List<ProjectIntegrationOperation>();
        var projectResources = ResolveProjectImageResources(projectRoot, appliedXaml, errors);
        AddPackageOperations(projectRoot, packages, projectResources, operations, errors);
        AddApplicationOperation(
            registry,
            blueprintJson,
            projectRoot,
            targetPath,
            appliedXaml,
            resources,
            operations,
            errors);
        AddCodeBehindOperation(appliedXaml, codeBehind, operations, errors);
        if (errors.Count > 0)
        {
            return new ProjectIntegrationPlan(false, string.Empty, operations, errors);
        }

        return new ProjectIntegrationPlan(true, ComputePlanHash(operations), operations, []);
    }

    private static void AddPackageOperations(
        string projectRoot,
        IReadOnlyList<RequiredNuGetPackage> packages,
        IReadOnlyList<string> projectResources,
        List<ProjectIntegrationOperation> operations,
        List<ApplyBlueprintIssue> errors)
    {
        if (packages.Count == 0 && projectResources.Count == 0)
        {
            return;
        }

        var guidance = PackageIntegrationPlanner.Create(projectRoot, packages);
        if (guidance.Mode is not "project" and not "central" || string.IsNullOrWhiteSpace(guidance.ProjectFile))
        {
            errors.Add(Issue(
                "IntegrationProjectFileMissing",
                "A machine-applicable project patch requires one inspectable project file.",
                "Add or select a WPF project file under projectRoot, then rerun the dry-run plan."));
            return;
        }

        var projectPath = ResolveInsideRoot(projectRoot, guidance.ProjectFile, "project file", errors);
        if (projectPath is not null)
        {
            var purposes = packages.Count == 0 ? new List<string>() : ["packages"];
            if (projectResources.Count > 0)
            {
                purposes.Add("project-resource");
            }

            AddPatchedOperation(
                packages.Count > 0 ? "package-reference" : "project-resource",
                projectPath,
                purposes,
                ProjectIntegrationXmlPatcher.PatchProject(
                    projectPath,
                    packages,
                    guidance.Mode == "central",
                    projectResources),
                packages.Count > 0 && projectResources.Count > 0
                    ? "Add pack-declared PackageReference items and project-owned WPF Resource images."
                    : packages.Count > 0
                        ? "Add pack-declared PackageReference items using the inspected package-management mode."
                        : "Declare project-owned application-local images as WPF Resource items.",
                operations,
                errors);
        }

        if (packages.Count == 0 || guidance.Mode != "central")
        {
            return;
        }

        var centralPath = ResolveInsideRoot(projectRoot, guidance.CentralPackageFile, "central package file", errors);
        if (centralPath is not null)
        {
            AddPatchedOperation(
                "central-package-version",
                centralPath,
                ["packages"],
                ProjectIntegrationXmlPatcher.PatchCentralPackages(centralPath, packages),
                "Add pack-declared PackageVersion items to the project-local central package file.",
                operations,
                errors);
        }
    }

    private static void AddApplicationOperation(
        PackRegistry registry,
        string blueprintJson,
        string projectRoot,
        string targetPath,
        string appliedXaml,
        IReadOnlyList<string> resources,
        List<ProjectIntegrationOperation> operations,
        List<ApplyBlueprintIssue> errors)
    {
        var selectedManifests = ComposerWindowRootResolver.ResolveSelectedManifests(registry, blueprintJson);
        var setStartup = ComposerWindowRootResolver.IsWindowRoot(selectedManifests, appliedXaml);
        var replacedResources = selectedManifests
            .SelectMany(manifest => manifest.ResourceSetup.Variants.Values)
            .SelectMany(variant => variant.ApplicationMergedDictionaries)
            .Except(resources, StringComparer.Ordinal)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (resources.Count == 0 && replacedResources.Length == 0 && !setStartup)
        {
            return;
        }

        var appPath = Path.Combine(projectRoot, "App.xaml");
        if (!File.Exists(appPath))
        {
            errors.Add(Issue(
                "IntegrationAppXamlMissing",
                "Resource or startup integration requires an existing App.xaml under projectRoot.",
                "Create the WPF application shell, then rerun the dry-run plan."));
            return;
        }

        var namespaces = ResolveResourceNamespaces(
            selectedManifests,
            resources.Concat(replacedResources).ToArray());
        var purposes = new List<string>();
        if (resources.Count > 0)
        {
            purposes.Add("resources");
        }

        if (setStartup)
        {
            purposes.Add("startup");
        }

        AddPatchedOperation(
            "application-xaml",
            appPath,
            purposes,
            ProjectIntegrationXmlPatcher.PatchApplication(
                appPath,
                projectRoot,
                targetPath,
                resources,
                replacedResources,
                namespaces,
                setStartup),
            setStartup
                ? resources.Count == 0
                    ? "Select the generated window as StartupUri."
                    : "Merge pack-declared resources and select the generated window as StartupUri."
                : "Merge pack-declared application resources.",
            operations,
            errors);
    }

    internal static IReadOnlyList<string> ResolveProjectImageResources(
        string projectRoot,
        string appliedXaml,
        List<ApplyBlueprintIssue> errors)
    {
        var guidance = PackageIntegrationPlanner.Create(projectRoot, []);
        if (string.IsNullOrWhiteSpace(guidance.ProjectFile))
        {
            return [];
        }

        var projectPath = ResolveInsideRoot(projectRoot, guidance.ProjectFile, "project file", errors);
        if (projectPath is null)
        {
            return [];
        }

        return ResolveProjectImageResources(
            projectRoot,
            ResolveAssemblyName(projectPath),
            appliedXaml,
            errors);
    }

    private static IReadOnlyList<string> ResolveProjectImageResources(
        string projectRoot,
        string assemblyName,
        string appliedXaml,
        List<ApplyBlueprintIssue> errors)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(appliedXaml);
        }
        catch (System.Xml.XmlException)
        {
            return [];
        }

        var resources = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var attribute in document.Descendants().Attributes().Where(IsImageSourceAttribute))
        {
            var source = attribute.Value.Trim();
            if (!PreviewResourcePolicy.IsApplicationLocalPackSource(source))
            {
                continue;
            }

            var relative = source.StartsWith("pack://application:,,,/", StringComparison.OrdinalIgnoreCase)
                ? source["pack://application:,,,/".Length..]
                : source.TrimStart('/');
            var component = relative.IndexOf(";component/", StringComparison.OrdinalIgnoreCase);
            if (component >= 0)
            {
                if (!string.Equals(relative[..component], assemblyName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                relative = relative[(component + ";component/".Length)..];
            }

            try
            {
                relative = Uri.UnescapeDataString(relative).Replace('/', Path.DirectorySeparatorChar);
                var fullPath = Path.GetFullPath(Path.Combine(projectRoot, relative));
                if (!ProjectWritePolicy.IsPathUnderRoot(projectRoot, fullPath))
                {
                    errors.Add(Issue(
                        "ProjectImageResourceOutsideRoot",
                        "An application-local image resolves outside projectRoot.",
                        "Use a project-owned image path below the reviewed projectRoot."));
                    continue;
                }

                if (!File.Exists(fullPath))
                {
                    errors.Add(Issue(
                        "ProjectImageResourceMissing",
                        $"Application-local image does not exist: {relative}.",
                        "Create the project-owned image before reviewing project integration."));
                    continue;
                }

                resources.Add(Path.GetRelativePath(projectRoot, fullPath));
            }
            catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException or UriFormatException)
            {
                errors.Add(Issue(
                    "ProjectImageResourceInvalid",
                    $"Application-local image path is invalid: {ex.Message}",
                    "Use a valid application-local pack URI for a project-owned image."));
            }
        }

        return resources.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string ResolveAssemblyName(string projectPath)
    {
        var fallback = Path.GetFileNameWithoutExtension(projectPath);
        try
        {
            using var reader = XmlReader.Create(projectPath, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            });
            var configured = XDocument.Load(reader).Descendants()
                .FirstOrDefault(element => element.Name.LocalName == "AssemblyName")
                ?.Value.Trim();
            return string.IsNullOrWhiteSpace(configured) ? fallback : configured;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException)
        {
            return fallback;
        }
    }

    private static bool IsImageSourceAttribute(XAttribute attribute)
        => attribute.Name.LocalName switch
        {
            "ImageSource" => true,
            "Source" => attribute.Parent?.Name.LocalName == "Image",
            "UriSource" => attribute.Parent?.Name.LocalName == "BitmapImage",
            _ => false
        };

    private static void AddCodeBehindOperation(
        string appliedXaml,
        CodeBehindIntegrationPlan? codeBehind,
        List<ProjectIntegrationOperation> operations,
        List<ApplyBlueprintIssue> errors)
    {
        if (codeBehind is null)
        {
            return;
        }

        if (!TryGetClassIdentity(appliedXaml, out var rootNamespace, out var className))
        {
            errors.Add(Issue(
                "IntegrationXClassMissing",
                "Pack-declared code-behind integration requires generated x:Class metadata.",
                "Use apply_ui_blueprint with a project target that can resolve x:Class, then rerun dry-run."));
            return;
        }

        AddPatchedOperation(
            "code-behind-base-type",
            codeBehind.TargetPath,
            ["code-behind"],
            ProjectIntegrationCodePatcher.Patch(
                codeBehind.TargetPath,
                rootNamespace,
                className,
                codeBehind.BaseType),
            $"Align the generated x:Class code-behind with pack-declared base type {codeBehind.BaseType}.",
            operations,
            errors);
    }

    private static void AddPatchedOperation(
        string role,
        string path,
        IReadOnlyList<string> purposes,
        ProjectContentPatchResult patch,
        string description,
        List<ProjectIntegrationOperation> operations,
        List<ApplyBlueprintIssue> errors)
    {
        if (!patch.Success)
        {
            errors.Add(patch.Error!);
            return;
        }

        var exists = File.Exists(path);
        string current;
        try
        {
            current = exists ? File.ReadAllText(path) : string.Empty;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            errors.Add(Issue(
                "IntegrationPreconditionReadFailed",
                $"Could not read integration target '{path}': {ex.Message}",
                "Resolve the file access issue, then rerun the dry-run plan."));
            return;
        }

        var currentHash = exists ? Sha256(current) : string.Empty;
        var proposedHash = Sha256(patch.Content);
        operations.Add(new ProjectIntegrationOperation(
            role,
            path,
            currentHash == proposedHash ? "none" : exists ? "update" : "create",
            purposes,
            new ProjectFilePrecondition(exists, currentHash),
            proposedHash,
            description,
            patch.Content));
    }

    private static IReadOnlyDictionary<string, string> ResolveResourceNamespaces(
        IReadOnlyList<UiPackManifest> manifests,
        IReadOnlyList<string> resources)
    {
        var namespaces = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var manifest in manifests)
        {
            foreach (var item in manifest.XmlNamespaces)
            {
                if (resources.Any(resource => resource.Contains(item.Key + ":", StringComparison.Ordinal)))
                {
                    namespaces[item.Key] = item.Value;
                }
            }
        }

        return namespaces;
    }

    private static bool TryGetClassIdentity(string xaml, out string rootNamespace, out string className)
    {
        rootNamespace = className = string.Empty;
        try
        {
            var value = XDocument.Parse(xaml).Root?.Attributes()
                .FirstOrDefault(attribute => attribute.Name.LocalName == "Class")?.Value;
            var separator = value?.LastIndexOf('.') ?? -1;
            if (separator <= 0 || separator == value!.Length - 1)
            {
                return false;
            }

            rootNamespace = value[..separator];
            className = value[(separator + 1)..];
            return true;
        }
        catch (System.Xml.XmlException)
        {
            return false;
        }
    }

    private static string? ResolveInsideRoot(
        string projectRoot,
        string relativePath,
        string role,
        List<ApplyBlueprintIssue> errors)
    {
        var path = Path.GetFullPath(Path.Combine(projectRoot, relativePath));
        if (ProjectWritePolicy.IsPathUnderRoot(projectRoot, path))
        {
            return path;
        }

        errors.Add(Issue(
            "IntegrationPathOutsideRoot",
            $"The inspected {role} is outside projectRoot and cannot be changed by guarded integration.",
            role == "central package file"
                ? "For an intentionally isolated project, create Directory.Packages.props inside projectRoot with <Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>, then rerun the dry-run plan. Otherwise choose a projectRoot that contains every reviewed integration target."
                : "Choose a projectRoot that contains every reviewed integration target."));
        return null;
    }

    private static string ComputePlanHash(IReadOnlyList<ProjectIntegrationOperation> operations)
    {
        var contract = operations.Select(operation => new
        {
            operation.Role,
            operation.TargetPath,
            operation.Action,
            operation.Purposes,
            operation.Precondition,
            operation.ProposedSha256
        });
        return Sha256(JsonSerializer.Serialize(contract));
    }

    internal static string Sha256(string content)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

    private static ApplyBlueprintIssue Issue(string code, string message, string repair)
        => new("$.projectRoot", code, message, repair);
}
